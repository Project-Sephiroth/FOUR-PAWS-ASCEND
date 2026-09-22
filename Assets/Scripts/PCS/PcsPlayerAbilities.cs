using System.Collections.Generic;
using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
public class PcsPlayerAbilities : NetworkBehaviour, IStateAuthorityChanged
{
    [SerializeField] private MyEnum.CharacterType characterType;
    [SerializeField, Min(0.1f)] private float interactionRange = 1.8f;
    [SerializeField, Min(0.1f)] private float pullRange = 7f;
    [SerializeField, Min(0.1f)] private float pullSpeed = 6f;
    [SerializeField, Min(0.1f)] private float ladderSpeed = 2.5f;
    [SerializeField, Min(0.05f)] private float abilityCooldown = 0.35f;
    [SerializeField, Min(0.1f)] private float throwSpeed = 10f;

    public MyEnum.CharacterType CharacterType => characterType;
    public string Feedback { get; private set; }
    public float PullRange => pullRange;
    public Collider2D BodyCollider => mover != null ? mover.BodyCollider : null;

    private static readonly HashSet<PcsPlayerAbilities> players = new HashSet<PcsPlayerAbilities>();
    private PlayerInput input;
    private Mover mover;
    private Rider rider;
    private Lifter lifter;
    private PcsLocalCameraFollow cameraFollow;
    private PcsPuzzleDevice[] devices;
    private readonly List<RaycastHit2D> castHits = new List<RaycastHit2D>(24);
    private ContactFilter2D solidFilter;
    private NetworkObject pullSource;
    private bool pullingToActor;
    private Vector2 anchorGoal;
    private bool pulling;
    private float pullDeadline;
    private float nextAbilityTime;
    private PcsPuzzleDevice ladder;
    private int observedResetEpoch;
    private bool hasDirectorEpoch;
    private PcsPuzzleDevice heldLever;
    private float nextHoldRenewal;
    private const float HoldRenewalInterval = 0.2f;

    private void Awake()
    {
        input = GetComponent<PlayerInput>();
        mover = GetComponent<Mover>();
        rider = GetComponent<Rider>();
        lifter = GetComponent<Lifter>();
        solidFilter.useTriggers = false;
        solidFilter.SetLayerMask(Physics2D.AllLayers);
    }

    public override void Spawned()
    {
        players.Add(this);
        devices = FindObjectsByType<PcsPuzzleDevice>(FindObjectsSortMode.None);
        ApplyLocalCameraAuthority();
    }

    public void StateAuthorityChanged()
    {
        ClearTransientMotion();
        hasDirectorEpoch = false;
        ApplyLocalCameraAuthority();
    }

    private void ApplyLocalCameraAuthority()
    {
        if (cameraFollow != null)
            cameraFollow.Unbind(transform);
        if (HasStateAuthority)
        {
            Camera sceneCamera = Camera.main;
            if (sceneCamera != null)
            {
                cameraFollow = sceneCamera.GetComponent<PcsLocalCameraFollow>();
                if (cameraFollow == null)
                    cameraFollow = sceneCamera.gameObject.AddComponent<PcsLocalCameraFollow>();
                cameraFollow.Bind(transform);
            }
        }
    }

    public static bool CanParticipate(NetworkObject actor)
    {
        if (actor == null || !actor.IsValid || !actor.gameObject.activeInHierarchy)
            return false;
        NetworkGameManager manager = NetworkGameManager.Instance;
        return manager == null || !manager.TeamLocked || manager.IsStartingPlayer(actor.StateAuthority);
    }

    // Called by Mover before it chooses exactly one locomotion mode for this network tick.
    public void SimulateInput()
    {
        if (!HasStateAuthority || input == null || mover == null)
            return;
        PcsPuzzleDirector director = PcsPuzzleDirector.Instance;
        if (director != null && director.Object != null && director.Object.IsValid)
        {
            int epoch = director.GetResetEpoch(characterType);
            if (!hasDirectorEpoch)
            {
                observedResetEpoch = epoch;
                hasDirectorEpoch = true;
            }
            else if (observedResetEpoch != epoch)
            {
                observedResetEpoch = epoch;
                if (CanParticipate(Object))
                    mover.ResetAt(director.GetRespawnPosition(characterType));
            }
        }
        if (!CanParticipate(Object))
        {
            ReleaseRemoteHold();
            input.ConsumeActions();
            Feedback = "다음 팀 시작을 기다리는 중입니다.";
            return;
        }
        if (input.CarryInput)
            HandleCarry(director);
        bool usingHoldLever = UpdateRemoteHold(director);
        if (!usingHoldLever && input.AbilityInput && Time.time >= nextAbilityTime)
        {
            nextAbilityTime = Time.time + abilityCooldown;
            HandleAbility(director);
        }
        if (director != null)
        {
            PcsPuzzleDevice nearby = FindDevice(false);
            if (input.InteractInput && nearby != null)
                director.RequestAction(Object, PcsPuzzleAction.Interact, nearby.DeviceId, input.AimWorld);
            if (input.ResetInput)
                director.RequestAction(Object, PcsPuzzleAction.Reset, -1, input.AimWorld);
        }
        input.ConsumeActions();
    }

    private bool UpdateRemoteHold(PcsPuzzleDirector director)
    {
        PcsPuzzleDevice target = null;
        if (characterType == MyEnum.CharacterType.Frog && input.enabled && input.AbilityHeldInput &&
            BodyCollider != null && BodyCollider.enabled && mover.Body != null && mover.Body.simulated &&
            director != null && director.Object != null && director.Object.IsValid)
        {
            PcsPuzzleDevice aimed = FindDevice(true);
            if (aimed != null && aimed.RequiresRemoteHold && HasClearLine(Center, aimed.InteractionPoint, aimed.transform))
            {
                PcsPlayerAbilities ally = FindAimedPlayer();
                if (ally == null || AimScore(aimed.InteractionPoint) <= AimScore(ally.Center))
                    target = aimed;
            }
        }
        if (heldLever != target)
        {
            ReleaseRemoteHold();
            heldLever = target;
            nextHoldRenewal = 0f;
        }
        if (heldLever == null)
            return false;
        // Reliable edges plus a bounded lease renewal recover lost owners without a per-frame RPC.
        if (Time.time >= nextHoldRenewal)
        {
            director.RequestRemoteHold(Object, heldLever.DeviceId, true, input.AimWorld);
            nextHoldRenewal = Time.time + HoldRenewalInterval;
        }
        return true;
    }

    private void ReleaseRemoteHold()
    {
        if (heldLever != null)
        {
            PcsPuzzleDirector director = PcsPuzzleDirector.Instance;
            if (Object != null && Object.IsValid && HasStateAuthority && director != null &&
                director.Object != null && director.Object.IsValid)
                director.RequestRemoteHold(Object, heldLever.DeviceId, false, input != null ? input.AimWorld : Center);
        }
        heldLever = null;
        nextHoldRenewal = 0f;
    }

    private void HandleCarry(PcsPuzzleDirector director)
    {
        if (rider != null && rider.IsRiding)
        {
            rider.Drop();
            return;
        }
        if (lifter != null && lifter.CurrentRider != null)
        {
            lifter.Release(Vector2.zero);
            return;
        }
        PcsPuzzleDevice dummy = FindDevice(false, PcsDeviceKind.Dummy);
        if (director != null && dummy != null)
        {
            director.RequestAction(Object, PcsPuzzleAction.Carry, dummy.DeviceId, input.AimWorld);
            return;
        }
        if (lifter == null)
            return;
        PcsPlayerAbilities nearest = null;
        float best = interactionRange;
        foreach (PcsPlayerAbilities candidate in players)
        {
            if (candidate == null || candidate == this || candidate.rider == null || candidate.BodyCollider == null ||
                !CanParticipate(candidate.Object) || candidate.rider.IsRiding)
                continue;
            float distance = Vector2.Distance(Center, candidate.Center);
            if (distance < best && HasClearLine(Center, candidate.Center, candidate.transform))
            {
                best = distance;
                nearest = candidate;
            }
        }
        if (nearest != null)
            lifter.Lift(nearest.rider);
    }

    private void HandleAbility(PcsPuzzleDirector director)
    {
        if (characterType == MyEnum.CharacterType.Bear)
        {
            Vector2 direction = input.AimWorld - Center;
            if (direction.sqrMagnitude < 0.01f)
                direction = Vector2.right * input.Facing;
            if (lifter != null && lifter.CurrentRider != null)
                lifter.Release(direction.normalized * throwSpeed + Vector2.up * 2f);
            else if (director != null)
                director.RequestAction(Object, PcsPuzzleAction.Throw, -1, input.AimWorld);
            return;
        }
        if (characterType != MyEnum.CharacterType.Frog)
            return;

        PcsPuzzleDevice target = FindDevice(true);
        PcsPlayerAbilities ally = FindAimedPlayer();
        if (ally != null && (target == null || AimScore(ally.Center) < AimScore(target.InteractionPoint)))
        {
            ally.RPC_RequestPull(Object);
            Feedback = "아군을 끌어옵니다.";
            return;
        }
        if (target == null)
        {
            Feedback = "가까운 대상이나 고정점을 조준하세요.";
            return;
        }
        if (target.Kind == PcsDeviceKind.Anchor && HasClearLine(Center, target.InteractionPoint, target.transform))
        {
            if (rider != null)
                rider.Drop();
            pullSource = null;
            pullingToActor = false;
            anchorGoal = target.InteractionPoint;
            pulling = true;
            ladder = null;
            pullDeadline = Time.time + pullRange / pullSpeed + 1f;
        }
        if (director != null)
            director.RequestAction(Object, PcsPuzzleAction.Remote, target.DeviceId, input.AimWorld);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestPull(NetworkObject source, RpcInfo info = default)
    {
        PcsPlayerAbilities frog = source != null ? source.GetComponent<PcsPlayerAbilities>() : null;
        if (frog == null || frog.characterType != MyEnum.CharacterType.Frog || info.Source != source.StateAuthority ||
            mover == null || frog.mover == null || source == Object || !CanParticipate(Object) || !CanParticipate(source) ||
            mover.Body.mass > frog.mover.Body.mass || Vector2.Distance(Center, frog.Center) > frog.pullRange ||
            !HasClearLine(Center, frog.Center, frog.transform))
            return;
        if (rider != null)
            rider.Drop();
        pullSource = source;
        pullingToActor = true;
        pulling = true;
        ladder = null;
        pullDeadline = Time.time + frog.pullRange / Mathf.Max(0.1f, pullSpeed) + 1f;
    }

    public bool TryGetMotionVelocity(float deltaTime, out Vector2 velocity)
    {
        velocity = Vector2.zero;
        if (!HasStateAuthority || mover == null || deltaTime <= 0f)
            return false;
        if (pulling)
        {
            if (input.JumpInput || (pullingToActor && pullSource == null))
            {
                input.JumpInput = false;
                ClearTransientMotion();
                return true;
            }
            Vector2 goal = anchorGoal;
            Transform ignored = null;
            if (pullSource != null)
            {
                PcsPlayerAbilities source = pullSource.GetComponent<PcsPlayerAbilities>();
                if (source == null || !CanParticipate(pullSource))
                {
                    ClearTransientMotion();
                    return true;
                }
                float side = mover.Body.position.x >= source.mover.Body.position.x ? 1f : -1f;
                goal = source.mover.Body.position + Vector2.right * side * 0.75f;
                ignored = source.transform;
            }
            Vector2 offset = goal - mover.Body.position;
            if (Time.time > pullDeadline || offset.sqrMagnitude <= 0.0225f)
            {
                pulling = false;
                pullSource = null;
                return true;
            }
            float distance = Mathf.Min(pullSpeed * deltaTime, offset.magnitude);
            distance = SafeTravel(offset.normalized, distance, ignored);
            if (distance <= 0.001f)
            {
                pulling = false;
                pullSource = null;
                Feedback = "앞이 막혀 끌기가 멈췄습니다.";
                return true;
            }
            velocity = offset.normalized * distance / deltaTime;
            return true;
        }

        bool leaveLadder = input.JumpInput || Mathf.Abs(input.MoveInput.x) > 0.01f;
        if (ladder != null && input.JumpInput)
        {
            input.JumpInput = false;
            mover.QueueLaunch(Vector2.up * mover.JumpSpeed);
        }
        if (leaveLadder)
            ladder = null;
        else if (Mathf.Abs(input.LadderInput) > 0.01f)
            ladder = FindLadder();
        if (ladder == null || !ladder.CanClimb || BodyCollider == null ||
            !ladder.Trigger.bounds.Intersects(BodyCollider.bounds))
        {
            ladder = null;
            return false;
        }
        Vector2 ladderVelocity = new Vector2(Mathf.Clamp(ladder.InteractionPoint.x - mover.Body.position.x, -0.5f, 0.5f) * 4f,
            input.LadderInput * ladderSpeed);
        float move = ladderVelocity.magnitude * deltaTime;
        if (move > 0.0001f)
            ladderVelocity = ladderVelocity.normalized * SafeTravel(ladderVelocity.normalized, move, null) / deltaTime;
        velocity = ladderVelocity;
        return true;
    }

    private float SafeTravel(Vector2 direction, float distance, Transform ignored)
    {
        castHits.Clear();
        mover.Body.Cast(direction, solidFilter, castHits, distance + 0.025f);
        foreach (RaycastHit2D hit in castHits)
        {
            if (hit.collider == null || hit.collider.attachedRigidbody == mover.Body ||
                (ignored != null && hit.collider.transform.IsChildOf(ignored)) || Vector2.Dot(hit.normal, direction) >= -0.01f)
                continue;
            distance = Mathf.Min(distance, Mathf.Max(0f, hit.distance - 0.025f));
        }
        return distance;
    }

    private bool HasClearLine(Vector2 from, Vector2 to, Transform target)
    {
        Vector2 offset = to - from;
        castHits.Clear();
        Physics2D.Raycast(from, offset.normalized, solidFilter, castHits, offset.magnitude);
        foreach (RaycastHit2D hit in castHits)
        {
            if (hit.collider == null || hit.collider.attachedRigidbody == mover.Body ||
                hit.collider.transform.IsChildOf(transform) || (target != null && hit.collider.transform.IsChildOf(target)))
                continue;
            if (hit.distance > 0.025f)
                return false;
        }
        return true;
    }

    private Vector2 Center => BodyCollider != null ? (Vector2)BodyCollider.bounds.center : (Vector2)transform.position;

    private float AimScore(Vector2 point)
    {
        Vector2 aim = input.AimWorld - Center;
        if (aim.sqrMagnitude < 0.001f)
            aim = Vector2.right * input.Facing;
        Vector2 offset = point - Center;
        float along = Vector2.Dot(offset, aim.normalized);
        if (along <= 0f || along > pullRange)
            return float.PositiveInfinity;
        float sideways = Mathf.Abs(offset.x * aim.normalized.y - offset.y * aim.normalized.x);
        return sideways > 0.9f ? float.PositiveInfinity : sideways * 3f + Vector2.Distance(point, input.AimWorld) * 0.1f;
    }

    private PcsPlayerAbilities FindAimedPlayer()
    {
        PcsPlayerAbilities best = null;
        float score = float.PositiveInfinity;
        foreach (PcsPlayerAbilities candidate in players)
        {
            if (candidate == null || candidate == this || candidate.mover == null || !CanParticipate(candidate.Object) ||
                candidate.mover.Body.mass > mover.Body.mass || Vector2.Distance(Center, candidate.Center) > pullRange)
                continue;
            float value = AimScore(candidate.Center);
            if (value < score && HasClearLine(Center, candidate.Center, candidate.transform))
            {
                score = value;
                best = candidate;
            }
        }
        return best;
    }

    private PcsPuzzleDevice FindDevice(bool remote, PcsDeviceKind? required = null)
    {
        if (devices == null)
            return null;
        PcsPuzzleDevice best = null;
        float score = float.PositiveInfinity;
        foreach (PcsPuzzleDevice device in devices)
        {
            if (device == null || device.IsPassiveShaftObject || !device.isActiveAndEnabled || !device.Available ||
                (PcsPuzzleDirector.Instance != null && device.Section >= 0 && device.Section != PcsPuzzleDirector.Instance.ActiveSection) ||
                (required.HasValue && device.Kind != required.Value) ||
                (remote && !device.IsRemoteTarget) || (!remote && !required.HasValue && !IsInteractive(device.Kind)))
                continue;
            if (device.RequiredRole != MyEnum.CharacterType.None && device.RequiredRole != characterType)
                continue;
            float distance = Vector2.Distance(Center, device.InteractionPoint);
            if (distance > (remote ? pullRange : Mathf.Min(interactionRange, device.InteractionRange)))
                continue;
            float value = remote ? AimScore(device.InteractionPoint) : distance;
            if (value < score)
            {
                score = value;
                best = device;
            }
        }
        return best;
    }

    private static bool IsInteractive(PcsDeviceKind kind)
    {
        return kind == PcsDeviceKind.HackConsole || kind == PcsDeviceKind.Lever || kind == PcsDeviceKind.RemoteButton;
    }

    private PcsPuzzleDevice FindLadder()
    {
        if (devices == null || BodyCollider == null)
            return null;
        foreach (PcsPuzzleDevice device in devices)
            if (device != null && !device.IsPassiveShaftObject && device.CanClimb && device.gameObject.activeInHierarchy &&
                (PcsPuzzleDirector.Instance == null || device.Section < 0 || device.Section == PcsPuzzleDirector.Instance.ActiveSection) &&
                device.Trigger.enabled && device.Trigger.bounds.Intersects(BodyCollider.bounds) &&
                (device.RequiredRole == MyEnum.CharacterType.None || device.RequiredRole == characterType))
                return device;
        return null;
    }

    public void ClearTransientMotion()
    {
        ReleaseRemoteHold();
        pulling = false;
        pullSource = null;
        pullingToActor = false;
        ladder = null;
        nextAbilityTime = 0f;
    }

    private void OnDisable()
    {
        players.Remove(this);
        if (cameraFollow != null)
            cameraFollow.Unbind(transform);
        ClearTransientMotion();
    }
}

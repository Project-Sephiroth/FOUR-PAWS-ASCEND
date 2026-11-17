using Fusion;
using UnityEngine;

public sealed class RollingDrum : NetworkBehaviour, IInteractable
{
    [SerializeField] private Transform rollingBody;
    [SerializeField] private Collider2D hitbox;
    [SerializeField] private SpriteRenderer sprite;
    [SerializeField, Min(0.1f)] private float rollRadius = 0.5f;
    [SerializeField] private AnimationClip rollMotion;

    [Networked] private NetworkObject Encounter { get; set; }
    [Networked] private float TravelLimit { get; set; }
    [Networked] private int LaunchSequence { get; set; }
    [Networked] private float Distance { get; set; }
    [Networked] private float Speed { get; set; }
    [Networked] private TickTimer Blink { get; set; }
    [Networked] private Tongue Holder { get; set; }

    private bool IsRolling => Speed > 0f;
    internal bool IsAvailable => !IsRolling && !Blink.IsRunning;
    internal bool IsActiveTarget => Object != null && Object.IsValid && IsRolling;
    internal bool CanBeHeld => IsActiveTarget && Holder == null;
    internal Vector2 TongueGripPosition => rollingBody.position;

    public void OnInteract(Interactor interactor)
    {
        if (interactor is Tongue tongue && CanBeHeld)
            tongue.TryTongue(this);
    }

    public void SetOutlineMat(bool isOn) => InteractableOutline.SetTarget(rollingBody.gameObject, isOn);

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    internal void RPC_RequestHold(Tongue tongue, RpcInfo info = default)
    {
        if (tongue == null || (!Runner.IsSinglePlayer && info.Source != tongue.Object.StateAuthority))
            return;

        bool accepted = CanBeHeld;
        if (accepted)
            Holder = tongue;
        tongue.RPC_HoldResult(this, accepted);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    internal void RPC_RequestRelease(Tongue tongue, RpcInfo info = default)
    {
        if (tongue != null && Holder == tongue &&
            (Runner.IsSinglePlayer || info.Source == tongue.Object.StateAuthority))
            Holder = null;
    }

    public override void Spawned() => ApplyVisibility();

    internal void Launch(NetworkObject encounter, float speed, float travelLimit)
    {
        // 네트워크 루트는 출발 지점에 고정합니다. 그 아래의 본체만 이동 거리에 맞춰 움직입니다.
        Encounter = encounter;
        TravelLimit = travelLimit;
        LaunchSequence++;
        Distance = 0f;
        ApplyDistance(0f);
        Speed = speed;
        ApplyVisibility();
    }

    internal void Deactivate()
    {
        if (IsAvailable)
            return;

        Speed = 0f;
        Holder = null;
        Blink = default;
        ApplyVisibility();
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority)
            return;

        if (Blink.Expired(Runner))
        {
            Blink = default;
            ApplyVisibility();
        }

        if (!IsRolling)
            return;

        if (Holder == null)
        {
            Distance = Mathf.Min(TravelLimit, Distance + Speed * Runner.DeltaTime);
            ApplyDistance(Distance);
        }
        if (Distance >= TravelLimit || (Encounter != null &&
            Encounter.TryGetComponent(out BranchPuzzle puzzle) && puzzle.CrusherHits(rollingBody.position)))
            BeginRemoval();
    }

    public void Remove()
    {
        if (Object == null || !Object.IsValid)
            return;
        if (HasStateAuthority)
            BeginRemoval();
        else
            RPC_Remove();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_Remove() => BeginRemoval();

    private void BeginRemoval()
    {
        if (!IsRolling)
            return;

        Speed = 0f;
        Holder = null;
        Blink = TickTimer.CreateFromSeconds(Runner, 0.7f);
        ApplyVisibility();
    }

    public override void Render()
    {
        float renderedDistance = Distance;
        if (!HasStateAuthority && TryGetSnapshotsBuffers(out var from, out var to, out float alpha))
        {
            // 같은 출발에 속한 위치끼리만 보간합니다. 슬롯을 재사용할 때 이전 위치는 건너뜁니다.
            var sequence = GetPropertyReader<int>(nameof(LaunchSequence));
            if (sequence.Read(from) == LaunchSequence && sequence.Read(to) == LaunchSequence)
            {
                var distance = GetPropertyReader<float>(nameof(Distance));
                renderedDistance = Mathf.Lerp(distance.Read(from), distance.Read(to), alpha);
            }
        }

        ApplyDistance(renderedDistance);
        ApplyVisibility();
        if (rollMotion != null)
        {
            float circumference = 2f * Mathf.PI * rollRadius;
            float phase = Mathf.Repeat(renderedDistance, circumference) / circumference;
            rollMotion.SampleAnimation(sprite.gameObject, phase * rollMotion.length);
        }
    }

    private void ApplyDistance(float distance) => rollingBody.position = transform.position + Vector3.left * distance;

    private void ApplyVisibility()
    {
        hitbox.enabled = IsRolling;
        sprite.enabled = IsRolling || (Blink.IsRunning &&
            Mathf.FloorToInt((Blink.RemainingTime(Runner) ?? 0f) * 14f) % 2 == 0);
    }

    internal void HandleContact(Collider2D other)
    {
        if (Object == null || !Object.IsValid || !HasStateAuthority || !IsRolling)
            return;

        if (other.TryGetComponent(out DrumRemovalZone zone))
        {
            if (!zone.OneUse || (Encounter != null && Encounter.TryGetComponent(out BranchPuzzle puzzle) &&
                                 puzzle.TryUseDrumBarrier()))
                BeginRemoval();
        }
        else if (Holder == null && !other.isTrigger && other.GetComponentInParent<PlayerHealth>() is { } health &&
                 health.Object != null && health.Object.IsValid && health.Health > 0)
        {
            health.RPC_TakeDrumHit(this);
            BeginRemoval();
        }
    }
}

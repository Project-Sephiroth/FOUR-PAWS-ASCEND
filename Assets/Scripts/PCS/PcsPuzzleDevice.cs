using UnityEngine;

public enum PcsDeviceKind
{
    HackConsole, ChannelConsole, Lever, RemoteButton, PressurePlate, SlidingWall,
    Elevator, TimedPlatform, BarrierControl, Arrival, Exit, TutorialExit, Dummy,
    Anchor, Ladder, Enemy, Battery, Checkpoint, KillZone, Instruction
}

public enum PcsPuzzleAction { Interact, HackStep, ActivateChannel, Refill, Remote, Throw, Carry, Reset }
public enum PcsLiftPolicy { Binary, StageOneThreeStop, MainContinuous }

/// <summary>Scene references and presentation; the director owns puzzle state.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-200)]
public class PcsPuzzleDevice : MonoBehaviour
{
    public int DeviceId = -1;
    public PcsDeviceKind Kind;
    [Tooltip("0 tutorial, 1 first puzzle, 2 shaft; -1 is always available.")]
    public int Section;
    public MyEnum.CharacterType RequiredRole;
    [Range(0, 3)] public int Channel;
    public PcsPuzzleDevice[] Links = new PcsPuzzleDevice[0];
    public Collider2D Trigger;
    public Collider2D Solid;
    public SpriteRenderer[] Visuals = new SpriteRenderer[0];
    public Transform InteractionTransform;
    public Transform LowerStop;
    public Transform MiddleStop;
    public Transform UpperStop;
    public PcsLiftPolicy LiftPolicy;
    public PcsElevator Elevator;
    public PcsSlidingWall SlidingWall;
    public PcsPressureButton PressureButton;
    public Rigidbody2D Body;
    [Min(0.01f)] public float Speed = 1f;
    [Min(0.1f)] public float Duration = 3f;
    [Min(0.1f)] public float InteractionRange = 1.6f;
    [Min(1)] public int RequiredInputs = 3;
    [Min(0)] public int RequiredDummyCount;
    [Min(1)] public int Health = 1;
    [Min(1)] public int BatteryValue = 2;
    public bool InitiallyActive;
    public bool RequireAllRoles;
    public int RequiredRolesMask;
    public bool RequireAllLinks;
    public bool AcceptDummyOnly;
    public bool DisableColliderWhenOpen;
    public bool EntryOpenBeforeShaftStart;
    public bool BlocksShaft;
    public bool OpensWhenActive = true;
    [Tooltip("Only the first puzzle ceiling lever uses owner-held remote input.")]
    public bool RequiresRemoteHold;
    public bool LatchOnActivate;
    [Tooltip("UpperStop is stowed; LowerStop is deployed. Other ladders remain static.")]
    public bool DeployableLadder;
    [Tooltip("Tutorial door retracts within its own frame. Stops must be outside the moving door.")]
    public bool RetractWithinFrame;
    [TextArea] public string Instruction;

    public bool IsShaftElevator => Section == 2 && Kind == PcsDeviceKind.Elevator && LiftPolicy == PcsLiftPolicy.MainContinuous;
    public bool IsPassiveShaftObject => Section == 2 && !IsShaftElevator;
    public Vector2 InteractionPoint => InteractionTransform != null ? (Vector2)InteractionTransform.position : (Vector2)transform.position;
    public bool IsRemoteTarget => !IsPassiveShaftObject && (Kind == PcsDeviceKind.RemoteButton || Kind == PcsDeviceKind.Lever ||
        Kind == PcsDeviceKind.Dummy || Kind == PcsDeviceKind.Battery || Kind == PcsDeviceKind.Enemy || Kind == PcsDeviceKind.Anchor);
    public bool IsLadder => Kind == PcsDeviceKind.Ladder;
    public bool CanClimb => !IsPassiveShaftObject && IsLadder && isActiveAndEnabled && Available && Trigger != null && Trigger.enabled &&
        (!DeployableLadder || (Active && LowerStop != null &&
         Vector2.Distance(transform.position, LowerStop.position) <= 0.025f));
    public Vector2 InitialPosition { get; private set; }
    public Vector2 BodySize { get; private set; }
    public bool Active { get; private set; }
    public bool Available { get; private set; } = true;
    public Bounds Bounds => Trigger != null ? Trigger.bounds : Solid != null ? Solid.bounds : new Bounds(transform.position, Vector3.one);

    private Color[] originalColors;
    private Vector3[] originalScales;
    private Vector3 originalRootScale;
    private bool retractFrameValid;
    private Collider2D[] passiveColliders;

    private void Awake()
    {
        InitialPosition = transform.position;
        originalRootScale = transform.localScale;
        BodySize = Solid != null ? (Vector2)Solid.bounds.size : Vector2.one * 0.5f;
        if (Body == null) Body = GetComponent<Rigidbody2D>();
        if (Body != null && Kind == PcsDeviceKind.Dummy) Body.useFullKinematicContacts = true;
        if (Elevator == null) Elevator = GetComponent<PcsElevator>();
        if (SlidingWall == null) SlidingWall = GetComponent<PcsSlidingWall>();
        if (PressureButton == null) PressureButton = GetComponent<PcsPressureButton>();
        if (Elevator != null) Elevator.UseExternalDrive();
        if (SlidingWall != null) SlidingWall.UseExternalDrive();
        if (PressureButton != null) PressureButton.UseExternalDrive();
        if (IsPassiveShaftObject)
        {
            passiveColliders = GetComponents<Collider2D>();
            PresentPassiveShaftObject();
            if (Elevator != null) Elevator.enabled = false;
            if (SlidingWall != null) SlidingWall.enabled = false;
            if (PressureButton != null) PressureButton.enabled = false;
            return;
        }
        if (IsLadder && DeployableLadder && Trigger != null) Trigger.enabled = false;
        if (RetractWithinFrame)
        {
            retractFrameValid = Kind == PcsDeviceKind.SlidingWall && Solid != null && LowerStop != null && UpperStop != null &&
                !LowerStop.IsChildOf(transform) && !UpperStop.IsChildOf(transform) &&
                float.IsFinite(UpperStop.position.y) && float.IsFinite(LowerStop.position.y) &&
                UpperStop.position.y > LowerStop.position.y + 0.01f &&
                Mathf.Abs(UpperStop.position.x - LowerStop.position.x) < 0.001f &&
                Vector3.Dot(transform.up, Vector3.up) > 0.999f && originalRootScale.y > 0f &&
                Mathf.Abs((UpperStop.position.y - LowerStop.position.y) - (Solid.bounds.max.y - transform.position.y)) < 0.025f;
            if (!retractFrameValid)
                Debug.LogError("수납형 문: 독립된 상하 정지점과 문 상단 높이, 수직 양수 Scale을 확인하세요.", this);
        }
        originalColors = new Color[Visuals.Length];
        originalScales = new Vector3[Visuals.Length];
        for (int i = 0; i < Visuals.Length; i++)
            if (Visuals[i] != null)
            {
                originalColors[i] = Visuals[i].color;
                originalScales[i] = Visuals[i].transform.localScale;
            }
    }

    public void ApplyPose(Vector2 position)
    {
        if (IsPassiveShaftObject) return;
        if (Elevator != null) Elevator.ApplyNetworkPose(position);
        else if (SlidingWall != null) SlidingWall.ApplyNetworkPose(position);
        else if (Body != null) Body.position = position;
        else transform.position = new Vector3(position.x, position.y, transform.position.z);
    }

    public Vector2 MoveAuthority(Vector2 from, Vector2 target, float deltaTime)
    {
        if (IsPassiveShaftObject) return transform.position;
        if (Elevator != null) return Elevator.MoveAuthority(from, target, Speed, deltaTime);
        if (SlidingWall != null) return SlidingWall.MoveAuthority(from, target, Speed, deltaTime);
        Vector2 next = Vector2.MoveTowards(from, target, Speed * deltaTime);
        if (Body != null) Body.MovePosition(next);
        else ApplyPose(next);
        return next;
    }

    public void Present(bool active, bool hidden = false)
    {
        if (IsPassiveShaftObject)
        {
            PresentPassiveShaftObject();
            return;
        }
        if (IsLadder && DeployableLadder && !active) hidden = true;
        Active = active;
        Available = !hidden;
        if (IsLadder && DeployableLadder && Trigger != null)
            Trigger.enabled = !hidden && active && LowerStop != null &&
                Vector2.Distance(transform.position, LowerStop.position) <= 0.025f;
        if (Kind == PcsDeviceKind.TimedPlatform && Solid != null) Solid.enabled = active;
        if (Kind == PcsDeviceKind.SlidingWall && DisableColliderWhenOpen && Solid != null)
            Solid.enabled = !active || UpperStop == null || Vector2.Distance(transform.position, UpperStop.position) > 0.04f;
        if (RetractWithinFrame && retractFrameValid)
        {
            float progress = Mathf.Clamp01((transform.position.y - LowerStop.position.y) /
                (UpperStop.position.y - LowerStop.position.y));
            Vector3 scale = originalRootScale;
            scale.y *= Mathf.Max(0.005f, 1f - progress);
            transform.localScale = scale;
            Solid.enabled = progress < 0.999f;
        }
        if ((Kind == PcsDeviceKind.Dummy || Kind == PcsDeviceKind.Enemy || Kind == PcsDeviceKind.Battery) && Solid != null)
            Solid.enabled = !hidden;
        for (int i = 0; i < Visuals.Length; i++)
        {
            if (Visuals[i] == null) continue;
            Visuals[i].enabled = !hidden;
            Color tint = originalColors[i];
            if (Kind == PcsDeviceKind.TimedPlatform)
            {
                // Fold only the artwork child, never the root carrying the collision surface.
                if (Visuals[i].transform != transform)
                {
                    Vector3 scale = originalScales[i];
                    if (!active) scale.x *= 0.12f;
                    Visuals[i].transform.localScale = scale;
                }
                if (!active) tint.a *= 0.35f;
            }
            else if (Kind == PcsDeviceKind.RemoteButton)
                tint = active ? Color.Lerp(tint, Color.white, 0.5f) : originalColors[i];
            else if (Kind == PcsDeviceKind.HackConsole ||
                Kind == PcsDeviceKind.Lever || Kind == PcsDeviceKind.BarrierControl || Kind == PcsDeviceKind.Arrival)
                tint = active ? Color.Lerp(tint, Color.green, 0.65f) : originalColors[i];
            Visuals[i].color = tint;
        }
    }

    private void PresentPassiveShaftObject()
    {
        Active = false;
        Available = false;
        bool keepSurface = Kind == PcsDeviceKind.TimedPlatform && Solid != null && !Solid.isTrigger;
        if (Trigger != null && (!keepSurface || Trigger != Solid)) Trigger.enabled = false;
        if (Solid != null) Solid.enabled = keepSurface;
        // Only this device's colliders are retired; child environment geometry remains intact.
        if (passiveColliders != null)
            foreach (Collider2D collider in passiveColliders)
                if (collider != null) collider.enabled = keepSurface && collider == Solid;
        if (Body != null)
        {
            if (Body.bodyType != RigidbodyType2D.Kinematic) Body.bodyType = RigidbodyType2D.Kinematic;
            Body.gravityScale = 0f;
            Body.linearVelocity = Vector2.zero;
            Body.angularVelocity = 0f;
        }
    }
}

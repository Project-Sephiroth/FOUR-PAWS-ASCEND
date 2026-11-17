using Fusion;
using UnityEngine;

public class Tongue : Interactor
{
    [SerializeField] private Animator anim;
    [Header("혀 설정")]
    [SerializeField] private Transform tonguePivot;
    [SerializeField] private LineRenderer line;
    [SerializeField] private float tongueSpeed = 15f;
    [SerializeField, Min(0.01f)] private float lengthPerScroll = 0.25f;
    [SerializeField, Min(0.01f)] private float minHoldLength = 0.5f;
    [SerializeField, Min(0.01f)] private float maxHoldLength = 7f;
    private float pendingScroll;
    private Vector2 scrollAimPosition;
    [SerializeField] private Mover mover;
    private Passenger passenger;

    public enum TongueState
    {
        Idle, Extending, WaitingForHold, Holding, Retracting
    }

    [Networked] public TongueState State { get; private set; }
    [Networked] public Vector2 EndPos { get; private set; }
    [Networked] public Vector2 HoldOffset { get; private set; }
    [Networked] public float TongueProgress { get; private set; }
    [Networked] private float PhaseDuration { get; set; }
    [Networked] private NetworkBehaviour AttachedTarget { get; set; }
    private Vector2 TargetGrip => AttachedTarget switch
    {
        MovingObject item => item.TongueGripPosition,
        RollingDrum drum => drum.TongueGripPosition,
        _ => EndPos
    };
    private bool TargetUnavailable => AttachedTarget switch
    {
        MovingObject item => item.TongueTargetUnavailable,
        RollingDrum drum => !drum.IsActiveTarget,
        _ => true
    };
    public bool IsTongueActive => State != TongueState.Idle;
    private bool IsCarried => passenger != null && passenger.IsCarried;
    protected override bool CanShowInteractionOutline => State == TongueState.Idle && !IsCarried;

    private Switch targetSwitch;
    private Vector3 mouthOffset;

    private Vector3 MouthPosition
    {
        get
        {
            Vector3 offset = mouthOffset;
            if (mover != null && mover.FacesLeftNow)
                offset.x = -offset.x;

            return tonguePivot.parent.TransformPoint(offset);
        }
    }

    private void Awake()
    {
        passenger = GetComponentInParent<Passenger>();
        if (line == null)
            line = GetComponent<LineRenderer>();
        if (line != null)
        {
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.enabled = false;
        }

        if (tonguePivot != null)
            mouthOffset = tonguePivot.localPosition;

        interactWithRange = false;
    }

    public override void Update()
    {
        base.Update();
        if (Object == null || !Object.IsValid || !HasStateAuthority || !CanUseWorldInput ||
            State != TongueState.Holding || AttachedTarget is not MovingObject)
            return;

        float scroll = Input.mouseScrollDelta.y;
        Vector3 cursor = Input.mousePosition;
        if (scroll != 0f && Camera.main is Camera camera && camera.pixelRect.Contains(cursor))
        {
            scrollAimPosition = camera.ScreenToWorldPoint(cursor);
            pendingScroll += scroll;
        }
    }

    protected override void TryIntetact()
    {
        if (IsCarried)
            return;

        if (State == TongueState.Holding)
        {
            if (AttachedTarget == null || !AttachedTarget.TryGetComponent<Passenger>(out _))
                ReleaseTarget();
            return;
        }

        if (State != TongueState.Idle || curClick == null || tonguePivot == null)
            return;

        if (curClick.TryGetComponent(out Switch button))
        {
            targetSwitch = button;
            BeginExtension(button.transform.position);
        }
        else if (curClick.TryGetComponent(out Key key))
        {
            key.OnInteract(this);
        }
        else if (curClick.TryGetComponent(out MovingObject item))
        {
            item.OnInteract(this);
        }
        else if (curClick.TryGetComponent(out RollingDrum drum))
        {
            drum.OnInteract(this);
        }
    }

    public void TryTongue(MovingObject item)
    {
        if (item != null && item.CanBeTongueHeld)
            AimAt(item);
    }

    internal void TryTongue(RollingDrum drum)
    {
        if (drum != null && drum.CanBeHeld)
            AimAt(drum);
    }

    private void AimAt(NetworkBehaviour target)
    {
        if (!HasStateAuthority || State != TongueState.Idle || IsCarried || target.Object == Object || tonguePivot == null)
            return;

        AttachedTarget = target;
        BeginExtension(TargetGrip);
    }

    private void BeginExtension(Vector2 target)
    {
        if (mover != null)
            mover.FaceWorldX(mousePos.x);

        EndPos = target;
        SetPhase(TongueState.Extending, Vector2.Distance(MouthPosition, target));
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority)
            return;

        if (IsCarried && (State == TongueState.Extending || State == TongueState.WaitingForHold || State == TongueState.Holding))
            ReleaseTarget();

        switch (State)
        {
            case TongueState.Extending:
                if (AttachedTarget != null)
                    EndPos = TargetGrip;

                if (AdvancePhase())
                    ReachTarget();
                break;
            case TongueState.WaitingForHold:
            case TongueState.Holding:
                if (TargetUnavailable)
                {
                    AttachedTarget = null;
                    BeginRetraction();
                }
                else if (State == TongueState.Holding)
                    AdjustHeldLength();
                break;
            case TongueState.Retracting:
                if (AdvancePhase())
                    FinishTongue();
                break;
        }
    }

    private void ReleaseTarget()
    {
        var target = AttachedTarget;
        bool requestedHold = State == TongueState.WaitingForHold || State == TongueState.Holding;
        BeginRetraction();
        AttachedTarget = null;
        targetSwitch = null;
        if (!requestedHold)
            return;

        if (target is MovingObject item)
            item.RPC_RequestRelease(this, EndPos);
        else if (target is RollingDrum drum)
            drum.RPC_RequestRelease(this);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_AllyReleased(MovingObject target, RpcInfo info = default)
    {
        if (target == null || AttachedTarget != target ||
            (!Runner.IsSinglePlayer && info.Source != target.Object.StateAuthority))
            return;

        BeginRetraction();
        AttachedTarget = null;
    }

    private void ReachTarget()
    {
        if (targetSwitch != null)
        {
            targetSwitch.OnInteract(this);
            if (targetSwitch.TryGetComponent(out BranchControl control))
                control.OperateByTongue();
            targetSwitch = null;
        }

        if (AttachedTarget == null)
        {
            BeginRetraction();
            return;
        }

        State = TongueState.WaitingForHold;
        HoldOffset = TargetGrip - (Vector2)MouthPosition;
        if (AttachedTarget is MovingObject item)
            item.RPC_RequestHold(this);
        else if (AttachedTarget is RollingDrum drum)
            drum.RPC_RequestHold(this);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_HoldResult(NetworkBehaviour target, bool accepted, RpcInfo info = default)
    {
        if (State != TongueState.WaitingForHold || target == null || AttachedTarget != target ||
            (!Runner.IsSinglePlayer && info.Source != target.Object.StateAuthority))
            return;

        if (!accepted)
        {
            AttachedTarget = null;
            BeginRetraction();
            return;
        }

        State = TongueState.Holding;
    }

    /// <summary>
    /// 마우스 휠에 따라 혀 집어넣거나 내밀기
    /// </summary>
    private void AdjustHeldLength()
    {
        if (pendingScroll == 0f)
            return;

        if (mover != null)
            mover.FaceWorldX(scrollAimPosition.x);

        Vector2 direction = scrollAimPosition - (Vector2)MouthPosition;
        if (direction.sqrMagnitude < 0.0001f)
            direction = HoldOffset.sqrMagnitude > 0.0001f ? HoldOffset :
                (mover != null && mover.FacesLeftNow ? Vector2.left : Vector2.right);

        float length = Mathf.Clamp(HoldOffset.magnitude + pendingScroll * lengthPerScroll,
            minHoldLength, Mathf.Max(minHoldLength, maxHoldLength));
        HoldOffset = direction.normalized * length;
        pendingScroll = 0f;
    }


    private void BeginRetraction()
    {
        pendingScroll = 0f;
        EndPos = GetTongueTip();
        SetPhase(TongueState.Retracting, Vector2.Distance(EndPos, MouthPosition));
    }

    private void SetPhase(TongueState state, float distance)
    {
        State = state;
        TongueProgress = 0f;
        PhaseDuration = Mathf.Max(distance / Mathf.Max(tongueSpeed, 0.01f), Runner.DeltaTime);
    }

    private bool AdvancePhase()
    {
        TongueProgress = Mathf.Clamp01(TongueProgress + Runner.DeltaTime / PhaseDuration);
        return TongueProgress >= 1f;
    }

    internal Vector2 GetFollowPosition() => (Vector2)MouthPosition + HoldOffset;

    public Vector2 GetTongueTip()
    {
        Vector2 start = MouthPosition;
        switch (State)
        {
            case TongueState.Extending:
                return Vector2.Lerp(start, EndPos, TongueProgress);
            case TongueState.Holding:
                return TargetGrip;
            case TongueState.WaitingForHold:
                return EndPos;
            case TongueState.Retracting:
                return Vector2.Lerp(EndPos, start, TongueProgress);
            default:
                return start;
        }
    }

    public override void Render()
    {
        if (anim != null)
            anim.SetBool("IsTongue", IsTongueActive);

        if (line == null || tonguePivot == null)
            return;

        line.enabled = IsTongueActive;
        if (IsTongueActive)
        {
            Vector3 mouth = MouthPosition;
            Vector2 tip = GetTongueTip();
            line.SetPosition(0, mouth);
            line.SetPosition(1, new Vector3(tip.x, tip.y, mouth.z));
        }
    }

    private void FinishTongue()
    {

        AttachedTarget = null;
        State = TongueState.Idle;
    }
}

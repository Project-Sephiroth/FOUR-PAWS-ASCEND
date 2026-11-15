using Fusion;
using UnityEngine;

public class Tongue : Interactor
{
    [SerializeField] private Animator anim;
    [Header("혀 설정")]
    [SerializeField] private Transform tonguePivot;
    [SerializeField] private LineRenderer line;
    [SerializeField] private float tongueSpeed = 15f;
    [SerializeField] private Mover mover;

    public enum TongueState
    {
        Idle, Extending, WaitingForHold, Holding, Retracting
    }

    [Networked] public TongueState State { get; private set; }
    [Networked] public Vector2 EndPos { get; private set; }
    [Networked] public Vector2 HoldOffset { get; private set; }
    [Networked] public float TongueProgress { get; private set; }
    [Networked] private float PhaseDuration { get; set; }
    [Networked] private MovingObject AttachedTarget { get; set; }
    public bool IsTongueActive => State != TongueState.Idle;
    protected override bool CanShowInteractionOutline => State == TongueState.Idle;

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

    protected override void TryIntetact()
    {
        if (State == TongueState.Holding)
        {
            BeginRetraction();
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
        else if (curClick.TryGetComponent(out MovingObject movingObject))
        {
            movingObject.OnInteract(this);
        }
    }

    public void TryTongue(MovingObject movingObject)
    {
        if (!HasStateAuthority || State != TongueState.Idle || movingObject == null ||
            tonguePivot == null || movingObject.IsHeld || movingObject.IsMoving)
            return;

        AttachedTarget = movingObject;
        BeginExtension(movingObject.transform.position);
    }

    private void BeginExtension(Vector2 target)
    {
        EndPos = target;
        SetPhase(TongueState.Extending, Vector2.Distance(MouthPosition, target));
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority)
            return;

        switch (State)
        {
            case TongueState.Extending:
                if (AttachedTarget != null)
                    EndPos = AttachedTarget.transform.position;

                if (AdvancePhase())
                    ReachTarget();
                break;
            case TongueState.WaitingForHold:
            case TongueState.Holding:
                if (AttachedTarget == null || AttachedTarget.IsCrushed)
                {
                    AttachedTarget = null;
                    BeginRetraction();
                }
                break;
            case TongueState.Retracting:
                if (AdvancePhase())
                    FinishTongue();
                break;
        }
    }

    private void ReachTarget()
    {
        if (targetSwitch != null)
        {
            targetSwitch.OnInteract(this);
            targetSwitch = null;
        }

        if (AttachedTarget == null)
        {
            BeginRetraction();
            return;
        }

        State = TongueState.WaitingForHold;
        HoldOffset = (Vector2)AttachedTarget.transform.position - (Vector2)MouthPosition;
        AttachedTarget.RPC_RequestHold(this);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_HoldResult(MovingObject target, bool accepted, RpcInfo info = default)
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

    private Vector2 DropPosition
    {
        get
        {
            if (holdPoint == null)
                return MouthPosition;

            Vector3 offset = holdPoint.localPosition;
            if (mover != null && mover.FacesLeftNow)
                offset.x = -offset.x;

            return holdPoint.parent.TransformPoint(offset);
        }
    }

    private void BeginRetraction()
    {
        EndPos = GetTongueTip();
        Vector2 destination = AttachedTarget != null ? DropPosition : (Vector2)MouthPosition;
        SetPhase(TongueState.Retracting, Vector2.Distance(EndPos, destination));
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

    public Vector2 GetFollowPosition()
    {
        if (State == TongueState.Retracting)
            return Vector2.Lerp(EndPos, DropPosition, TongueProgress);

        return (Vector2)MouthPosition + HoldOffset;
    }

    public Vector2 GetTongueTip()
    {
        Vector2 start = MouthPosition;
        switch (State)
        {
            case TongueState.Extending:
                return Vector2.Lerp(start, EndPos, TongueProgress);
            case TongueState.Holding:
                return AttachedTarget != null ? (Vector2)AttachedTarget.transform.position : EndPos;
            case TongueState.WaitingForHold:
                return EndPos;
            case TongueState.Retracting:
                return AttachedTarget != null && !AttachedTarget.IsCrushed ? (Vector2)AttachedTarget.transform.position : Vector2.Lerp(EndPos, start, TongueProgress);
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
        if (AttachedTarget != null)
            AttachedTarget.RPC_RequestRelease(this, DropPosition);

        AttachedTarget = null;
        State = TongueState.Idle;
    }
}

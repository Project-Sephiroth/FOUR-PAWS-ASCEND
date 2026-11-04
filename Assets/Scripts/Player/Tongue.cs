using Fusion;
using UnityEngine;

public class Tongue : Interactor
{
    [SerializeField] private Animator anim;

    [Header("혀 설정")]
    [SerializeField] private Transform tonguePivot;
    [SerializeField] private LineRenderer line;
    [SerializeField] private float tongueSpeed = 15f;
    [SerializeField] private LayerMask tongueDetectLayer;

    public enum TongueState
    {
        Idle,
        Extending,
        Holding,
        Retracting
    }

    [Networked] public TongueState State { get; set; }
    [Networked] public Vector2 EndPos { get; set; }
    [Networked] public Vector2 HoldOffset { get; set; }
    [Networked] public float TongueProgress { get; set; }

    [Networked] private float PhaseDuration { get; set; }
    [Networked] private MovingObject AttachedTarget { get; set; }
    [Networked] public NetworkBool IsTongueActive { get; set; }

    private void Awake()
    {
        line = GetComponent<LineRenderer>();
        line.positionCount = 2;
        line.useWorldSpace = true;
        line.enabled = false;

        interactWithRange = false;
    }

    public override void Update()
    {
        if (Object == null || !Object.IsValid || !HasStateAuthority)
            return;

        if (!Input.GetMouseButtonDown(0))
            return;

        //물체를 잡고 있다면 클릭으로 회수
        if (State == TongueState.Holding)
        {
            RPC_Retract();
            return;
        }

        //혀가 움직이는 동안 재발사 방지
        if (State != TongueState.Idle)
            return;

        if (Camera.main == null)
            return;

        Vector2 mousePosition = Camera.main.ScreenToWorldPoint(Input.mousePosition);

        Collider2D hit = Physics2D.OverlapPoint(mousePosition, tongueDetectLayer);

        if (hit == null)
            return;

        IInteractable interactable = hit.GetComponent<IInteractable>();

        if (interactable == null)
            return;

        MovingObject movingObject = hit.GetComponent<MovingObject>();

        if (movingObject != null)
        {
            if (movingObject.IsMoving || movingObject.IsHeld)
                return;

            AttachedTarget = movingObject;
        }
        else 
            interactable.OnInteract(this);

        RPC_FireTongue(hit.gameObject.transform.position);
    }


    public override void FixedUpdateNetwork()
    {
        base.FixedUpdateNetwork();

        if (!HasStateAuthority)
            return;

        switch (State)
        {
            case TongueState.Extending:
                UpdateExtending();
                break;
            case TongueState.Holding:
                if (AttachedTarget == null)
                    RPC_Retract();
                break;
            case TongueState.Retracting:
                UpdateRetracting();
                break;
        }
    }


    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_FireTongue(Vector2 targetPos)
    {
        if (State != TongueState.Idle)
            return;

        EndPos = targetPos;

        HoldOffset = Vector2.zero;
        TongueProgress = 0f;

        PhaseDuration = Mathf.Max(Vector2.Distance(tonguePivot.position, EndPos) / Mathf.Max(tongueSpeed, 0.01f), Runner.DeltaTime);

        IsTongueActive = true;
        State = TongueState.Extending;
    }

    /// <summary>
    /// 상호작용 할 물체에 혀 갖다대기
    /// </summary>
    private void UpdateExtending()
    {
        TongueProgress += Runner.DeltaTime / PhaseDuration;
        Debug.Log(TongueProgress);

        if (TongueProgress < 1f)
            return;

        TongueProgress = 1f;

        if (AttachedTarget == null)
        {
            RPC_Retract();
            return;
        }

        if (AttachedTarget.IsMoving || AttachedTarget.IsHeld)
        {
            AttachedTarget = null;
            RPC_Retract();
            return;
        }

        HoldOffset = (Vector2)AttachedTarget.transform.position - (Vector2)tonguePivot.position;
        State = TongueState.Holding;

        AttachedTarget.RPC_RequestHold(this);
        TryHold(AttachedTarget.GetComponent<NetworkObject>());
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_Retract()
    {
        BeginRetract();
    }

    /// <summary>
    /// 혀 수납에 들어갑니다
    /// </summary>
    private void BeginRetract()
    {
        TongueProgress = 0f;

        float distance = AttachedTarget != null ? HoldOffset.magnitude : Vector2.Distance(tonguePivot.position, EndPos);
        PhaseDuration = Mathf.Max(distance / Mathf.Max(tongueSpeed, 0.01f), Runner.DeltaTime);

        State = TongueState.Retracting;
    }

    private void UpdateRetracting()
    {
        TongueProgress += Runner.DeltaTime / PhaseDuration;

        if (TongueProgress >= 1f)
        {
            TongueProgress = 1f;
            FinishTongue();
        }
    }

    /// <summary>
    /// 물체가 따라갈 목표 위치
    /// </summary>
    public Vector2 GetFollowPosition()
    {
        Vector2 start = tonguePivot.position;

        switch (State)
        {
            case TongueState.Holding:
                return start + HoldOffset;
            case TongueState.Retracting:
                return start + HoldOffset * (1f - Mathf.Clamp01(TongueProgress));
            default:
                return start;
        }
    }

    /// <summary>
    /// 혀 끝의 월드 좌표
    /// </summary>
    public Vector2 GetTongueTip()
    {
        Vector2 start = tonguePivot.position;

        switch (State)
        {
            case TongueState.Extending:
                return Vector2.Lerp(start, EndPos, Mathf.Clamp01(TongueProgress));
            case TongueState.Holding:
                if (AttachedTarget != null)
                    return AttachedTarget.transform.position;
                return start;
            case TongueState.Retracting:
                return Vector2.Lerp(EndPos, start, Mathf.Clamp01(TongueProgress));
            default:
                return start;
        }
    }

    public override void Render()
    {
        bool active = IsTongueActive && State != TongueState.Idle;

        line.enabled = active;

        if (anim != null)
            anim.SetBool("IsTongue", active);

        if (!active)
            return;

        line.SetPosition(0, tonguePivot.position);
        line.SetPosition(1, GetTongueTip());

        //holdPoint 도 혀 끝을 따라감
        holdPoint.transform.position = GetTongueTip();
        
        //혀를 집어넣을 때 혀에 부착된 물체도 혀 끝을 따라갑니다
        if (State == TongueState.Retracting && AttachedTarget != null)
            AttachedTarget.transform.position = GetTongueTip();
    }

    private void FinishTongue()
    {
        if (AttachedTarget != null)
            AttachedTarget.RPC_RequestRelease();

        ReleaseHold();

        AttachedTarget = null;
        curInteratcble = null;

        HoldOffset = Vector2.zero;
        TongueProgress = 0f;
        PhaseDuration = 0f;

        IsTongueActive = false;
        State = TongueState.Idle;
    }

}

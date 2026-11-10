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

        interactWithClick = true;
        interactWithRange = false;
    }

    public override void FixedUpdateNetwork()
    {
        base.FixedUpdateNetwork();

        if (!HasStateAuthority)
            return;

        //혀로 무언가 잡고 있을 땐 움직이기 X
        if (AttachedTarget == null && mover.CanJump == true)
        {
            mover.CanMove = false;
            mover.CanJump = false;
        }
        else if (AttachedTarget == null && mover.CanJump == false)
        {
            mover.CanMove = true;
            mover.CanJump = true;
        }

        switch (State)
        {
            case TongueState.Extending:
                UpdateExtending();
                break;
            case TongueState.Holding:
                //마우스 클릭 시 잡고 있는 물체를 끌어당김
                if (Input.GetMouseButtonDown(0))
                    RPC_Retract();
                break;
            case TongueState.Retracting:
                UpdateRetracting();
                break;
        }
    }

    protected override void TryIntetact()
    {
        base.TryIntetact();

        //혀가 움직이는 동안 재발사 방지
        if (State != TongueState.Idle)
            return;

        if (curInteratcble != null)
        {
            //혀 발사
            RPC_FireTongue();
        }
    }

    public void TryTongue(MovingObject movingObject)
    {
        //상호작용 한 애가 null 이 아닐경우 뱉기
        if (AttachedTarget != null)
            RPC_Retract();

        //이미 상호작용 한 상태라면 return
        if (AttachedTarget == movingObject)
            return;

        AttachedTarget = movingObject;
        Debug.Log($"Tongue: attached target 을 {AttachedTarget.gameObject.name} 으로 설정");
    }


    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_FireTongue()
    {
        if (State != TongueState.Idle)
            return;

        EndPos = curClick.transform.position;

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

        if (TongueProgress < 1f)
            return;

        TongueProgress = 1f;

        if (AttachedTarget == null)
        {
            RPC_Retract();
            return;
        }

        HoldOffset = (Vector2)AttachedTarget.transform.position - (Vector2)tonguePivot.position;
        Debug.Log("Holding");

        State = TongueState.Holding;

        AttachedTarget.RPC_RequestHold(this);
        TryHold(AttachedTarget.GetComponent<NetworkObject>());
    }

    /// <summary>
    /// 혀 수납
    /// </summary>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_Retract()
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

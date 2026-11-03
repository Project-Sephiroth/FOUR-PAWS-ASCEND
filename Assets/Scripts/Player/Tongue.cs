
using Fusion;
using UnityEngine;
using static IMoveable;

public class Tongue : Interactor
{
    [Header("혀 설정")]
    [SerializeField] private Transform tonguePivot;
    [SerializeField] private LineRenderer line;
    [SerializeField] private float tongueSpeed = 15f;

    public MovingObject TongueTarget;

    [Networked] public Vector2 EndPos { get; set; }
    [Networked] private Vector2 StartPos { get; set; }
    [Networked] private float TongueProgress { get; set; }
    [Networked] private NetworkBool IsTongueActive { get; set; }

    private void Awake()
    {
        InitLineRenderer();

        //혀는 범위 감지로 작동하지 않음
        interactWithRange = false;
    }

    private void InitLineRenderer()
    {
        line = GetComponent<LineRenderer>();
        line.positionCount = 2;
        line.useWorldSpace = true;
        line.enabled = false;
    }

    /// <summary>
    /// 혀로 끌고 올 대상에게 끌고 오기 시도
    /// </summary>
    public void Pull(MovingObject movingObject)
    {
        if (!HasStateAuthority || movingObject == null)
            return;

        if (TongueTarget != null || movingObject.IsMoving)
            return;

        Vector2 destination = tonguePivot.position;

        TongueTarget = movingObject;

        movingObject.RPC_RequestMove(MoveType.Linear, destination);
    }

    protected override void GetMouseClickRay()
    {
        //혀가 이미 나와있으면 리턴
        if (IsTongueActive)
            return;

        base.GetMouseClickRay();

        //클릭 위치를 StateAuthority에 전달
        RPC_FireTongue(mousePos);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_FireTongue(Vector2 targetPos)
    {
        if (IsTongueActive)
            return;

        StartPos = tonguePivot.position;
        EndPos = targetPos;

        TongueProgress = 0f;
        IsTongueActive = true;

        TongueTarget = null;
    }

    public override void FixedUpdateNetwork()
    {
        base.FixedUpdateNetwork();

        //권한을 가진 클라이언트만 상태 변경
        if (!HasStateAuthority || !IsTongueActive)
            return;

        float distance = Vector2.Distance(StartPos, EndPos);
        float duration = Mathf.Max(distance / tongueSpeed, 0.01f);

        TongueProgress += Runner.DeltaTime / duration;

        if (TongueProgress >= 2f)
        {
            TongueProgress = 2f;
            IsTongueActive = false;
        }
    }

    public override void Render()
    {
        if (!IsTongueActive)
        {
            line.enabled = false;
            return;
        }

        line.enabled = true;

        Vector2 startPos = tonguePivot.position;
        Vector2 targetPos = EndPos;
        Vector2 tongueTip;

        if (TongueProgress <= 1f) //혀 내밀기
            tongueTip = Vector2.Lerp(startPos,targetPos,TongueProgress);
        else
            tongueTip = Vector2.Lerp(targetPos, startPos, TongueProgress - 1f);

        line.SetPosition(0, startPos);
        line.SetPosition(1, tongueTip);
    }
}

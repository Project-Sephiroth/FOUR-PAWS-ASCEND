using Fusion;
using UnityEngine;
using static IMoveable;

public class Tongue : Interactor
{
    [SerializeField] private Transform tonguePivot;
    [SerializeField] private LineRenderer line;

    public MovingObject TongueTarget;
    [Networked] public Vector2 EndPos { get; set; }
    float tongueSpeed = 3f;

    private void Awake()
    {
        InitLineRenderer();

        //혀는 범위 감지로 작동 X
        interactWithRange = false;
    }

    void InitLineRenderer()
    {
        line = GetComponent<LineRenderer>();
        line.positionCount = 2;
        line.useWorldSpace = true;
        line.enabled = false;
    }

    /// <summary>
    /// 혀로 끌고 올 대상에게 끌고 오기 시도
    /// </summary>
    /// <param name="movingObject"></param>
    public void Pull(MovingObject movingObject)
    {
        if (!HasStateAuthority || movingObject == null)
            return;

        if (TongueTarget != null || movingObject.IsMoving)
            return;

        //개구리 입 위치로 끌어오기
        Vector2 destination = tonguePivot.position;

        TongueTarget = movingObject;

        movingObject.RPC_RequestMove(MoveType.Linear, destination);
    }

    protected override void GetMouseClickRay()
    {
        base.GetMouseClickRay();

        //개구리는 마우스 클릭에 따라 매번 혀를 뱉습니다
        EndPos = mousePos;
        TongueTarget = null;
    }

    public override void Render()
    {
        line.enabled = true;
        EndPos = Vector2.Lerp(EndPos, tonguePivot.transform.position, tongueSpeed * Time.deltaTime);

        line.SetPosition(0, tonguePivot.transform.position);
        line.SetPosition(1, EndPos);
    }
}

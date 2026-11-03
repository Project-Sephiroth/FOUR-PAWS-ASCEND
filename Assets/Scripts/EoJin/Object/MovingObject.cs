using Fusion;
using UnityEngine;
using static IMoveable;

[RequireComponent(typeof(Rigidbody2D))]
public class MovingObject : NetworkBehaviour, IInteractable, IMoveable
{
    [SerializeField] private float moveSpeed = 3f;
    [SerializeField] private float arcHeight = 2f;

    private Rigidbody2D rb;
    
    private Vector2 startPosition;
    private Vector2 targetPosition;

    MoveType curMoveType;

    private float moveDuration;
    private float elapsedTime;

    private bool isMoving = false;
    public bool IsMoving => isMoving;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority)
            return;

        if (isMoving)
            Move();
    }

    public void OnInteract(Interactor interactor)
    {
        //인터랙션이 시도 되면 움직일 수 있는 상태가 됩니다
        Tongue tongue = interactor as Tongue;
        Thrower thrower = interactor as Thrower;

        if (tongue != null)
            tongue.Pull(this);

        if (thrower != null)
            thrower.Throw(this);

    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestMove(MoveType type, Vector2 target, RpcInfo info = default)
    {
        TryMove(type, target);
    }


    public void TryMove(MoveType type, Vector2 targetPosition)
    {
        if (!HasStateAuthority || isMoving)
            return;

        startPosition = rb.position;
        this.targetPosition = targetPosition;
        curMoveType = type;

        float distance = Vector2.Distance(startPosition, targetPosition);

        if (distance <= 0.001f || moveSpeed <= 0f)
            return;

        moveDuration = distance / moveSpeed;
        elapsedTime = 0f;

        isMoving = true;
    }

    public void Move()
    {
        if (!HasStateAuthority || !isMoving)
            return;

        elapsedTime += Time.fixedDeltaTime;

        float t = Mathf.Clamp01(elapsedTime / moveDuration);

        //기본 직선 궤적
        Vector2 nextPosition = Vector2.Lerp(startPosition,targetPosition,t);

        //포물선인 경우 Y축에 높이 추가
        if (curMoveType == MoveType.Parabola)
        {
            float height = 4f * arcHeight * t * (1f - t);
            nextPosition.y += height;
        }

        rb.MovePosition(nextPosition);

        if (t >= 1f)
            isMoving = false;
    }
}

using Fusion;
using UnityEngine;

public class MovingFloor : NetworkBehaviour
{
    public enum MoveDirection { Up, Down, Left, Right }

    [SerializeField] private MoveDirection curDir;
    [SerializeField] private Vector2 startPos;
    [SerializeField] private Vector2 nextPos;
    [SerializeField] private float speed = 2f;
    [SerializeField] private bool keepMove;
    [SerializeField] private bool useRelativeTravel;
    [SerializeField, Min(0.01f)] private float travelDistance = 2f;

    public override void Spawned()
    {
        if (!useRelativeTravel)
            return;
        startPos = transform.localPosition;
        Vector2 direction = curDir switch
        {
            MoveDirection.Up => Vector2.up,
            MoveDirection.Down => Vector2.down,
            MoveDirection.Left => Vector2.left,
            _ => Vector2.right
        };
        nextPos = startPos + direction * travelDistance;
    }

    public bool HasReachedDestination => Returning && !isMoving;

    private bool MovesHorizontally => curDir == MoveDirection.Left || curDir == MoveDirection.Right;

    private void OnValidate()
    {
        if (MovesHorizontally)
            nextPos.y = startPos.y;
        else
            nextPos.x = startPos.x;
    }

    [Networked] public NetworkBool isMoving { get; set; }
    [Networked] private NetworkBool Returning { get; set; }

    public void StartMove(bool isMove)
    {
        if (Object != null && Object.IsValid)
            RPC_StartMove(isMove);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_StartMove(bool isMove)
    {
        if (keepMove)
            isMoving = isMove;
        else if (isMove)
        {
            if (isMoving && useRelativeTravel)
                Returning = !Returning;
            isMoving = true;
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority || !isMoving)
            return;

        Vector2 target = Returning ? startPos : nextPos;
        Vector3 position = transform.localPosition;
        int axis = MovesHorizontally ? 0 : 1;
        position[axis] = Mathf.MoveTowards(position[axis], target[axis], Mathf.Max(0f, speed) * Runner.DeltaTime);
        transform.localPosition = position;

        if (position[axis] == target[axis])
        {
            MoveNext();
            if (!keepMove)
                isMoving = false;
        }
    }

    public void MoveNext()
    {
        if (HasStateAuthority)
            Returning = !Returning;
    }
}

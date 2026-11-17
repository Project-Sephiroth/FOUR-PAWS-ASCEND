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
    [SerializeField] private bool returnToNeutral;
    [SerializeField, Min(0.01f)] private float travelDistance = 2f;

    private bool movementSoundPlaying;
    private Vector3 lastRenderedPosition;
    private Vector2 authoredPosition;

    [SerializeField] private MovingFloor collisionPartner;
    private BoxCollider2D[] collisionTiles;

    private void Awake()
    {
        authoredPosition = transform.localPosition;
        if (collisionPartner != null)
            collisionTiles = GetComponentsInChildren<BoxCollider2D>();
    }

    public override void Spawned()
    {
        lastRenderedPosition = transform.localPosition;
        movementSoundPlaying = isMoving;
        if (!useRelativeTravel)
            return;
        startPos = returnToNeutral ? authoredPosition : (Vector2)transform.localPosition;
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
    [Networked] private int PressureDirection { get; set; }

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

    public void SelectEndpoint(bool moveToEnd)
    {
        if (Object != null && Object.IsValid)
            RPC_SelectEndpoint(moveToEnd);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_SelectEndpoint(bool moveToEnd)
    {
        Returning = !moveToEnd;
        int axis = MovesHorizontally ? 0 : 1;
        isMoving = transform.localPosition[axis] != TargetPosition[axis];
    }

    internal void SetPressureDirection(int direction)
    {
        if (Object != null && Object.IsValid && returnToNeutral)
            RPC_SetPressureDirection(direction);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_SetPressureDirection(int direction)
    {
        PressureDirection = Mathf.Clamp(direction, -1, 1);
        int axis = MovesHorizontally ? 0 : 1;
        isMoving = transform.localPosition[axis] != TargetPosition[axis];
    }

    private Vector2 TargetPosition => returnToNeutral
        ? startPos + (nextPos - startPos) * PressureDirection
        : Returning ? startPos : nextPos;

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority || !isMoving)
            return;

        Vector2 target = TargetPosition;
        Vector3 position = transform.localPosition;
        int axis = MovesHorizontally ? 0 : 1;
        float next = Mathf.MoveTowards(position[axis], target[axis], Mathf.Max(0f, speed) * Runner.DeltaTime);
        if (collisionPartner != null && collisionPartner.collisionTiles != null)
            next = position[axis] + LimitTravel(axis, next - position[axis]);
        position[axis] = next;
        transform.localPosition = position;

        if (position[axis] == target[axis])
        {
            if (!returnToNeutral)
                MoveNext();
            if (returnToNeutral || !keepMove)
                isMoving = false;
        }
    }

    private float LimitTravel(int axis, float step)
    {
        const float tolerance = 0.001f;
        int crossAxis = 1 - axis;
        float scale = transform.parent == null ? 1f : Mathf.Abs(transform.parent.lossyScale[axis]);
        float sign = Mathf.Sign(step);
        float allowed = Mathf.Abs(step) * scale;
        foreach (var tile in collisionTiles)
        {
            Vector3 center = tile.transform.TransformPoint(tile.offset);
            Vector2 half = WorldHalfSize(tile);
            foreach (var obstacle in collisionPartner.collisionTiles)
            {
                Vector3 otherCenter = obstacle.transform.TransformPoint(obstacle.offset);
                Vector2 otherHalf = WorldHalfSize(obstacle);
                if (Mathf.Abs(otherCenter[crossAxis] - center[crossAxis]) >= half[crossAxis] + otherHalf[crossAxis] - tolerance)
                    continue;

                float separation = (otherCenter[axis] - center[axis]) * sign;
                if (separation > 0f)
                    allowed = Mathf.Min(allowed, Mathf.Max(0f, separation - half[axis] - otherHalf[axis]));
            }
        }
        return allowed < Mathf.Abs(step) * scale ? sign * allowed / scale : step;
    }

    private static Vector2 WorldHalfSize(BoxCollider2D tile)
    {
        Vector3 scale = tile.transform.lossyScale;
        return Vector2.Scale(tile.size, new Vector2(Mathf.Abs(scale.x), Mathf.Abs(scale.y))) * 0.5f;
    }

    public override void Render()
    {
        Vector3 position = transform.localPosition;
        bool moved = (position - lastRenderedPosition).sqrMagnitude > 0.00000001f;
        if (moved && !movementSoundPlaying)
            SoundManager.Play(SoundEffect.MovingFloor);

        lastRenderedPosition = position;
        movementSoundPlaying = moved || (movementSoundPlaying && isMoving);
    }

    public void MoveNext()
    {
        if (HasStateAuthority)
            Returning = !Returning;
    }
}

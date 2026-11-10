using Fusion;
using UnityEngine;

/// <summary>
/// 움직이는 발판입니다.
/// </summary>
public class MovingFloor : NetworkBehaviour
{
    public enum MoveDirection
    {
        Up,
        Down,
        Left,
        Right
    }

    [Header("이동 설정")]
    [SerializeField] private MoveDirection startDirection;
    [SerializeField] private float dest_A;
    [SerializeField] private float dest_B;
    [SerializeField] private float speed = 2f;
    [SerializeField] private bool keepMove = false;

    [Networked] private NetworkBool isA { get; set; }
    [Networked] public NetworkBool isMoving { get; set; }

    private bool IsHorizontal => startDirection == MoveDirection.Left || startDirection == MoveDirection.Right;

    public override void Spawned()
    {
        if (!HasStateAuthority)
            return;

        //시작 방향에 따라 목적지 결정
        isA = startDirection == MoveDirection.Left || startDirection == MoveDirection.Down;

        isMoving = false;
    }

    private void Update()
    {
        if (!HasStateAuthority || !isMoving)
            return;

        float current = IsHorizontal  ? transform.position.x : transform.position.y;

        float target = isA ? dest_A : dest_B;

        float next = Mathf.MoveTowards( current, target, speed * Time.deltaTime);

        Vector3 pos = transform.position;

        if (IsHorizontal)
            pos.x = next;
        else
            pos.y = next;

        transform.position = pos;

        if (Mathf.Approximately(next, target))
        {
            if (keepMove)
                isA = !isA;
            else
                isMoving = false;
        }
    }

    /// <summary>
    /// true면 A로, false면 B로 이동합니다.
    /// </summary>
    public void SwitchMove(bool moveToA)
    {
        if (!HasStateAuthority)
            return;

        isA = moveToA;
        isMoving = true;
    }

    /// <summary>
    /// Inspector에 지정한 시작 방향으로 이동합니다.
    /// </summary>
    public void StartMove()
    {
        Debug.Log($"MovingFloor {gameObject.name}: 움직임 시작");
        if (!HasStateAuthority)
            return;

        isA = startDirection == MoveDirection.Left || startDirection == MoveDirection.Down;

        isMoving = true;
    }
}
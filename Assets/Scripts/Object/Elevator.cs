using Fusion;
using UnityEngine;

/// <summary>
/// 올라가고 내려가는 엘리베이터입니다.
/// </summary>
public class Elevator : NetworkBehaviour
{
    [SerializeField] private GameObject floor;
    [SerializeField] private SpriteRenderer column;
    [SerializeField] private float originPos;
    [SerializeField] private float dest;
    [SerializeField] private float speed;
    [SerializeField] private bool keepMove = false;

    [Networked] private NetworkBool isUp { get; set; }
    [Networked] private NetworkBool isMoving { get; set; }

    private void Update()
    {
        if (!HasStateAuthority || !isMoving)
            return;

        float targetY = isUp ? dest : originPos;
        float currentY = floor.transform.position.y;

        float nextY = Mathf.MoveTowards(currentY, targetY, speed * Time.deltaTime);

        float amount = nextY - currentY;
        Elevate(amount);

        //목표 지점 도착
        if (Mathf.Approximately(nextY, targetY))
        {
            if (keepMove) //계속 움직이는 엘레베이터면 반대방향 시작
                isUp = !isUp;
            else
                isMoving = false;
        }
    }

    public void DoUp(bool up)
    {
        if (!HasStateAuthority)
            return;

        isUp = up;
        isMoving = true;
    }

    public void Elevate(float amount)
    {
        floor.transform.position += Vector3.up * amount;
        column.size += Vector2.up * amount;
    }
}
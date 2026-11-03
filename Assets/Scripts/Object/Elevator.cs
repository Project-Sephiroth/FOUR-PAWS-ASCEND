using Fusion;
using UnityEngine;

/// <summary>
/// 올라가고 내려가는 엘리베이터 입니다.
/// </summary>
public class Elevator : NetworkBehaviour
{
    [SerializeField] GameObject floor;
    [SerializeField] SpriteRenderer column;
    [SerializeField] float originPos;
    [SerializeField] float dest;
    [SerializeField] float speed;
    private bool isUp = false;

    private void Update()
    {
        if (isUp && dest - floor.transform.position.y > 0.1f ||
            !isUp && floor.transform.position.y - originPos > 0.1f)
            Elevate(speed * Time.deltaTime);
    }

    public void DoUp(bool isUp)
    {
        this.isUp = isUp;
    }

    public void Elevate(float amount)
    {
        Vector3 dir = isUp ? Vector3.up : Vector3.down;

        floor.transform.position += dir * amount;
        column.size += (Vector2)dir* amount;
    }
}

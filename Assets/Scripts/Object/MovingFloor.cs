using Fusion;
using UnityEngine;

/// <summary>
/// 움직이는 발판입니다
/// </summary>
public class MovingFloor : NetworkBehaviour
{
    [SerializeField] float dest_L;
    [SerializeField] float dest_R;
    [SerializeField] float speed;
    private bool isRight = false;

    private void Update()
    {
        if (isRight && dest_R - transform.position.x > 0.1f ||
            !isRight && transform.position.x - dest_L > 0.1f)
            Move(speed * Time.deltaTime);
    }

    public void SwitchMove(bool isRight)
    {
        this.isRight = isRight;
    }

    void Move(float amount)
    {
        if (isRight)
            transform.position += Vector3.right * amount;
        else
            transform.position += Vector3.left * amount;
    }
}


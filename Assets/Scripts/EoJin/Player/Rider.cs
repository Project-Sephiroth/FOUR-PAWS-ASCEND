using Fusion;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

/// <summary>
/// Liftable 에 탈 수 있습니다
/// </summary>
public class Rider : NetworkBehaviour, IRideable
{
    private PlayerInput input; //input 을 받는 Player 객체
    private Lifter curLifter; //현재 타고 있는 태울 수 있는 객체
    private Rigidbody2D rb;
    private FixedJoint2D rideJoint;

    private void Awake()
    {
        input = GetComponent<PlayerInput>();
        rb = GetComponent<Rigidbody2D>();
    }

    private void Update()
    {
        if (Object == null || !HasStateAuthority)
            return;

        //탑승 중이라면
        if (curLifter == null)
            return;

        if (input != null && input.JumpInput) //인풋을 받을 수 있고 점프했다면
        {
            input.JumpInput = false;
            Drop(); //내리기
        }
    }

    /// <summary>
    /// 태울 수 있는 객체에 탑승합니다
    /// </summary>
    public void Ride(Lifter lifter)
    {
        if (curLifter != lifter)
            return;

        curLifter = lifter;

        if (input != null)
        {
            input.CanMoveInput = false;
            input.MoveInput = Vector2.zero; //이동 입력이 있었다면 제거
        }

        //머리 위치로 이동
        rb.position = curLifter.Head.transform.position; 

        //Lifter와 물리적으로 연결
        rideJoint = gameObject.AddComponent<FixedJoint2D>();

        rideJoint.connectedBody = lifter.Rb;
        rideJoint.enableCollision = false;
    }

    /// <summary>
    /// 현재 탑승한 개체에서 내립니다
    /// </summary>
    public void Drop()
    {
        if (curLifter != null)
        {
            curLifter.OnRiderDrop(this);
            curLifter = null;
        }

        if (rideJoint != null)
        {
            Destroy(rideJoint);
            rideJoint = null;
        }

        //내린 후엔 move input 을 받습니다
        input.MoveInput = Vector2.zero;
        input.CanMoveInput = true;
    }

}

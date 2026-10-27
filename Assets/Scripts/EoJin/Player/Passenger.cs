using Fusion;
using UnityEngine;

/// <summary>
/// Liftable 에 탈 수 있습니다.
/// </summary>
public class Passenger : NetworkBehaviour, ICarriable, IInteractable
{
    private PlayerInput input; //input 을 받는 Player 객체
    private Carrier curCarrier; //현재 타고 있는 태울 수 있는 객체
    
    private Rigidbody2D rb;
    private Collider2D col;

    public bool IsCarried => curCarrier != null;

    public override void Spawned()
    {
        input = GetComponent<PlayerInput>();
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
    }

    private void Update()
    {
        if (!HasStateAuthority || curCarrier == null)
            return;

        if (input.JumpInput) //인풋을 받을 수 있고 점프했다면
            RequestDrop(); //내리기
    }

    /// <summary>
    /// 태울 수 있는 객체에 탑승합니다
    /// </summary>
    public void OnCarry(Carrier carrier)
    {
        if (curCarrier != null || carrier == null)
            return;

        Debug.Log($"{carrier.gameObject.name} 에 {gameObject.name} 이 탑승");

        curCarrier = carrier;
        input.CanMoveInput = false;

        transform.SetParent(curCarrier.Head.transform);
        transform.position = curCarrier.Head.transform.position;

        col.enabled = false;
        rb.simulated = false;
    }

    /// <summary>
    /// 현재 탑승한 개체에서 내립니다
    /// </summary>
    public void OnDrop()
    {
        if (curCarrier == null)
            return;

        transform.SetParent(null, true);
        transform.position = curCarrier.transform.position;

        input.CanMoveInput = true;
        curCarrier = null;

        col.enabled = true;
        rb.simulated = true;
    }

    private void RequestDrop()
    {
        if (!HasStateAuthority)
            return;

        if (curCarrier == null)
            return;

        curCarrier.RPC_RequestPassengerDrop(this);
    }

    public void OnInteract()
    {
    }
}

using Fusion;
using UnityEngine;
using UnityEngine.Windows;

/// <summary>
/// Liftable 에 탈 수 있습니다.
/// </summary>
public class Rider : NetworkBehaviour, IRideable
{
    private PlayerInput input; //input 을 받는 Player 객체
    private Lifter curLifter; //현재 타고 있는 태울 수 있는 객체
    private Rigidbody2D rb;
    private Collider2D col;
    private NetworkObject networkObj;

    private void Awake()
    {
        input = GetComponent<PlayerInput>();
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();

        if (HasInputAuthority)
            networkObj = GetComponent<NetworkObject>();
    }

    private void Update()
    {
        if (input.JumpInput) //인풋을 받을 수 있고 점프했다면
            Drop(); //내리기
    }

    /// <summary>
    /// 태울 수 있는 객체에 탑승합니다
    /// </summary>
    public void Ride(Lifter lifter)
    {
        if (curLifter != null)
            return;

        Debug.Log($"{lifter.gameObject.name} 에 {gameObject.name} 이 탑승");

        curLifter = lifter;
        input.CanMoveInput = false;

        transform.SetParent(curLifter.Head.transform);
        transform.position = curLifter.Head.transform.position;

        col.enabled = false;
        rb.simulated = false;
    }

    /// <summary>
    /// 현재 탑승한 개체에서 내립니다
    /// </summary>
    public void Drop()
    {
        if (curLifter == null)
            return;

        transform.SetParent(null, true);
        transform.position = curLifter.transform.position;

        input.CanMoveInput = true;
        curLifter = null;

        col.enabled = true;
        rb.simulated = true;

        RPC_RequestLifterDrop();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_RequestLifterDrop()
    {
        curLifter.OnRiderDrop();
    }
}

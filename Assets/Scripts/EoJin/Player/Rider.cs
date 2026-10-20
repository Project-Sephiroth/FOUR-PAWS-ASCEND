using Fusion;
using UnityEngine;

/// <summary>
/// Liftable 에 탈 수 있습니다.
/// </summary>
public class Rider : NetworkBehaviour, IRideable
{
    private PlayerInput input; //input 을 받는 Player 객체
    private Lifter curLifter; //현재 타고 있는 태울 수 있는 객체
    private Rigidbody2D rb;
    private Collider2D col;
    private NetworkTransform networkTransform;

    private void Awake()
    {
        input = GetComponent<PlayerInput>();
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        networkTransform = GetComponent<NetworkTransform>();
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

        RPC_SetLifter(lifter);
    }

    /// <summary>
    /// 현재 탑승한 개체에서 내립니다
    /// </summary>
    public void Drop()
    {
        if (curLifter == null)
            return;

        RPC_OnDrop();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_SetLifter(Lifter lifter)
    {
        if (lifter == null)
            return;

        curLifter = lifter;

        transform.SetParent(curLifter.Head.transform);
        networkTransform.Teleport(curLifter.Head.transform.position);

        col.enabled = false;
        rb.simulated = false;
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_OnDrop()
    {
        curLifter.OnRiderDrop();

        col.enabled = true;
        rb.simulated = true;

        networkTransform.Teleport(curLifter.transform.position);
        transform.SetParent(null, true);
        
        curLifter = null;
    }
}

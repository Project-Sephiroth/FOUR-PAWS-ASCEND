using Fusion;
using UnityEngine;
using UnityEngine.Events;

public class Switch : NetworkBehaviour
{
    [Tooltip("true : 스위치에 닿아야지만 On")]
    [SerializeField] bool isWait = true;
    public UnityEvent<bool> action;
    private Animator anim;

    private GameObject curOperator = null;
    bool isOn = false;

    private void Awake()
    {
        anim = GetComponent<Animator>();
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (Object == null || !Object.IsValid || !HasStateAuthority)
            return;

        if (!collision.CompareTag("Player"))
            return;

        if (isWait)
        {
            if (curOperator != null) //이미 조작하고 있는 플레이어가 있다면 리턴
                return;

            curOperator = collision.gameObject;  //스위치 조작 플레이어 등록

            RPC_Switch(true);
        }
        else
        {
            RPC_Switch(!isOn);
        }
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        if (Object == null || !Object.IsValid || !HasStateAuthority)
            return;

        if (!collision.CompareTag("Player"))
            return;

        if (isWait)
        {
            if (curOperator != collision.gameObject) //조작한 플레이어와 다를 경우 Exit 해도 스위치 끄기 안 됨
                return;

            curOperator = null;

            RPC_Switch(false);
        }
    }

    [Rpc(RpcSources.All, RpcTargets.All)]
    public void RPC_Switch(bool doOn)
    {
        anim.SetBool("IsOn", doOn);
        action?.Invoke(doOn);
        isOn = doOn;
    }
}

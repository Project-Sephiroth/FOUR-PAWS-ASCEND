using Fusion;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 열쇠를 모두 획득 시 열립니다.
/// </summary>
public class KeyDoor : NetworkBehaviour
{
    [Header("키 사용 시 키 없애기")]
    [SerializeField] bool disactiveKeys = false;
    [Header("오픈에 필요한 키들")]
    [SerializeField] private Key[] openKeys;
    [Header("오픈 때 발생할 이벤트")]
    [SerializeField] private UnityEvent openAction;

    [Networked] public NetworkBool IsOpened { get; set; }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (!HasStateAuthority || IsOpened)
            return;

        if (!collision.gameObject.CompareTag("Player"))
            return;

        for (int i = 0; i < openKeys.Length; i++)
        {
            if (openKeys[i] == null || !openKeys[i].isEarned)
                return;
        }

        IsOpened = true;
        RPC_OpenDoor();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_OpenDoor()
    {
        openAction?.Invoke();

        if (disactiveKeys)
        {
            for (int i = 0; i < openKeys.Length; i++)
                openKeys[i].gameObject.SetActive(false);
        }
    }
}

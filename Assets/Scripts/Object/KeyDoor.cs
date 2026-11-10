using Fusion;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// ���踦 ��� ȹ�� �� �����ϴ�.
/// </summary>
public class KeyDoor : NetworkBehaviour
{
    [SerializeField] bool isBring = false;
    List<Key> bringKeys;
    [SerializeField] bool disactive = false;
    [SerializeField] private Key[] openKeys;
    [SerializeField] private UnityEvent openAction;

    [Networked] public NetworkBool IsOpened { get; set; }

    public override void Spawned()
    {
        base.Spawned();
        bringKeys = new List<Key>();
        bringKeys.Capacity = openKeys.Length;
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (!HasStateAuthority || IsOpened)
            return;

        if (isBring)
        {
            Key curKey = collision.gameObject.GetComponentInChildren<Key>();

            if (!bringKeys.Contains(curKey))
                bringKeys.Add(curKey);

            if (bringKeys.Count < openKeys.Length)
                return;
        }

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

        if (disactive)
        {
            for (int i = 0; i < openKeys.Length; i++)
                openKeys[i].gameObject.SetActive(false);
        }
    }
}

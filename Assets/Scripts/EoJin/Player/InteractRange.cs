using Fusion;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class InteractRange : NetworkBehaviour
{
    public event UnityAction<GameObject> OnDetect;

    private readonly HashSet<GameObject> targets = new();
    public bool Contains(GameObject target)
    {
        return target != null && targets.Contains(target);
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (Object == null || !Object.IsValid || !HasStateAuthority)
            return;

        //Interactable 이 있을 경우 해당 개체를 반환
        IInteractable interactable = collision.GetComponent<IInteractable>();

        if (interactable == null)
            return;

        GameObject target = collision.gameObject;

        if (!targets.Add(target))
            return;

        Debug.Log($"InteractRange 가 {collision.gameObject} 를 감지");
        OnDetect?.Invoke(collision.gameObject);
    }

    private void OnTriggerExit2D(Collider2D collision)
    {
        if (Object == null || !Object.IsValid || !HasStateAuthority)
            return;

        targets.Remove(collision.gameObject);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        targets.Clear();
    }
}

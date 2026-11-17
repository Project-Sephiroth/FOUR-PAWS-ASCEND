using System.Collections.Generic;
using Fusion;
using UnityEngine;

public class InteractRange : NetworkBehaviour
{
    private readonly Dictionary<Collider2D, GameObject> contacts = new();

    public bool Contains(GameObject target)
    {
        foreach (var contact in contacts)
            if (contact.Value == target && IsInRange(contact.Key, target))
                return true;
        return false;
    }

    public void CopyTargetsTo(HashSet<GameObject> targets)
    {
        foreach (var contact in contacts)
            if (IsInRange(contact.Key, contact.Value))
                targets.Add(contact.Value);
    }

    private static bool IsInRange(Collider2D collider, GameObject target) =>
        collider != null && collider.isActiveAndEnabled && target != null && target.activeInHierarchy;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (Interactor.WorldInteractionsDisabled || Object == null || !Object.IsValid || !HasStateAuthority)
            return;
        if (other.GetComponentInParent<IInteractable>() is Component target)
            contacts[other] = target.gameObject;
    }

    private void OnTriggerExit2D(Collider2D other) => contacts.Remove(other);

    public override void Despawned(NetworkRunner runner, bool hasState) => contacts.Clear();
}

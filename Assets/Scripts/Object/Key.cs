using Fusion;
using UnityEngine;

public class Key : NetworkBehaviour, IInteractable
{
    [Networked] public NetworkBool isEarned { get; set; }
    [Networked, OnChangedRender(nameof(ApplyDrop))] private NetworkBool KnockedDown { get; set; }
    [Networked, OnChangedRender(nameof(ApplyConsumed))] public NetworkBool IsConsumed { get; private set; }

    [SerializeField] private bool disactiveOnEarned;
    [SerializeField] private bool deactivateOnConsume;
    [SerializeField] private bool requiresRockHit;
    [SerializeField] private bool throwWithBear;
    [SerializeField] private InteractableOutline outline;
    [SerializeField] private Passenger passenger;
    [SerializeField] private MovingObject movingObject;

    public bool IsReachableBy(Interactor interactor) => throwWithBear && interactor is Thrower &&
        CanPickUp && (transform.position - interactor.transform.position).sqrMagnitude <= 6.25f;

    public bool CanPickUp => !IsConsumed && (!requiresRockHit || KnockedDown);

    public override void Spawned()
    {
        ApplyDrop();
        ApplyConsumed();
    }

    public bool CanInteractWith(Interactor interactor)
    {
        if (Object == null || !Object.IsValid || !CanPickUp)
            return false;
        if (passenger != null)
            return (interactor is Carrier || interactor is Thrower) && passenger.CanBeCarried && !passenger.IsCarried;
        if (movingObject != null)
            return (interactor is Tongue || interactor is Thrower) && movingObject.CanInteract;
        return interactor is not Tongue && !isEarned;
    }

    public void OnInteract(Interactor interactor)
    {
        if (Object == null || !Object.IsValid || !CanPickUp || interactor == null || !interactor.HasStateAuthority)
            return;

        if (passenger != null)
            passenger.OnInteract(interactor);
        else if (movingObject != null && interactor is Thrower thrower)
        {
            if (throwWithBear)
                thrower.TryThrow(movingObject);
            else
                thrower.TryCarryKey(movingObject);
        }
        else if (movingObject != null)
            movingObject.OnInteract(interactor);
        else
            RPC_Earn(interactor);
    }

    public NetworkObject GetCarryingPlayer()
    {
        if (Object == null || !Object.IsValid || IsConsumed)
            return null;

        if (passenger != null && passenger.RabbitCarrier != null)
            return passenger.RabbitCarrier.Object;

        return movingObject != null && movingObject.Holder is Thrower thrower ? thrower.Object : null;
    }

    public void OnRockHit()
    {
        if (requiresRockHit && Object != null && Object.IsValid)
            RPC_KnockDown();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_KnockDown()
    {
        if (KnockedDown || IsConsumed)
            return;

        KnockedDown = true;
        ApplyDrop();
    }

    private void ApplyDrop()
    {
        if (requiresRockHit && TryGetComponent(out Rigidbody2D body))
            body.bodyType = KnockedDown ? RigidbodyType2D.Dynamic : RigidbodyType2D.Kinematic;
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_Earn(Interactor interactor, RpcInfo info = default)
    {
        if (interactor == null || (!Runner.IsSinglePlayer && info.Source != interactor.Object.StateAuthority) || isEarned)
            return;

        isEarned = true;
        if (disactiveOnEarned)
            Consume();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_Consume(KeyDoor door, RpcInfo info = default)
    {
        if (door != null && (Runner.IsSinglePlayer || info.Source == door.Object.StateAuthority))
            Consume();
    }

    private void Consume()
    {
        isEarned = true;
        IsConsumed = true;
        if (passenger != null)
            passenger.OnDrop();
        if (movingObject != null)
            movingObject.ConsumeKey();

        ApplyConsumed();
    }

    private void ApplyConsumed()
    {
        if (!IsConsumed)
            return;

        if (deactivateOnConsume)
        {
            gameObject.SetActive(false);
            return;
        }

        foreach (var sprite in GetComponentsInChildren<SpriteRenderer>())
            sprite.enabled = false;
        foreach (var collider in GetComponentsInChildren<Collider2D>())
            collider.enabled = false;
        if (TryGetComponent(out Rigidbody2D body))
            body.simulated = false;
    }

    public void SetOutlineMat(bool isOn)
    {
        if (outline != null)
            outline.SetOutline(isOn && CanPickUp);
    }
}

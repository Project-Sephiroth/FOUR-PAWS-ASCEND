using Fusion;
using UnityEngine;

public class Rider : NetworkBehaviour, IRideable
{
    [Networked] public NetworkObject CarrierObject { get; private set; }
    [Networked] private TickTimer PickupCooldown { get; set; }
    public bool IsRiding => Object != null && Object.IsValid && CarrierObject != null;
    public bool CanBePickedUp => Object != null && Object.IsValid && !IsRiding && PickupCooldown.ExpiredOrNotRunning(Runner);
    private Rigidbody2D body;
    private Collider2D bodyCollider;
    private PlayerInput input;
    private Mover mover;
    private Lifter ignoredCarrier;
    private Collider2D[] riderColliders;
    private Collider2D[] carrierColliders;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        bodyCollider = GetComponent<Collider2D>();
        input = GetComponent<PlayerInput>();
        mover = GetComponent<Mover>();
        riderColliders = GetComponentsInChildren<Collider2D>();
    }

    public override void Render()
    {
        if (Object != null && Object.IsValid)
            SetCollisionCarrier(CarrierObject != null ? CarrierObject.GetComponent<Lifter>() : null);
    }

    public void Ride(Lifter lifter)
    {
        if (lifter != null)
            lifter.Lift(this);
    }

    public void RequestRide(Lifter lifter)
    {
        if (Object == null || !Object.IsValid || lifter == null || lifter.Object == null || !lifter.HasStateAuthority)
            return;
        RPC_Attach(lifter.Object);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_Attach(NetworkObject carrier, RpcInfo info = default)
    {
        Lifter lifter = carrier != null ? carrier.GetComponent<Lifter>() : null;
        Lifter ownLifter = GetComponent<Lifter>();
        if (lifter == null || lifter.Head == null || lifter.Rb == null || info.Source != carrier.StateAuthority ||
            body == null || !body.simulated || !CanBePickedUp || body.mass > lifter.Rb.mass ||
            (ownLifter != null && ownLifter.CarriedObject == carrier) ||
            !PcsPlayerAbilities.CanParticipate(Object) || !PcsPlayerAbilities.CanParticipate(carrier) ||
            Vector2.Distance(lifter.Head.transform.position, body.position) > 2f)
        {
            if (lifter != null)
                lifter.OnRiderDrop(this);
            return;
        }
        CarrierObject = carrier;
        SetCollisionCarrier(lifter);
        if (input != null)
        {
            input.CanMoveInput = false;
            input.MoveInput = Vector2.zero;
        }
    }

    // Mover alone applies this pose, on the passenger's state authority.
    public bool TryGetCarryPosition(out Vector2 position)
    {
        position = body != null ? body.position : (Vector2)transform.position;
        if (!HasStateAuthority || !IsRiding)
            return false;
        Lifter lifter = CarrierObject.GetComponent<Lifter>();
        if (lifter == null || lifter.Head == null || !CarrierObject.gameObject.activeInHierarchy)
        {
            Drop();
            return false;
        }
        SetCollisionCarrier(lifter);
        float footOffset = bodyCollider != null ? bodyCollider.bounds.min.y - body.position.y : 0f;
        position = new Vector2(lifter.Head.transform.position.x, lifter.Head.Top - footOffset + 0.015f);
        return true;
    }

    public void RequestRelease(Lifter lifter, Vector2 velocity)
    {
        if (Object != null && Object.IsValid && lifter != null && lifter.Object != null && lifter.HasStateAuthority)
            RPC_Release(lifter.Object, velocity);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_Release(NetworkObject carrier, Vector2 velocity, RpcInfo info = default)
    {
        if (carrier == null || CarrierObject != carrier || info.Source != carrier.StateAuthority ||
            float.IsNaN(velocity.x) || float.IsInfinity(velocity.x) ||
            float.IsNaN(velocity.y) || float.IsInfinity(velocity.y))
            return;
        Drop();
        if (mover != null && velocity.sqrMagnitude > 0f)
            mover.QueueLaunch(Vector2.ClampMagnitude(velocity, 16f));
    }

    public void Drop()
    {
        if (Object == null || !Object.IsValid || !HasStateAuthority)
            return;
        Lifter lifter = CarrierObject != null ? CarrierObject.GetComponent<Lifter>() : null;
        CarrierObject = null;
        PickupCooldown = TickTimer.CreateFromSeconds(Runner, 0.5f);
        SetCollisionCarrier(null);
        if (lifter != null)
            lifter.OnRiderDrop(this);
        if (input != null)
        {
            input.MoveInput = Vector2.zero;
            input.CanMoveInput = true;
        }
    }

    private void SetCollisionCarrier(Lifter lifter)
    {
        if (ignoredCarrier == lifter)
            return;
        IgnorePairs(false);
        ignoredCarrier = lifter;
        carrierColliders = lifter != null ? lifter.GetComponentsInChildren<Collider2D>() : null;
        IgnorePairs(true);
    }

    private void IgnorePairs(bool ignore)
    {
        if (riderColliders == null || carrierColliders == null)
            return;
        foreach (Collider2D own in riderColliders)
            foreach (Collider2D other in carrierColliders)
                if (own != null && other != null)
                    Physics2D.IgnoreCollision(own, other, ignore);
    }

    private void OnDisable()
    {
        if (Object != null && Object.IsValid && HasStateAuthority)
            Drop();
        IgnorePairs(false);
        ignoredCarrier = null;
        carrierColliders = null;
    }
}

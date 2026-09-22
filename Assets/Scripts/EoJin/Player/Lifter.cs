using Fusion;
using UnityEngine;

public class Lifter : NetworkBehaviour, ILiftable
{
    [SerializeField] private Head head;
    [SerializeField, Min(0.1f)] private float pickupRange = 1.8f;
    [Networked] public NetworkObject CarriedObject { get; private set; }
    public Head Head => head;
    public Rigidbody2D Rb { get; private set; }
    public Rider CurrentRider => Object != null && Object.IsValid && CarriedObject != null
        ? CarriedObject.GetComponent<Rider>() : null;
    private float reservationDeadline;

    private void Awake()
    {
        Rb = GetComponent<Rigidbody2D>();
        if (head != null)
            head.OnHit += OnHeadHit;
    }

    private void OnDestroy()
    {
        if (head != null)
            head.OnHit -= OnHeadHit;
    }

    private void OnHeadHit(GameObject target)
    {
        if (Object == null || !Object.IsValid || !HasStateAuthority)
            return;
        Rider rider = target.GetComponentInParent<Rider>();
        if (rider != null)
            Lift(rider);
    }

    public void Lift(Rider rider)
    {
        if (Object == null || !Object.IsValid || rider == null || rider.Object == null || !rider.Object.IsValid)
            return;
        if (HasStateAuthority)
            TryReserve(rider);
        else
            RPC_RequestLift(rider.Object);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestLift(NetworkObject passenger, RpcInfo info = default)
    {
        if (passenger == null || (info.Source != Object.StateAuthority && info.Source != passenger.StateAuthority))
            return;
        TryReserve(passenger.GetComponent<Rider>());
    }

    private void TryReserve(Rider rider)
    {
        if (!HasStateAuthority || head == null || Rb == null || rider == null || rider.Object == Object ||
            CarriedObject != null || !rider.CanBePickedUp || !PcsPlayerAbilities.CanParticipate(Object) ||
            !PcsPlayerAbilities.CanParticipate(rider.Object))
            return;
        Rigidbody2D other = rider.GetComponent<Rigidbody2D>();
        if (other == null || !other.simulated || other.mass > Rb.mass ||
            Vector2.Distance(head.transform.position, other.position) > pickupRange)
            return;
        Rider ancestor = GetComponent<Rider>();
        for (int i = 0; ancestor != null && ancestor.IsRiding && i < 8; i++)
        {
            if (ancestor.CarrierObject == rider.Object)
                return;
            ancestor = ancestor.CarrierObject.GetComponent<Rider>();
        }
        CarriedObject = rider.Object;
        reservationDeadline = Time.time + 2f;
        rider.RequestRide(this);
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority || CarriedObject == null)
            return;
        Rider rider = CurrentRider;
        if (!CarriedObject.gameObject.activeInHierarchy || rider == null ||
            (Time.time > reservationDeadline && rider.CarrierObject != Object))
            CarriedObject = null;
    }

    public void OnRiderDrop(IRideable rideable)
    {
        Rider rider = rideable as Rider;
        if (Object == null || !Object.IsValid || rider == null || rider.Object == null)
            return;
        if (HasStateAuthority)
        {
            if (CarriedObject == rider.Object)
                CarriedObject = null;
        }
        else
            RPC_ClearRider(rider.Object);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_ClearRider(NetworkObject passenger, RpcInfo info = default)
    {
        if (passenger != null && CarriedObject == passenger &&
            (info.Source == passenger.StateAuthority || info.Source == Object.StateAuthority))
            CarriedObject = null;
    }

    public void Release(Vector2 launchVelocity)
    {
        if (Object == null || !Object.IsValid || !HasStateAuthority)
            return;
        Rider rider = CurrentRider;
        if (rider != null)
            rider.RequestRelease(this, launchVelocity);
    }
}

using Fusion;
using UnityEngine;

public class Carrier : Interactor
{
    [SerializeField] private Head head;
    public Head Head => head;

    protected override bool CanShowInteractionOutline => CurPassenger == null;

    [Networked] public Passenger CurPassenger { get; private set; }

    protected override void TryIntetact()
    {
        if (CurPassenger != null)
        {
            OnPassengerDrop();
            return;
        }

        base.TryIntetact();
    }

    public void TryCarry()
    {
        if (curClick != null)
            TryCarry(curClick.GetComponentInParent<Passenger>());
    }

    public void TryCarry(Passenger passenger)
    {
        if (!HasStateAuthority || CurPassenger != null || passenger == null || Head == null)
            return;

        if (passenger.Object == null || !passenger.Object.IsValid || !passenger.CanBeCarried || passenger.IsCarried)
            return;

        CurPassenger = passenger;
        passenger.RPC_RequestCarry(this);
    }

    public void OnPassengerDrop()
    {
        if (!HasStateAuthority || CurPassenger == null)
            return;

        CurPassenger.RPC_RequestDrop(this);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestPassengerDrop(Passenger passenger, RpcInfo info = default)
    {
        if (passenger == CurPassenger && passenger != null &&
            (Runner.IsSinglePlayer || info.Source == passenger.Object.StateAuthority))
            CurPassenger = null;
    }
}

using Fusion;
using UnityEngine;

public class Carrier : Interactor
{
    [SerializeField] private Head head;
    [SerializeField, Min(0f)] private float dropForward = 0.85f;
    [SerializeField, Min(0f)] private float dropHeight = 0.2f;
    public Head Head => head;

    protected override bool CanShowInteractionOutline => CurPassenger == null;

    [Networked] public Passenger CurPassenger { get; private set; }

    protected override void TryIntetact()
    {
        if (CurPassenger == null)
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

    internal Vector2 GetDropPosition()
    {
        // 토끼의 발을 기준으로 바라보는 쪽에 내려놓습니다. 머리 위치와 탑승 오프셋은 쓰지 않습니다.
        Transform rabbit = Object.transform;
        float direction = rabbit.TryGetComponent(out Mover mover) && mover.FacesLeftNow ? -1f : 1f;
        return (Vector2)rabbit.position + new Vector2(direction * dropForward, dropHeight);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestPassengerDrop(Passenger passenger, RpcInfo info = default)
    {
        if (passenger == CurPassenger && passenger != null &&
            (Runner.IsSinglePlayer || info.Source == passenger.Object.StateAuthority))
            CurPassenger = null;
    }
}

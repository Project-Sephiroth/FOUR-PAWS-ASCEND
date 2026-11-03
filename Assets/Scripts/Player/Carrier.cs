using Fusion;
using UnityEngine;

/// <summary>
/// Carriable 을 머리 위에 갖습니다. Carrier 의 local 에서만 발현 됩니다.
/// </summary>
public class Carrier : Interactor
{
    [SerializeField] private Head head; //나의 머리
    public Head Head => head; //다른 객체에서 나의 머리 확인 가능

    [SerializeField] LayerMask playerLayer;
    [SerializeField] Interactor interactor;

    [Networked, OnChangedRender(nameof(ApplyPassenger))]
    public Passenger CurPassenger { get; set; }
    Passenger appliedPassenger;

    public override void Spawned()
    {
        base.Spawned();

        ApplyPassenger();
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        base.Despawned(runner, hasState);
    }

    void TryCarry()
    {
        if (!HasStateAuthority)
            return;

        //탈 수 있음 속성이 없거나 이미 타고 있다면 return
        if (interactor.CurInteractable is Passenger)
            return;

        var newPassenger = interactor.CurInteractable as Passenger;

        if (newPassenger == CurPassenger)
            return;

        Carry(newPassenger);
    }

    /// <summary>
    /// Passenger 를 설정합니다
    /// </summary>
    /// <param name="rider"></param>
    public void Carry(Passenger passenger)
    {
        if (!HasStateAuthority || passenger == null)
            return;

        //현재 탑승하고 있는 rider 가 있으면 내립니다
        if (CurPassenger != null)
            OnPassengerDrop();

        CurPassenger = passenger;

        ApplyPassenger();
    }

    public void OnPassengerDrop()
    {
        if (!HasStateAuthority)
            return;

        if (CurPassenger == null)
            return;

        CurPassenger = null;
        ApplyPassenger();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestPassengerDrop(Passenger passenger, RpcInfo info = default)
    {
        if (passenger == null)
            return;

        if (CurPassenger != passenger)
            return;

        //실제 탑승자만 하차 요청 가능
        if (info.Source != passenger.Object.StateAuthority)
            return;

        OnPassengerDrop();
    }

    /// <summary>
    /// 동기화된 탑승 상태를 각 Runner에서 적용합니다.
    /// </summary>
    private void ApplyPassenger()
    {
        if (appliedPassenger == CurPassenger)
            return;

        if (appliedPassenger != null)
            appliedPassenger.OnDrop();

        appliedPassenger = CurPassenger;

        if (appliedPassenger != null)
        {
            Debug.Log($"{gameObject.name} 이 어부바를 시도");

            appliedPassenger.OnCarry(this);
        }
    }
}

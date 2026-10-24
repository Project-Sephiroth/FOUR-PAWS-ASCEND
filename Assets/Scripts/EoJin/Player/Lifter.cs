using Fusion;
using UnityEngine;
using static UnityEngine.GraphicsBuffer;

/// <summary>
/// Rideable 을 머리 위에 갖습니다. lifter 의 local 에서만 발현 됩니다.
/// </summary>
public class Lifter : NetworkBehaviour, ILiftable
{
    [SerializeField] private Head head; //나의 머리
    public Head Head => head; //다른 객체에서 나의 머리 확인 가능
    [Networked] public Rider CurRider { get; set; } //현재 탑승한 타겟

    [SerializeField] LayerMask playerLayer;
    [SerializeField] Interactor interactor;

    void Start()
    {
        //Interactor 에서 인터랙션을 시도할 때 리프팅 시도
        interactor.OnTryInteract += TryLift;
    }

    void OnDestroy()
    {
        interactor.OnTryInteract -= TryLift; 
    }

    void TryLift(GameObject target)
    {
        //탈 수 있음 속성이 없으면 return
        var newRider = target.GetComponent<Rider>();

        if (newRider == null)
            return;

        //현재 탑승하고 있는 rider 가 있으면 내립니다
        if (CurRider != null)
            OnRiderDrop();

        Lift(newRider);
    }

    /// <summary>
    /// Rider 를 설정합니다
    /// </summary>
    /// <param name="rider"></param>
    public void Lift(Rider rider)
    {
        CurRider = rider;

        RPC_RequestRide();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_RequestRide()
    {
        Debug.Log($"{gameObject.name} 이 {CurRider.gameObject.name} 에게 탑승을 리퀘스트");
        CurRider.Ride(this);
    }

    public void OnRiderDrop()
    {
        //현재 탑승하고 있는 rider 가 없으면 return
        RPC_RequestRiderDrop();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    public void RPC_RequestRiderDrop()
    {
        if (CurRider == null)
            return;

        CurRider.Drop();
        CurRider = null;
    }

}

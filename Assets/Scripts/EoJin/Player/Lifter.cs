using Fusion;
using UnityEngine;

/// <summary>
/// Rideable 을 머리 위에 갖습니다. lifter 의 local 에서만 발현 됩니다.
/// </summary>
public class Lifter : NetworkBehaviour, ILiftable
{
    [SerializeField] private Head head; //나의 머리
    public Head Head => head; //다른 객체에서 나의 머리 확인 가능

    [Networked] public Rider CurRider { get; set; } //현재 탑승한 타겟

    private void Start()
    {
        //머리에서 충돌이 일어났다면 확인합니다
        head.OnHit += OnHeadHit;
    }

    private void OnDestroy()
    {
        if (head != null)
            head.OnHit -= OnHeadHit;
    }

    /// <summary>
    /// 나의 머리에 무언가가 충돌했다면 Rideable 인지 확인합니다
    /// </summary>
    /// <param name="target">머리에 부딪힌 오브젝트</param>
    private void OnHeadHit(GameObject target)
    {
        //탈 수 있음 속성이 없으면 return
        var newRider = target.GetComponent<Rider>();

        if (newRider == null)
            return;

        //현재 탑승하고 있는 rider 가 있으면 더이상 태우지 않습니다
        if (CurRider == null)
            Lift(newRider);
    }

    /// <summary>
    /// Rider 를 설정합니다
    /// </summary>
    /// <param name="rider"></param>
    public void Lift(Rider rider)
    {
        CurRider = rider;
        CurRider.Ride(this);
    }

    public void OnRiderDrop()
    {
        //현재 탑승하고 있는 rider 가 없으면 return
        CurRider = null;
    }

}

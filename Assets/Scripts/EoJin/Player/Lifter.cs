using Fusion;
using UnityEngine;

/// <summary>
/// Rideable 을 옮깁니다
/// </summary>
public class Lifter : NetworkBehaviour, ILiftable
{
    [SerializeField] private Head head; //나의 머리
    public Head Head => head; //다른 객체에서 나의 머리 확인 가능

    private IRideable curRideable; //현재 탑승한 타겟
    private Rigidbody2D rb; //나의 리지드바디(몸무게 확인)
    public Rigidbody2D Rb => rb;

    private void Start()
    {
        rb = GetComponent<Rigidbody2D>();

        //머리에서 충돌이 일어났다면 확인합니다
        head.OnHit += OnHeadHit;
    }
    private void OnDestroy()
    {
        if (head != null)
            head.OnHit -= OnHeadHit;
    }

    private void FixedUpdate()
    {
        if (Object == null || !HasStateAuthority)
            return;

        //현재 누군가 타고 있다면
        if (curRideable != null)
            Lift(); //옮기기
    }

    /// <summary>
    /// Ridable 을 옮기기
    /// </summary>
    public void Lift()
    {
        curRideable.Ride(this);
    }

    /// <summary>
    /// 나의 머리에 무언가가 충돌했다면 Rideable 인지 확인합니다
    /// </summary>
    /// <param name="target">머리에 부딪힌 오브젝트</param>
    private void OnHeadHit(GameObject target)
    {
        if (Object == null || !HasStateAuthority)
            return;

        //이미 누군가 타고 있다면 리턴
        if (curRideable != null)
            return;

        //탈 수 있음 속성이 없으면 리턴
        var newRideable = target.GetComponent<IRideable>();

        if (newRideable == null)
            return;

        var targetRb = target.GetComponent<Rigidbody2D>();

        //나보다 몸무게가 무겁다면 탑승할 수 없음
        if (rb != null && targetRb != null && targetRb.mass > rb.mass)
            return;

        curRideable = newRideable;
    }

    public void OnRiderDrop(IRideable rideable)
    {
        if (curRideable == rideable)
            curRideable = null;
    }
}

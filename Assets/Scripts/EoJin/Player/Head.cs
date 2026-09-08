using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 머리는 누군가를 태울 수 있고, 맞으면 기절 하기도 합니다
/// </summary>
public class Head : MonoBehaviour
{
    /// <summary>
    /// 충돌한 물체를 리턴
    /// </summary>
    public event UnityAction<GameObject> OnHit;

    private void OnCollisionEnter2D(Collision2D collision)
    {
        //머리 위에서 충돌 됐는가
        foreach (ContactPoint2D contact in collision.contacts)
        {
            if (contact.point.y > transform.position.y)
            {
                OnHit?.Invoke(collision.gameObject);
                return;
            }
        }
    }
}

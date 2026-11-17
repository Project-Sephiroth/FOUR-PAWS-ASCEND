using UnityEngine;

// 본체의 콜라이더에 생긴 충돌을 부모의 네트워크 드럼통에 보냅니다.
public sealed class RollingDrumCollision : MonoBehaviour
{
    [SerializeField] private RollingDrum drum;

    private void OnTriggerEnter2D(Collider2D other) => HandleContact(other);

    private void OnTriggerStay2D(Collider2D other) => HandleContact(other);

    private void HandleContact(Collider2D other)
    {
        if (other.GetComponentInParent<Rock>() is { } rock)
            rock.TryHitDrum(drum);
        else
            drum.HandleContact(other);
    }
}

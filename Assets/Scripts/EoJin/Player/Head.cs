using UnityEngine;
using UnityEngine.Events;

public class Head : MonoBehaviour
{
    public event UnityAction<GameObject> OnHit;
    private Collider2D support;
    private Rigidbody2D ownerBody;
    public float Top => support != null ? support.bounds.max.y : transform.position.y;

    private void Awake()
    {
        support = GetComponent<Collider2D>();
        ownerBody = GetComponentInParent<Rigidbody2D>();
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.rigidbody == null || collision.rigidbody == ownerBody)
            return;
        Collider2D otherBody = collision.rigidbody.GetComponent<Collider2D>();
        if (otherBody == null || otherBody.bounds.center.y <= Top || Mathf.Abs(otherBody.bounds.min.y - Top) > 0.15f)
            return;
        for (int i = 0; i < collision.contactCount; i++)
        {
            ContactPoint2D contact = collision.GetContact(i);
            if (contact.point.y >= Top - 0.08f && Mathf.Abs(contact.normal.y) > 0.5f)
            {
                OnHit?.Invoke(collision.rigidbody.gameObject);
                return;
            }
        }
    }
}

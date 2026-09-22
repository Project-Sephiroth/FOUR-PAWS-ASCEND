using UnityEngine;

/// <summary>Presentation of a director-owned projectile slot; no local hit decisions.</summary>
public class PcsProjectile : MonoBehaviour
{
    private SpriteRenderer visual;

    public void Initialize(Sprite sprite)
    {
        visual = gameObject.AddComponent<SpriteRenderer>();
        visual.sprite = sprite;
        visual.color = new Color(0.63f, 0.35f, 0.13f);
        visual.sortingOrder = 30;
        if (sprite != null) transform.localScale = Vector3.one * (0.32f / Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y));
    }

    public void Present(bool active, Vector2 position)
    {
        if (visual != null) visual.enabled = active;
        transform.position = new Vector3(position.x, position.y, 0f);
    }
}

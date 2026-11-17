using UnityEngine;

[RequireComponent(typeof(SpriteRenderer), typeof(Collider2D))]
public sealed class LaserButton : MonoBehaviour
{
    [SerializeField] private LaserHazard beam;
    [SerializeField] private Sprite activeSprite;
    [SerializeField] private Sprite inactiveSprite;

    private SpriteRenderer model;

    private void Awake() => model = GetComponent<SpriteRenderer>();

    internal void OnRockHit() => beam.HitButton();

    private void LateUpdate()
    {
        if (beam.Object != null && beam.Object.IsValid)
            model.sprite = beam.IsOff ? inactiveSprite : activeSprite;
    }
}

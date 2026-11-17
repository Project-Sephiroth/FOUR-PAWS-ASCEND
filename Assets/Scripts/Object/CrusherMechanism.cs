using UnityEngine;

public sealed class CrusherMechanism : MonoBehaviour
{
    [SerializeField] private SpriteRenderer plate;
    [SerializeField] private AnimationClip plateMotion;

    public float Duration => plateMotion != null ? plateMotion.length : 0f;

    public void SamplePlate(float elapsed)
    {
        if (plate == null || plateMotion == null)
            return;

        Transform movingPlate = plate.transform;
        Vector3 position = movingPlate.localPosition;
        plateMotion.SampleAnimation(plate.gameObject, Mathf.Clamp(elapsed, 0f, Duration));
        position.y = movingPlate.localPosition.y;
        movingPlate.localPosition = position;
    }

    internal bool Hits(Bounds body) => plate != null && body.size.sqrMagnitude > 0f && plate.bounds.Intersects(body);

    public bool Hits(Vector3 position)
    {
        if (plate == null)
            return false;

        var bounds = plate.bounds;
        bounds.Expand(new Vector3(0.3f, 0.4f, 2f));
        return bounds.Contains(position);
    }
}

using Fusion;
using UnityEngine;

[RequireComponent(typeof(BoxCollider2D))]
public sealed class FloorStepButton : MonoBehaviour
{
    [SerializeField] private Transform buttonVisual;

    private BoxCollider2D trigger;
    private Vector3 restPosition;

    private void Awake()
    {
        trigger = GetComponent<BoxCollider2D>();
        restPosition = buttonVisual.localPosition;
    }

    internal bool IsPressedBy(NetworkObject player)
    {
        if (!isActiveAndEnabled || !trigger.enabled || !player.TryGetComponent(out Mover mover) ||
            (player.TryGetComponent(out Passenger passenger) && (passenger.IsCarried || passenger.IsFlying)))
            return false;

        Vector2 foot = transform.InverseTransformPoint(mover.FeetPosition);
        Vector2 delta = foot - trigger.offset;
        Vector2 halfSize = trigger.size * 0.5f;
        const float footToleranceBelowButton = 0.3f;
        return Mathf.Abs(delta.x) <= halfSize.x && delta.y <= halfSize.y &&
            delta.y >= -halfSize.y - footToleranceBelowButton;
    }

    internal void ShowPressed(bool pressed) =>
        buttonVisual.localPosition = restPosition + Vector3.down * (pressed ? 0.1f : 0f);
}

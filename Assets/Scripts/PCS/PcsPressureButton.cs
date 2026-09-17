using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider2D))]
[DefaultExecutionOrder(-100)]
public class PcsPressureButton : MonoBehaviour
{
    [SerializeField] private PcsSlidingWall wall;
    [SerializeField] private Collider2D supportCollider;
    [SerializeField, Min(0f)] private float topTolerance = 0.05f;

    private readonly List<Collider2D> overlaps = new List<Collider2D>(16);
    private readonly List<ContactPoint2D> contacts = new List<ContactPoint2D>(32);
    private BoxCollider2D sensor;
    private ContactFilter2D playerFilter;
    private int playerLayer;
    private PcsSlidingWall requestedWall;

    public bool IsPressed { get; private set; }

    private void Awake()
    {
        sensor = GetComponent<BoxCollider2D>();
        playerLayer = LayerMask.NameToLayer("Player");

        if (wall == null || supportCollider == null || supportCollider == sensor ||
            supportCollider.isTrigger || !sensor.isTrigger || gameObject.layer != 0 ||
            playerLayer < 0 || !IsValidTolerance())
        {
            Debug.LogError("PcsPressureButton requires a wall, a separate non-trigger supportCollider, " +
                "a trigger BoxCollider2D on the Default layer, an existing Player layer, " +
                "and a finite, non-negative topTolerance.", this);
            enabled = false;
            return;
        }

        playerFilter.SetLayerMask(1 << playerLayer);
        playerFilter.useTriggers = false;
    }

    private void FixedUpdate()
    {
        SetPressed(HasStandingPlayer());
    }

    private bool HasStandingPlayer()
    {
        if (!IsAvailable(sensor) || !sensor.isTrigger || gameObject.layer != 0 ||
            !IsAvailable(supportCollider) || supportCollider.isTrigger || !IsValidTolerance())
            return false;

        // Query current physics state so missed exit callbacks cannot leave the button held.
        // Lists retain their capacity and only grow when a new occupancy high-water mark is reached.
        overlaps.Clear();
        contacts.Clear();
        sensor.Overlap(playerFilter, overlaps);
        supportCollider.GetContacts(contacts);

        Bounds supportBounds = supportCollider.bounds;
        float top = supportBounds.max.y;

        for (int i = 0; i < overlaps.Count; i++)
        {
            Collider2D candidate = overlaps[i];
            if (!IsAvailable(candidate) || candidate.isTrigger || candidate.gameObject.layer != playerLayer ||
                candidate.attachedRigidbody == null)
                continue;

            Bounds bodyBounds = candidate.bounds;
            if (bodyBounds.center.y <= top || Mathf.Abs(bodyBounds.min.y - top) > topTolerance ||
                bodyBounds.max.x <= supportBounds.min.x || bodyBounds.min.x >= supportBounds.max.x)
                continue;

            for (int j = 0; j < contacts.Count; j++)
            {
                ContactPoint2D contact = contacts[j];
                bool matchesBody = (contact.collider == candidate && contact.otherCollider == supportCollider) ||
                    (contact.otherCollider == candidate && contact.collider == supportCollider);

                // A sensor overlap alone also includes airborne and side approaches.
                // The support must have a responding contact on its horizontal top surface.
                if (matchesBody && contact.enabled && Mathf.Abs(contact.normal.y) >= 0.5f &&
                    Mathf.Abs(contact.point.y - top) <= topTolerance)
                    return true;
            }
        }

        return false;
    }

    private static bool IsAvailable(Collider2D collider)
    {
        return collider != null && collider.enabled && collider.gameObject.activeInHierarchy &&
            (collider.attachedRigidbody == null || collider.attachedRigidbody.simulated);
    }

    private bool IsValidTolerance()
    {
        return !float.IsNaN(topTolerance) && !float.IsInfinity(topTolerance) && topTolerance >= 0f;
    }

    private void SetPressed(bool pressed)
    {
        if (requestedWall != null && requestedWall != wall)
            requestedWall.SetOpen(false);

        requestedWall = wall;
        IsPressed = pressed;
        if (requestedWall != null)
            requestedWall.SetOpen(pressed);
    }

    private void OnDisable()
    {
        SetPressed(false);
        overlaps.Clear();
        contacts.Clear();
    }
}

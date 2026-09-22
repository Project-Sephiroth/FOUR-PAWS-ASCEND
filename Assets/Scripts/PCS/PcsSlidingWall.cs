using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
public class PcsSlidingWall : MonoBehaviour
{
    [SerializeField] private Transform openTarget;
    [SerializeField, Min(0.01f)] private float speed = 1f;

    private const float AlignmentTolerance = 0.001f;
    private Rigidbody2D body;
    private Vector2 closedPosition;
    private Vector2 openPosition;
    private bool openRequested;
    private bool initialized;
    private bool externalDrive;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        closedPosition = body.position;

        if (body.bodyType != RigidbodyType2D.Kinematic || !body.simulated)
        {
            Fail("requires a simulated Kinematic Rigidbody2D.");
            return;
        }

        if (externalDrive)
        {
            initialized = true;
            StopMotion();
            return;
        }

        if (openTarget == null)
        {
            Fail("openTarget is not assigned.");
            return;
        }

        if (openTarget == transform || openTarget.IsChildOf(transform))
        {
            Fail("openTarget must be independent of the wall, not the wall itself or its child.");
            return;
        }

        Vector3 targetPosition = openTarget.position;
        if (!IsFinite(closedPosition.x) || !IsFinite(closedPosition.y) || !IsFinite(transform.position.z) ||
            !IsFinite(targetPosition.x) || !IsFinite(targetPosition.y) || !IsFinite(targetPosition.z) ||
            Mathf.Abs(targetPosition.x - closedPosition.x) > AlignmentTolerance ||
            Mathf.Abs(targetPosition.z - transform.position.z) > AlignmentTolerance ||
            targetPosition.y <= closedPosition.y + AlignmentTolerance)
        {
            Fail("openTarget must have a finite world position directly above the closed position (same X and Z).");
            return;
        }

        if (!IsValidSpeed())
        {
            Fail("speed must be finite and greater than zero.");
            return;
        }

        // Capture both endpoints once; repeated requests never reset the movement origin.
        openPosition = new Vector2(closedPosition.x, targetPosition.y);
        initialized = true;
        StopMotion();
    }

    public void SetOpen(bool open)
    {
        openRequested = open;
    }

    public void UseExternalDrive() { externalDrive = true; }

    public Vector2 MoveAuthority(Vector2 from, Vector2 target, float movementSpeed, float deltaTime)
    {
        Vector2 next = Vector2.MoveTowards(from, target, movementSpeed * deltaTime);
        if (body != null) body.MovePosition(next);
        return next;
    }

    public void ApplyNetworkPose(Vector2 position)
    {
        if (body == null) body = GetComponent<Rigidbody2D>();
        body.position = position;
        StopMotion();
    }

    private void FixedUpdate()
    {
        if (!initialized || externalDrive)
            return;

        if (body.bodyType != RigidbodyType2D.Kinematic || !body.simulated)
        {
            Fail("requires its Rigidbody2D to remain Kinematic and simulated.");
            return;
        }

        float step = speed * Time.fixedDeltaTime;
        if (!IsValidSpeed() || !IsFinite(step) || step <= 0f ||
            !IsFinite(body.position.x) || !IsFinite(body.position.y))
        {
            Fail("cannot move with a non-finite position or a non-positive/non-finite speed or physics step.");
            return;
        }

        Vector2 target = openRequested ? openPosition : closedPosition;
        Vector2 current = body.position;
        if (current.x == target.x && current.y == target.y)
        {
            StopMotion();
            return;
        }

        body.MovePosition(Vector2.MoveTowards(current, target, step));
    }

    private bool IsValidSpeed()
    {
        return IsFinite(speed) && speed > 0f;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private void StopMotion()
    {
        if (body == null || body.bodyType != RigidbodyType2D.Kinematic)
            return;

        body.linearVelocity = Vector2.zero;
        body.angularVelocity = 0f;
    }

    private void Fail(string reason)
    {
        Debug.LogError("PcsSlidingWall " + reason, this);
        initialized = false;
        StopMotion();
        enabled = false;
    }

    private void OnDisable()
    {
        StopMotion();
    }
}

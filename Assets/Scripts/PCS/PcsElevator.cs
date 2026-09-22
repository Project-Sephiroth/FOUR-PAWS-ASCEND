using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
public class PcsElevator : MonoBehaviour
{
    [Tooltip("An independent world-space target directly above the starting position. Captured when Play starts.")]
    [SerializeField] private Transform upperTarget;
    [Tooltip("Movement speed in world units per second.")]
    [SerializeField, Min(0.01f)] private float speed = 1f;
    [Tooltip("Checked: rise to upperTarget. Unchecked: return to the starting position. Can be changed during Play.")]
    [SerializeField] private bool raised = true;
    [Tooltip("The visual-only GIDONG SpriteRenderer, directly parented to this platform.")]
    [SerializeField] private SpriteRenderer column;
    [Tooltip("Visible bottom edge within the Sprite rect, measured from bottom (0) to top (1). Captured at Play start.")]
    [SerializeField, Range(0f, 1f)] private float columnBottom = 237f / 1886f;
    [Tooltip("Visible top edge within the Sprite rect. Defaults exclude the faint padding in the current GIDONG image.")]
    [SerializeField, Range(0f, 1f)] private float columnTop = 1704f / 1886f;
    [Tooltip("Keep the visible upper end fixed for a hanging platform. Otherwise keep the lower end fixed. Captured at Play start.")]
    [SerializeField] private bool columnFixedAtTop;

    private const float AlignmentTolerance = 0.001f;
    private Rigidbody2D body;
    private Vector2 lowerPosition;
    private Vector2 upperPosition;
    private bool initialized;
    private bool externalDrive;

    private Transform columnTransform;
    private Sprite columnSprite;
    private Vector3 initialColumnScale;
    private Vector3 initialColumnPosition;
    private Vector3 initialPlatformScale;
    private Quaternion initialPlatformRotation;
    private Vector3 initialPlatformPosition;
    private Vector3 fixedColumnEndWorld;
    private Vector3 initialMovingColumnEndWorld;
    private bool fixedEndIsLocalTop;
    private bool fixedEndIsWorldTop;
    private float visibleColumnLocalHeight;
    private float columnLocalBottomY;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        if (body.bodyType != RigidbodyType2D.Kinematic || !body.simulated)
        {
            Fail("Set Rigidbody2D to Kinematic and enable Simulated.");
            return;
        }

        if ((body.constraints & RigidbodyConstraints2D.FreezePositionY) != 0)
        {
            Fail("Uncheck Rigidbody2D Freeze Position Y so the platform can move vertically.");
            return;
        }

        if (externalDrive)
        {
            lowerPosition = body.position;
            if (column != null && !InitializeColumn()) return;
            initialized = true;
            StopMotion();
            return;
        }

        if (upperTarget == null)
        {
            Fail("Assign upperTarget to a separate target above the platform.");
            return;
        }

        if (upperTarget == transform || upperTarget.IsChildOf(transform))
        {
            Fail("upperTarget must not be the platform itself or a child of the platform.");
            return;
        }

        lowerPosition = body.position;
        Vector3 targetPosition = upperTarget.position;
        if (!IsFinite(transform.position) || !IsFinite(targetPosition) ||
            !IsFinite(lowerPosition.x) || !IsFinite(lowerPosition.y) ||
            Mathf.Abs(targetPosition.x - lowerPosition.x) > AlignmentTolerance ||
            Mathf.Abs(targetPosition.z - transform.position.z) > AlignmentTolerance ||
            !IsFinite(targetPosition.y - lowerPosition.y) ||
            targetPosition.y <= lowerPosition.y + AlignmentTolerance)
        {
            Fail("upperTarget must have finite coordinates, the same world X/Z, and a higher world Y than the platform.");
            return;
        }

        if (!IsFinite(speed) || speed <= 0f)
        {
            Fail("speed must be finite and greater than zero.");
            return;
        }

        if (!InitializeColumn())
            return;

        upperPosition = new Vector2(lowerPosition.x, targetPosition.y);
        initialized = true;
        StopMotion();
    }

    private bool InitializeColumn()
    {
        if (column == null || column.sprite == null)
            return Fail("Assign column to the GIDONG SpriteRenderer with a Sprite.");

        columnTransform = column.transform;
        if (columnTransform.parent != transform)
            return Fail("column must be a direct child of the platform, as in ground-long > GIDONG.");

        if (column.drawMode != SpriteDrawMode.Simple || column.flipY)
            return Fail("Use Simple draw mode and disable Flip Y on column.");

        if (!IsFinite(columnBottom) || !IsFinite(columnTop) ||
            columnBottom < 0f || columnTop > 1f || columnTop <= columnBottom)
            return Fail("Set visible column edges so 0 <= columnBottom < columnTop <= 1.");

        if (columnTransform.GetComponentInChildren<Collider2D>(true) != null ||
            columnTransform.GetComponentInChildren<Rigidbody2D>(true) != null)
            return Fail("column must be visual-only. Put the platform's Collider2D and Rigidbody2D on the platform root.");

        initialPlatformScale = transform.lossyScale;
        initialPlatformRotation = transform.rotation;
        initialColumnScale = columnTransform.localScale;
        if (!IsPositiveScale(initialPlatformScale) || !IsPositiveScale(initialColumnScale) ||
            !IsPlanarRotation(initialPlatformRotation) || !IsPlanarRotation(columnTransform.localRotation))
            return Fail("Use positive, finite scales and only 2D Z rotations for the platform and column.");

        columnSprite = column.sprite;
        float pixelHeight = columnSprite.rect.height;
        float pixelsPerUnit = columnSprite.pixelsPerUnit;
        if (!IsFinite(pixelHeight) || pixelHeight <= 0f || !IsFinite(pixelsPerUnit) || pixelsPerUnit <= 0f)
            return Fail("column Sprite must have a positive, finite rect height and pixels per unit.");

        // GIDONG has faint pixels in otherwise empty padding. Sprite bounds do not identify
        // the visible cap and foot; use their measured positions inside the untrimmed Sprite rect.
        columnLocalBottomY = (pixelHeight * columnBottom - columnSprite.pivot.y) / pixelsPerUnit;
        visibleColumnLocalHeight = pixelHeight * (columnTop - columnBottom) / pixelsPerUnit;
        Vector3 bottomWorld = columnTransform.TransformPoint(new Vector3(0f, columnLocalBottomY, 0f));
        Vector3 topWorld = columnTransform.TransformPoint(new Vector3(0f, columnLocalBottomY + visibleColumnLocalHeight, 0f));
        initialColumnPosition = columnTransform.localPosition;
        initialPlatformPosition = transform.position;

        if (!IsFinite(visibleColumnLocalHeight) || visibleColumnLocalHeight <= 0f ||
            !IsFinite(columnLocalBottomY) || !IsFinite(bottomWorld) || !IsFinite(topWorld) ||
            !IsFinite(initialColumnPosition) || !IsFinite(initialPlatformPosition) ||
            Mathf.Abs(topWorld.y - bottomWorld.y) <= AlignmentTolerance)
            return Fail("column must have a finite position and a positive Sprite height.");

        // A rotated platform can put the Sprite's local bottom at the ceiling.
        // Choose the fixed end in world space without flipping or replacing its artwork.
        fixedEndIsWorldTop = columnFixedAtTop;
        fixedEndIsLocalTop = (topWorld.y > bottomWorld.y) == fixedEndIsWorldTop;
        fixedColumnEndWorld = fixedEndIsLocalTop ? topWorld : bottomWorld;
        initialMovingColumnEndWorld = fixedEndIsLocalTop ? bottomWorld : topWorld;
        return true;
    }

    public void SetRaised(bool value)
    {
        raised = value;
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

        if (body == null || body.bodyType != RigidbodyType2D.Kinematic || !body.simulated ||
            (body.constraints & RigidbodyConstraints2D.FreezePositionY) != 0)
        {
            Fail("Keep the Rigidbody2D Kinematic, simulated, and free to move on Y.");
            return;
        }

        float step = speed * Time.fixedDeltaTime;
        Vector2 current = body.position;
        if (!IsFinite(speed) || speed <= 0f || !IsFinite(step) || step <= 0f ||
            !IsFinite(current.x) || !IsFinite(current.y))
        {
            Fail("Movement requires a finite position and a positive, finite speed and physics step.");
            return;
        }

        Vector2 target = raised ? upperPosition : lowerPosition;
        if (current.x == target.x && current.y == target.y)
        {
            StopMotion();
            return;
        }

        body.MovePosition(Vector2.MoveTowards(current, target, step));
    }

    private void LateUpdate()
    {
        if (!initialized || (externalDrive && column == null))
            return;

        if (column == null || columnTransform == null || columnTransform.parent != transform ||
            column.sprite != columnSprite || column.drawMode != SpriteDrawMode.Simple || column.flipY ||
            !IsFinite(transform.lossyScale) ||
            (transform.lossyScale - initialPlatformScale).sqrMagnitude > 0.000001f ||
            Quaternion.Angle(transform.rotation, initialPlatformRotation) > 0.01f)
        {
            Fail("Keep the column's parent, Sprite, draw mode, Flip Y, platform scale and platform rotation unchanged during Play.");
            return;
        }

        // Use the displayed platform pose, including Rigidbody interpolation, not its next physics target.
        Vector3 movingEndWorld = initialMovingColumnEndWorld + transform.position - initialPlatformPosition;
        float verticalSpan = fixedEndIsWorldTop ? fixedColumnEndWorld.y - movingEndWorld.y :
            movingEndWorld.y - fixedColumnEndWorld.y;
        Vector3 fixedEndLocal = transform.InverseTransformPoint(fixedColumnEndWorld);
        Vector3 movingEndLocal = transform.InverseTransformPoint(movingEndWorld);
        Vector3 bottomLocal = fixedEndIsLocalTop ? movingEndLocal : fixedEndLocal;
        Vector3 topLocal = fixedEndIsLocalTop ? fixedEndLocal : movingEndLocal;
        Vector3 columnAxis = topLocal - bottomLocal;
        Vector3 scale = initialColumnScale;
        scale.y = columnAxis.magnitude / visibleColumnLocalHeight;
        float angle = Mathf.Atan2(-columnAxis.x, columnAxis.y) * Mathf.Rad2Deg;
        Quaternion rotation = Quaternion.Euler(0f, 0f, angle);
        Vector3 position = bottomLocal - rotation * new Vector3(0f, columnLocalBottomY * scale.y, 0f);
        position.z = initialColumnPosition.z;

        if (!IsFinite(verticalSpan) || verticalSpan <= 0f || !IsPositiveScale(scale) ||
            !IsFinite(angle) || !IsFinite(position))
        {
            Fail("The platform crossed the fixed column end or produced an invalid column height.");
            return;
        }

        // Resolve both visible ends in the parent's coordinates. This also preserves their
        // attachment for inverted artwork and non-uniform platform scale, without an initial snap.
        // Only the visual child changes scale/position; the platform is moved exclusively by MovePosition.
        columnTransform.localRotation = rotation;
        columnTransform.localScale = scale;
        columnTransform.localPosition = position;
    }

    private static bool IsPlanarRotation(Quaternion rotation)
    {
        return Vector3.Dot(rotation * Vector3.forward, Vector3.forward) > 0.999999f;
    }

    private static bool IsPositiveScale(Vector3 value)
    {
        return IsFinite(value) && value.x > 0f && value.y > 0f && value.z > 0f;
    }

    private static bool IsFinite(Vector3 value)
    {
        return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
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

    private bool Fail(string reason)
    {
        Debug.LogError("PcsElevator: " + reason, this);
        initialized = false;
        StopMotion();
        enabled = false;
        return false;
    }

    private void OnDisable()
    {
        StopMotion();
    }
}

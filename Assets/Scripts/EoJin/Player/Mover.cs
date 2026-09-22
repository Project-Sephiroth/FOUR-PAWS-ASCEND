using System.Collections.Generic;
using Fusion;
using UnityEngine;

public class Mover : NetworkBehaviour, IStateAuthorityChanged
{
    [Header("Movement")]
    [SerializeField] private float speed = 2f;
    [Header("Jump")]
    [SerializeField] private float jumpForce = 5f;
    public bool CanJump = true;
    [SerializeField] private bool canDoubleJump = false;
    [Header("Ground check")]
    [SerializeField] private Transform groundCheck;
    [SerializeField] private float groundCheckRadius = 0.15f;
    [SerializeField] private LayerMask groundLayer;

    private PlayerInput input;
    private Rigidbody2D rb;
    private Collider2D bodyCollider;
    private Rider rider;
    private PcsPlayerAbilities abilities;
    private NetworkTransform networkTransform;
    private float normalGravity;
    private RigidbodyType2D normalBodyType;
    private bool normalFullKinematicContacts;
    private bool doubleJumped;
    private bool launchQueued;
    private Vector2 launchVelocity;
    private float launchRemaining;
    private readonly List<ContactPoint2D> groundContacts = new List<ContactPoint2D>(16);
    private readonly List<RaycastHit2D> groundCasts = new List<RaycastHit2D>(16);
    private ContactFilter2D groundFilter;
    private Vector2 groundNormal = Vector2.up;
    private bool contactSupport;
    private Rigidbody2D previousSupport;
    private Vector2 previousSupportPosition;

    public Rigidbody2D Body => rb;
    public Collider2D BodyCollider => bodyCollider;
    public Rider Rider => rider;
    public float MoveSpeed => speed;
    public float JumpSpeed => jumpForce;
    public bool Grounded { get; private set; }

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        bodyCollider = GetComponent<Collider2D>();
        input = GetComponent<PlayerInput>();
        rider = GetComponent<Rider>();
        abilities = GetComponent<PcsPlayerAbilities>();
        networkTransform = GetComponent<NetworkTransform>();
        normalGravity = rb.gravityScale;
        normalBodyType = rb.bodyType;
        normalFullKinematicContacts = rb.useFullKinematicContacts;
        groundFilter.SetLayerMask(groundLayer);
        groundFilter.useTriggers = false;
    }

    public override void Spawned()
    {
        ApplyPhysicsAuthority();
        abilities = GetComponent<PcsPlayerAbilities>();
    }

    public void StateAuthorityChanged()
    {
        ApplyPhysicsAuthority();
    }

    private void ApplyPhysicsAuthority()
    {
        if (input != null)
        {
            input.ResetInputState();
            input.enabled = HasStateAuthority;
        }
        if (rb == null)
            return;
        // NetworkTransform drives proxies. Only the owner simulates the dynamic body.
        rb.bodyType = HasStateAuthority ? normalBodyType : RigidbodyType2D.Kinematic;
        rb.useFullKinematicContacts = HasStateAuthority ? normalFullKinematicContacts : true;
        rb.gravityScale = normalGravity;
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;
        previousSupport = null;
        Grounded = false;
        launchQueued = false;
        launchRemaining = 0f;
        if (abilities != null)
            abilities.ClearTransientMotion();
    }

    public override void FixedUpdateNetwork()
    {
        if (Object == null || !Object.IsValid || !HasStateAuthority || input == null || rb == null || !rb.simulated)
            return;
        float deltaTime = Runner.DeltaTime;
        if (abilities != null && abilities.isActiveAndEnabled)
            abilities.SimulateInput();

        if (!PcsPlayerAbilities.CanParticipate(Object))
        {
            // A late joiner stays on the safe waiting deck while gravity finishes settling.
            rb.gravityScale = normalGravity;
            rb.linearVelocity = new Vector2(0f, Mathf.Min(0f, rb.linearVelocity.y));
            input.MoveInput = Vector2.zero;
            input.JumpInput = false;
            previousSupport = null;
            Grounded = false;
            return;
        }

        if (rider != null && rider.IsRiding)
        {
            if (input.JumpInput)
            {
                input.JumpInput = false;
                rider.Drop();
                QueueLaunch(Vector2.up * jumpForce);
            }
            else if (rider.TryGetCarryPosition(out Vector2 carryPosition))
            {
                rb.gravityScale = 0f;
                rb.linearVelocity = Vector2.zero;
                rb.MovePosition(carryPosition);
                Grounded = false;
                previousSupport = null;
                return;
            }
        }

        if (abilities != null && abilities.isActiveAndEnabled && abilities.TryGetMotionVelocity(deltaTime, out Vector2 controlledVelocity))
        {
            rb.gravityScale = 0f;
            rb.linearVelocity = controlledVelocity;
            Grounded = false;
            previousSupport = null;
            return;
        }

        rb.gravityScale = normalGravity;
        Vector2 supportVelocity = FindSupportVelocity(deltaTime);
        if (launchQueued)
        {
            launchQueued = false;
            rb.linearVelocity = launchVelocity;
            // Preserve a thrown horizontal impulse; a ladder jump must still accept steering.
            launchRemaining = Mathf.Abs(launchVelocity.x) > 0.01f ? 0.25f : 0f;
            input.JumpInput = false;
            doubleJumped = false;
            Grounded = false;
        }

        if (Grounded)
            doubleJumped = false;
        float movement = input.CanMoveInput ? input.MoveInput.x * speed : 0f;
        if (Grounded && launchRemaining <= 0f)
        {
            // Follow the surface at the existing walk speed. A positive slope velocity is not a jump.
            Vector2 tangent = new Vector2(groundNormal.y, -groundNormal.x);
            rb.linearVelocity = supportVelocity + tangent * movement;
            if (contactSupport && groundNormal.y < 0.999f)
                rb.gravityScale = 0f;
        }
        else
            rb.linearVelocity = new Vector2(launchRemaining > 0f ? rb.linearVelocity.x : movement, rb.linearVelocity.y);
        launchRemaining = Mathf.Max(0f, launchRemaining - deltaTime);
        Jump(supportVelocity);
    }

    private Vector2 FindSupportVelocity(float deltaTime)
    {
        Grounded = false;
        groundNormal = Vector2.up;
        contactSupport = false;
        if (groundCheck == null || bodyCollider == null)
        {
            previousSupport = null;
            return Vector2.zero;
        }
        Collider2D support = null;
        Vector2 center = bodyCollider.bounds.center;
        groundContacts.Clear();
        rb.GetContacts(groundFilter, groundContacts);
        foreach (ContactPoint2D contact in groundContacts)
        {
            Collider2D hit = contact.collider != null && contact.collider.attachedRigidbody == rb ? contact.otherCollider : contact.collider;
            if (!contact.enabled || !IsGroundCandidate(hit) || contact.point.y > center.y)
                continue;
            Vector2 normal = contact.normal;
            if (Vector2.Dot(normal, center - contact.point) < 0f)
                normal = -normal;
            if (normal.y >= 0.55f && (support == null || normal.y > groundNormal.y))
            {
                support = hit;
                groundNormal = normal;
            }
        }
        contactSupport = support != null;
        if (support == null)
        {
            // A short shape cast keeps descending feet attached across contact gaps and ramp joints.
            // An overlapping cast has a synthetic upward normal, so only real positive-distance hits qualify.
            // Cast-only support keeps gravity until physical contact so idle feet cannot hover above a slope.
            groundCasts.Clear();
            bodyCollider.Cast(Vector2.down, groundFilter, groundCasts, groundCheckRadius + 0.05f);
            float nearest = float.PositiveInfinity;
            foreach (RaycastHit2D hit in groundCasts)
            {
                if (!IsGroundCandidate(hit.collider) || hit.distance <= 0f || hit.normal.y < 0.55f ||
                    hit.point.y > center.y || hit.distance >= nearest)
                    continue;
                support = hit.collider;
                groundNormal = hit.normal;
                nearest = hit.distance;
            }
        }
        if (support == null)
        {
            previousSupport = null;
            return Vector2.zero;
        }
        Rigidbody2D supportBody = support.attachedRigidbody;
        Vector2 velocity = supportBody != null ? supportBody.linearVelocity : Vector2.zero;
        if (supportBody != null && previousSupport == supportBody && deltaTime > 0f && velocity.sqrMagnitude < 0.000001f)
            velocity = (supportBody.position - previousSupportPosition) / deltaTime;
        previousSupport = supportBody;
        if (supportBody != null)
            previousSupportPosition = supportBody.position;
        Grounded = Vector2.Dot(rb.linearVelocity - velocity, groundNormal) <= 0.1f;
        return velocity;
    }

    private bool IsGroundCandidate(Collider2D hit)
    {
        // Ignore the player's own Ground-layer head, triggers and non-responding physics bodies.
        return hit != null && hit.enabled && hit.gameObject.activeInHierarchy && !hit.isTrigger &&
            hit.attachedRigidbody != rb && !hit.transform.IsChildOf(transform) &&
            (hit.attachedRigidbody == null || hit.attachedRigidbody.simulated) &&
            (groundLayer.value & (1 << hit.gameObject.layer)) != 0;
    }

    private void Jump(Vector2 supportVelocity)
    {
        if (!input.JumpInput)
            return;
        input.JumpInput = false;
        if (!CanJump || !input.CanJumpInput)
            return;
        if (!Grounded)
        {
            if (!canDoubleJump || doubleJumped)
                return;
            doubleJumped = true;
        }
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce + (Grounded ? Mathf.Max(0f, supportVelocity.y) : 0f));
        rb.gravityScale = normalGravity;
        Grounded = false;
    }

    public void QueueLaunch(Vector2 velocity)
    {
        if (Object == null || !Object.IsValid || !HasStateAuthority)
            return;
        launchVelocity = velocity;
        launchQueued = true;
    }

    // Checkpoint recovery is explicit; ordinary movement, pulls and boarding never teleport.
    public void ResetAt(Vector2 position)
    {
        if (Object == null || !Object.IsValid || !HasStateAuthority || rb == null)
            return;
        if (rider != null)
            rider.Drop();
        Lifter lifter = GetComponent<Lifter>();
        if (lifter != null)
            lifter.Release(Vector2.zero);
        if (abilities != null)
            abilities.ClearTransientMotion();
        launchQueued = false;
        launchRemaining = 0f;
        doubleJumped = false;
        Grounded = false;
        previousSupport = null;
        rb.gravityScale = normalGravity;
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;
        rb.position = position;
        if (networkTransform != null)
            networkTransform.Teleport(new Vector3(position.x, position.y, transform.position.z));
        if (input != null)
            input.ResetInputState();
    }

    private void OnDisable()
    {
        if (rb != null)
            rb.gravityScale = normalGravity;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (groundCheck != null)
            Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
    }
#endif
}

using Fusion;
using UnityEngine;

public class DroneRobot : RobotBase
{
    [SerializeField] private float flightHeight = 0.65f;
    [SerializeField] private LayerMask flightObstacles;
    [SerializeField] private AnimationClip deathAnimation;

    private const float Clearance = 0.025f;
    [SerializeField] private float firingRange = 3f;
    private BoxCollider2D flightHitbox;
    private readonly RaycastHit2D[] flightHits = new RaycastHit2D[4];
    private Vector2 bypassDirection;
    private int groundMask;
    private readonly Collider2D[] groundContacts = new Collider2D[1];
    [Networked] private NetworkBool HasGroundContact { get; set; }

    protected override bool FlipModel => true;
    protected override float RemovalDuration => deathAnimation != null
        ? Mathf.Max(base.RemovalDuration, deathAnimation.length) : base.RemovalDuration;

    protected override void Awake()
    {
        base.Awake();
        flightHitbox = GetComponent<BoxCollider2D>();
        groundMask = LayerMask.GetMask("Ground");
    }

    protected override void UpdateMovement()
    {
        if (flightHitbox == null)
            return;

        if (HasGroundContact)
        {
            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(groundMask);
            Bounds bounds = flightHitbox.bounds;
            Vector2 size = (Vector2)bounds.size + Vector2.one * (2f * Clearance + 0.01f);
            HasGroundContact = Physics2D.OverlapBox(bounds.center, size, 0f, filter, groundContacts) > 0;
        }

        Vector2 position = transform.position;
        bool contactChase = Encounter != null && Encounter.UsesContactDrones;
        var target = BranchPuzzle.FindClosestPlayer(Runner, position, hackingOnly: !contactChase);
        if (target != null)
        {
            Vector2 targetPoint = GetTargetPoint(target);
            float distanceSquared = (targetPoint - position).sqrMagnitude;
            if (contactChase)
            {
                Chase(position, targetPoint);
                if (distanceSquared <= 0.7f * 0.7f && CanShootFromHere(target))
                    TryContactAttack(target);
            }
            else if (UsesRangedAttack && distanceSquared <= firingRange * firingRange && CanShootFromHere(target))
            {
                bypassDirection = Vector2.zero;
                FacingLeft = targetPoint.x < position.x;
                if (!UpdateRangedAttack(target))
                    State = RobotState.Aim;
            }
            else
            {
                UpdateRangedAttack(null);
                Chase(position, targetPoint);
            }
            return;
        }

        UpdateRangedAttack(null);
        bypassDirection = Vector2.zero;
        if (contactChase)
        {
            State = RobotState.Idle;
            return;
        }

        float edge = GoingRight ? patrolX.y : patrolX.x;
        float span = Mathf.Max(0.01f, patrolX.y - patrolX.x);
        float progress = Mathf.Clamp01((position.x - patrolX.x) / span);
        float height = spawnPosition.y + Mathf.Sin(progress * Mathf.PI) * flightHeight;
        Vector2 step = Vector2.ClampMagnitude(new Vector2(edge, height) - position, speed * Runner.DeltaTime);
        Vector2 shift = ClearStep(step, out bool hitsGround);
        ApplyStep(shift, RobotState.Patrol, hitsGround);
        if (shift.sqrMagnitude < step.sqrMagnitude * 0.99f || Mathf.Abs(position.x + shift.x - edge) < Clearance)
            GoingRight = !GoingRight;
    }

    private void Chase(Vector2 position, Vector2 target)
    {
        Vector2 delta = target - position;

        // 바로 갈 수 없으면 벽면을 따라 돌다가 표적 쪽 길이 다시 열릴 때 방향을 바꿉니다.
        float stepSize = speed * Runner.DeltaTime;
        Vector2 direct = Vector2.ClampMagnitude(delta, stepSize);
        bool hasDirectPath = ClearStep(delta).sqrMagnitude >= delta.sqrMagnitude * 0.99f;
        if (hasDirectPath)
            bypassDirection = Vector2.zero;

        Vector2 step = bypassDirection != Vector2.zero ? bypassDirection * stepSize : direct;
        Vector2 shift = ClearStep(step, out bool hitsGround);
        if (shift.sqrMagnitude < direct.sqrMagnitude * 0.25f)
        {
            Vector2 horizontal = delta.x < 0f ? Vector2.left : Vector2.right;
            Vector2 vertical = delta.y < 0f ? Vector2.down : Vector2.up;
            Vector2 preferred = Mathf.Abs(delta.x) >= Mathf.Abs(delta.y) ? horizontal : vertical;
            Vector2 secondary = preferred == horizontal ? vertical : horizontal;
            shift = FindBypass(stepSize, preferred, secondary);
        }

        ApplyStep(shift, RobotState.Chase, hitsGround);
    }

    private Vector2 FindBypass(float stepSize, Vector2 preferred, Vector2 secondary)
    {
        Vector2 shift = ClearStep(preferred * stepSize);
        if (shift.sqrMagnitude < stepSize * stepSize * 0.25f)
            shift = ClearStep(secondary * stepSize);
        if (shift.sqrMagnitude < stepSize * stepSize * 0.25f)
            shift = ClearStep(-secondary * stepSize);
        if (shift.sqrMagnitude < stepSize * stepSize * 0.25f)
            shift = ClearStep(-preferred * stepSize);

        bypassDirection = shift.normalized;
        return shift;
    }

    private Vector2 ClearStep(Vector2 step) => ClearStep(step, out _);

    private Vector2 ClearStep(Vector2 step, out bool hitsGround)
    {
        hitsGround = false;
        float distance = step.magnitude;
        if (distance < 0.0001f)
            return Vector2.zero;

        var filter = new ContactFilter2D { useTriggers = false };
        filter.SetLayerMask(flightObstacles.value | groundMask);
        Bounds bounds = flightHitbox.bounds;
        Vector2 direction = step / distance;
        int count = Physics2D.BoxCast(bounds.center, bounds.size, 0f, direction, filter, flightHits, distance + Clearance);
        float nearest = distance + Clearance;
        for (int i = 0; i < count; i++)
        {
            var hit = flightHits[i];
            if (hit.distance > nearest)
                continue;

            if (hit.distance < nearest)
                hitsGround = false;
            nearest = hit.distance;
            hitsGround |= (groundMask & (1 << hit.collider.gameObject.layer)) != 0;
        }
        return direction * Mathf.Clamp(nearest - Clearance, 0f, distance);
    }

    internal void HitGround()
    {
        HasGroundContact = true;
        TakeGroundImpact();
    }

    private void ApplyStep(Vector2 shift, RobotState movingState, bool hitsGround)
    {
        transform.position += (Vector3)shift;
        if (Mathf.Abs(shift.x) > 0.001f)
            FacingLeft = shift.x < 0f;
        State = shift.sqrMagnitude > 0.000001f ? movingState : RobotState.Idle;
        if (hitsGround && !HasGroundContact)
            HitGround();
    }
}

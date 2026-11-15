using System.Collections.Generic;
using Fusion;
using UnityEngine;

// 돌이 날아가는 위치는 MovingObject가 계산. 해당 클래스는 충돌 판정만 합니다
public class Rock : NetworkBehaviour
{
    private readonly List<RaycastHit2D> hits = new(16);
    private readonly List<Collider2D> launchOverlaps = new(8);
    private readonly List<Collider2D> overlaps = new(8);
    private const float ImpactVisibleSeconds = 0.15f;
    [Networked] private TickTimer ImpactTimer { get; set; }
    private bool launched;
    private Rigidbody2D body;
    private MovingObject movingObject;
    private Transform spriteTransform;
    private Vector2 flightFrom;
    private Vector2 flightTo;
    private float steppedAt;
    private float stepDuration;
    internal bool HasImpacted => ImpactTimer.IsRunning;
    [Networked] private Thrower LaunchOwner { get; set; } //돌 소유자는 돌 충돌에서 벗어나기 위해 캐싱

    
    private Collider2D[] rockColliders; //돌과 던진 캐릭터는 충돌체가 여러 개일 수 있어 배열로 모으기
    private Collider2D[] throwerColliders;
    private Thrower collisionOwner;

    private void Awake()
    {
        rockColliders = GetComponentsInChildren<Collider2D>();
        body = GetComponent<Rigidbody2D>();
        movingObject = GetComponent<MovingObject>();
        spriteTransform = transform.Find("Flight Visual");
    }

    public void SetThrower(Thrower thrower)
    {
        if (HasStateAuthority)
            LaunchOwner = thrower;
    }

    public override void Render()
    {
        IgnoreThrowerContacts();
        if (spriteTransform == null || !HasStateAuthority)
            return;

        spriteTransform.localPosition = Vector3.zero;
        if (stepDuration <= 0f || !movingObject.IsMoving)
            return;

        float progress = Mathf.Clamp01((Time.time - steppedAt) / stepDuration);
        Vector2 interpolated = Vector2.Lerp(flightFrom, flightTo, progress);
        spriteTransform.position = new Vector3(interpolated.x, interpolated.y, transform.position.z);
    }

    public void TrackFlightStep(Vector2 from, Vector2 to, float duration)
    {
        flightFrom = from;
        flightTo = to;
        stepDuration = duration;
        steppedAt = Time.time;
    }

    public void ResetFlightPose()
    {
        stepDuration = 0f;
        if (spriteTransform != null)
            spriteTransform.localPosition = Vector3.zero;
    }

    public override void FixedUpdateNetwork()
    {
        if (HasStateAuthority && ImpactTimer.Expired(Runner))
            Runner.Despawn(Object);
    }

    public void IgnoreThrowerContacts()
    {
        if (LaunchOwner == null)
            return;

        if (collisionOwner != LaunchOwner)
        {
            collisionOwner = LaunchOwner;
            throwerColliders = LaunchOwner.Object.GetComponentsInChildren<Collider2D>();
        }

        // Unity clears IgnoreCollision when a collider is disabled while the rock is held.
        foreach (var rockCollider in rockColliders)
        foreach (var throwerCollider in throwerColliders)
        {
            if (rockCollider != null && rockCollider.isActiveAndEnabled &&
                throwerCollider != null && throwerCollider.isActiveAndEnabled &&
                !Physics2D.GetIgnoreCollision(rockCollider, throwerCollider))
                Physics2D.IgnoreCollision(rockCollider, throwerCollider);
        }
    }

    private bool IsThrowerCollider(Collider2D collider)
    {
        return LaunchOwner != null && collider.transform.IsChildOf(LaunchOwner.Object.transform);
    }

    public void BeginFlight()
    {
        launched = true;
        launchOverlaps.Clear();
        Physics2D.SyncTransforms();

        foreach (Collider2D collider in rockColliders)
        {
            if (!collider.isActiveAndEnabled)
                continue;

            collider.Overlap(CreateImpactFilter(), overlaps);
            foreach (Collider2D other in overlaps)
            {
                if (CanImpact(other) && !launchOverlaps.Contains(other))
                    launchOverlaps.Add(other);
            }
        }
    }

    private ContactFilter2D CreateImpactFilter()
    {
        var filter = new ContactFilter2D { useTriggers = true };
        filter.SetLayerMask(Physics2D.GetLayerCollisionMask(gameObject.layer));
        return filter;
    }

    private bool CanImpact(Collider2D hit)
    {
        return hit != null && !hit.transform.IsChildOf(transform) && !IsThrowerCollider(hit) &&
               (!hit.isTrigger || hit.GetComponentInParent<Switch>() != null ||
                hit.GetComponentInParent<RobotBase>() != null || hit.GetComponentInParent<RollingDrum>() != null);
    }

    private bool StillOverlaps(Collider2D other)
    {
        if (other == null || !other.isActiveAndEnabled)
            return false;

        foreach (Collider2D collider in rockColliders)
        {
            if (collider.isActiveAndEnabled && collider.Distance(other).isOverlapped)
                return true;
        }

        return false;
    }

    public bool TryImpactAlong(Vector2 destination)
    {
        Vector2 step = destination - body.position;
        if (!HasStateAuthority || !launched || ImpactTimer.IsRunning || step.sqrMagnitude <= 0f)
            return false;

        launchOverlaps.RemoveAll(other => !StillOverlaps(other));
        int count = body.Cast(step.normalized, CreateImpactFilter(), hits, step.magnitude);
        Collider2D nearest = null;
        float nearestDistance = float.PositiveInfinity;

        for (int i = 0; i < count; i++)
        {
            Collider2D hit = hits[i].collider;
            if (!CanImpact(hit) || launchOverlaps.Contains(hit))
                continue;

            if (hits[i].distance < nearestDistance)
            {
                nearest = hit;
                nearestDistance = hits[i].distance;
            }
        }

        if (nearest == null)
            return false;

        Vector2 impactPosition = body.position + step.normalized * nearestDistance;
        Impact(nearest, impactPosition);
        return true;
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        Impact(collision.collider, body.position);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.GetComponentInParent<Switch>() != null || other.GetComponentInParent<RobotBase>() != null || other.GetComponentInParent<RollingDrum>() != null)
            Impact(other, body.position);
    }

    private void Impact(Collider2D hit, Vector2 position)
    {
        if (Object == null || !Object.IsValid || !HasStateAuthority || !launched || ImpactTimer.IsRunning ||
            !CanImpact(hit) || launchOverlaps.Contains(hit))
            return;

        ImpactTimer = TickTimer.CreateFromSeconds(Runner, ImpactVisibleSeconds);
        movingObject.StopRockAt(position);

        //TO DO : 맞을 수 있는 걸 인터페이스로 빼보기
        var button = hit.GetComponentInParent<Switch>();
        if (button != null)
            button.OnRockHit();

        if (hit.GetComponentInParent<RobotBase>() is RobotBase enemy)
            enemy.HitByRock();

        if (hit.GetComponentInParent<RollingDrum>() is RollingDrum drum)
            drum.Remove();

        var key = hit.GetComponentInParent<Key>();
        if (key != null)
            key.OnRockHit();

    }
}

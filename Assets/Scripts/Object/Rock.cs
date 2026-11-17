using System.Collections.Generic;
using Fusion;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
public sealed class Rock : NetworkBehaviour
{
    private const float ImpactLifetime = 0.6f;

    private Rigidbody2D body;
    private BoxCollider2D hitbox;
    private readonly List<RaycastHit2D> flightHits = new(8);
    private ContactFilter2D flightFilter;
    private TickTimer lifetime;
    private bool impacted;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        hitbox = GetComponent<BoxCollider2D>();
        int layers = Physics2D.GetLayerCollisionMask(gameObject.layer) & ~hitbox.excludeLayers.value;
        flightFilter = new ContactFilter2D { useTriggers = true };
        flightFilter.SetLayerMask(layers);
    }

    public override void Spawned()
    {
        body.simulated = HasStateAuthority;
    }

    internal void Launch(float signedRange, float angleDegrees, float speed)
    {
        float radians = angleDegrees * Mathf.Deg2Rad;
        body.gravityScale = 0f;
        body.linearVelocity = new Vector2(Mathf.Sign(signedRange) * Mathf.Cos(radians),
            Mathf.Sin(radians)) * speed;
        lifetime = TickTimer.CreateFromSeconds(Runner, Mathf.Abs(signedRange) / speed);
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority || !lifetime.Expired(Runner))
            return;

        if (impacted)
            Runner.Despawn(Object);
        else
            Stop();
    }

    private void FixedUpdate()
    {
        if (!CanImpact)
            return;

        Vector2 step = body.linearVelocity * Time.fixedDeltaTime;
        float distance = step.magnitude;
        if (distance <= 0f)
            return;

        Vector2 direction = step / distance;
        hitbox.Cast(direction, flightFilter, flightHits, distance);
        Component nearestTarget = null;
        float nearestDistance = float.PositiveInfinity;
        foreach (var hit in flightHits)
        {
            if (hit.distance >= nearestDistance || FindImpactTarget(hit.collider) is not { } target)
                continue;

            nearestTarget = target;
            nearestDistance = hit.distance;
        }

        if (nearestTarget != null)
        {
            Vector2 contact = body.position + direction * nearestDistance;
            body.position = contact;
            transform.position = new Vector3(contact.x, contact.y, transform.position.z);
            Impact(nearestTarget);
        }
    }

    private void OnCollisionEnter2D(Collision2D collision) => TryImpact(collision.collider);

    private void OnTriggerEnter2D(Collider2D other) => TryImpact(other);

    private void TryImpact(Collider2D other)
    {
        if (CanImpact && FindImpactTarget(other) is { } target)
            Impact(target);
    }

    private static Component FindImpactTarget(Collider2D other)
    {
        if (other.GetComponentInParent<RobotBase>() is { } robot)
            return robot.CanBeHitByRock ? robot : null;
        if (other.GetComponentInParent<RollingDrum>() is { } drum)
            return drum.IsActiveTarget ? drum : null;
        if (other.GetComponentInParent<LaserButton>() is { } laserButton)
            return laserButton;
        if (other.GetComponentInParent<Switch>() is { } button)
            return button;
        if (other.GetComponentInParent<Key>() is { } key)
            return key;
        return other.isTrigger ? null : other;
    }

    private bool CanImpact => Object != null && Object.IsValid && HasStateAuthority && !impacted;

    internal void TryHitDrum(RollingDrum drum)
    {
        if (CanImpact && drum.IsActiveTarget)
            Impact(drum);
    }

    private void Impact(Component target)
    {
        Stop();
        switch (target)
        {
            case RobotBase robot:
                robot.RPC_HitByRock(this);
                break;
            case RollingDrum drum:
                drum.Remove();
                break;
            case LaserButton laserButton:
                laserButton.OnRockHit();
                break;
            case Switch button:
                button.OnRockHit();
                break;
            case Key key:
                key.OnRockHit();
                break;
        }
    }

    private void Stop()
    {
        impacted = true;
        body.simulated = false;
        lifetime = TickTimer.CreateFromSeconds(Runner, ImpactLifetime);
    }
}

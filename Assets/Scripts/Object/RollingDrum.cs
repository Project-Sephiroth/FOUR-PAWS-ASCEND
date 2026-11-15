using Fusion;
using UnityEngine;

public class RollingDrum : NetworkBehaviour
{
    [SerializeField] private BranchPuzzle encounter;
    [SerializeField] private Vector2 track;
    [Networked] private float Speed { get; set; }
    [Networked] private TickTimer Blink { get; set; }
    [Networked] private TickTimer Refill { get; set; }
    private Collider2D hitbox;
    private SpriteRenderer sprite;
    private bool IsRolling => encounter != null && encounter.EnemyEnabled() && !Blink.IsRunning && !Refill.IsRunning;

    private void Awake()
    {
        hitbox = GetComponent<Collider2D>();
        sprite = GetComponentInChildren<SpriteRenderer>(true);
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority || encounter == null)
            return;

        if (Blink.Expired(Runner))
        {
            Blink = default;
            Refill = TickTimer.CreateFromSeconds(Runner, 3f);
        }

        if (Refill.Expired(Runner))
        {
            Refill = default;
            Vector3 restart = transform.position;
            restart.x = track.y;
            if (TryGetComponent(out NetworkTransform networkTransform))
                networkTransform.Teleport(restart);
            transform.position = restart;
        }

        ApplyVisibility();

        if (!IsRolling)
            return;

        if (Speed <= 0f)
            Speed = Random.Range(1.8f, 3.4f);

        Vector3 next = transform.position + Vector3.left * (Speed * Runner.DeltaTime);
        transform.position = next;
        if (next.x <= track.x || encounter.CrusherHits(next))
            BeginRemoval();
    }

    public void Remove()
    {
        if (Object == null || !Object.IsValid)
            return;
        if (HasStateAuthority)
            BeginRemoval();
        else
            RPC_Remove();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_Remove() => BeginRemoval();

    private void BeginRemoval()
    {
        if (!IsRolling)
            return;
        Speed = 0f;
        Blink = TickTimer.CreateFromSeconds(Runner, 0.7f);
        ApplyVisibility();
    }

    public override void Render() => ApplyVisibility();

    private void ApplyVisibility()
    {
        if (hitbox != null)
            hitbox.enabled = IsRolling;
        if (sprite != null)
            sprite.enabled = IsRolling || (Blink.IsRunning &&
                Mathf.FloorToInt((Blink.RemainingTime(Runner) ?? 0f) * 14f) % 2 == 0);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (Object == null || !Object.IsValid || !HasStateAuthority || !IsRolling)
            return;

        if (other.GetComponent<DrumRemovalZone>() != null)
            BeginRemoval();

        else if (!other.isTrigger && other.attachedRigidbody != null &&
                 other.attachedRigidbody.TryGetComponent(out PlayerHealth health))
        {
            health.RPC_TakeDrumHit(this);
            BeginRemoval();
        }
    }
}
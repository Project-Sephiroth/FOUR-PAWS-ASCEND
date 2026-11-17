using Fusion;
using UnityEngine;

/// <summary>
/// 떨어지는 발판 및 압착기에 할당 됩니다
/// </summary>
public class FallingPlatform : NetworkBehaviour
{
    [SerializeField] private float fallDelay = 0.25f;
    [SerializeField] private float fallDuration = 1f;
    [SerializeField] private bool startsHidden;
    [SerializeField] private bool lockAfterRestore = true;
    [SerializeField] private float detectBelow;
    [SerializeField] private bool detectFeet;
    [SerializeField] private float fallDistance = 4f;

    [Networked] private TickTimer FallDelay { get; set; }
    [Networked] private float FallProgress { get; set; }
    [Networked] private NetworkBool LockedInPlace { get; set; }

    private Vector3 start;
    private Collider2D surface;
    private SpriteRenderer model;
    private readonly System.Collections.Generic.List<Collider2D> detected = new();

    private void Awake()
    {
        start = transform.localPosition;
        surface = GetComponent<Collider2D>();
        model = GetComponent<SpriteRenderer>();
    }

    public override void Spawned()
    {
        ApplyState();
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (Object == null || !Object.IsValid || !HasStateAuthority || LockedInPlace ||
            detectFeet || FallDelay.IsRunning || FallProgress > 0f || !collision.gameObject.CompareTag("Player"))
            return;

        if (collision.transform.position.y >= transform.position.y)
            FallDelay = TickTimer.CreateFromSeconds(Runner, fallDelay);
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority || LockedInPlace || FallProgress >= 1f)
            return;

        if (detectFeet && !FallDelay.IsRunning && surface.enabled)
        {
            Bounds platform = surface.bounds;
            foreach (var player in NetworkGameManager.GetPlayerObjects(Runner))
            {
                if (!player.TryGetComponent(out Collider2D feet) || !feet.isActiveAndEnabled ||
                    (feet.attachedRigidbody != null && !feet.attachedRigidbody.simulated))
                    continue;

                Bounds body = feet.bounds;
                if (body.max.x > platform.min.x && body.min.x < platform.max.x &&
                    Mathf.Abs(body.min.y - platform.max.y) <= 0.12f)
                {
                    FallDelay = TickTimer.CreateFromSeconds(Runner, fallDelay);
                    break;
                }
            }
        }

        if (detectBelow > 0f && !FallDelay.IsRunning)
        {
            Physics2D.SyncTransforms();
            ScanBelow(surface.bounds, detectBelow, false);
            return;
        }

        if (!FallDelay.Expired(Runner))
            return;

        float nextProgress = Mathf.Clamp01(FallProgress + Runner.DeltaTime / Mathf.Max(0.1f, fallDuration));
        if (detectBelow > 0f)
        {
            Physics2D.SyncTransforms();
            float displacement = fallDistance * (nextProgress * nextProgress - FallProgress * FallProgress);
            float parentScale = transform.parent == null ? 1f : Mathf.Abs(transform.parent.lossyScale.y);
            ScanBelow(surface.bounds, displacement * parentScale, true);
        }

        FallProgress = nextProgress;
        ApplyState();
    }

    private void ScanBelow(Bounds bounds, float depth, bool impact)
    {
        Vector2 center = new Vector2(bounds.center.x, bounds.min.y - depth * 0.5f);
        var filter = new ContactFilter2D { useTriggers = true };
        filter.SetLayerMask(LayerMask.GetMask("Player", "Object", "Rock"));
        Physics2D.OverlapBox(center, new Vector2(bounds.size.x, depth), 0f, filter, detected);
        foreach (Collider2D hit in detected)
        {
            var robot = hit.GetComponentInParent<RobotBase>();
            if (hit == surface || (robot != null
                    ? robot.Object == null || !robot.Object.IsValid || robot.SuppressBody
                    : hit.isTrigger))
                continue;

            if (!impact)
            {
                FallDelay = TickTimer.CreateFromTicks(Runner, 1);
                return;
            }

            if (robot != null)
                robot.Crush();
            if (hit.GetComponentInParent<PlayerHealth>() is PlayerHealth player)
                player.RPC_Crushed(this);
        }
    }

    public void Restore(bool activate)
    {
        if (activate && Object != null && Object.IsValid)
            RPC_Restore();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_Restore()
    {
        LockedInPlace = lockAfterRestore;
        FallDelay = default;
        FallProgress = 0f;
        ApplyState();
    }

    public override void Render()
    {
        ApplyState();
    }

    private void ApplyState()
    {
        transform.localPosition = start + Vector3.down * (fallDistance * FallProgress * FallProgress);
        bool available = !startsHidden || LockedInPlace;
        surface.enabled = available && (detectBelow > 0f || FallProgress <= 0f);
        model.enabled = available && (detectBelow > 0f || FallProgress < 1f);
    }
}

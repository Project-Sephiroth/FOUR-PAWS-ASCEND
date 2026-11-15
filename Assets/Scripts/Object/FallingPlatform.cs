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

    private RobotBase triggeredRobot;

    public override void Spawned()
    {
        ApplyState();
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (Object == null || !Object.IsValid || !HasStateAuthority || LockedInPlace ||
            FallDelay.IsRunning || FallProgress > 0f || !collision.gameObject.CompareTag("Player"))
            return;

        if (collision.transform.position.y >= transform.position.y)
            FallDelay = TickTimer.CreateFromSeconds(Runner, fallDelay);
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority || LockedInPlace || FallProgress >= 1f)
            return;

        if (detectBelow > 0f)
        {
            Bounds bounds = surface.bounds;
            if (!FallDelay.IsRunning)
            {
                ScanBelow(bounds, detectBelow, false);
                return;
            }
            ScanBelow(bounds, fallDistance * 2f * Runner.DeltaTime / Mathf.Max(0.1f, fallDuration), true);
        }

        if (!FallDelay.Expired(Runner))
            return;

        if (triggeredRobot != null)
        {
            if (triggeredRobot.IsTongueHeld && !triggeredRobot.IsCrushed)
                triggeredRobot.Crush();
            triggeredRobot = null;
        }

        FallProgress = Mathf.Clamp01(FallProgress + Runner.DeltaTime / Mathf.Max(0.1f, fallDuration));
        ApplyState();
    }

    private void ScanBelow(Bounds bounds, float depth, bool impact)
    {
        Vector2 center = new Vector2(bounds.center.x, bounds.min.y - depth * 0.5f);
        var scan = new Bounds(center, new Vector3(bounds.size.x, depth + 0.02f, 2f));
        foreach (RobotBase robot in RobotBase.LiveRobots)
        {
            if (robot == null || robot.Object == null || !robot.Object.IsValid || robot.Runner != Runner ||
                robot.IsCrushed || !robot.IsTongueHeld || !scan.Contains(robot.transform.position))
                continue;

            if (impact)
                robot.Crush();
            else
            {
                triggeredRobot = robot;
                FallDelay = TickTimer.CreateFromTicks(Runner, 1);
                return;
            }
        }
        var filter = new ContactFilter2D { useTriggers = true };
        filter.SetLayerMask(LayerMask.GetMask("Player", "Object", "Rock"));
        Physics2D.OverlapBox(center, new Vector2(bounds.size.x, depth + 0.02f), 0f, filter, detected);
        foreach (Collider2D hit in detected)
        {
            if (hit == surface || (hit.isTrigger && hit.GetComponentInParent<RobotBase>() == null))
                continue;

            if (!impact)
            {
                FallDelay = TickTimer.CreateFromTicks(Runner, 1);
                return;
            }

            if (hit.GetComponentInParent<RobotBase>() is RobotBase robot)
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

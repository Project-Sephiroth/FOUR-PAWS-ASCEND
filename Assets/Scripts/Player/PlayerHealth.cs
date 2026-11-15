using Fusion;
using UnityEngine;

public class PlayerHealth : NetworkBehaviour
{
    [SerializeField, Min(1)] private int maxHealth = 3;

    [SerializeField] private float fallDeathY = -35f; //낙사 높이
    [SerializeField] private SpriteRenderer damageSprite;

    //피격 시 빨갛게 깜박이는 듯한 연출을 위한 필드 
    private const float HitFlashSeconds = 0.20f;
    private Color originalColor;
    private bool showingHitFlash;

    public override void FixedUpdateNetwork()
    {
        if (HasStateAuthority && Health > 0 && transform.position.y < fallDeathY)
            Die();
    }

    [Networked] public int Health { get; private set; } //체력 UI 도 참조 중
    [Networked] private TickTimer HitCooldown { get; set; }
    [Networked] private TickTimer HitFlash { get; set; }
    [Networked] public NetworkBool IsHacking { get; private set; } //로봇의 타겟 탐색 시 쓰임

    public override void Spawned()
    {
        if (HasStateAuthority)
            Health = maxHealth; //스폰 시 최대 체력 설정
    }

    /// <summary>
    /// 피격 시 붉게 깜박이는 연출 렌더
    /// </summary>
    public override void Render()
    {
        if (damageSprite == null)
            return;

        float remaining = HitFlash.RemainingTime(Runner) ?? 0f;
        if (remaining <= 0f)
        {
            RestoreSpriteColor();
            return;
        }
        if (!showingHitFlash)
        {
            originalColor = damageSprite.color;
            showingHitFlash = true;
        }

        var red = new Color(1f, 0.19f, 0.23f, originalColor.a);
        damageSprite.color = Color.Lerp(originalColor, red, Mathf.Clamp01(remaining / HitFlashSeconds));
    }

    public override void Despawned(NetworkRunner runner, bool hasState) => RestoreSpriteColor();

    private void RestoreSpriteColor()
    {
        if (!showingHitFlash)
            return;

        if (damageSprite != null)
            damageSprite.color = originalColor;

        showingHitFlash = false;
    }

    /// <summary>
    /// 해킹 상태인지 확인. 죽을 시 해킹 UI 도 같이 꺼지며 로봇이 해킹 상대를 1순위로 추적
    /// </summary>
    /// <param name="value"></param>
    public static void SetLocalHacking(bool value)
    {
        var manager = NetworkGameManager.Instance;

        if (manager != null && manager.TryGetGameplayPlayer(manager.Runner.LocalPlayer, out var player) &&
            player != null && player.TryGetComponent(out PlayerHealth health))
            health.SetHacking(value);
    }

    public void SetHacking(bool value)
    {
        if (HasStateAuthority && IsHacking != value)
            IsHacking = value;
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_TakeHit(RobotBase enemy, RpcInfo info = default)
    {
        if (enemy == null || (!Runner.IsSinglePlayer && enemy.Object.StateAuthority != info.Source) ||
            Health == 0 || !HitCooldown.ExpiredOrNotRunning(Runner))
            return;

        ApplyDamage(enemy.Damage);
    }

    /// <summary>
    /// TO DO : IDamageable 로 묶어서 레이저 및 드럼통에서 데미지를 주는 것으로 처리할 것
    /// 네트워크 호출이 필요하다면 체력 변경 시 RPC 동기화를 요청하는 식으로
    /// 즉사는 최대 체력만큼 체력을 깎는 것으로 구현
    /// </summary>
    /// <param name="laser"></param>
    /// <param name="info"></param>
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_TakeLaserHit(LaserHazard laser, RpcInfo info = default)
    {
        if (laser != null && (Runner.IsSinglePlayer || laser.Object.StateAuthority == info.Source))
            ApplyDamage(1);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_TakeDrumHit(RollingDrum drum, RpcInfo info = default)
    {
        if (drum != null && (Runner.IsSinglePlayer || drum.Object.StateAuthority == info.Source))
            ApplyDamage(1);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_Crushed(FallingPlatform platform, RpcInfo info = default)
    {
        if (platform != null && (Runner.IsSinglePlayer || platform.Object.StateAuthority == info.Source) && Health > 0)
        {
            HitFlash = TickTimer.CreateFromSeconds(Runner, HitFlashSeconds);
            Die();
        }
    }

    private void ApplyDamage(int damage)
    {
        if (Health == 0 || !HitCooldown.ExpiredOrNotRunning(Runner))
            return;

        Health = Mathf.Max(0, Health - damage);
        HitCooldown = TickTimer.CreateFromSeconds(Runner, 0.75f);
        HitFlash = TickTimer.CreateFromSeconds(Runner, HitFlashSeconds);
        if (Health == 0)
            Die();
    }

    private void Die()
    {
        Health = 0;
        NetworkGameManager.Instance?.RPC_RequestRestart(this);
    }
}

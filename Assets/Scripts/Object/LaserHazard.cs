using Fusion;
using UnityEngine;

/// <summary>
/// 해킹으로 끌 수 있는 네트워크 레이저. 씬의 배치와 연결은 Inspector에서 결정
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class LaserHazard : NetworkBehaviour
{
    [Networked] private TickTimer DamageInterval { get; set; }
    [Networked] public NetworkBool IsOff { get; private set; }

    public void TurnOff()
    {
        if (Object != null && Object.IsValid)
            RPC_TurnOff();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_TurnOff(RpcInfo info = default)
    {
        //현재 레이저 비활성화는 쥐만 가능(해킹이 가능해서)
        if (NetworkGameManager.Instance != null &&
            NetworkGameManager.Instance.GetGameplayCharacter(Runner.IsSinglePlayer ? Runner.LocalPlayer : info.Source) == MyEnum.CharacterType.Mouse)
            IsOff = true;
    }

    public override void Render()
    {
        if (TryGetComponent(out SpriteRenderer sprite))
            sprite.enabled = !IsOff;
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority || IsOff || !DamageInterval.ExpiredOrNotRunning(Runner))
            return;

        DamageInterval = TickTimer.CreateFromSeconds(Runner, 0.25f);
        Bounds bounds = GetComponent<Collider2D>().bounds;
        foreach (var obj in NetworkGameManager.GetPlayerObjects(Runner))
        {
            if (!obj.TryGetComponent(out PlayerHealth health) || !obj.TryGetComponent(out Collider2D hitbox))
                continue;
            if (bounds.Intersects(hitbox.bounds))
                health.RPC_TakeLaserHit(this);
        }
    }
}

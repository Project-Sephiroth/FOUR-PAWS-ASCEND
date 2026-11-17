using Fusion;
using UnityEngine;

/// <summary>
/// 연결된 두 레이저는 하나의 네트워크 상태를 반대로 읽습니다.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class LaserHazard : NetworkBehaviour
{
    [Networked] private TickTimer DamageInterval { get; set; }
    [SerializeField] private LaserHazard oppositeState;
    [Networked] private NetworkBool BeamOff { get; set; }
    [Networked] internal NetworkBool HasSwitched { get; private set; }
    internal bool IsOff => oppositeState != null ? !oppositeState.BeamOff : BeamOff;

    internal void HitButton()
    {
        var state = oppositeState != null ? oppositeState : this;
        if (state.Object != null && state.Object.IsValid)
            state.RPC_DisableBeam(oppositeState == null);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_DisableBeam(bool lowerOff, RpcInfo info = default)
    {
        if (!Runner.IsSinglePlayer && NetworkGameManager.Instance?.GetGameplayCharacter(info.Source) != MyEnum.CharacterType.Bear)
            return;

        BeamOff = lowerOff;
        HasSwitched = true;
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

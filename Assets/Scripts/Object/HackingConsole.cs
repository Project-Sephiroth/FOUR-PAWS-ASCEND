using Fusion;
using UnityEngine;
using UnityEngine.Events;

public class HackingConsole : NetworkBehaviour
{
    [Networked, OnChangedRender(nameof(ApplyCompletion))]
    public NetworkBool IsComplete { get; private set; }

    /// <summary>
    /// 쥐를 플레이하는 유저에게만 해킹 권한 부여
    /// </summary>
    public static bool CanLocalPlayerHack
    {
        get
        {
            var manager = NetworkGameManager.Instance;
            return manager != null && manager.Object != null && manager.Object.IsValid &&
                   manager.GetGameplayCharacter(manager.Runner.LocalPlayer) == MyEnum.CharacterType.Mouse;
        }
    }

    public static bool IsGameplayScene(UnityEngine.SceneManagement.Scene scene) =>
        scene.name is "Malkuth" or "Yesod" or "Hod" or "Netzach" or "GameScene" or "StageScene";

    public static bool CanDisplayIn(GameObject host) => !IsGameplayScene(host.scene) || CanLocalPlayerHack;

    public static bool IsLocalHackOpen
    {
        get
        {
            var manager = NetworkGameManager.Instance;
            return manager != null && manager.Object != null && manager.Object.IsValid &&
                manager.TryGetGameplayPlayer(manager.Runner.LocalPlayer, out var player) &&
                player != null && player.IsValid && player.TryGetComponent(out PlayerHealth health) && health.IsHacking;
        }
    }

    public UnityEvent<bool> completeAction;
    private bool completionApplied;

    public override void Spawned()
    {
        ApplyCompletion();
    }

    //PCS의 퍼즐 성공 UnityEvent에서 호출하는 기존 진입점
    public void OnComplete()
    {
        if (Object != null && Object.IsValid && !IsComplete && CanLocalPlayerHack)
            RPC_Complete();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_Complete(RpcInfo info = default)
    {
        var manager = NetworkGameManager.Instance;

        if (IsComplete || manager == null || manager.GetGameplayCharacter(Runner.IsSinglePlayer ? Runner.LocalPlayer : info.Source) != MyEnum.CharacterType.Mouse)
            return;

        IsComplete = true;
        ApplyCompletion();
    }

    public void DebugComplete()
    {
        if (!NetworkGameManager.CanUseStageDebug || Object == null || !Object.IsValid || !HasStateAuthority || IsComplete)
            return;
        IsComplete = true;
        ApplyCompletion();
    }

    private void ApplyCompletion()
    {
        if (!IsComplete || completionApplied)
            return;

        completionApplied = true;
        foreach (var collider in GetComponentsInChildren<Collider2D>())
            collider.enabled = false;
        foreach (var collider in GetComponentsInChildren<Collider>())
            collider.enabled = false;

        if (HasStateAuthority)
            completeAction?.Invoke(true);
    }
}

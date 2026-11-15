using Fusion;
using UnityEngine;

public class RockMaker : NetworkBehaviour
{
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private MovingObject rockPrefab;
    [SerializeField] private Thrower thrower;
    [SerializeField] private Animator animator;
    [SerializeField] private AnimationClip makeRockClip;

    private MovingObject curRock;
    [Networked] private TickTimer MakeTimer { get; set; }
    public bool IsPreparing => MakeTimer.IsRunning;

    public bool TryMakeRock(Vector2 mousePosition)
    {
        if (!HasStateAuthority || IsPreparing || (curRock != null && curRock.Object != null && curRock.Object.IsValid) ||
            rockPrefab == null || thrower == null || thrower.HoldPoint == null ||
            Physics2D.OverlapPoint(mousePosition, groundLayer) == null)
            return false;

        MakeTimer = TickTimer.CreateFromSeconds(Runner, makeRockClip != null ? makeRockClip.length : 0f);
        if (animator != null && makeRockClip != null)
            RPC_PlayMakeRock();
        else
            SpawnPreparedRock();
        return true;
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_PlayMakeRock()
    {
        if (animator != null)
            animator.SetTrigger("MakeRock");
    }

    // 화면이 꺼져 애니메이션 이벤트를 놓친 경우에도 네트워크 시간이 끝나면 돌을 완성합니다.
    public override void FixedUpdateNetwork()
    {
        if (HasStateAuthority && MakeTimer.Expired(Runner))
            SpawnPreparedRock();
    }

    public void SpawnPreparedRock()
    {
        if (Object == null || !Object.IsValid || !HasStateAuthority || !IsPreparing)
            return;
        MakeTimer = default;
        if (!rockPrefab.TryGetComponent(out NetworkObject prefab))
            return;

        var spawned = Runner.Spawn(prefab, thrower.HoldPoint.position, Quaternion.identity);
        if (spawned != null && spawned.TryGetComponent(out curRock))
            thrower.TryThrow(curRock);
    }
}

using Fusion;
using UnityEngine;

public class RockMaker : NetworkBehaviour
{
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private Rock rockPrefab;
    [SerializeField] private Thrower thrower;
    [SerializeField] private Animator animator;
    [SerializeField] private AnimationClip makeRockClip;

    private const float GroundClickTolerance = 0.25f;
    [Networked] private TickTimer MakeTimer { get; set; }
    public bool IsPreparing => MakeTimer.IsRunning;

    public bool TryMakeRock(Vector2 mousePosition)
    {
        if (!HasStateAuthority || IsPreparing || rockPrefab == null || thrower == null ||
            Physics2D.OverlapCircle(mousePosition, GroundClickTolerance, groundLayer) == null)
            return false;

        MakeTimer = TickTimer.CreateFromSeconds(Runner, makeRockClip != null ? makeRockClip.length : Runner.DeltaTime);
        if (animator != null && makeRockClip != null)
            RPC_PlayMakeRock();
        else
            FinishPreparingRock();
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
            FinishPreparingRock();
    }

    public void FinishPreparingRock()
    {
        if (Object == null || !Object.IsValid || !HasStateAuthority || !IsPreparing)
            return;

        MakeTimer = default;
        thrower.BeginRockAim();
    }

    internal void ThrowRock(Vector2 origin, float signedRange, float angle, float speed)
    {
        var rock = Runner.Spawn(rockPrefab, origin, Quaternion.identity);
        if (rock != null)
            rock.Launch(signedRange, angle, speed);
    }
}

using Fusion;
using UnityEngine;

public sealed class Generate : MonoBehaviour
{
    // BranchPuzzle이 이 값을 동기화합니다. 발전기 컴포넌트는 충전 규칙과 인스펙터 설정을 맡습니다.
    public struct State : INetworkStruct
    {
        public float Progress;
        public NetworkBool Started;
        public TickTimer DrainDelay;

        public bool IsCharged => Progress >= 1f;
    }

    [SerializeField, Min(0f), Tooltip("마지막 입력 후 충전량이 줄기 시작하기까지 걸리는 초")]
    private float drainDelaySeconds = 0.5f;

    [SerializeField, Min(0f), Tooltip("충전량 100%를 비우는 데 필요한 초. 0이면 줄어들지 않습니다.")]
    private float drainDurationSeconds = 18.181818f;

    [SerializeField, Range(0.01f, 1f), Tooltip("한 번 누를 때 차는 양. 0.22는 22%입니다.")]
    private float chargePerPress = 0.22f;

    internal State Charge(State state, NetworkRunner runner)
    {
        state.Started = true;
        state.Progress = Mathf.Min(1f, state.Progress + chargePerPress);
        // 연타할 때마다 감소가 시작되기까지의 대기 시간을 다시 설정합니다.
        state.DrainDelay = TickTimer.CreateFromSeconds(runner, drainDelaySeconds);
        return state;
    }

    internal State Tick(State state, NetworkRunner runner)
    {
        if (state.Progress > 0f && !state.IsCharged && drainDurationSeconds > 0f &&
            state.DrainDelay.ExpiredOrNotRunning(runner))
            state.Progress = Mathf.MoveTowards(state.Progress, 0f, runner.DeltaTime / drainDurationSeconds);
        return state;
    }

    internal State Complete(State state)
    {
        state.Started = true;
        state.Progress = 1f;
        state.DrainDelay = default;
        return state;
    }
}

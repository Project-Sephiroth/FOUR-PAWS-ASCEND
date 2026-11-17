using System;
using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class StagePuzzleDirector : MonoBehaviour
{
    [Serializable]
    private sealed class MalkuthPuzzle
    {
        public KeyDoor[] keyDoors;
        public MovingFloor[] movingFloors;
        public HackingConsole terminal;
        public StageExit exit;
    }

    [Serializable]
    private sealed class YesodPuzzle
    {
        public StageCircuitTerminal lowerTerminal;
        public LaserHazard lowerLaser;
        public Elevator lowerElevator;
        public StageCircuitTerminal upperTerminal;
        public StageBattery battery;
    }

    [Serializable]
    private sealed class HodPuzzle
    {
        public StageCircuitTerminal innerTerminal;
        public GameObject innerGate;
        public StageCircuitTerminal exitTerminal;
        public GameObject exitGate;
        public Vector2 exitCenter;
        [Min(0f)] public float exitRadius = 2.8f;
        [Min(1)] public int requiredPlayers = 4;
    }

    [Serializable]
    private sealed class NetzachPuzzle
    {
        public BranchControl generator;
        public Generate generatorLogic;
        public Vector2 generatorCenter;
        public BranchControl crusherLever;
        public Vector2 crusherLeverCenter;
        public CrusherMechanism crusher;
        public GeneratorProgressView generatorProgress;
        public StageCircuitTerminal terminal;
        public DrumRemovalZone drumBarrier;
        public BranchControl barrierSwitch;
    }

    [SerializeField] private NetworkGameManager.SceneName stage;
    [SerializeField] private MalkuthPuzzle malkuth = new();
    [SerializeField] private YesodPuzzle yesod = new();
    [SerializeField] private HodPuzzle hod = new();
    [SerializeField] private NetzachPuzzle netzach = new();

    [SerializeField] private Elevator exitElevator;
    private MovingFloor mouseDoor;
    private BranchPuzzle branch;

    // 퍼즐이 모두 끝나고 참가한 플레이어가 발판 위에 모이면 출발합니다. 승강이 끝난 시점에 기존 맵 전환을 호출합니다.
    public bool TryExitByElevator(NetworkRunner runner)
    {
        if (exitElevator == null || exitElevator.Object == null || !exitElevator.Object.IsValid)
            return false;
        if (exitElevator.ExitFinished)
            return TryCompleteStage();
        if (exitElevator.AllPlayersOnDeck(runner))
            exitElevator.BeginExit();
        return false;
    }

    // 스테이지 목표의 완료율을 0~1 값으로 표시합니다.
    public float ReadProgress()
    {
        switch (stage)
        {
            case NetworkGameManager.SceneName.Malkuth:
                // 동물마다 한 칸입니다. 쥐는 해킹 성공 직후가 아니라 연결된 문이 열리는 위치에 도착했을 때 채웁니다.
                int opened = Ready(mouseDoor) && mouseDoor.HasReachedDestination ? 1 : 0;
                if (malkuth.keyDoors != null)
                    foreach (var door in malkuth.keyDoors)
                        if (Ready(door) && door.IsOpened)
                            opened++;
                return Mathf.Clamp01(opened / 4f);
            case NetworkGameManager.SceneName.Yesod:
                return ((yesod.lowerElevator != null && yesod.lowerElevator.HasRisen ? 1f : 0f) +
                    (Ready(yesod.lowerLaser) && yesod.lowerLaser.HasSwitched ? 1f : 0f) +
                    (Ready(yesod.battery) && yesod.battery.HasBeenPickedUp ? 1f : 0f)) / 3f;
            case NetworkGameManager.SceneName.Hod:
                return Ready(branch) ? ((branch.FirstTerminalDone ? 1f : 0f) +
                    (branch.FinalTerminalDone ? 1f : 0f)) / 2f : 0f;
            case NetworkGameManager.SceneName.Netzach:
                return Ready(branch) ? branch.HackProgress : 0f;
            default:
                return 0f;
        }
    }

    private static bool Ready(NetworkBehaviour item) =>
        item != null && item.Object != null && item.Object.IsValid;

    public bool UsesPressurePads => stage == NetworkGameManager.SceneName.Hod;
    public CrusherMechanism Crusher => netzach.crusher;

    internal void ApplyDrumBarrier(bool blocking, float leverProgress, bool canOperate)
    {
        if (netzach.drumBarrier != null)
            netzach.drumBarrier.gameObject.SetActive(blocking);
        if (netzach.barrierSwitch != null)
            netzach.barrierSwitch.SetLeverPosition(leverProgress, canOperate);
    }
    public Generate Generator => netzach.generatorLogic;
    internal float ReadGeneratorProgress() => Ready(branch) ? branch.GeneratorProgress : 0f;
    internal int NetzachHackStageCount => netzach.terminal != null ? netzach.terminal.CircuitStageCount : 0;
    public Vector2 GetControlPosition(int control)
    {
        return control switch
        {
            0 => netzach.generator != null ? (Vector2)netzach.generator.transform.position : netzach.generatorCenter,
            1 => netzach.crusherLever != null ? (Vector2)netzach.crusherLever.transform.position : netzach.crusherLeverCenter,
            BranchControl.BarrierSwitch when netzach.barrierSwitch != null => netzach.barrierSwitch.transform.position,
            _ => new Vector2(float.PositiveInfinity, float.PositiveInfinity)
        };
    }

    // 1. 현재 씬에 해당하는 묶음을 고릅니다.
    // 2. 장치가 서로 직접 찾아다니지 않게 여기서 단말, 문, 레이저와 출구의 연결을 설정합니다.
    // 3. 플레이 중에는 각 장치가 실제 입력과 네트워크 상태를 처리합니다.
    private void Awake()
    {
        switch (stage)
        {
            case NetworkGameManager.SceneName.Malkuth:
                malkuth.exit?.SetDirector(this);
                mouseDoor = FindMouseDoor();
                break;
            case NetworkGameManager.SceneName.Yesod:
                yesod.lowerTerminal?.AddCompletionAction(CompleteLowerHack);
                yesod.battery?.SetDirector(this);
                yesod.upperTerminal?.AddCompletionAction(CompleteUpperHack);
                break;
            case NetworkGameManager.SceneName.Hod:
                branch = GetComponent<BranchPuzzle>();
                // 이전 씬 데이터에도 적용되도록 이름으로 명시된 두 단말의 역할을 맞춥니다.
                var consoleB = FindSceneObject("HackingConsoleB");
                var consoleA = FindSceneObject("HackingConsoleA");
                if (consoleB != null) hod.innerTerminal = consoleB.GetComponent<StageCircuitTerminal>();
                if (consoleA != null) hod.exitTerminal = consoleA.GetComponent<StageCircuitTerminal>();
                var block = FindSceneObject("CompleteDisappearBlock");
                if (block != null) hod.innerGate = block;
                if (exitElevator == null)
                {
                    var lift = FindSceneObject("Elevator");
                    if (lift != null) exitElevator = lift.GetComponent<Elevator>();
                }
                hod.innerTerminal?.SetBranch(branch, 0);
                hod.exitTerminal?.SetBranch(branch, 1);
                break;
            case NetworkGameManager.SceneName.Netzach:
                branch = GetComponent<BranchPuzzle>();
                netzach.generator?.SetPuzzle(branch, 0);
                netzach.generatorProgress?.Bind(this);
                netzach.crusherLever?.SetPuzzle(branch, 1);
                netzach.barrierSwitch?.SetPuzzle(branch, BranchControl.BarrierSwitch);
                netzach.terminal?.SetBranch(branch, 2);
                break;
        }
    }

    // 쥐 콘솔이 원래 열던 문을 읽습니다. 진행도와 실제로 움직이는 문이 다른 대상을 가리키지 않게 합니다.
    private MovingFloor FindMouseDoor()
    {
        var actions = malkuth.terminal != null ? malkuth.terminal.completeAction : null;
        if (actions == null)
            return null;
        for (int i = 0; i < actions.GetPersistentEventCount(); i++)
            if (actions.GetPersistentMethodName(i) == nameof(MovingFloor.StartMove) &&
                actions.GetPersistentTarget(i) is MovingFloor door)
                return door;
        return null;
    }

    private GameObject FindSceneObject(string objectName)
    {
        foreach (var root in gameObject.scene.GetRootGameObjects())
            foreach (var candidate in root.GetComponentsInChildren<Transform>(true))
                if (candidate.name == objectName)
                    return candidate.gameObject;
        return null;
    }

    private void CompleteUpperHack() => yesod.battery?.TrySpawn();

    private void CompleteLowerHack() => yesod.lowerElevator?.DoUp(true);

    public bool TryCompleteStage()
    {
        var manager = NetworkGameManager.Instance;
        if (manager == null || !manager.HasStateAuthority || !manager.Runner.IsSceneAuthority)
            return false;
        manager.CompleteStage(stage);
        return true;
    }

    public bool CanHack(int terminal, bool innerDone, bool exitDone, bool padsActivated)
    {
        return terminal switch
        {
            0 => !innerDone && padsActivated,
            1 => innerDone && !exitDone,
            _ => false
        };
    }

    public void ApplyGates(bool innerDone, bool exitDone)
    {
        if (hod.innerGate != null)
            hod.innerGate.SetActive(!innerDone);
        if (hod.exitGate != null)
            hod.exitGate.SetActive(!exitDone);
    }

    public string GetHint(bool innerDone, bool exitDone)
    {
        if (exitDone)
            return "모든 플레이어가 엘리베이터로 돌아오세요.";
        return innerDone ? "해킹 콘솔 A를 작동하세요."
            : "양쪽 발판을 누르고 해킹 콘솔 B를 작동하세요.";
    }

    private void OnDrawGizmosSelected()
    {
        if (!UsesPressurePads)
            return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(hod.exitCenter, hod.exitRadius);
    }
}

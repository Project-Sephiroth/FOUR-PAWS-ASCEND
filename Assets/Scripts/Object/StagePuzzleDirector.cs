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
        public StageCircuitTerminal upperTerminal;
        public LaserHazard upperLaser;
        public StageCircuitTerminal lowerTerminal;
        public LaserHazard lowerLaser;
        public Elevator lowerElevator;
        public Switch[] batterySwitches = Array.Empty<Switch>();
        public StageBattery battery;
    }

    [Serializable]
    private struct PressurePad
    {
        public Vector2 center;
    }

    [Serializable]
    private sealed class HodPuzzle
    {
        public PressurePad leftPad;
        public PressurePad rightPad;
        [Min(0f)] public float padRadius = 1.45f;
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
        public Vector2 generatorCenter;
        public BranchControl crusherLever;
        public Vector2 crusherLeverCenter;
        public Transform crusher;
        public SpriteRenderer crusherPlate;
        public AnimationClip crusherMotion;
        [Min(0f)] public float crusherTravel = 2.4f;
        [Range(0f, 1f)] public float requiredPower = 0.25f;
        public StageCircuitTerminal terminal;
    }

    [SerializeField] private NetworkGameManager.SceneName stage;
    [SerializeField] private MalkuthPuzzle malkuth = new();
    [SerializeField] private YesodPuzzle yesod = new();
    [SerializeField] private HodPuzzle hod = new();
    [SerializeField] private NetzachPuzzle netzach = new();

    [SerializeField] private Elevator exitElevator;
    private MovingFloor mouseDoor;

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

    // 완료할 때만 올라가는 씬과, 세기와 함께 오르내리는 발전기를 같은 0~1 값으로 표시합니다.
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
                return ((Ready(yesod.upperLaser) && yesod.upperLaser.IsOff ? 1f : 0f) +
                    (Ready(yesod.lowerLaser) && yesod.lowerLaser.IsOff ? 1f : 0f) +
                    (Ready(yesod.battery) && yesod.battery.HasBeenPickedUp ? 1f : 0f)) / 3f;
            case NetworkGameManager.SceneName.Hod:
                var hodBranch = GetComponent<BranchPuzzle>();
                return Ready(hodBranch) ? ((hodBranch.FirstTerminalDone ? 1f : 0f) +
                    (hodBranch.FinalTerminalDone ? 1f : 0f)) / 2f : 0f;
            case NetworkGameManager.SceneName.Netzach:
                var netzachBranch = GetComponent<BranchPuzzle>();
                return Ready(netzachBranch) ? netzachBranch.GeneratorProgress : 0f;
            default:
                return 0f;
        }
    }

    private static bool Ready(NetworkBehaviour item) =>
        item != null && item.Object != null && item.Object.IsValid;

    public bool UsesPressurePads => stage == NetworkGameManager.SceneName.Hod;
    public Transform Crusher => netzach.crusher;
    public float CrusherTravel => netzach.crusherTravel;
    public AnimationClip CrusherMotion => netzach.crusherMotion;
    public Bounds CrusherPlateBounds => netzach.crusherPlate != null ? netzach.crusherPlate.bounds : default;
    public float RequiredPower => netzach.requiredPower;
    public Vector2 GetControlPosition(int control)
    {
        var device = control == 0 ? netzach.generator : netzach.crusherLever;
        return device != null ? (Vector2)device.transform.position : control == 0 ? netzach.generatorCenter : netzach.crusherLeverCenter;
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
                yesod.upperTerminal?.AddCompletionAction(CompleteUpperHack);
                yesod.lowerTerminal?.AddCompletionAction(CompleteLowerHack);
                yesod.battery?.SetDirector(this, yesod.upperLaser, yesod.lowerLaser);
                foreach (var batterySwitch in yesod.batterySwitches)
                {
                    if (batterySwitch == null)
                        continue;
                    batterySwitch.switchAction ??= new UnityEngine.Events.UnityEvent<bool>();
                    batterySwitch.switchAction.AddListener(TryReleaseBattery);
                }
                break;
            case NetworkGameManager.SceneName.Hod:
                var hodBranch = GetComponent<BranchPuzzle>();
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
                hod.innerTerminal?.SetBranch(hodBranch, 0);
                hod.exitTerminal?.SetBranch(hodBranch, 1);
                break;
            case NetworkGameManager.SceneName.Netzach:
                var netzachBranch = GetComponent<BranchPuzzle>();
                netzach.generator?.SetPuzzle(netzachBranch, 0);
                netzach.crusherLever?.SetPuzzle(netzachBranch, 1);
                netzach.terminal?.SetBranch(netzachBranch, 2);
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

    private void CompleteUpperHack() => yesod.upperLaser?.TurnOff();

    private void TryReleaseBattery(bool enabled) => yesod.battery?.TrySpawn(enabled);

    private void CompleteLowerHack()
    {
        yesod.lowerLaser?.TurnOff();
        yesod.lowerElevator?.DoUp(true);
    }

    public bool TryCompleteStage()
    {
        var manager = NetworkGameManager.Instance;
        if (manager == null || !manager.HasStateAuthority || !manager.Runner.IsSceneAuthority)
            return false;
        manager.CompleteStage(stage);
        return true;
    }

    public bool CanHack(int terminal, bool innerDone, bool exitDone, NetworkRunner runner)
    {
        return terminal switch
        {
            0 => !innerDone && PadsOccupied(runner),
            1 => innerDone && !exitDone,
            _ => false
        };
    }

    // 캐릭터 종류에 관계없이 서로 다른 두 플레이어가 왼쪽과 오른쪽 패드를 각각 밟아야 합니다.
    private bool PadsOccupied(NetworkRunner runner)
    {
        var manager = NetworkGameManager.Instance;
        if (manager == null)
            return false;

        NetworkObject left = null;
        NetworkObject right = null;
        foreach (var player in NetworkGameManager.GetPlayerObjects(runner))
        {
            Vector2 position = player.transform.position;
            bool onLeft = Vector2.Distance(position, hod.leftPad.center) <= hod.padRadius;
            bool onRight = Vector2.Distance(position, hod.rightPad.center) <= hod.padRadius;
            if ((onLeft && right != null && player != right) || (onRight && left != null && player != left))
                return true;
            if (onLeft) left = player;
            if (onRight) right = player;
        }
        return false;
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
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(hod.leftPad.center, hod.padRadius);
        Gizmos.DrawWireSphere(hod.rightPad.center, hod.padRadius);
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(hod.exitCenter, hod.exitRadius);
    }
}

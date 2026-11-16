using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;
using Character = MyEnum.CharacterType;

// 모든 빌드에서 로컬 협동 테스트 UI와 독립 Fusion 세션을 제공한다.
[DefaultExecutionOrder(-100)]
public sealed class LocalMultiplayerDebug : MonoBehaviour
{
    // 기존 NetworkGameManager 프리팹을 Fusion 등록 GUID로 참조한다. 프리팹이 이동해도 GUID는 유지된다.
    private const string ManagerPrefabGuid = "171131909f6593543a529c097f12b625";
    // 메뉴를 여는 확인용 코드다. 빌드에 포함되므로 보안 인증으로 사용하지 않는다.
    private const string AccessCode = "user257";
    private static readonly string[] Stages = { "Malkuth", "Yesod", "Hod", "Netzach" };
    private static LocalMultiplayerDebug instance;
    // Fusion 참가자는 한 명이다. 씬에 생성한 테스트 캐릭터는 이 목록으로 따로 추적한다.
    private readonly List<NetworkObject> players = new();
    private readonly Dictionary<NetworkObject, Character> characterTypes = new();
    private NetworkRunner runner;
    private NetworkGameManager manager;
    private NetworkObject selected;
    private Character selectedType = Character.Rabbit;
    // CharacterType의 값에 해당하는 비트를 사용한다. 초기에는 토끼, 곰, 쥐, 개구리를 모두 선택한다.
    private int roster = (1 << (int)Character.Count) - 2;
    private int stageIndex;
    private bool visible;
    private bool promptVisible;
    private bool shortcutHeld;
    [SerializeField] private LocalDebugMenuView view;
    private bool invalidCode;
    private bool busy;
    private string status;

    // 시작 씬에 컴포넌트를 배치하지 않아도 메뉴가 열리도록 씬 로드 전에 생성한다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        var prefab = Resources.Load<LocalMultiplayerDebug>("UI/LocalMultiplayerDebug");
        if (prefab == null || prefab.view == null)
            return;
        instance = Instantiate(prefab);
        DontDestroyOnLoad(instance.gameObject);
        UIManager.SetCanvasOrder(instance.gameObject, UIManager.Layer.Debug);
        instance.BindMenu();
    }

    private void Update()
    {
        bool control = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        UpdateShortcut(control && shift);
        if (promptVisible)
        {
            if (Input.GetKeyDown(KeyCode.Escape))
                ClosePrompt();
            else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                SubmitCode();
        }
        RefreshMenu();
        // 입력창 또는 개발자 메뉴가 열려 있는 동안 Tab이 캐릭터를 바꾸지 않게 한다.
        if (!GameMenu.BlocksInput && !ScenePresentation.BlocksInput && !promptVisible && !visible && CanSwitch && Input.GetKeyDown(KeyCode.Tab))
            Select(players[(players.IndexOf(selected) + 1) % players.Count]);
    }

    private void UpdateShortcut(bool pressed)
    {
        // 두 키가 처음 함께 눌린 순간에만 토글한다. 키를 계속 누르고 있어도 창이 다시 열리지 않는다.
        bool toggled = pressed && !shortcutHeld;
        shortcutHeld = pressed;
        if (!toggled || busy || GameMenu.BlocksInput || ScenePresentation.BlocksInput)
            return;

        if (visible)
            visible = false;
        else if (promptVisible)
            ClosePrompt();
        else
        {
            promptVisible = true;
            UIManager.EnsureLocalEventSystem();
            view.prompt.SetActive(true);
            view.FocusCode();
        }
    }

    // 입력창을 닫을 때 오류 표시도 초기화합니다. 다음에 열면 프리팹의 입력칸을 비운 뒤 다시 초점을 줍니다.
    private void ClosePrompt()
    {
        promptVisible = false;
        invalidCode = false;
    }

    private void SubmitCode()
    {
        // 코드가 정확히 일치했을 때만 세션 메뉴로 진입한다.
        if (!string.Equals(view.accessInput.text, AccessCode, StringComparison.Ordinal))
        {
            invalidCode = true;
            UIManager.EnsureLocalEventSystem();
            view.prompt.SetActive(true);
            view.FocusCode();
            return;
        }

        ClosePrompt();
        visible = true;
        if (runner == null)
        {
            int activeStage = Array.IndexOf(Stages, SceneManager.GetActiveScene().name);
            if (activeStage >= 0)
                stageIndex = activeStage;
        }
    }

    // 해킹 UI가 활성화된 동안에는 완료 신호가 다른 캐릭터에 적용되지 않도록 전환을 제한한다.
    private bool CanSwitch => selected != null && selected.IsValid &&
        (!selected.TryGetComponent(out PlayerHealth health) || !health.IsHacking);

    // 게임플레이 코드에서 호출한다. 이 도구가 만든 Single 러너에만 테스트 캐릭터 목록을 제공한다.
    public static IReadOnlyList<NetworkObject> GetPlayers(NetworkRunner context) =>
        instance != null && context != null && instance.runner == context && context.IsSinglePlayer ? instance.players : null;

    public static NetworkObject GetSelected(NetworkRunner context) =>
        GetPlayers(context) != null ? instance.selected : null;

    public static bool TryGetCharacter(NetworkObject player, out Character character)
    {
        character = Character.None;
        return instance != null && player != null && instance.characterTypes.TryGetValue(player, out character);
    }

    public static Character GetSelectedCharacter(NetworkRunner context) =>
        GetSelected(context) != null ? instance.selectedType : Character.None;

    // Single 모드에서 여러 오브젝트가 모두 상태 권한을 갖는다. 조작 대상을 별도로 검사해 동시 입력을 방지한다.
    public static bool AllowsInput(NetworkObject player)
    {
        if (GameMenu.BlocksInput || ScenePresentation.BlocksInput || (instance != null && instance.promptVisible))
            return false;

        if (GetPlayers(player.Runner) == null)
            return true;

        return !instance.visible && instance.selected == player;
    }

    // 메뉴 바깥을 클릭해도 디버그 창이 열린 동안에는 게임 오브젝트가 반응하지 않습니다.
    public static bool BlocksWorldClick => instance != null && (instance.promptVisible || instance.visible);

    private void BindMenu()
    {
        view.Bind(Stages, SubmitCode, ClosePrompt, StartTest, StopTest, () => visible = false,
            index => stageIndex = index, (slot, included) =>
            {
                int mask = 1 << (slot + 1);
                roster = included ? roster | mask : roster & ~mask;
            }, SelectSlot);
        if (view.clearConsoles != null)
            view.clearConsoles.onClick.AddListener(ClearSceneConsoles);
        if (view.forceClearStage != null)
            view.forceClearStage.onClick.AddListener(() => NetworkGameManager.Instance?.DebugForceClear());
        if (view.exitStage != null)
            view.exitStage.onClick.AddListener(() => NetworkGameManager.Instance?.DebugExitStage());
        if (view.toggleStageAccess != null)
            view.toggleStageAccess.onClick.AddListener(() => NetworkGameManager.Instance?.ToggleDebugStageAccess());
        RefreshMenu();
    }

    private void ClearSceneConsoles()
    {
        if (!NetworkGameManager.CanUseStageDebug)
            return;
        foreach (var console in FindObjectsByType<HackingConsole>(FindObjectsSortMode.None))
            console.DebugComplete();
        foreach (var terminal in FindObjectsByType<StageCircuitTerminal>(FindObjectsSortMode.None))
            terminal.DebugComplete();
    }

    private void SelectSlot(int slot)
    {
        if (!CanSwitch)
            return;
        var type = (Character)(slot + 1);
        var player = players.Find(candidate => candidate != null && candidate.IsValid && characterTypes[candidate] == type);
        if (player != null)
            Select(player);
    }

    // 현재 상태를 그립니다. 러너 시작 전에는 씬과 참가자를, 시작 뒤에는 전환 가능한 캐릭터를 표시합니다.
    private void RefreshMenu()
    {
        view.prompt.SetActive(promptVisible);
        view.panel.SetActive(visible);
        if (promptVisible)
            view.accessMessage.text = invalidCode ? "Incorrect code. Try again." : "Enter access code.";
        if (!visible)
            return;

        bool inSession = runner != null;
        bool online = !inSession && NetworkGameManager.Instance != null &&
            NetworkGameManager.Instance.Object != null && NetworkGameManager.Instance.Object.IsValid;
        var stageManager = NetworkGameManager.Instance;
        bool inStage = NetworkGameManager.CanUseStageDebug;
        bool canUnlock = stageManager != null && stageManager.CanManageDebugStageAccess;
        if (view.sessionTools != null)
            view.sessionTools.SetActive(true);
        if (view.clearConsoles != null) view.clearConsoles.interactable = inStage && !busy;
        if (view.forceClearStage != null) view.forceClearStage.interactable = inStage && !busy;
        if (view.exitStage != null) view.exitStage.interactable = inStage && !busy;
        if (view.toggleStageAccess != null)
        {
            view.toggleStageAccess.interactable = canUnlock && !busy;
            view.toggleStageAccess.GetComponentInChildren<UnityEngine.UI.Text>().text = canUnlock && stageManager.DebugStageAccess
                ? "Restore stage locks" : "Unlock every stage";
        }
        view.setup.SetActive(!inSession && !online);
        view.playing.SetActive(inSession);
        view.stage.SetValueWithoutNotify(stageIndex);
        view.stage.interactable = !busy;
        view.start.interactable = !busy && roster != 0;
        view.stop.interactable = !busy;
        var network = NetworkGameManager.Instance;
        view.status.text = status ?? (online
            ? network.Runner.GameMode + " / " + SceneManager.GetActiveScene().name +
                (network.Runner.IsSceneAuthority ? " / Scene authority" : " / Client")
            : string.Empty);
        for (int i = 0; i < view.roster.Length; i++)
        {
            var type = (Character)(i + 1);
            view.roster[i].SetIsOnWithoutNotify((roster & (1 << (i + 1))) != 0);
            view.roster[i].interactable = !busy;
            bool available = inSession && players.Exists(player => player != null && player.IsValid && characterTypes[player] == type);
            view.characters[i].interactable = !busy && CanSwitch && available && selectedType != type;
        }
    }

    // 1. 다른 Fusion 세션이 없는지 봅니다. 2. 화면을 닫고 고른 씬으로 Single 세션을 엽니다. 3. 게임 매니저와 고른 캐릭터를 만들고 화면을 다시 엽니다.
    private async void StartTest()
    {
        // 로비를 포함해 다른 러너가 살아 있는 동안 별도 세션이 씬 권한을 차지하지 않도록 한다.
        foreach (var other in NetworkRunner.Instances)
        {
            if (other != null)
            {
                status = "Return to Title before starting a local test.";
                return;
            }
        }

        busy = true;
        status = "Starting local test...";
        try
        {
            var root = new GameObject("Local Multiplayer Runner");
            runner = root.AddComponent<NetworkRunner>();
            // 런타임에 추가한 NetworkEvents의 씬 이벤트를 초기화한 뒤 콜백을 등록한다.
            var events = root.AddComponent<NetworkEvents>();
            events.OnSceneLoadStart = new NetworkEvents.RunnerEvent();
            events.OnSceneLoadDone = new NetworkEvents.RunnerEvent();
            events.OnSceneLoadStart.AddListener(OnSceneLoadStarted);
            events.OnSceneLoadDone.AddListener(OnSceneLoaded);

            // 빌드 목록의 씬 인덱스를 Fusion에 전달해 기존 씬 네트워크 오브젝트도 등록되게 한다.
            string path = "Assets/Scenes/" + Stages[stageIndex] + ".unity";
            int buildIndex = SceneUtility.GetBuildIndexByScenePath(path);
            if (buildIndex < 0)
                throw new InvalidOperationException("The selected stage is missing from the build.");

            // Photon 방에 접속할 필요 없이 Fusion의 스폰, RPC, 씬 로드 경로를 사용한다.
            await ScenePresentation.CoverAsync();
            var result = await runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Single,
                Scene = SceneRef.FromIndex(buildIndex),
                SceneManager = root.AddComponent<NetworkSceneManagerDefault>(),
                ObjectProvider = root.AddComponent<NetworkObjectProviderDefault>()
            });

            if (!result.Ok)
                throw new InvalidOperationException(result.ShutdownReason.ToString());

            // 세션 시작 결과를 받은 뒤 기존 매니저 프리팹을 생성한다. 이후 로드는 OnSceneLoaded에서 처리한다.
            selectedType = Character.Rabbit;
            manager = runner.Spawn(NetworkPrefabRef.Parse(ManagerPrefabGuid))
                .GetComponent<NetworkGameManager>();
            if (!runner.IsSceneManagerBusy && players.Count == 0)
                SpawnPlayers();
            visible = false;
            status = null;
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            await Shutdown();
            status = "Local test failed: " + error.Message;
        }
        finally
        {
            ScenePresentation.Reveal();
            busy = false;
        }
    }

    private void OnSceneLoadStarted(NetworkRunner context)
    {
        ScenePresentation.Cover(immediate: true);
        ClearPlayers(context);
    }

    // 이전 씬의 유효한 테스트 오브젝트를 Fusion을 통해 제거하고 참조를 비운다.
    private void ClearPlayers(NetworkRunner context)
    {
        selected = null;
        foreach (var player in players)
            if (player != null && player.IsValid)
                context.Despawn(player);
        players.Clear();
        characterTypes.Clear();
    }

    // 매니저의 씬 전환 상태를 초기화하고 새 씬의 PlayerSpawner에서 다시 생성한다.
    private void OnSceneLoaded(NetworkRunner context)
    {
        if (manager == null)
            return;

        manager.OnSceneLoaded();
        SpawnPlayers();
    }

    // 기존 씬의 프리팹과 시작 위치를 재사용한다. 스폰 지점이 없는 Map 같은 씬에서는 목록을 비워 둔다.
    private void SpawnPlayers()
    {
        var root = GameObject.FindWithTag("Spawn");
        if (root == null || !root.TryGetComponent(out PlayerSpawner spawner))
            return;

        for (Character type = Character.Rabbit; type < Character.Count; type++)
        {
            if ((roster & (1 << (int)type)) == 0)
                continue;
            var player = spawner.SpawnCharacter(runner, type, runner.LocalPlayer);
            if (player != null)
            {
                players.Add(player);
                characterTypes.Add(player, type);
            }
        }

        if (players.Count == 0)
            return;

        // 이전 씬에서 조작하던 캐릭터를 우선 선택한다. 생성되지 않았다면 첫 캐릭터를 사용한다.
        var next = players.Find(player => characterTypes[player] == selectedType);
        Select(next != null ? next : players[0]);
    }

    private void Select(NetworkObject player)
    {
        // 전환 직전에 남아 있을 수 있는 이동과 점프 입력을 지운 뒤 카메라를 새 대상에 연결한다.
        if (selected != null && selected.TryGetComponent(out PlayerInput input))
        {
            input.MoveInput = Vector2.zero;
            input.JumpInput = false;
        }

        selected = player;
        selectedType = characterTypes[player];
        CameraFollow.Instance?.SetTarget(player.transform);
    }

    // 선택적으로 실행한 테스트를 종료하고 타이틀에 복귀한다.
    private async void StopTest()
    {
        busy = true;
        try
        {
            await ScenePresentation.LoadLocalAsync(nameof(NetworkGameManager.SceneName.Title), LeaveForTitle);
            status = null;
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            status = "Local test shutdown failed: " + error.Message;
        }
        finally
        {
            busy = false;
        }
    }

    // 정상 종료와 시작 실패 처리에서 러너와 캐릭터 참조 정리를 공유한다.
    public static async System.Threading.Tasks.Task LeaveForTitle()
    {
        if (instance == null)
            return;
        await instance.Shutdown();
        instance.visible = false;
        instance.ClosePrompt();
    }

    private async System.Threading.Tasks.Task Shutdown()
    {
        if (runner != null && runner.IsRunning)
        {
            ClearPlayers(runner);
            await runner.Shutdown();
        }
        else if (runner != null)
            Destroy(runner.gameObject);

        runner = null;
        manager = null;
        selected = null;
        players.Clear();
        characterTypes.Clear();
    }

}

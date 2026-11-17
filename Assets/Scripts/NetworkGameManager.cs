using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

public class NetworkGameManager : NetworkBehaviour
{
    public static NetworkGameManager Instance { get; private set; }

    // 캐릭터 슬롯의 소유자를 저장합니다. PlayerRef를 배열 인덱스로 쓰지 않습니다.
    [Networked, Capacity(4)]
    private NetworkArray<PlayerRef> CharacterOwners => default;

    public PlayerRef PlayerRef => Runner.LocalPlayer;
    public enum SceneName { Title, Lobby, Malkuth, Map, Yesod, Hod, Netzach, Credits }

    private float SceneCoverSeconds => ScenePresentation.CloseSeconds + 0.25f;

    [Networked] public NetworkBool MalkuthCleared { get; private set; }
    [Networked] public NetworkBool YesodCleared { get; private set; }
    [Networked] public NetworkBool BranchCleared { get; private set; }
    [Networked] public NetworkBool DebugStageAccess { get; private set; }
    // loadingScene은 실제 씬을 로드 중인지, observedTransition은 이 화면에서 이미 닫기 연출을 시작했는지 나타냅니다. 타이머로 클라이언트들이 같은 씬 변경을 준비합니다.
    private bool loadingScene;
    private bool observedTransition;
    [Networked] internal NetworkBool DemoCompleted { get; private set; }
    [Networked] private TickTimer SceneTransitionTimer { get; set; }
    [Networked] private int NextSceneIndex { get; set; }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestRestart(PlayerHealth player, RpcInfo info = default)
    {
        if (player == null || (!Runner.IsSinglePlayer && info.Source != player.Object.StateAuthority))
            return;

        QueueScene(SceneManager.GetActiveScene().buildIndex);
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority || !Runner.IsSceneAuthority || loadingScene || !SceneTransitionTimer.Expired(Runner))
            return;

        loadingScene = true;
        Runner.LoadScene(SceneRef.FromIndex(NextSceneIndex));
    }

    public override void Render()
    {
        if (DemoCompleted && SceneManager.GetActiveScene().name == nameof(SceneName.Netzach))
            ScenePresentation.ShowDemoComplete(true);

        bool pending = SceneTransitionTimer.IsRunning;
        if (pending && !observedTransition)
            ScenePresentation.Cover();
        observedTransition = pending;
    }

    // 현재 씬과 앞 스테이지 클리어 여부를 확인합니다. 조건을 만족하면 클리어 기록을 남기고 월드맵 전환을 예약합니다.
    public void CompleteStage(SceneName stage)
    {
        if (!HasStateAuthority || !Runner.IsSceneAuthority || loadingScene || DemoCompleted || SceneTransitionTimer.IsRunning ||
            SceneManager.GetActiveScene().name != stage.ToString())
            return;

        if (stage == SceneName.Malkuth)
            MalkuthCleared = true;
        else if (stage == SceneName.Yesod && (MalkuthCleared || Runner.IsSinglePlayer || DebugStageAccess))
        {
            if (Runner.IsSinglePlayer)
                MalkuthCleared = true;
            YesodCleared = true;
        }
        else if ((YesodCleared || Runner.IsSinglePlayer || DebugStageAccess) && (stage == SceneName.Hod || stage == SceneName.Netzach))
        {
            if (Runner.IsSinglePlayer)
                MalkuthCleared = true;
            YesodCleared = true;
            BranchCleared = true;
        }
        else
            return;

        LoadStageCompletion(stage == SceneName.Netzach);
    }

    private void LoadStageCompletion(bool demoComplete)
    {
        if (demoComplete)
            DemoCompleted = true;
        else
            LoadScene(nameof(SceneName.Map));
    }

    // 개발 환경의 씬 권한자만 강제 완료할 수 있게 해 일반 빌드와 다른 참가자의 진행도를 보호합니다.
    public static bool CanUseStageDebug
    {
        get
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var manager = Instance;
            return manager != null && manager.Object != null && manager.Object.IsValid &&
                manager.HasStateAuthority && manager.Runner.IsSceneAuthority &&
                HackingConsole.IsGameplayScene(SceneManager.GetActiveScene());
#else
            return false;
#endif
        }
    }

    // 온라인에서는 방장이 두 명령을 실행합니다. 클리어 기록과 잠금 해제는 네트워크 상태로 같은 방에 공유됩니다.
    private bool CanRunRoomStageCommands
    {
        get
        {
            if (this != Instance || Object == null || !Object.IsValid || !HasStateAuthority ||
                !Runner.IsSceneAuthority || loadingScene || DemoCompleted || SceneTransitionTimer.IsRunning)
                return false;
            if (Runner.IsSharedModeMasterClient)
                return true;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            return Runner.IsSinglePlayer;
#else
            return false;
#endif
        }
    }

    public bool CanForceClearStage => CanRunRoomStageCommands &&
        IsStoryStageScene(SceneManager.GetActiveScene().name);

    public bool CanManageDebugStageAccess
    {
        get
        {
            string scene = SceneManager.GetActiveScene().name;
            return CanRunRoomStageCommands &&
                (IsStoryStageScene(scene) || scene == nameof(SceneName.Map) || scene == nameof(SceneName.Lobby));
        }
    }

    private static bool IsStoryStageScene(string scene) => scene is
        nameof(SceneName.Malkuth) or nameof(SceneName.Yesod) or nameof(SceneName.Hod) or nameof(SceneName.Netzach);

    public void ToggleDebugStageAccess()
    {
        if (CanManageDebugStageAccess)
            DebugStageAccess = !DebugStageAccess;
    }

    public void DebugExitStage()
    {
        if (this == Instance && CanUseStageDebug)
            LoadScene(nameof(SceneName.Map));
    }

    public void DebugForceClear()
    {
        if (!CanForceClearStage)
            return;
        switch (SceneManager.GetActiveScene().name)
        {
            case nameof(SceneName.Malkuth): MalkuthCleared = true; break;
            case nameof(SceneName.Yesod): MalkuthCleared = true; YesodCleared = true; break;
            case nameof(SceneName.Hod):
            case nameof(SceneName.Netzach):
                MalkuthCleared = true; YesodCleared = true; BranchCleared = true; break;
            default: return;
        }
        LoadStageCompletion(SceneManager.GetActiveScene().name == nameof(SceneName.Netzach));
    }

    // 공유 세션의 마스터와 로컬 Single 세션에 씬 진입 권한을 준다. 둘 다 씬 권한과 맵 로딩 상태를 검사한다.
    public bool CanSelectStage => Object != null && Object.IsValid && HasStateAuthority &&
        (Runner.IsSharedModeMasterClient || Runner.IsSinglePlayer) && Runner.IsSceneAuthority && !loadingScene && !SceneTransitionTimer.IsRunning &&
        SceneManager.GetActiveScene().name == nameof(SceneName.Map);

    public bool IsStageSelectable(SceneName stage) => (DebugStageAccess && IsPlayableStage(stage)) || (stage switch
    {
        SceneName.Malkuth => !MalkuthCleared,
        SceneName.Yesod => MalkuthCleared && !YesodCleared,
        SceneName.Hod or SceneName.Netzach => (bool)YesodCleared,
        _ => false
    });

    private static bool IsPlayableStage(SceneName stage) => stage is
        SceneName.Malkuth or SceneName.Yesod or SceneName.Hod or SceneName.Netzach;

    public void SelectStage(SceneName stage)
    {
        if (!CanSelectStage || !IsStageSelectable(stage))
            return;

        BranchCleared = false;
        LoadScene(stage.ToString());
    }

    public void OnSceneLoaded()
    {
        loadingScene = false;
        if (HasStateAuthority)
            SceneTransitionTimer = default;
        ScenePresentation.ShowDemoComplete(false);
        ScenePresentation.Reveal();
    }

    public override void Spawned()
    {
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (Instance == this)
            Instance = null;
    }

    public void LoadScene(string sceneName)
    {
        QueueScene(SceneUtility.GetBuildIndexByScenePath("Assets/Scenes/" + sceneName + ".unity"));
    }

    // 1. 씬을 바꿀 수 있는 쪽인지와 빌드 목록에 씬이 있는지 확인합니다.
    // 2. 다음 씬 번호와 닫기 시간을 공유합니다. 먼저 화면을 닫을 시간을 주고 타이머가 끝났을 때만 Fusion으로 씬을 불러옵니다.
    private void QueueScene(int buildIndex)
    {
        if (!HasStateAuthority || !Runner.IsSceneAuthority || loadingScene || DemoCompleted || SceneTransitionTimer.IsRunning)
            return;
        if (buildIndex < 0)
        {
            Debug.LogError("The destination scene is missing from the build.");
            return;
        }

        NextSceneIndex = buildIndex;
        SceneTransitionTimer = TickTimer.CreateFromSeconds(Runner, SceneCoverSeconds);
    }

    public bool IsCharacterTaken(MyEnum.CharacterType type)
    {
        return type > MyEnum.CharacterType.None && type < MyEnum.CharacterType.Count &&
               CharacterOwners[(int)type - 1] != Fusion.PlayerRef.None;
    }

    // 출구, 발판, 로봇, 레이저, 방 가시성에서 같은 캐릭터 목록을 사용한다. 로컬 테스트 목록 또는 Fusion의 등록 플레이어를 반환한다.
    public static IEnumerable<NetworkObject> GetPlayerObjects(NetworkRunner runner)
    {
        var localPlayers = LocalMultiplayerDebug.GetPlayers(runner);
        if (localPlayers != null)
        {
            for (int i = 0; i < localPlayers.Count; i++)
            {
                var player = localPlayers[i];
                if (player != null && player.IsValid)
                    yield return player;
            }
            yield break;
        }
        foreach (PlayerRef player in runner.ActivePlayers)
            if (runner.TryGetPlayerObject(player, out var obj) && obj != null)
                yield return obj;
    }

    // 기존 RPC는 PlayerRef로 행위자를 식별한다. 로컬 테스트에서는 하나의 PlayerRef를 현재 조작 중인 캐릭터에 대응시킨다.
    public bool TryGetGameplayPlayer(PlayerRef player, out NetworkObject obj)
    {
        if (LocalMultiplayerDebug.GetPlayers(Runner) != null && player == Runner.LocalPlayer)
        {
            obj = LocalMultiplayerDebug.GetSelected(Runner);
            return obj != null && obj.IsValid;
        }
        return Runner.TryGetPlayerObject(player, out obj);
    }

    // 해킹과 스테이지 장치가 같은 행위자 규칙을 사용하도록 캐릭터 종류 확인을 한곳에 둔다.
    public MyEnum.CharacterType GetGameplayCharacter(PlayerRef player)
    {
        if (LocalMultiplayerDebug.GetPlayers(Runner) != null && player == Runner.LocalPlayer)
            return LocalMultiplayerDebug.GetSelectedCharacter(Runner);
        return GetCharacter(player);
    }

    // 공유 세션의 캐릭터 선택 정보는 기존 CharacterOwners에서 읽는다.
    public MyEnum.CharacterType GetCharacter(PlayerRef player)
    {
        if (player == Fusion.PlayerRef.None)
            return MyEnum.CharacterType.None;

        for (int i = 0; i < CharacterOwners.Length; i++)
        {
            if (CharacterOwners[i] == player)
                return (MyEnum.CharacterType)(i + 1);
        }

        return MyEnum.CharacterType.None;
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_SelectCharacter(MyEnum.CharacterType type, RpcInfo info = default)
    {
        if (info.Source == Fusion.PlayerRef.None || type < MyEnum.CharacterType.None ||
            type >= MyEnum.CharacterType.Count)
            return;

        var previous = GetCharacter(info.Source);
        if (previous == type || IsCharacterTaken(type))
            return;

        if (previous != MyEnum.CharacterType.None)
            CharacterOwners.Set((int)previous - 1, Fusion.PlayerRef.None);
        if (type != MyEnum.CharacterType.None)
            CharacterOwners.Set((int)type - 1, info.Source);
    }
}

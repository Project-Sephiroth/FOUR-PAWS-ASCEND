using Fusion;
using Fusion.Sockets;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using static NetworkGameManager;

public class NetworkLauncher : MonoBehaviour, INetworkRunnerCallbacks
{
    [SerializeField] private NetworkRunner runnerPrefab; //네트워크에 접속 할 대상(나) 프리팹
    [SerializeField] private NetworkGameManager networkGMPrefab; //네트워크 게임 매니저 프리팹
    [SerializeField] NetworkObject selectCharUI;
    [SerializeField] Transform UICanvas;

    private NetworkGameManager networkGM; //네트워크 게임 매니저 내부 참조
    private NetworkRunner runner; //네트워크 접속 대상 내부 참조

    public UnityAction OnLobbyJoined;

    private List<SessionInfo> sessions;
    public List<SessionInfo> Sessions => sessions;
    public event UnityAction OnSessionUpdated;

    private void Start()
    {
        JoinLobby();

        DontDestroyOnLoad(this.gameObject);
    }

    #region Lobby

    /// <summary>
    /// 로비로 들어갑니다
    /// </summary>
    public async void JoinLobby()
    {
        runner = Instantiate(runnerPrefab);

        runner.AddCallbacks(this);

        var result = await runner.JoinSessionLobby(SessionLobby.Shared);

        Debug.Log($"로비 접속: {result.Ok}");

        if (result.Ok)
        {
            OnLobbyJoined?.Invoke();
        }
    }

    /// <summary>
    /// 세션이 새로 만들어지면 호출 받습니다
    /// </summary>
    /// <param name="runner"></param>
    /// <param name="sessionList"></param>
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
    {
        foreach (SessionInfo session in sessionList)
        {
            Debug.Log($"방 이름: {session.Name}");
            Debug.Log($"인원: {session.PlayerCount}/{session.MaxPlayers}");
            Debug.Log($"입장 가능: {session.IsOpen}");
        }

        sessions = sessionList;
        OnSessionUpdated?.Invoke();
    }
    #endregion

    #region Session Access

    public async void TryAccessSession(string sessionName)
    {
        //현재 접속 시도하려는 방 이름이 없다면 리턴
        if (string.IsNullOrWhiteSpace(sessionName))
            return;
            
        var sceneManager = runner.gameObject.AddComponent<NetworkSceneManagerDefault>();

        var result = await runner.StartGame(new StartGameArgs()
        {
            GameMode = GameMode.Shared, //포톤 섀어 모드 사용
            SessionName = sessionName,
            SceneManager = sceneManager,
            PlayerCount = 4 //4명으로 방 인원 제한
        });

        if (result.Ok)
        {
            Debug.Log("방 접속 성공");

            if (runner.IsSharedModeMasterClient)
            {
                networkGM = runner.Spawn(networkGMPrefab, Vector3.zero, Quaternion.identity);
                runner.Spawn(selectCharUI, Vector3.zero, Quaternion.identity); 
            }
            
        }
        else
        {
            Debug.LogError($"방 접속 실패: {result.ShutdownReason}");
        }
    }

    // 씬 로드가 끝나면 게임 매니저에 알리고, 씬에 있는 Spawn 태그에서 로컬 캐릭터를 만듭니다. 로비에서는 캐릭터 선택 UI가 별도로 처리합니다.
    public void OnSceneLoadDone(NetworkRunner runner)
    {
        Debug.Log("씬 로드 완료");

        //씬 전환 후 플레이어 오브젝트 스폰
        if (NetworkGameManager.Instance == null)
        {
            ScenePresentation.Reveal();
            return;
        }

        NetworkGameManager.Instance.OnSceneLoaded();
        if (SceneManager.GetActiveScene().name == nameof(SceneName.Lobby))
            return;
        if (PlayerSpawner.TryFindInScene(out var spawner))
            spawner.SpawnCharacter(runner);
    }

    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player)
    {
    }

    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player)
    {
    }

    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
    }

    private bool isLeaving = false;

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        var manager = NetworkGameManager.Instance;
        if (ScenePresentation.IsDemoCompleteVisible ||
            (manager != null && manager.Object != null && manager.Object.IsValid && manager.DemoCompleted))
            return;

        if (SceneManager.GetActiveScene().name != nameof(SceneName.Credits))
            ReturnToLobby(runner);
    }

    public async void ReturnToLobby(NetworkRunner runner)
    {
        if (isLeaving)
            return;

        isLeaving = true;
        try
        {
            await ScenePresentation.LoadLocalAsync(nameof(SceneName.Lobby), () => ShutdownSession(runner));
        }
        catch (Exception error)
        {
            isLeaving = false;
            Debug.LogException(error);
        }
    }

    public Task LeaveForTitle()
    {
        isLeaving = true;
        return ShutdownSession(runner);
    }

    // 먼저 Fusion 연결을 정리한 뒤 러너와 런처 오브젝트를 제거합니다. 씬만 바꿨을 때 이전 연결이 남는 일을 막습니다.
    private async Task ShutdownSession(NetworkRunner session)
    {
        if (session != null && session.IsRunning)
            await session.Shutdown();
        if (session != null)
            Destroy(session.gameObject);
        Destroy(gameObject);
    }

    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
    }

    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
    }

    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token)
    {
    }

    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
    {
    }

    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data)
    {
    }

    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress)
    {
    }

    public void OnInput(NetworkRunner runner, NetworkInput input)
    {
    }

    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input)
    {
    }

    public void OnConnectedToServer(NetworkRunner runner)
    {
    }


    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data)
    {
    }

    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken)
    {
    }

    public void OnSceneLoadStart(NetworkRunner runner)
    {
        ScenePresentation.Cover(immediate: true);
        if (runner.TryGetPlayerObject(runner.LocalPlayer, out var player) &&
            player != null && player.IsValid && player.HasStateAuthority)
            runner.Despawn(player);
    }
    #endregion
}
using Fusion;
using Fusion.Sockets;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class NetworkLauncher : MonoBehaviour, INetworkRunnerCallbacks
{
    [SerializeField] private NetworkRunner runnerPrefab; //네트워크에 접속 할 대상(나) 프리팹
    [SerializeField] private NetworkGameManager networkGMPrefab; //네트워크 게임 매니저 프리팹

    private NetworkGameManager networkGM; //네트워크 게임 매니저 내부 참조
    private NetworkRunner runner; //네트워크 접속 대상 내부 참조

    public UnityAction OnLobbyJoined;

    private List<SessionInfo> sessions;
    public List<SessionInfo> Sessions => sessions;
    public event UnityAction OnSessionUpdated;

    private void Start()
    {
        JoinLobby(); 
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
            OnLobbyJoined?.Invoke();
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

            networkGM = runner.Spawn(networkGMPrefab, Vector3.zero,Quaternion.identity);
        }
        else
        {
            Debug.LogError($"방 접속 실패: {result.ShutdownReason}");
        }
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

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
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

    public void OnSceneLoadDone(NetworkRunner runner)
    {
    }

    public void OnSceneLoadStart(NetworkRunner runner)
    {
    }
    #endregion
}
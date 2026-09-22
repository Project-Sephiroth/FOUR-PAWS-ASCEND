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

    private List<SessionInfo> sessions = new List<SessionInfo>();
    private bool joiningSession;
    public event UnityAction<bool> OnSessionAccessFinished;
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
        if (runner != null || runnerPrefab == null)
            return;

        runner = Instantiate(runnerPrefab);
        DontDestroyOnLoad(runner.gameObject);
        runner.AddCallbacks(this);
        var result = await runner.JoinSessionLobby(SessionLobby.Shared);
        if (result.Ok)
            OnLobbyJoined?.Invoke();
        else
            Debug.LogError("Unable to join the Shared lobby.", this);
    }

    private void OnDestroy()
    {
        if (runner != null)
            runner.RemoveCallbacks(this);
    }

    /// <summary>
    /// 세션이 새로 만들어지면 호출 받습니다
    /// </summary>
    /// <param name="runner"></param>
    /// <param name="sessionList"></param>
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
    {
        sessions = sessionList;
        OnSessionUpdated?.Invoke();
    }
    #endregion

    #region Session Access

    public async void TryAccessSession(string sessionName)
    {
        if (joiningSession || runner == null || runner.IsRunning ||
            string.IsNullOrWhiteSpace(sessionName))
            return;
        if (GameManager.Instance == null ||
            GameManager.Instance.MyCharacterType == MyEnum.CharacterType.None)
        {
            Debug.LogWarning("Select a character before entering a session.", this);
            OnSessionAccessFinished?.Invoke(false);
            return;
        }
        if (networkGMPrefab == null)
        {
            Debug.LogError("NetworkLauncher needs its NetworkGameManager prefab.", this);
            OnSessionAccessFinished?.Invoke(false);
            return;
        }

        joiningSession = true;
        var sceneManager = runner.GetComponent<NetworkSceneManagerDefault>();
        if (sceneManager == null)
            sceneManager = runner.gameObject.AddComponent<NetworkSceneManagerDefault>();

        try
        {
            var result = await runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Shared,
                SessionName = sessionName,
                SceneManager = sceneManager,
                PlayerCount = 4
            });

            if (result.Ok)
            {
                // Other peers receive this object; they must not spawn a second manager.
                if (runner.IsSharedModeMasterClient)
                    networkGM = runner.Spawn(networkGMPrefab, Vector3.zero, Quaternion.identity);
                OnSessionAccessFinished?.Invoke(true);
            }
            else
            {
                Debug.LogError("Unable to enter the Shared session.", this);
                OnSessionAccessFinished?.Invoke(false);
                await runner.Shutdown();
                Destroy(runner.gameObject);
                runner = null;
                JoinLobby();
            }
        }
        catch (Exception exception)
        {
            Debug.LogError($"Session setup failed ({exception.GetType().Name}).", this);
            OnSessionAccessFinished?.Invoke(false);
        }
        finally
        {
            joiningSession = false;
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
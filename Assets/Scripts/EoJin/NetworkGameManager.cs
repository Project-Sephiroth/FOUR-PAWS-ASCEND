using Fusion;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

public class NetworkGameManager : NetworkBehaviour
{
    public static NetworkGameManager Instance { get; private set; }

    [Networked]
    public int PlayerIdIndex { get; set; }

    public PlayerSpawner PlayerSpawner { get; private set; }

    public override void Spawned()
    {
        Instance = this;
        DontDestroyOnLoad(this.gameObject);

        PlayerIdIndex = 0;

        Debug.Log("NetworkGameManager 가 스폰");

        PlayerSpawner = Runner.GetComponent<PlayerSpawner>();
        Debug.Log("PlayerSpawner 를 NetworkGameManager 에 할당");
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (Instance == this)
            Instance = null;
    }

    public void LoadScene(string sceneName)
    {
        if(!Runner.IsSceneAuthority)
            return;

        Runner.LoadScene(sceneName);
        Debug.Log($"NetworkManager 에서 Scene 을 {sceneName}로 변경");
    }
}

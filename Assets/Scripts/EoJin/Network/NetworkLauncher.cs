using Fusion;
using UnityEngine;

public class NetworkLauncher : MonoBehaviour
{
    [SerializeField] private NetworkRunner runnerPrefab;
    [SerializeField] private NetworkGameManager networkGMPrefab;

    private NetworkGameManager networkGM;
    private NetworkRunner runner;

    private void Start()
    {
        StartSharedGame();
    }

    public async void StartSharedGame()
    {
        runner = Instantiate(runnerPrefab);

        var sceneManager = runner.gameObject.AddComponent<NetworkSceneManagerDefault>();

        var result = await runner.StartGame(new StartGameArgs()
        {
            GameMode = GameMode.Shared,
            SessionName = "MyRoom",
            SceneManager = sceneManager
        });

        if (result.Ok)
        {
            Debug.Log("规 立加 己傍");
            
            if (networkGM == null)
                runner.Spawn(networkGMPrefab, Vector3.zero, Quaternion.identity);
        }
        else
        {
            Debug.LogError($"规 立加 角菩: {result.ShutdownReason}");
        }
    }
}
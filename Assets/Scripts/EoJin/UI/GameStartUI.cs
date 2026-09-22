using UnityEngine;
using UnityEngine.UI;

public class GameStartUI : MonoBehaviour
{
    [SerializeField] private GameObject startButton;
    [SerializeField] private string gameplayScene = "GameScene";

    private Button button;

    private void Awake()
    {
        if (startButton != null)
            button = startButton.GetComponent<Button>();
    }

    private void Update()
    {
        var manager = NetworkGameManager.Instance;
        bool isSceneAuthority = manager != null && manager.IsReady && manager.Runner.IsSceneAuthority;
        if (startButton != null && startButton.activeSelf != isSceneAuthority)
            startButton.SetActive(isSceneAuthority);
        if (button != null)
            button.interactable = isSceneAuthority && manager.CanStartGame;
    }

    public void StartGame()
    {
        var manager = NetworkGameManager.Instance;
        if (manager != null)
            manager.LoadScene(gameplayScene);
    }

    public void StartLegacyGame()
    {
        var manager = NetworkGameManager.Instance;
        if (manager != null)
            manager.LoadScene("GameScene");
    }

    public async void QuitGame()
    {
        var manager = NetworkGameManager.Instance;
        if (manager != null && manager.Runner != null)
            await manager.Runner.Shutdown();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}

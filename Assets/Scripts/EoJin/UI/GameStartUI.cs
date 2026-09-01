using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameStartUI : MonoBehaviour
{
    [SerializeField] GameObject startButton;

    private void Start()
    {
        startButton.SetActive(GameManager.Instance.MyPlayerId == 0);
    }

    public void StartGame()
    {
        NetworkGameManager.Instance.LoadScene("GameScene");
    }

    public async void QuitGame()
    {
        if (NetworkGameManager.Instance.Runner != null)
            await NetworkGameManager.Instance.Runner.Shutdown();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}

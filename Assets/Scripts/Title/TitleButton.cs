using UnityEngine;
using UnityEngine.SceneManagement;

public class TitleButton : MonoBehaviour
{
    public void GameStart()
    {
        SceneManager.LoadScene(NetworkGameManager.SceneName.Lobby.ToString());
    }

    public void ExitGame()
    {
        Application.Quit();
    }
}

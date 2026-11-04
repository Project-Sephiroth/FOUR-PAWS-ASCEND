using UnityEngine;
using UnityEngine.SceneManagement;

public class TitleButton : MonoBehaviour
{
    public void GameStart()
    {
        SceneManager.LoadScene("LobbyScene");
    }

    public void ExitGame()
    {
        Application.Quit();
    }
}

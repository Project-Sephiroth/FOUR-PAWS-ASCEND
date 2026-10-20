using UnityEngine;
using UnityEngine.SceneManagement;

public class TitleButton : MonoBehaviour
{
    public void GameStart()
    {
        SceneManager.LoadScene(MyEnum.Scene.Lobby.ToString());
    }

    public void ExitGame()
    {
        Application.Quit();
    }
}

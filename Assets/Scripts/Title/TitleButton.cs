using UnityEngine;

public class TitleButton : MonoBehaviour
{
    public async void GameStart()
    {
        try
        {
            await ScenePresentation.LoadLocalAsync(nameof(NetworkGameManager.SceneName.Lobby));
        }
        catch (System.Exception error)
        {
            Debug.LogException(error);
        }
    }

    public void ExitGame()
    {
        GameMenu.ExitGame();
    }
}

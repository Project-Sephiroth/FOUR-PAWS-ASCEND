using UnityEngine;

public class TitleButton : MonoBehaviour
{
    // 게임 시작을 누르면 화면이 닫힌 뒤 로비로 들어갑니다. 로드가 끝난 후의 화면 열기도 같은 전환 프리팹이 처리합니다.
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
        Application.Quit();
    }
}

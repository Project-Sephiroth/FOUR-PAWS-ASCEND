using Fusion;
using UnityEngine;

public class GameStartUI : NetworkBehaviour
{
    [SerializeField] GameObject startButton;
    [SerializeField] SelectCharacterUI selectCharUI;

    public override void FixedUpdateNetwork()
    {
        if (startButton != null)
            startButton.SetActive(HasStateAuthority && CanStart());
    }

    // 씬을 열 권한이 있는 방장과 네 캐릭터의 선택 상태를 확인합니다. 한 명이라도 선택하지 않았으면 시작 버튼이 눌리지 않습니다.
    private bool CanStart()
    {
        var manager = NetworkGameManager.Instance;
        return manager != null && manager.Object != null && manager.Object.IsValid &&
               manager.Runner.IsSharedModeMasterClient && manager.Runner.IsSceneAuthority &&
               manager.IsCharacterTaken(MyEnum.CharacterType.Rabbit) &&
               manager.IsCharacterTaken(MyEnum.CharacterType.Frog) &&
               manager.IsCharacterTaken(MyEnum.CharacterType.Bear) &&
               manager.IsCharacterTaken(MyEnum.CharacterType.Mouse);
    }

    public void StartGame()
    {
        if (HasStateAuthority && CanStart())
            NetworkGameManager.Instance.LoadScene(NetworkGameManager.SceneName.Map.ToString());
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

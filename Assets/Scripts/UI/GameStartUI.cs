using Fusion;
using UnityEngine;

public class GameStartUI : NetworkBehaviour
{
    [SerializeField] GameObject startButton;
    [SerializeField] SelectCharacterUI selectCharUI;

    public override void Spawned()
    {
        base.Spawned();

        selectCharUI.OnSelect += CheckAllCharactersSelected;
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        base.Despawned(runner, hasState);

        selectCharUI.OnSelect -= CheckAllCharactersSelected;
    }

    void CheckAllCharactersSelected()
    {
        for (int i = 0; i <= GameManager.MaxPlayerCount; i++)
        {
            Debug.Log($"플레이어 {i}의 캐릭터 타입은 {(MyEnum.CharacterType)NetworkGameManager.Instance.PlayerCharacterType[i]}");

            //한 명이라도 설정이 안 됐다면
            if (NetworkGameManager.Instance.PlayerCharacterType[i] == (int)(MyEnum.CharacterType.None)) 
                return; //리턴
        }

        //다 설정이 되었다면 스타트 버튼 활성화
        //if (HasStateAuthority)
            startButton.SetActive(true);
    }

    public void StartGame()
    {
        if (!HasStateAuthority)
            return;

        if (GameManager.Instance.MyCharacterType == MyEnum.CharacterType.None)
            return;

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

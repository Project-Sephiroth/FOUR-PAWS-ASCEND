using System;
using UnityEngine;
using UnityEngine.UI;

// 입력칸과 버튼은 프리팹에서 배치합니다. 테스트 시작과 캐릭터 전환은 LocalMultiplayerDebug가 처리합니다.
public sealed class LocalDebugMenuView : MonoBehaviour
{
    public GameObject prompt;
    public InputField accessInput;
    public Text accessMessage;
    public Button confirm;
    public Button cancel;
    public GameObject panel;
    public GameObject setup;
    public GameObject playing;
    public Dropdown stage;
    public Toggle[] roster;
    public Button[] characters;
    public Button start;
    public Button stop;
    public Button close;
    public Text status;
    public GameObject sessionTools;
    public Button clearConsoles;
    public Button forceClearStage;

    public void Bind(string[] stageNames, Action confirmCode, Action cancelCode, Action startTest,
        Action stopTest, Action closeMenu, Action<int> selectStage, Action<int, bool> changeRoster,
        Action<int> selectCharacter)
    {
        stage.ClearOptions();
        stage.AddOptions(new System.Collections.Generic.List<string>(stageNames));
        stage.onValueChanged.AddListener(index => selectStage(index));
        confirm.onClick.AddListener(() => confirmCode());
        cancel.onClick.AddListener(() => cancelCode());
        start.onClick.AddListener(() => startTest());
        stop.onClick.AddListener(() => stopTest());
        close.onClick.AddListener(() => closeMenu());
        for (int i = 0; i < roster.Length; i++)
        {
            int slot = i;
            roster[i].onValueChanged.AddListener(included => changeRoster(slot, included));
            characters[i].onClick.AddListener(() => selectCharacter(slot));
        }
    }

    public void FocusCode()
    {
        accessInput.SetTextWithoutNotify(string.Empty);
        accessInput.Select();
        accessInput.ActivateInputField();
    }
}

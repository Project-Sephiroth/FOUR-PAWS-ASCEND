using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SelectCharacterUI : MonoBehaviour
{
    [SerializeField] private Button rabbit;
    [SerializeField] private Button bear;
    [SerializeField] private Button mouse;
    [SerializeField] private Button frog;
    [SerializeField] private TMP_Text selectionStatus;
    private MyEnum.CharacterType displayedRole = MyEnum.CharacterType.None;
    private bool displayedInSession;
    private bool hasDisplayedStatus;

    private void OnEnable()
    {
        rabbit.onClick.AddListener(SelectRabbit);
        bear.onClick.AddListener(SelectBear);
        mouse.onClick.AddListener(SelectMouse);
        frog.onClick.AddListener(SelectFrog);
    }

    private void OnDisable()
    {
        rabbit.onClick.RemoveListener(SelectRabbit);
        bear.onClick.RemoveListener(SelectBear);
        mouse.onClick.RemoveListener(SelectMouse);
        frog.onClick.RemoveListener(SelectFrog);
    }

    private void Update()
    {
        var manager = NetworkGameManager.Instance;
        if (manager == null || !manager.IsReady)
        {
            SetButtonsInteractable(true, true, true, true);
            var localRole = GameManager.Instance != null
                ? GameManager.Instance.MyCharacterType : MyEnum.CharacterType.None;
            ShowSelectionStatus(localRole, false);
            return;
        }

        var player = manager.Runner.LocalPlayer;
        SetButtonsInteractable(
            manager.IsRoleAvailable(MyEnum.CharacterType.Rabbit, player),
            manager.IsRoleAvailable(MyEnum.CharacterType.Bear, player),
            manager.IsRoleAvailable(MyEnum.CharacterType.Mouse, player),
            manager.IsRoleAvailable(MyEnum.CharacterType.Frog, player));

        if (manager.TryGetSelectedRole(player, out var role))
            ShowSelectionStatus(role, true);
        else
            ShowSelectionStatus(MyEnum.CharacterType.None, true);
    }

    private void SelectRabbit() => OnSelect(MyEnum.CharacterType.Rabbit);
    private void SelectBear() => OnSelect(MyEnum.CharacterType.Bear);
    private void SelectMouse() => OnSelect(MyEnum.CharacterType.Mouse);
    private void SelectFrog() => OnSelect(MyEnum.CharacterType.Frog);

    private void OnSelect(MyEnum.CharacterType type)
    {
        if (GameManager.Instance != null)
            GameManager.Instance.SetMyCharacter(type);
        var manager = NetworkGameManager.Instance;
        if (manager != null)
            manager.RequestCharacter(type);
    }

    private void ShowSelectionStatus(MyEnum.CharacterType role, bool inSession)
    {
        if (selectionStatus == null ||
            (hasDisplayedStatus && displayedRole == role && displayedInSession == inSession))
            return;

        hasDisplayedStatus = true;
        displayedRole = role;
        displayedInSession = inSession;
        if (role == MyEnum.CharacterType.None)
        {
            selectionStatus.text = inSession
                ? "빈 역할을 선택하세요."
                : "캐릭터를 선택한 뒤 방에 참가하세요.";
            return;
        }

        string characterName = role switch
        {
            MyEnum.CharacterType.Rabbit => "토끼",
            MyEnum.CharacterType.Bear => "곰",
            MyEnum.CharacterType.Mouse => "쥐",
            MyEnum.CharacterType.Frog => "개구리",
            _ => role.ToString()
        };
        selectionStatus.text = inSession
            ? $"선택 확정: {characterName}"
            : $"선택: {characterName}\n방을 만들거나 참가하세요.";
    }

    private void SetButtonsInteractable(bool rabbitAvailable, bool bearAvailable, bool mouseAvailable, bool frogAvailable)
    {
        rabbit.interactable = rabbitAvailable;
        bear.interactable = bearAvailable;
        mouse.interactable = mouseAvailable;
        frog.interactable = frogAvailable;
    }
}

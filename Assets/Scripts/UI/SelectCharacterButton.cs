using Fusion;
using UnityEngine;
using UnityEngine.Events;

public class SelectCharacterButton : NetworkBehaviour
{
    [SerializeField] private MyEnum.CharacterType myType;
    [SerializeField] private GameObject blocker;
    public UnityAction<MyEnum.CharacterType> OnClick;

    public bool IsBlocked { get; private set; }

    public void Blocked(bool locked)
    {
        IsBlocked = locked;
        if (blocker != null && blocker.activeSelf != locked)
            blocker.SetActive(locked);
    }

    public void OnButtonClicked()
    {
        var manager = NetworkGameManager.Instance;
        if (manager == null)
            return;

        if (manager.GetCharacter(Runner.LocalPlayer) == myType)
            OnClick?.Invoke(MyEnum.CharacterType.None);
        else if (!manager.IsCharacterTaken(myType))
            OnClick?.Invoke(myType);
    }
}

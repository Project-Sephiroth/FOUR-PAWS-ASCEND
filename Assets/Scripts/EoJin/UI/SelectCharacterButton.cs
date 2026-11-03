using Fusion;
using UnityEngine;
using UnityEngine.Events;

public class SelectCharacterButton : NetworkBehaviour
{
    [SerializeField] MyEnum.CharacterType myType;
    [SerializeField] GameObject blocker;
    public UnityAction<MyEnum.CharacterType> OnClick;

    [Networked, OnChangedRender(nameof(OnActiveChanged))]
    public NetworkBool IsBlocked { get; set; }


    public override void Spawned()
    {
        ApplyActive();
    }

    public void Blocked(bool locked)
    {
        if (!HasStateAuthority)
            return;

        IsBlocked = locked;
    }

    public void OnButtonClicked()
    {
        OnClick?.Invoke(myType);
    }

    private void OnActiveChanged()
    {
        ApplyActive();
    }

    private void ApplyActive()
    {
        if (blocker != null)
            blocker.SetActive(IsBlocked);
    }
}

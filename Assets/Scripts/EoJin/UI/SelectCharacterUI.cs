using Fusion;
using UnityEngine;
using UnityEngine.Events;

public class SelectCharacterUI : NetworkBehaviour
{
    [SerializeField] SelectCharacterButton rabbit;
    [SerializeField] SelectCharacterButton bear;
    [SerializeField] SelectCharacterButton mouse;
    [SerializeField] SelectCharacterButton frog;
    public event UnityAction OnSelect;

    public override void Spawned()
    {
        base.Spawned();

        RegisterButtonEvents();
    }
    
    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        base.Despawned(runner, hasState);

        UnRegisterButtonEvents();
    }

    void OnButtonSelected(MyEnum.CharacterType type)
    {
        Debug.Log($"캐릭터 선택 버튼: {type}");

        if (GameManager.Instance.MyCharacterType == MyEnum.CharacterType.None)
            RPC_OnSelectCharacter(type);
        else
            RPC_OnSelectCharacter(type, GameManager.Instance.MyCharacterType);
        
        GameManager.Instance.SetMyCharacter(type);
        NetworkGameManager.Instance.SetCharacter(GameManager.Instance.MyPlayerId, type);

        OnSelect?.Invoke();
    }

    [Rpc(RpcSources.All, RpcTargets.All)]
    public void RPC_OnSelectCharacter(MyEnum.CharacterType newType, MyEnum.CharacterType prevType = MyEnum.CharacterType.None)
    {
        if (prevType != MyEnum.CharacterType.None)
            BlockCharacterButton(prevType, false);

        BlockCharacterButton(newType, true);
    }

    void BlockCharacterButton(MyEnum.CharacterType type, bool isBlock)
    {
        switch (type)
        {
            case MyEnum.CharacterType.Rabbit:
                rabbit.Blocked(isBlock);
                break;
            case MyEnum.CharacterType.Bear:
                bear.Blocked(isBlock);
                break;
            case MyEnum.CharacterType.Mouse:
                mouse.Blocked(isBlock);
                break;
            case MyEnum.CharacterType.Frog:
                frog.Blocked(isBlock);
                break;
        }
    }

    private void RegisterButtonEvents()
    {
        rabbit.OnClick += OnButtonSelected;
        bear.OnClick += OnButtonSelected;
        mouse.OnClick += OnButtonSelected;
        frog.OnClick += OnButtonSelected;
    }

    private void UnRegisterButtonEvents()
    {
        rabbit.OnClick -= OnButtonSelected;
        bear.OnClick -= OnButtonSelected;
        mouse.OnClick -= OnButtonSelected;
        frog.OnClick -= OnButtonSelected;
    }
}

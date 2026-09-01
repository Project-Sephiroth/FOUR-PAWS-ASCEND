using UnityEngine;
using UnityEngine.UI;

public class SelectCharacterUI : MonoBehaviour
{
    [SerializeField] Button rabbit;
    [SerializeField] Button bear;
    [SerializeField] Button mouse;
    [SerializeField] Button frog;

    private void Start()
    {
        RegisterButtonEvents();
    }

    private void OnDisable()
    {
        UnRegisterButtonEvents();
    }

    void OnSelect(MyEnum.CharacterType type)
    {
        Debug.Log($"캐릭터 선택 버튼: {type}");
        GameManager.Instance.SetMyCharacter(type);
    }

    void RegisterButtonEvents()
    {
        rabbit.onClick.AddListener(() => { OnSelect(MyEnum.CharacterType.Rabbit); });
        bear.onClick.AddListener(() => { OnSelect(MyEnum.CharacterType.Bear); });
        mouse.onClick.AddListener(() => { OnSelect(MyEnum.CharacterType.Mouse); });
        frog.onClick.AddListener(() => { OnSelect(MyEnum.CharacterType.Frog); });
    }

    void UnRegisterButtonEvents()
    {
        rabbit.onClick.RemoveAllListeners();
        bear.onClick.RemoveAllListeners();
        mouse.onClick.RemoveAllListeners();
        frog.onClick.RemoveAllListeners();
    }
}

using UnityEngine;
using UnityEngine.Events;
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
            Destroy(this.gameObject);
        else
        {
            Instance = this;
            DontDestroyOnLoad(this.gameObject);
        }
    }

    public MyEnum.CharacterType MyCharacterType { get; private set; }
    public UnityAction OnCharacterChanged;
    public void SetMyCharacter(MyEnum.CharacterType type)
    {
        MyCharacterType = type;
        OnCharacterChanged?.Invoke();
        Debug.Log($"GameManager 에서 나의 캐릭터를 {MyCharacterType} 으로 변경했습니다");

    }

    public const int MaxPlayerCount = 4;
}

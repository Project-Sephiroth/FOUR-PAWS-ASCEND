using UnityEngine;

/// <summary>
/// SO 데이터들을 씬 내 구성원 들이 바로 참조할 수 있게 합니다
/// </summary>
public class SODataReferencer : MonoBehaviour
{
    [SerializeField] CharacterSO rabbit;
    public CharacterSO Rabbit => rabbit;
    [SerializeField] CharacterSO bear;
    public CharacterSO Bear => bear;
    [SerializeField] CharacterSO mouse;
    public CharacterSO Mouse => mouse;
    [SerializeField] CharacterSO frog;
    public CharacterSO Frog => frog;

    public static SODataReferencer Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
            Destroy(this.gameObject);

        Instance = this;
    }
}

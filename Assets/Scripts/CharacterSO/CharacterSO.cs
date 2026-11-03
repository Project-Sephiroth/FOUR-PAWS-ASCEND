using UnityEngine;

[CreateAssetMenu(fileName = "CharacterSO",menuName = "EoJin/CharacterSO")]
public class CharacterSO : ScriptableObject
{
    public MyEnum.CharacterType CharacterType;
    public GameObject Prefab;
}

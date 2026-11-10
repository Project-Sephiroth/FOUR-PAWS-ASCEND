using Fusion;
using UnityEngine;

public class PlayerSpawner : SimulationBehaviour
{
    [Header("플레이어 프리팹")]
    public GameObject rabbitPrefab;
    public GameObject bearPrefab;
    public GameObject mousePrefab;
    public GameObject frogPrefab;

    [Header("플레이어 스폰 지점")]
    public Vector2 rabbitSpawnPos;
    public Vector2 bearSpawnPos;
    public Vector2 mouseSpawnPos;
    public Vector2 frogSpawnPos;

    public void SpawnCharacter(NetworkRunner runner)
    {
        PlayerRef myPlayerRef = NetworkGameManager.Instance.PlayerRef;
        Vector2 mySpawnPos = Vector2.zero;
        GameObject myPrefab = null;

        switch (GameManager.Instance.MyCharacterType)
        {
            case MyEnum.CharacterType.Rabbit:
                mySpawnPos = rabbitSpawnPos;
                myPrefab = rabbitPrefab;
                break;
            case MyEnum.CharacterType.Bear:
                mySpawnPos = bearSpawnPos;
                myPrefab = bearPrefab;
                break;
            case MyEnum.CharacterType.Mouse:
                mySpawnPos = mouseSpawnPos;
                myPrefab = mousePrefab;
                break;
            case MyEnum.CharacterType.Frog:
                mySpawnPos = frogSpawnPos;
                myPrefab = frogPrefab;
                break;

        }
        var myPlayerObj = runner.Spawn(myPrefab, mySpawnPos, Quaternion.identity, myPlayerRef);

        runner.SetPlayerObject(myPlayerRef, myPlayerObj);

        Debug.Log($"내 캐릭터 {GameManager.Instance.MyCharacterType} 를 스폰");
    }

}

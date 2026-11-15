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

    // 온라인 로비와 씬 전환에서 쓰는 진입점이다. 현재 로컬 참가자가 선택한 캐릭터를 생성하고 Fusion에 등록한다.
    public NetworkObject SpawnCharacter(NetworkRunner runner)
    {
        PlayerRef myPlayerRef = runner.LocalPlayer;
        var type = NetworkGameManager.Instance.GetCharacter(myPlayerRef);
        var player = SpawnCharacter(runner, type, myPlayerRef);
        if (player != null)
            runner.SetPlayerObject(myPlayerRef, player);

        return player;
    }

    // 로컬 테스트도 기존 씬의 프리팹과 시작 좌표를 쓰도록 스폰 로직을 공유한다. PlayerRef 등록은 호출자가 결정한다.
    public NetworkObject SpawnCharacter(NetworkRunner runner, MyEnum.CharacterType type, PlayerRef inputAuthority)
    {
        // mySpawnPos는 선택한 캐릭터의 시작 좌표, myPrefab은 그 캐릭터를 만들 프리팹입니다. 위치와 프리팹을 씬에 두어서 코드 수정 없이 출발점을 바꿀 수 있습니다.
        Vector2 mySpawnPos = Vector2.zero;
        GameObject myPrefab = null;

        switch (type)
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
        if (myPrefab == null)
        {
            Debug.LogError("선택한 캐릭터의 프리팹이 없습니다.");
            return null;
        }

        var myPlayerObj = runner.Spawn(myPrefab, mySpawnPos, Quaternion.identity, inputAuthority);
        if (myPlayerObj == null)
            return null;

        // 캐릭터 종류에 따른 탑승 허용 여부와 토끼의 이단 점프를 기존 컴포넌트에 적용한다.
        if (myPlayerObj.TryGetComponent(out Passenger passenger))
            passenger.SetCharacterType(type);

        if (myPlayerObj.TryGetComponent(out Mover mover))
            mover.SetDoubleJump(type == MyEnum.CharacterType.Rabbit);

        Debug.Log($"내 캐릭터 {type} 를 스폰");
        return myPlayerObj;
    }

}

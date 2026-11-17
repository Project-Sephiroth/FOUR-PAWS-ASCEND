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

    internal static bool TryFindInScene(out PlayerSpawner spawner)
    {
        var spawnRoot = GameObject.FindWithTag("Spawn");
        spawner = null;
        return spawnRoot != null && spawnRoot.TryGetComponent(out spawner);
    }

    // 온라인 로비와 씬 전환에서 쓰는 진입점이다. 현재 로컬 참가자가 선택한 캐릭터를 생성하고 Fusion에 등록한다.
    public NetworkObject SpawnCharacter(NetworkRunner runner)
    {
        PlayerRef myPlayerRef = runner.LocalPlayer;
        var type = NetworkGameManager.Instance.GetCharacter(myPlayerRef);
        // 같은 씬에서 같은 캐릭터를 다시 등록하라는 요청이 들어와도 기존 플레이어를 사용합니다.
        if (runner.TryGetPlayerObject(myPlayerRef, out var existing) && existing != null && existing.IsValid &&
            existing.gameObject.scene == gameObject.scene && existing.TryGetComponent(out Passenger current) &&
            current.CharacterType == type)
            return existing;
        var player = SpawnCharacter(runner, type, myPlayerRef);
        if (player != null)
            runner.SetPlayerObject(myPlayerRef, player);

        return player;
    }

    // 로컬 테스트도 기존 씬의 프리팹과 시작 좌표를 쓰도록 스폰 로직을 공유한다. PlayerRef 등록은 호출자가 결정한다.
    public NetworkObject SpawnCharacter(NetworkRunner runner, MyEnum.CharacterType type, PlayerRef inputAuthority)
    {
        var (prefab, position) = type switch
        {
            MyEnum.CharacterType.Rabbit => (rabbitPrefab, rabbitSpawnPos),
            MyEnum.CharacterType.Bear => (bearPrefab, bearSpawnPos),
            MyEnum.CharacterType.Mouse => (mousePrefab, mouseSpawnPos),
            MyEnum.CharacterType.Frog => (frogPrefab, frogSpawnPos),
            _ => (null, Vector2.zero)
        };
        if (prefab == null)
        {
            Debug.LogError("선택한 캐릭터의 프리팹이 없습니다.");
            return null;
        }

        var player = runner.Spawn(prefab, position, Quaternion.identity, inputAuthority);
        if (player == null)
            return null;

        // 캐릭터 종류에 따른 탑승 허용 여부와 토끼의 이단 점프를 기존 컴포넌트에 적용한다.
        if (player.TryGetComponent(out Passenger passenger))
            passenger.SetCharacterType(type);

        if (player.TryGetComponent(out Mover mover))
            mover.SetDoubleJump(type == MyEnum.CharacterType.Rabbit);

        Debug.Log($"내 캐릭터 {type} 를 스폰");
        return player;
    }

}

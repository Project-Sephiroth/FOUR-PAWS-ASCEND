using Fusion;
using UnityEngine;

// The Runner's only player spawner. Scene objects must be registered before spawning.
public class PcsPlayerSpawner : SimulationBehaviour, IPlayerJoined, ISceneLoadStart, ISceneLoadDone
{
    [SerializeField] private CharacterSO[] characters;
    [SerializeField] private string legacyGameplayScene = "GameScene";
    [SerializeField] private Vector3 legacySpawnPosition = new Vector3(0f, 1f, 0f);

    private PcsPuzzleDirector director;
    private NetworkGameManager selectionManager;
    private MyEnum.CharacterType requestedRole = MyEnum.CharacterType.None;
    private bool sceneReady;
    private bool legacyScene;
    private bool spawnFailed;

    void IPlayerJoined.PlayerJoined(PlayerRef player)
    {
        // Joining the room is intentionally not a spawn trigger.
        if (player == Runner.LocalPlayer)
            requestedRole = MyEnum.CharacterType.None;
    }

    void ISceneLoadStart.SceneLoadStart(SceneRef sceneRef)
    {
        sceneReady = false;
        director = null;
        legacyScene = false;
        spawnFailed = false;
    }

    void ISceneLoadDone.SceneLoadDone(in SceneLoadDoneArgs sceneInfo)
    {
        director = null;
        foreach (var root in sceneInfo.RootGameObjects)
        {
            director = root.GetComponentInChildren<PcsPuzzleDirector>(true);
            if (director != null)
                break;
        }

        legacyScene = sceneInfo.Scene.name == legacyGameplayScene;
        sceneReady = director != null || legacyScene;
        spawnFailed = false;
    }

    public override void FixedUpdateNetwork()
    {
        var manager = NetworkGameManager.Instance;
        if (manager == null || manager.Runner != Runner || !manager.Object.IsValid)
            return;

        // Selection can happen before the session and its manager finish joining.
        var localGame = GameManager.Instance;
        if (localGame != null && localGame.MyCharacterType != MyEnum.CharacterType.None &&
            (selectionManager != manager || requestedRole != localGame.MyCharacterType))
        {
            selectionManager = manager;
            requestedRole = localGame.MyCharacterType;
            manager.RequestCharacter(requestedRole);
        }

        if (!sceneReady || spawnFailed || Runner.SceneManager.IsBusy ||
            !manager.TryGetSelectedRole(Runner.LocalPlayer, out var role))
            return;

        if (Runner.TryGetPlayerObject(Runner.LocalPlayer, out var existing) && existing != null)
            return;

        Vector3 position;
        Quaternion rotation;
        if (director != null)
        {
            if (!manager.TeamLocked || !director.CanSpawnPlayers ||
                !director.TryGetSpawnPose(Runner.LocalPlayer, role, out position, out rotation))
                return;
        }
        else if (legacyScene)
        {
            position = legacySpawnPosition;
            rotation = Quaternion.identity;
        }
        else
        {
            return;
        }

        var prefab = GetPrefab(role);
        if (prefab == null || prefab.GetComponent<NetworkObject>() == null)
        {
            spawnFailed = true;
            Debug.LogError("PcsPlayerSpawner: the selected CharacterSO needs a NetworkObject prefab.", this);
            return;
        }

        var playerObject = Runner.Spawn(prefab, position, rotation, Runner.LocalPlayer);
        if (playerObject != null)
            Runner.SetPlayerObject(Runner.LocalPlayer, playerObject);
    }

    private GameObject GetPrefab(MyEnum.CharacterType role)
    {
        if (characters == null)
            return null;

        foreach (var character in characters)
        {
            if (character != null && character.CharacterType == role)
                return character.Prefab;
        }
        return null;
    }
}

using Fusion;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

public class SelectCharacterUI : NetworkBehaviour
{
    [SerializeField] public SelectCharacterButton rabbit;
    [SerializeField] public SelectCharacterButton bear;
    [SerializeField] public SelectCharacterButton mouse;
    [SerializeField] public SelectCharacterButton frog;
    public event UnityAction OnSelect;
    // lobbyPlayer는 로비에서 지금 고른 캐릭터의 오브젝트입니다. 선택을 바꾸면 이전 캐릭터를 치우고 바뀐 캐릭터를 표시합니다.
    private NetworkObject lobbyPlayer;
    private MyEnum.CharacterType spawnedType;

    public override void Spawned()
    {
        rabbit.OnClick += OnButtonSelected;
        bear.OnClick += OnButtonSelected;
        mouse.OnClick += OnButtonSelected;
        frog.OnClick += OnButtonSelected;
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        rabbit.OnClick -= OnButtonSelected;
        bear.OnClick -= OnButtonSelected;
        mouse.OnClick -= OnButtonSelected;
        frog.OnClick -= OnButtonSelected;
    }

    private void OnButtonSelected(MyEnum.CharacterType type)
    {
        var manager = NetworkGameManager.Instance;
        if (manager != null)
            manager.RPC_SelectCharacter(type);
    }

    private void UpdateLobbyPlayer(MyEnum.CharacterType type)
    {
        if (type == spawnedType || SceneManager.GetActiveScene().name != NetworkGameManager.SceneName.Lobby.ToString())
            return;

        spawnedType = type;
        if (lobbyPlayer != null && lobbyPlayer.IsValid && lobbyPlayer.HasStateAuthority)
        {
            Runner.Despawn(lobbyPlayer);
            lobbyPlayer = null;
        }

        if (type == MyEnum.CharacterType.None)
            return;

        var root = GameObject.FindWithTag("Spawn");
        if (root != null && root.TryGetComponent(out PlayerSpawner spawner))
            lobbyPlayer = spawner.SpawnCharacter(Runner);
    }

    public override void Render()
    {
        var manager = NetworkGameManager.Instance;
        var game = GameManager.Instance;
        if (manager == null || game == null)
            return;

        var selected = manager.GetCharacter(Runner.LocalPlayer);
        if (game.MyCharacterType != selected)
        {
            game.SetMyCharacter(selected);
            OnSelect?.Invoke();
        }
        UpdateLobbyPlayer(selected);

        rabbit.Blocked(manager.IsCharacterTaken(MyEnum.CharacterType.Rabbit));
        bear.Blocked(manager.IsCharacterTaken(MyEnum.CharacterType.Bear));
        mouse.Blocked(manager.IsCharacterTaken(MyEnum.CharacterType.Mouse));
        frog.Blocked(manager.IsCharacterTaken(MyEnum.CharacterType.Frog));
    }
}

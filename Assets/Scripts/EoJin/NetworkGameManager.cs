using Fusion;
using System.Collections.Generic;
using UnityEngine;

public class NetworkGameManager : NetworkBehaviour
{
    public static NetworkGameManager Instance { get; private set; }

    [Networked] public int PlayerIdIndex { get; set; }
    [Networked] public NetworkBool TeamLocked { get; private set; }
    [Networked, Capacity(4)] private NetworkDictionary<PlayerRef, int> SelectedRoles => default;
    [Networked, Capacity(4)] private NetworkDictionary<PlayerRef, int> StartingTeam => default;

    public PcsPlayerSpawner PlayerSpawner { get; private set; }
    private readonly List<PlayerRef> departedPlayers = new List<PlayerRef>(4);

    public bool IsReady => Object != null && Object.IsValid;
    public bool CanStartGame
    {
        get
        {
            if (!IsReady || TeamLocked || !Runner.IsSceneAuthority || !HasStateAuthority)
                return false;

            int count = 0;
            foreach (var player in Runner.ActivePlayers)
            {
                if (!SelectedRoles.TryGet(player, out var role) || !IsValidRole(role))
                    return false;
                count++;
            }
            // Solo entry supports inspection; the puzzle itself still requires all four roles.
            return count > 0;
        }
    }

    public override void Spawned()
    {
        Instance = this;
        Runner.MakeDontDestroyOnLoad(gameObject);
        PlayerSpawner = Runner.GetComponent<PcsPlayerSpawner>();
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (Instance == this)
            Instance = null;
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority)
            return;

        RemoveDepartedPlayers();
    }

    private void RemoveDepartedPlayers()
    {
        departedPlayers.Clear();
        foreach (var entry in SelectedRoles)
        {
            if (!IsConnected(entry.Key))
                departedPlayers.Add(entry.Key);
        }
        foreach (var player in departedPlayers)
            SelectedRoles.Remove(player);
        // StartingTeam is deliberately retained: late join is not role replacement.
    }

    public bool TryGetSelectedRole(PlayerRef player, out MyEnum.CharacterType role)
    {
        role = MyEnum.CharacterType.None;
        if (!IsReady || !SelectedRoles.TryGet(player, out var value) || !IsValidRole(value))
            return false;
        role = (MyEnum.CharacterType)value;
        return true;
    }

    public bool IsStartingPlayer(PlayerRef player)
    {
        return IsReady && TeamLocked && StartingTeam.ContainsKey(player);
    }

    public bool IsRoleAvailable(MyEnum.CharacterType role, PlayerRef player)
    {
        if (!IsReady || !IsValidRole((int)role))
            return false;
        if (TeamLocked)
            return !StartingTeam.ContainsKey(player);

        foreach (var entry in SelectedRoles)
        {
            if (entry.Key != player && entry.Value == (int)role)
                return false;
        }
        return true;
    }

    public void RequestCharacter(MyEnum.CharacterType role)
    {
        if (IsReady && IsValidRole((int)role))
            RPC_RequestCharacter((int)role);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestCharacter(int role, RpcInfo info = default)
    {
        if (!HasStateAuthority || !IsConnected(info.Source) || !IsValidRole(role))
            return;
        if (TeamLocked && StartingTeam.ContainsKey(info.Source))
            return;
        if (!IsRoleAvailable((MyEnum.CharacterType)role, info.Source))
            return;
        RemoveDepartedPlayers();
        if (!SelectedRoles.ContainsKey(info.Source) && SelectedRoles.Count >= SelectedRoles.Capacity)
            return;

        SelectedRoles.Set(info.Source, role);
    }

    public void LoadScene(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName) || !CanStartGame)
            return;

        StartingTeam.Clear();
        foreach (var player in Runner.ActivePlayers)
        {
            if (SelectedRoles.TryGet(player, out var role))
                StartingTeam.Add(player, role);
        }
        TeamLocked = true;
        Runner.LoadScene(sceneName);
    }

    private bool IsConnected(PlayerRef player)
    {
        foreach (var active in Runner.ActivePlayers)
        {
            if (active == player)
                return true;
        }
        return false;
    }

    private static bool IsValidRole(int role)
    {
        return role >= (int)MyEnum.CharacterType.Rabbit && role <= (int)MyEnum.CharacterType.Frog;
    }
}

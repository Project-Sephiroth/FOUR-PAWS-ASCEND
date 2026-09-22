using System.Collections.Generic;
using Fusion;
using UnityEngine;

public struct PcsDeviceState : INetworkStruct
{
    public Vector2 Position;
    public Vector2 Velocity;
    public NetworkId Actor;
    public TickTimer Timer;
    public int Active;
    public int Phase;
    public int Counter;
    public int Window;
    public PlayerRef HoldOwner;
}

/// <summary>One Shared state authority owns all puzzle decisions and device poses.</summary>
public partial class PcsPuzzleDirector : NetworkBehaviour, IStateAuthorityChanged
{
    public static PcsPuzzleDirector Instance { get; private set; }
    public PcsPuzzleDevice[] Devices = new PcsPuzzleDevice[0];
    public Transform[] TutorialSpawns = new Transform[6];
    public Transform[] StageOneSpawns = new Transform[6];
    public Transform[] StageTwoSpawns = new Transform[6];
    public Transform LateJoinWaiting;
    public PcsPuzzleDevice ShaftLeftBoardingArea;
    public PcsPuzzleDevice ShaftRabbitBoardingArea;
    public bool RequireShaftBoarding = true;
    public Sprite ProjectileSprite;
    [Min(1)] public int InitialEnergy = 5;
    [Min(1)] public int MaximumEnergy = 8;
    [Min(1)] public int ChannelCost = 1;
    [Min(0.1f)] public float ChannelDuration = 3f;
    [Min(0.1f)] public float RefillCooldown = 3f;
    [Min(1)] public int PlayerHealth = 3;
    [Min(0.1f)] public float RemoteRange = 5f;
    [Min(0.1f)] public float ProjectileSpeed = 14f;
    [Min(0.1f)] public float ProjectileLifetime = 3f;
    [Min(0.1f)] public float ThrowCooldown = 0.7f;
    [Min(0.1f)] public float DamageInvulnerability = 1.2f;
    [Min(0.1f)] public float StunDuration = 1.5f;
    [Min(0.1f)] public float PullSpeed = 6f;
    [Min(0.1f)] public float ResetDelay = 0.4f;
    [Min(0.1f)] public float HackPulseInterval = 0.3f;
    public LayerMask ObstructionMask;
    public Font HudFont;

    [Networked] public int ResetEpoch { get; private set; }
    [Networked] public int ActiveSection { get; private set; }
    [Networked] public int CompletedTutorialMask { get; private set; }
    [Networked] public NetworkBool StageOneHacked { get; private set; }
    [Networked] public NetworkBool StageOneComplete { get; private set; }
    [Networked] public NetworkBool LeverLower { get; private set; }
    [Networked] public NetworkBool ShaftStarted { get; private set; }
    [Networked] public NetworkBool ShaftArrived { get; private set; }
    [Networked] public int Energy { get; private set; }
    [Networked] public int Batteries { get; private set; }
    [Networked] private NetworkBool Initialized { get; set; }
    [Networked] private TickTimer RefillTimer { get; set; }
    [Networked] private TickTimer ResetTimer { get; set; }
    [Networked, Capacity(128)] public NetworkArray<PcsDeviceState> States => default;
    [Networked, Capacity(4)] public NetworkArray<PcsDeviceState> Channels => default;
    [Networked, Capacity(24)] private NetworkArray<PcsDeviceState> Projectiles => default;
    [Networked, Capacity(6)] private NetworkArray<int> Health => default;
    [Networked, Capacity(6)] private NetworkArray<int> RoomEpochs => default;
    [Networked, Capacity(6)] private NetworkArray<TickTimer> DamageTimers => default;
    [Networked, Capacity(6)] private NetworkArray<TickTimer> ThrowTimers => default;

    public bool CanSpawnPlayers => Object != null && Object.IsValid && Initialized;
    private readonly PcsProjectile[] projectileViews = new PcsProjectile[24];
    private readonly List<NetworkObject> players = new List<NetworkObject>(4);
    private readonly List<RaycastHit2D> obstructionHits = new List<RaycastHit2D>(16);
    private int lastPresentedEpoch = -1;
    private int lastResetFrame = -1;
    private int resetSerial;
    private const int AllRoles = 30;
    private const float RemoteHoldLease = 0.65f;
    private static readonly string[] ChannelNames = { "주황", "초록", "보라", "하늘" };
    private GUIStyle hudLabel;
    private GUIStyle hudBox;

    public override void Spawned()
    {
#if UNITY_EDITOR
        EditorForgetDeviceTests();
#endif
        Instance = this;
        if (Devices.Length > 128)
        {
            Debug.LogError("Puzzle has more than 128 registered devices.", this);
            enabled = false;
            return;
        }
        for (int i = 0; i < Devices.Length; i++)
        {
            if (Devices[i] == null) continue;
            if (Devices[i].DeviceId != i)
                Debug.LogError("Puzzle device ID must equal its explicit registry index: " + Devices[i].name, Devices[i]);
        }
        if (HasStateAuthority && !Initialized)
        {
            ActiveSection = 0;
            Energy = InitialEnergy;
            for (int i = 1; i <= 4; i++) Health.Set(i, PlayerHealth);
            for (int i = 0; i < Devices.Length; i++) ResetDevice(i);
            Initialized = true;
        }
        if (Initialized) ApplyPresentation(true);
    }

    public void StateAuthorityChanged()
    {
#if UNITY_EDITOR
        EditorForgetDeviceTests();
#endif
        if (!HasStateAuthority || !CanSpawnPlayers) return;
        // A transferred director never inherits an unconfirmed owner's hold.
        // A still-held key can establish a fresh, validated lease on its next renewal.
        for (int i = 0; i < Devices.Length; i++)
            if (Devices[i] != null && Devices[i].RequiresRemoteHold) ClearRemoteHold(Devices[i]);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
#if UNITY_EDITOR
        EditorForgetDeviceTests();
#endif
        if (Instance == this) Instance = null;
        for (int i = 0; i < projectileViews.Length; i++)
            if (projectileViews[i] != null) Destroy(projectileViews[i].gameObject);
    }

    public bool TryGetSpawnPose(PlayerRef player, MyEnum.CharacterType role, out Vector3 position, out Quaternion rotation)
    {
        rotation = Quaternion.identity;
        bool waiting = ActiveSection != 0 || (NetworkGameManager.Instance != null &&
            NetworkGameManager.Instance.TeamLocked && !NetworkGameManager.Instance.IsStartingPlayer(player));
        Transform spawn = waiting ? (ActiveSection >= 2 ? GetSpawn(StageTwoSpawns, role) : LateJoinWaiting) : GetSpawn(TutorialSpawns, role);
        position = spawn != null ? spawn.position : transform.position;
        return CanSpawnPlayers && spawn != null;
    }

    public Vector2 GetRespawnPosition(MyEnum.CharacterType role)
    {
        Transform[] set = ActiveSection == 0 ? TutorialSpawns : ActiveSection == 1 ? StageOneSpawns : StageTwoSpawns;
        Transform spawn = GetSpawn(set, role);
        return spawn != null ? (Vector2)spawn.position : (Vector2)transform.position;
    }

    public int GetResetEpoch(MyEnum.CharacterType role)
    {
        return CanSpawnPlayers ? ResetEpoch + RoomEpochs[(int)role] * 1000000 : 0;
    }

    public int GetHealth(MyEnum.CharacterType role) => CanSpawnPlayers ? Health[(int)role] : PlayerHealth;
    public float ChannelRemaining(int channel) => CanSpawnPlayers && channel >= 0 && channel < 4 ?
        Channels[channel].Timer.RemainingTime(Runner) ?? 0f : 0f;

    private static Transform GetSpawn(Transform[] set, MyEnum.CharacterType role)
    {
        int index = (int)role;
        return index > 0 && index < set.Length ? set[index] : null;
    }

    public PcsPuzzleDevice GetDevice(int id) => id >= 0 && id < Devices.Length ? Devices[id] : null;

    public void RequestAction(NetworkObject actor, PcsPuzzleAction action, int targetId, Vector2 aim, int channel = 0)
    {
        if (!CanSpawnPlayers || actor == null || !actor.HasStateAuthority) return;
        PcsPlayerAbilities abilities = actor.GetComponent<PcsPlayerAbilities>();
        if (abilities != null) RPC_Action(actor.Id, action, targetId, aim, channel, GetResetEpoch(abilities.CharacterType));
    }

    public void RequestRemoteHold(NetworkObject actor, int targetId, bool held, Vector2 aim)
    {
        if (!CanSpawnPlayers || actor == null || !actor.IsValid || !actor.HasStateAuthority) return;
        PcsPlayerAbilities ability = actor.GetComponent<PcsPlayerAbilities>();
        if (ability != null) RPC_RemoteHold(actor.Id, targetId, held, aim, GetResetEpoch(ability.CharacterType));
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RemoteHold(NetworkId actorId, int targetId, bool held, Vector2 aim, int epoch, RpcInfo info = default)
    {
        if (!HasStateAuthority || !Initialized) return;
        PcsPuzzleDevice device = GetDevice(targetId);
        if (device == null || device.DeviceId != targetId || !device.RequiresRemoteHold || device.Kind != PcsDeviceKind.Lever) return;
#if UNITY_EDITOR
        if (EditorOverridesRemoteHold(device)) return;
#endif
        PcsDeviceState state = States[targetId];
        // Release remains valid after leaving the range, dying or losing participation.
        // The recorded actor and original sender must still match the held request.
        if (!held)
        {
            if (state.Actor == actorId && state.HoldOwner == info.Source) ClearRemoteHold(device);
            return;
        }
        if (!Runner.TryFindObject(actorId, out NetworkObject actor) || actor.StateAuthority != info.Source ||
            epoch != GetResetEpoch(MyEnum.CharacterType.Frog) ||
            !ResetTimer.ExpiredOrNotRunning(Runner) || !ValidRemoteHold(actor, device, aim)) return;
        if (state.Active != 0 && state.Actor != actorId && !state.Timer.ExpiredOrNotRunning(Runner)) return;
        state.Active = 1;
        state.Actor = actorId;
        state.HoldOwner = info.Source;
        state.Velocity = aim;
        state.Timer = TickTimer.CreateFromSeconds(Runner, RemoteHoldLease);
        States.Set(targetId, state);
        LeverLower = true;
        SetLinks(device, true);
    }

    private bool ValidRemoteHold(NetworkObject actor, PcsPuzzleDevice device, Vector2 aim)
    {
        if (ActiveSection != 1 || device.Section != 1 || !StageOneHacked || !device.isActiveAndEnabled ||
            !IsParticipant(actor) || !actor.gameObject.activeInHierarchy ||
            !float.IsFinite(aim.x) || !float.IsFinite(aim.y)) return false;
        PcsPlayerAbilities ability = actor.GetComponent<PcsPlayerAbilities>();
        Mover mover = actor.GetComponent<Mover>();
        Rigidbody2D body = actor.GetComponent<Rigidbody2D>();
        if (ability == null || !ability.isActiveAndEnabled || ability.CharacterType != MyEnum.CharacterType.Frog ||
            (device.RequiredRole != MyEnum.CharacterType.None && device.RequiredRole != ability.CharacterType) ||
            Health[(int)ability.CharacterType] <= 0 || mover == null || !mover.isActiveAndEnabled ||
            body == null || !body.simulated) return false;
        bool connected = false;
        foreach (PlayerRef player in Runner.ActivePlayers)
            if (player == actor.StateAuthority) { connected = true; break; }
        if (!connected) return false;
        Collider2D collider = actor.GetComponent<Collider2D>();
        if (collider == null || !collider.enabled) return false;
        Vector2 center = collider.bounds.center;
        Vector2 direction = aim - center;
        Vector2 offset = device.InteractionPoint - center;
        if (direction.sqrMagnitude < 0.001f || offset.sqrMagnitude > RemoteRange * RemoteRange) return false;
        direction.Normalize();
        float along = Vector2.Dot(offset, direction);
        float sideways = Mathf.Abs(offset.x * direction.y - offset.y * direction.x);
        return along > 0f && along <= RemoteRange && sideways <= 0.9f && !IsBlocked(actor, device.InteractionPoint, device);
    }

    private void ClearRemoteHold(PcsPuzzleDevice device)
    {
        PcsDeviceState state = States[device.DeviceId];
        state.Active = 0;
        state.Actor = default;
        state.HoldOwner = default;
        state.Timer = default;
        state.Velocity = Vector2.zero;
        States.Set(device.DeviceId, state);
        LeverLower = false;
        SetLinks(device, false);
    }

    private void TickRemoteHolds()
    {
        for (int i = 0; i < Devices.Length; i++)
        {
            PcsPuzzleDevice device = Devices[i];
            if (device == null || !device.RequiresRemoteHold) continue;
#if UNITY_EDITOR
            if (EditorApplyRemoteHoldInput(device)) continue;
#endif
            PcsDeviceState state = States[i];
            if (state.Active == 0) continue;
            if (state.Timer.ExpiredOrNotRunning(Runner) || !Runner.TryFindObject(state.Actor, out NetworkObject actor) ||
                actor.StateAuthority != state.HoldOwner || !ValidRemoteHold(actor, device, state.Velocity))
                ClearRemoteHold(device);
        }
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_Action(NetworkId actorId, PcsPuzzleAction action, int targetId, Vector2 aim, int channel, int epoch, RpcInfo info = default)
    {
        if (!HasStateAuthority || !Initialized || !ResetTimer.ExpiredOrNotRunning(Runner)) return;
        if (!Runner.TryFindObject(actorId, out NetworkObject actor) || actor.StateAuthority != info.Source || !IsParticipant(actor)) return;
        PcsPlayerAbilities abilities = actor.GetComponent<PcsPlayerAbilities>();
        if (abilities == null) return;
        MyEnum.CharacterType role = abilities.CharacterType;
        if (epoch != GetResetEpoch(role)) return;
        if (action == PcsPuzzleAction.Reset) { ResetSection(role); return; }
        if (ActiveSection == 3) return;
        if (action == PcsPuzzleAction.Throw)
        {
            if (role == MyEnum.CharacterType.Bear && ThrowTimers[(int)role].ExpiredOrNotRunning(Runner))
                Throw(actor, aim, role);
            return;
        }
        PcsPuzzleDevice device = GetDevice(targetId);
        if (device == null || (device.Section >= 0 && device.Section != ActiveSection)) return;
        if (device.RequiredRole != MyEnum.CharacterType.None && device.RequiredRole != role) return;
        bool remote = action == PcsPuzzleAction.Remote;
        float range = remote ? RemoteRange : device.InteractionRange;
        PcsDeviceState state = States[targetId];
        bool droppingOwnedAlly = action == PcsPuzzleAction.Carry && device.Kind == PcsDeviceKind.Dummy &&
            state.Phase == 1 && state.Actor == actor.Id;
        if (!droppingOwnedAlly && Vector2.Distance(actor.transform.position, device.InteractionPoint) > range) return;
        if (remote && (role != MyEnum.CharacterType.Frog || !device.IsRemoteTarget || IsBlocked(actor, device.InteractionPoint, device))) return;
        switch (action)
        {
            case PcsPuzzleAction.Carry:
                if (device.Kind == PcsDeviceKind.Dummy) CarryDummy(actor, device, state, false);
                else if (device.Kind == PcsDeviceKind.Battery) CollectBattery(device);
                break;
            case PcsPuzzleAction.ActivateChannel:
                if (role == MyEnum.CharacterType.Mouse && device.Kind == PcsDeviceKind.ChannelConsole && ShaftStarted) ActivateChannel(channel);
                break;
            case PcsPuzzleAction.Refill:
                if (role == MyEnum.CharacterType.Mouse && device.Kind == PcsDeviceKind.ChannelConsole &&
                    Batteries > 0 && Energy < MaximumEnergy && RefillTimer.ExpiredOrNotRunning(Runner))
                {
                    Batteries--;
                    Energy = Mathf.Min(MaximumEnergy, Energy + device.BatteryValue);
                    RefillTimer = TickTimer.CreateFromSeconds(Runner, RefillCooldown);
                }
                break;
            case PcsPuzzleAction.Remote:
                if (device.Kind == PcsDeviceKind.Dummy) CarryDummy(actor, device, state, true);
                else if (device.Kind == PcsDeviceKind.Battery)
                {
                    if (state.Active == 0 || state.Phase == 4) return;
                    state.Actor = actor.Id;
                    state.Phase = 2;
                    States.Set(device.DeviceId, state);
                }
                else if (device.Kind == PcsDeviceKind.Enemy) StunEnemy(device);
                else if (device.Kind == PcsDeviceKind.Anchor) SetActive(device, true);
                else OperateDevice(actor, device, state, role, true);
                break;
            case PcsPuzzleAction.Interact:
            case PcsPuzzleAction.HackStep:
                OperateDevice(actor, device, state, role, false);
                break;
        }
    }

    private bool IsParticipant(NetworkObject actor)
    {
        return actor != null && actor.IsValid && (NetworkGameManager.Instance == null ||
            !NetworkGameManager.Instance.TeamLocked || NetworkGameManager.Instance.IsStartingPlayer(actor.StateAuthority));
    }

    private void OperateDevice(NetworkObject actor, PcsPuzzleDevice device, PcsDeviceState state, MyEnum.CharacterType role, bool remote)
    {
        if (device.Kind == PcsDeviceKind.HackConsole)
        {
            if (role != MyEnum.CharacterType.Mouse || state.Active != 0 || !state.Timer.ExpiredOrNotRunning(Runner)) return;
            if (device.Section == 2 && state.Counter + 1 >= device.RequiredInputs && RequireShaftBoarding &&
                (ShaftLeftBoardingArea == null || ShaftRabbitBoardingArea == null ||
                 (Occupants(ShaftLeftBoardingArea) & 28) != 28 || (Occupants(ShaftRabbitBoardingArea) & 2) != 2)) return;
            state.Counter++;
            state.Actor = actor.Id;
            state.Timer = TickTimer.CreateFromSeconds(Runner, HackPulseInterval);
            if (state.Counter >= device.RequiredInputs)
            {
                CompleteHack(device, ref state);
            }
            States.Set(device.DeviceId, state);
        }
        else if (device.Kind == PcsDeviceKind.Lever)
        {
            if (device.RequiresRemoteHold || role != MyEnum.CharacterType.Frog ||
                (device.LatchOnActivate && !remote) || (device.Section == 1 && !StageOneHacked) ||
                !state.Timer.ExpiredOrNotRunning(Runner)) return;
            state.Active = device.LatchOnActivate || state.Active == 0 ? 1 : 0;
            state.Timer = TickTimer.CreateFromSeconds(Runner, 0.35f);
            States.Set(device.DeviceId, state);
            if (device.Section == 1) LeverLower = state.Active != 0;
            SetLinks(device, state.Active != 0);
        }
        else if (device.Kind == PcsDeviceKind.RemoteButton)
        {
                if (!device.AcceptDummyOnly && remote) HitButton(device, -1);
        }
        else if (device.Kind == PcsDeviceKind.BarrierControl)
        {
            if (role != MyEnum.CharacterType.Rabbit) return;
            SetActive(device, true);
            SetLinks(device, true);
        }
        else if (device.Kind == PcsDeviceKind.Battery) CollectBattery(device);
    }

    private void ActivateChannel(int index)
    {
        if (index < 0 || index >= 4 || Energy < ChannelCost) return;
        PcsDeviceState channel = Channels[index];
        if (!channel.Timer.ExpiredOrNotRunning(Runner)) return;
        channel.Window++;
        channel.Active = 1;
        channel.Phase = 0;
        channel.Timer = TickTimer.CreateFromSeconds(Runner, ChannelDuration);
        Channels.Set(index, channel);
        Energy -= ChannelCost;
        ClearChannel(index);
    }

    private void HitButton(PcsPuzzleDevice device, int dummyId)
    {
        PcsDeviceState state = States[device.DeviceId];
        if (device.AcceptDummyOnly)
        {
            if (dummyId < 0 || state.Active != 0) return;
            for (int i = 0; i < Devices.Length; i++)
                if (Devices[i] != null && Devices[i].AcceptDummyOnly && States[i].Counter == dummyId + 1) return;
            state.Counter = dummyId + 1;
        }
        if (device.Section == 2)
        {
            PcsDeviceState channel = Channels[device.Channel];
            if (channel.Active == 0 || channel.Timer.ExpiredOrNotRunning(Runner)) return;
            if (state.Active != 0 && state.Window == channel.Window) return;
            state.Window = channel.Window;
            channel.Phase = 1;
            Channels.Set(device.Channel, channel);
        }
        state.Active = 1;
        States.Set(device.DeviceId, state);
        SetLinks(device, true);
        if (dummyId >= 0)
        {
            PcsDeviceState dummy = States[dummyId];
            dummy.Phase = 4;
            dummy.Actor = default;
            States.Set(dummyId, dummy);
        }
    }

    private void ClearChannel(int channel)
    {
        for (int i = 0; i < Devices.Length; i++)
            if (Devices[i] != null && Devices[i].Section == 2 && Devices[i].Channel == channel &&
                (Devices[i].Kind == PcsDeviceKind.RemoteButton || Devices[i].Kind == PcsDeviceKind.TimedPlatform))
                SetActive(Devices[i], false);
    }

    private void SetActive(PcsPuzzleDevice device, bool active)
    {
        if (device == null || device.DeviceId < 0 || device.DeviceId >= Devices.Length || Devices[device.DeviceId] != device) return;
        PcsDeviceState state = States[device.DeviceId];
        state.Active = active ? 1 : 0;
        States.Set(device.DeviceId, state);
    }

    private void CompleteHack(PcsPuzzleDevice device, ref PcsDeviceState state)
    {
        state.Active = 1;
        if (device.Section == 1) StageOneHacked = true;
        if (device.Section == 2) ShaftStarted = true;
        SetLinks(device, true);
    }

    private void SetLinks(PcsPuzzleDevice device, bool active)
    {
        for (int i = 0; i < device.Links.Length; i++) SetActive(device.Links[i], active);
    }

    private bool AllLinksActive(PcsPuzzleDevice device)
    {
        if (device.Links.Length == 0) return true;
        for (int i = 0; i < device.Links.Length; i++)
        {
            PcsPuzzleDevice link = device.Links[i];
            if (link == null || link.DeviceId < 0 || link.DeviceId >= Devices.Length ||
                Devices[link.DeviceId] != link || States[link.DeviceId].Active == 0) return false;
        }
        return true;
    }

    public override void FixedUpdateNetwork()
    {
        if (!Initialized) return;
#if UNITY_EDITOR
        EditorTickDeviceTests();
#endif
        if (HasStateAuthority)
        {
            if (!ResetTimer.ExpiredOrNotRunning(Runner))
            {
                ApplyPresentation(false);
                return;
            }
            RefreshPlayers();
            TickRemoteHolds();
            TickPressurePlates();
            for (int i = 0; i < 4; i++)
            {
                PcsDeviceState channel = Channels[i];
                if (channel.Active != 0 && channel.Timer.Expired(Runner))
                {
                    channel.Active = 0;
                    channel.Phase = 0;
                    Channels.Set(i, channel);
                    ClearChannel(i);
                }
            }
            int serial = resetSerial;
            for (int i = 0; i < Devices.Length; i++)
            {
                TickDevice(i);
                if (serial != resetSerial) break;
            }
            if (serial == resetSerial) TickProjectiles();
        }
        ApplyPresentation(false);
    }

    public override void Render()
    {
        // Shared proxies receive state without running this object's simulation callbacks.
        // Their local collision surfaces and artwork must follow the received device poses.
        if (CanSpawnPlayers && !HasStateAuthority) ApplyPresentation(false);
    }

    private void RefreshPlayers()
    {
        players.Clear();
        foreach (PlayerRef player in Runner.ActivePlayers)
            if (Runner.TryGetPlayerObject(player, out NetworkObject actor) && IsParticipant(actor)) players.Add(actor);
    }

    private void TickPressurePlates()
    {
        for (int i = 0; i < Devices.Length; i++)
        {
            PcsPuzzleDevice plate = Devices[i];
            if (plate == null || plate.Kind != PcsDeviceKind.PressurePlate) continue;
            bool pressed = plate.isActiveAndEnabled && (plate.Section < 0 || plate.Section == ActiveSection) && plate.PressureButton != null &&
                plate.PressureButton.EvaluatePressure(plate.AcceptDummyOnly, plate.RequiredRole);
#if UNITY_EDITOR
            if (EditorTryPressureInput(plate, out bool forced)) pressed = forced;
#endif
            SetActive(plate, pressed);
            plate.PressureButton?.SetNetworkPressed(pressed);
        }
        for (int i = 0; i < Devices.Length; i++)
        {
            PcsPuzzleDevice plate = Devices[i];
            if (plate == null || plate.Kind != PcsDeviceKind.PressurePlate) continue;
            foreach (PcsPuzzleDevice target in plate.Links)
            {
                if (target == null) continue;
                bool anyPressed = false;
                for (int j = 0; j < Devices.Length && !anyPressed; j++)
                {
                    PcsPuzzleDevice other = Devices[j];
                    if (other == null || other.Kind != PcsDeviceKind.PressurePlate || States[j].Active == 0) continue;
                    for (int k = 0; k < other.Links.Length; k++)
                        if (other.Links[k] == target) { anyPressed = true; break; }
                }
                SetActive(target, anyPressed);
            }
        }
    }

    private int Occupants(PcsPuzzleDevice device)
    {
        int mask = 0;
        Bounds area = device.Bounds;
        for (int i = 0; i < players.Count; i++)
        {
            PcsPlayerAbilities actor = players[i].GetComponent<PcsPlayerAbilities>();
            if (actor != null && Contains2D(area, players[i].transform.position)) mask |= 1 << (int)actor.CharacterType;
        }
        return mask;
    }

    private static bool Contains2D(Bounds bounds, Vector2 point) =>
        point.x >= bounds.min.x && point.x <= bounds.max.x && point.y >= bounds.min.y && point.y <= bounds.max.y;

    private void TickDevice(int id)
    {
        PcsPuzzleDevice device = Devices[id];
        if (device == null || !device.isActiveAndEnabled) return;
        PcsDeviceState state = States[id];
        int serial = resetSerial;
        if (device.RequireAllLinks) state.Active = AllLinksActive(device) ? 1 : 0;
        bool currentSection = device.Section < 0 || device.Section == ActiveSection;
        switch (device.Kind)
        {
            case PcsDeviceKind.HackConsole:
            case PcsDeviceKind.ChannelConsole:
                if (state.Active == 0 && state.Counter > 0 &&
                    (!Runner.TryFindObject(state.Actor, out NetworkObject hacker) ||
                    Vector2.Distance(hacker.transform.position, device.InteractionPoint) > device.InteractionRange))
                {
                    state.Counter = 0;
                    state.Actor = default;
                    state.Timer = default;
                }
                break;
            case PcsDeviceKind.PressurePlate:
                break;
            case PcsDeviceKind.Ladder:
                if (device.DeployableLadder)
                    state.Position = device.MoveAuthority(state.Position,
                        StopPosition(state.Active != 0 ? device.LowerStop : device.UpperStop, device.InitialPosition), Runner.DeltaTime);
                break;
            case PcsDeviceKind.SlidingWall:
                state.Position = device.MoveAuthority(state.Position,
                    state.Active != 0 && device.UpperStop != null ? (Vector2)device.UpperStop.position : device.InitialPosition, Runner.DeltaTime);
                break;
            case PcsDeviceKind.Elevator:
                Vector2 target = device.InitialPosition;
                if (device.LiftPolicy == PcsLiftPolicy.StageOneThreeStop)
                    target = StopPosition(!StageOneHacked ? device.UpperStop : LeverLower ? device.LowerStop : device.MiddleStop, target);
                else if (device.LiftPolicy == PcsLiftPolicy.MainContinuous)
                    target = ShaftStarted ? StopPosition(device.UpperStop, target) : StopPosition(device.LowerStop, target);
                else if (state.Active != 0) target = StopPosition(device.UpperStop, target);
#if UNITY_EDITOR
                bool shaftMovementTest = EditorTryShaftLiftTarget(device, out Vector2 testTarget);
                if (shaftMovementTest) target = testTarget;
#else
                const bool shaftMovementTest = false;
#endif
                // An isolated Editor movement trial must not reset the section or complete its puzzle.
                if (!shaftMovementTest && device.LiftPolicy == PcsLiftPolicy.MainContinuous && ShaftStarted && IsShaftBlocked(device, state.Position))
                {
                    ResetSection(MyEnum.CharacterType.Bear);
                    return;
                }
                state.Position = device.MoveAuthority(state.Position, target, Runner.DeltaTime);
                if (!shaftMovementTest && device.LiftPolicy == PcsLiftPolicy.MainContinuous && ShaftStarted && Vector2.Distance(state.Position, target) < 0.002f)
                    ShaftArrived = true;
                break;
            case PcsDeviceKind.Arrival:
                int arrivalMask = device.RequiredRolesMask != 0 ? device.RequiredRolesMask : 1 << (int)device.RequiredRole;
                if (currentSection && ShaftArrived && (Occupants(device) & arrivalMask) == arrivalMask) state.Active = 1;
                break;
            case PcsDeviceKind.TutorialExit:
                if (currentSection && state.Active == 0 && AllLinksActive(device) &&
                    (Occupants(device) & (1 << (int)device.RequiredRole)) != 0 && CountDummies(device) >= device.RequiredDummyCount)
                {
                    state.Active = 1;
                    CompletedTutorialMask |= 1 << (int)device.RequiredRole;
                    // Practice allies have reached safety. They must not follow into another section
                    // or trigger this room's fall recovery during the return descent.
                    for (int allyId = 0; allyId < Devices.Length; allyId++)
                    {
                        PcsPuzzleDevice ally = Devices[allyId];
                        if (ally == null || ally.Kind != PcsDeviceKind.Dummy || ally.Section != 0 || ally.RequiredRole != device.RequiredRole) continue;
                        PcsDeviceState rescued = States[allyId];
                        rescued.Actor = default;
                        rescued.Velocity = Vector2.zero;
                        rescued.Phase = 4;
                        rescued.Active = 0;
                        States.Set(allyId, rescued);
                    }
                }
                break;
            case PcsDeviceKind.Exit:
            case PcsDeviceKind.Checkpoint:
                int required = device.RequireAllRoles ? AllRoles : device.RequiredRolesMask;
                if (currentSection && required != 0 && (Occupants(device) & required) == required && AllLinksActive(device))
                {
                    if (device.Section == 0 && CompletedTutorialMask == AllRoles) { ActiveSection = 1; state.Active = 1; }
                    else if (device.Section == 1 && device.Kind == PcsDeviceKind.Exit && StageOneHacked) { StageOneComplete = true; state.Active = 1; }
                    else if (device.Section == 1 && device.Kind == PcsDeviceKind.Checkpoint && StageOneComplete) { ActiveSection = 2; state.Active = 1; }
                    else if (device.Section == 2 && ShaftArrived) { ActiveSection = 3; state.Active = 1; }
                }
                break;
            case PcsDeviceKind.KillZone:
                if (currentSection)
                {
                    int victims = Occupants(device);
                    for (int role = 1; role <= 4; role++)
                        if ((victims & (1 << role)) != 0) { ResetSection((MyEnum.CharacterType)role); return; }
                }
                break;
            case PcsDeviceKind.Dummy:
                TickDummy(device, ref state);
                break;
            case PcsDeviceKind.Enemy:
                if (currentSection) TickEnemy(device, ref state);
                break;
            case PcsDeviceKind.Battery:
                if (currentSection) TickBattery(device, ref state);
                break;
        }
        if (serial == resetSerial) States.Set(id, state);
    }

    private static Vector2 StopPosition(Transform stop, Vector2 fallback) => stop != null ? (Vector2)stop.position : fallback;

    private int CountDummies(PcsPuzzleDevice exit)
    {
        int count = 0;
        for (int i = 0; i < Devices.Length; i++)
            if (Devices[i] != null && Devices[i].Kind == PcsDeviceKind.Dummy && Devices[i].Section == exit.Section &&
                Devices[i].RequiredRole == exit.RequiredRole && States[i].Phase != 4 && States[i].Counter > 0 &&
                Contains2D(exit.Bounds, States[i].Position)) count++;
        return count;
    }

    private void CarryDummy(NetworkObject actor, PcsPuzzleDevice device, PcsDeviceState state, bool pull)
    {
        if (state.Phase == 4) return;
        if (state.Actor == actor.Id && state.Phase == 1)
        {
            state.Actor = default;
            state.Phase = 0;
            state.Velocity = Vector2.zero;
        }
        else
        {
            if (device.Section == 0 && device.RequiredRole == MyEnum.CharacterType.Frog && !pull && state.Counter == 0) return;
            if (state.Phase == 1 || state.Phase == 2) return;
            for (int i = 0; i < Devices.Length; i++)
                if (Devices[i] != null && Devices[i].Kind == PcsDeviceKind.Dummy && States[i].Actor == actor.Id &&
                    (States[i].Phase == 1 || States[i].Phase == 2)) return;
            state.Actor = actor.Id;
            state.Phase = pull ? 2 : 1;
            state.Velocity = Vector2.zero;
            state.Counter = 1;
        }
        States.Set(device.DeviceId, state);
    }

    private void TickDummy(PcsPuzzleDevice device, ref PcsDeviceState state)
    {
        if (state.Phase == 4) return;
        if (state.Phase == 1 || state.Phase == 2)
        {
            if (!Runner.TryFindObject(state.Actor, out NetworkObject carrier)) { state.Phase = 0; state.Actor = default; return; }
            Collider2D body = carrier.GetComponent<Collider2D>();
            Lifter lifter = carrier.GetComponent<Lifter>();
            float supportTop = body != null ? body.bounds.max.y : carrier.transform.position.y + 0.5f;
            if (lifter != null && lifter.Head != null) supportTop = Mathf.Max(supportTop, lifter.Head.Top);
            // The whole ally body must clear the carrier's Ground-layer head before a throw.
            Vector2 head = new Vector2(body != null ? body.bounds.center.x : carrier.transform.position.x,
                supportTop + device.BodySize.y * 0.5f + 0.03f);
            if (state.Phase == 1) state.Position = head;
            else
            {
                Vector2 next = Vector2.MoveTowards(state.Position, head, PullSpeed * Runner.DeltaTime);
                if (MoveItem(device, ref state, next)) { state.Phase = 0; state.Actor = default; }
                if (Vector2.Distance(state.Position, head) < 0.08f) state.Phase = 1;
            }
        }
        else
        {
            state.Velocity += Physics2D.gravity * Runner.DeltaTime;
            Vector2 next = state.Position + state.Velocity * Runner.DeltaTime;
            if (state.Phase == 3)
            {
                for (int i = 0; i < Devices.Length; i++)
                {
                    PcsPuzzleDevice receiver = Devices[i];
                    if (receiver != null && receiver.AcceptDummyOnly && Contains2D(Expanded(receiver.Bounds, 0.25f), next))
                    {
                        HitButton(receiver, device.DeviceId);
                        state = States[device.DeviceId];
                        if (state.Phase == 4) return;
                    }
                }
            }
            if (MoveItem(device, ref state, next))
            {
                state.Velocity = Vector2.zero;
                state.Phase = 0;
            }
        }
        device.ApplyPose(state.Position);
        if (state.Position.y < device.InitialPosition.y - 12f) ResetSection(device.RequiredRole);
    }

    private bool MoveItem(PcsPuzzleDevice device, ref PcsDeviceState state, Vector2 next)
    {
        Vector2 delta = next - state.Position;
        if (delta.sqrMagnitude < 0.0000001f) return false;
        // Reserve both shapes' contact margins before an enemy step reaches the surface.
        // Allies retain their exact shape so their physical pressure-plate contacts still form.
        float padding = device.Kind == PcsDeviceKind.Enemy ? Mathf.Max(0.02f, Physics2D.defaultContactOffset * 2f) : 0f;
        RaycastHit2D hit = Physics2D.BoxCast(state.Position, device.BodySize + Vector2.one * (padding * 2f),
            0f, delta.normalized, delta.magnitude, ObstructionMask);
        if (hit.collider != null && hit.collider != device.Solid)
        {
            state.Position = hit.centroid + hit.normal * 0.002f;
            return true;
        }
        state.Position = next;
        return false;
    }

    private static Bounds Expanded(Bounds bounds, float amount) { bounds.Expand(amount * 2f); return bounds; }

    private void Throw(NetworkObject actor, Vector2 aim, MyEnum.CharacterType role)
    {
        Vector2 origin = (Vector2)actor.transform.position + Vector2.up * 0.6f;
        Vector2 direction = aim - origin;
        if (!float.IsFinite(direction.x) || !float.IsFinite(direction.y) || direction.sqrMagnitude < 0.01f) return;
        direction.Normalize();
        ThrowTimers.Set((int)role, TickTimer.CreateFromSeconds(Runner, ThrowCooldown));
        for (int i = 0; i < Devices.Length; i++)
        {
            if (Devices[i] == null || Devices[i].Kind != PcsDeviceKind.Dummy) continue;
            PcsDeviceState dummy = States[i];
            if (dummy.Actor != actor.Id || dummy.Phase != 1) continue;
            dummy.Actor = default;
            dummy.Phase = 3;
            dummy.Velocity = ThrowVelocity(dummy.Position, aim);
            States.Set(i, dummy);
            return;
        }
        for (int i = 0; i < 24; i++)
        {
            if (Projectiles[i].Active != 0) continue;
            Projectiles.Set(i, new PcsDeviceState { Active = 1, Position = origin + direction * 0.45f,
                Velocity = ThrowVelocity(origin + direction * 0.45f, aim), Actor = actor.Id,
                Timer = TickTimer.CreateFromSeconds(Runner, ProjectileLifetime) });
            return;
        }
    }

    private Vector2 ThrowVelocity(Vector2 origin, Vector2 aim)
    {
        Vector2 offset = aim - origin;
        float gravity = -Physics2D.gravity.y;
        float horizontal = Mathf.Abs(offset.x);
        float speedSquared = ProjectileSpeed * ProjectileSpeed;
        float discriminant = speedSquared * speedSquared - gravity * (gravity * horizontal * horizontal + 2f * offset.y * speedSquared);
        if (gravity > 0.01f && horizontal > 0.05f && discriminant >= 0f)
        {
            float tangent = (speedSquared - Mathf.Sqrt(discriminant)) / (gravity * horizontal);
            float horizontalSpeed = ProjectileSpeed / Mathf.Sqrt(1f + tangent * tangent);
            return new Vector2(Mathf.Sign(offset.x) * horizontalSpeed, horizontalSpeed * tangent);
        }
        // Out-of-range aim still launches at finite speed; it never teleports to the cursor.
        return offset.sqrMagnitude > 0.001f ? offset.normalized * ProjectileSpeed : Vector2.up * ProjectileSpeed;
    }

    private void TickProjectiles()
    {
        for (int i = 0; i < 24; i++)
        {
            PcsDeviceState shot = Projectiles[i];
            if (shot.Active == 0) continue;
            if (shot.Timer.Expired(Runner)) { shot.Active = 0; Projectiles.Set(i, shot); continue; }
            shot.Velocity += Physics2D.gravity * Runner.DeltaTime;
            Vector2 next = shot.Position + shot.Velocity * Runner.DeltaTime;
            RaycastHit2D obstruction = Physics2D.Linecast(shot.Position, next, ObstructionMask);
            Vector2 testEnd = obstruction.collider != null ? obstruction.point : next;
            for (int j = 0; j < Devices.Length; j++)
            {
                PcsPuzzleDevice device = Devices[j];
                if (device == null || device.Section != ActiveSection ||
                    (device.Kind != PcsDeviceKind.RemoteButton && device.Kind != PcsDeviceKind.Enemy)) continue;
                if (!SegmentIntersects(device.Bounds, shot.Position, testEnd, 0.2f)) continue;
                if (device.Kind == PcsDeviceKind.RemoteButton && !device.AcceptDummyOnly) HitButton(device, -1);
                else if (device.Kind == PcsDeviceKind.Enemy) DamageEnemy(device);
                shot.Active = 0;
                break;
            }
            if (shot.Active != 0 && obstruction.collider != null) shot.Active = 0;
            shot.Position = next;
            Projectiles.Set(i, shot);
        }
    }

    private static bool SegmentIntersects(Bounds bounds, Vector2 start, Vector2 end, float radius)
    {
        bounds = Expanded(bounds, radius);
        if (Contains2D(bounds, start) || Contains2D(bounds, end)) return true;
        Vector3 direction = (Vector3)(end - start);
        return direction.sqrMagnitude > 0f && bounds.IntersectRay(new Ray(new Vector3(start.x, start.y, bounds.center.z), direction.normalized), out float distance) && distance <= direction.magnitude;
    }

    private void StunEnemy(PcsPuzzleDevice device)
    {
        PcsDeviceState state = States[device.DeviceId];
        if (state.Phase == 4) return;
        state.Timer = TickTimer.CreateFromSeconds(Runner, StunDuration);
        States.Set(device.DeviceId, state);
    }

    private void DamageEnemy(PcsPuzzleDevice device)
    {
        PcsDeviceState state = States[device.DeviceId];
        if (state.Phase == 4) return;
        state.Counter--;
        if (state.Counter <= 0)
        {
            state.Phase = 4;
            state.Active = 0;
            for (int i = 0; i < device.Links.Length; i++)
            {
                PcsPuzzleDevice drop = device.Links[i];
                if (drop == null || drop.Kind != PcsDeviceKind.Battery) continue;
                PcsDeviceState battery = States[drop.DeviceId];
                battery.Position = state.Position;
                battery.Active = 1;
                battery.Phase = 0;
                States.Set(drop.DeviceId, battery);
            }
        }
        States.Set(device.DeviceId, state);
    }

    private void TickEnemy(PcsPuzzleDevice device, ref PcsDeviceState state)
    {
        if (state.Phase == 4) return;
        if (!state.Timer.ExpiredOrNotRunning(Runner)) return;
        NetworkObject target = null;
        float closest = float.MaxValue;
        for (int i = 0; i < players.Count; i++)
        {
            PcsPlayerAbilities ability = players[i].GetComponent<PcsPlayerAbilities>();
            float distance = Vector2.Distance(state.Position, players[i].transform.position);
            if (ability != null && ability.CharacterType == MyEnum.CharacterType.Mouse && distance < 12f) distance *= 0.4f;
            if (distance < closest) { closest = distance; target = players[i]; }
        }
        if (target == null || closest > 14f) return;
        if (state.Timer.ExpiredOrNotRunning(Runner))
        {
            Vector2 next = Vector2.MoveTowards(state.Position, target.transform.position, device.Speed * Runner.DeltaTime);
            // Resolve each axis with the whole body so a lower target cannot pull the enemy into its floor.
            MoveItem(device, ref state, new Vector2(next.x, state.Position.y));
            MoveItem(device, ref state, new Vector2(state.Position.x, next.y));
        }
        device.ApplyPose(state.Position);
        for (int i = 0; i < players.Count; i++)
        {
            Collider2D body = players[i].GetComponent<Collider2D>();
            PcsPlayerAbilities ability = players[i].GetComponent<PcsPlayerAbilities>();
            if (body == null || ability == null || !Contains2D(Expanded(body.bounds, 0.2f), state.Position)) continue;
            int role = (int)ability.CharacterType;
            if (!DamageTimers[role].ExpiredOrNotRunning(Runner)) continue;
            Health.Set(role, Mathf.Max(0, Health[role] - 1));
            DamageTimers.Set(role, TickTimer.CreateFromSeconds(Runner, DamageInvulnerability));
            if (Health[role] == 0) { ResetSection(ability.CharacterType); return; }
        }
    }

    private void CollectBattery(PcsPuzzleDevice device)
    {
        PcsDeviceState state = States[device.DeviceId];
        if (state.Active == 0 || state.Phase == 4) return;
        state.Active = 0;
        state.Phase = 4;
        States.Set(device.DeviceId, state);
        Batteries += device.BatteryValue;
    }

    private void TickBattery(PcsPuzzleDevice device, ref PcsDeviceState state)
    {
        if (state.Active == 0 || state.Phase == 4) return;
        if (state.Phase == 2)
        {
            if (!Runner.TryFindObject(state.Actor, out NetworkObject actor)) { state.Phase = 0; state.Actor = default; return; }
            Vector2 next = Vector2.MoveTowards(state.Position, actor.transform.position, PullSpeed * Runner.DeltaTime);
            if (MoveItem(device, ref state, next)) { state.Phase = 0; state.Actor = default; }
            if (Vector2.Distance(state.Position, actor.transform.position) < 0.4f)
            {
                state.Active = 0;
                state.Phase = 4;
                Batteries += device.BatteryValue;
            }
        }
        else
        {
            state.Velocity += Physics2D.gravity * Runner.DeltaTime;
            Vector2 next = state.Position + state.Velocity * Runner.DeltaTime;
            if (MoveItem(device, ref state, next))
            {
                state.Velocity = Vector2.zero;
            }
        }
        device.ApplyPose(state.Position);
    }

    private bool IsShaftBlocked(PcsPuzzleDevice platform, Vector2 position)
    {
        if (platform.Solid == null) return false;
        Bounds surface = platform.Solid.bounds;
        for (int i = 0; i < Devices.Length; i++)
        {
            PcsPuzzleDevice obstacle = Devices[i];
            if (obstacle == null || !obstacle.BlocksShaft || obstacle.Solid == null || !obstacle.Solid.enabled) continue;
            Bounds barrier = obstacle.Solid.bounds;
            if (surface.max.x <= barrier.min.x || surface.min.x >= barrier.max.x) continue;
            // Fail before a closed barrier crushes riders; normal travel never pauses here.
            if (surface.max.y + 1.7f >= barrier.min.y && surface.min.y < barrier.max.y) return true;
        }
        return false;
    }

    private bool IsBlocked(NetworkObject actor, Vector2 end, PcsPuzzleDevice target)
    {
        Collider2D body = actor.GetComponent<Collider2D>();
        Vector2 start = body != null ? (Vector2)body.bounds.center : (Vector2)actor.transform.position;
        Vector2 delta = end - start;
        ContactFilter2D filter = new ContactFilter2D();
        filter.SetLayerMask(ObstructionMask);
        filter.useTriggers = false;
        obstructionHits.Clear();
        Physics2D.Raycast(start, delta.normalized, filter, obstructionHits, delta.magnitude);
        foreach (RaycastHit2D hit in obstructionHits)
        {
            // Player heads use Ground for stacking, but cannot obstruct their own ability.
            if (hit.collider == null || hit.collider.transform.IsChildOf(actor.transform) ||
                hit.collider == target.Solid || hit.collider == target.Trigger) continue;
            return true;
        }
        return false;
    }

    private void ResetDevice(int id)
    {
        PcsPuzzleDevice device = Devices[id];
        if (device == null || !device.isActiveAndEnabled) return;
        Vector2 position = (device.Kind == PcsDeviceKind.Elevator && device.LiftPolicy == PcsLiftPolicy.StageOneThreeStop) ||
            (device.Kind == PcsDeviceKind.Ladder && device.DeployableLadder) ?
            StopPosition(device.UpperStop, device.InitialPosition) : device.InitialPosition;
        PcsDeviceState state = new PcsDeviceState { Position = position, Active = device.InitiallyActive ? 1 : 0, Counter = device.Kind == PcsDeviceKind.Enemy ? device.Health : 0 };
        States.Set(id, state);
        if (Moves(device)) device.ApplyPose(position);
    }

    private void ResetSection(MyEnum.CharacterType role)
    {
        if (!HasStateAuthority || !ResetTimer.ExpiredOrNotRunning(Runner) || lastResetFrame == Runner.Tick.Raw) return;
#if UNITY_EDITOR
        EditorReleaseForcedInputs();
#endif
        lastResetFrame = Runner.Tick.Raw;
        resetSerial++;
        ResetTimer = TickTimer.CreateFromSeconds(Runner, ResetDelay);
        if (ActiveSection == 0)
        {
            if (role == MyEnum.CharacterType.None) return;
            RoomEpochs.Set((int)role, RoomEpochs[(int)role] + 1);
            CompletedTutorialMask &= ~(1 << (int)role);
            Health.Set((int)role, PlayerHealth);
            for (int i = 0; i < Devices.Length; i++)
                if (Devices[i] != null && Devices[i].Section == 0 && Devices[i].RequiredRole == role) ResetDevice(i);
        }
        else
        {
            ResetEpoch++;
            if (ActiveSection == 1) { StageOneHacked = false; StageOneComplete = false; LeverLower = false; }
            else { ActiveSection = 2; ShaftStarted = false; ShaftArrived = false; Energy = InitialEnergy; Batteries = 0; RefillTimer = default; }
            for (int i = 0; i < 4; i++) Channels.Set(i, default);
            for (int i = 1; i <= 4; i++) { Health.Set(i, PlayerHealth); DamageTimers.Set(i, default); ThrowTimers.Set(i, default); }
            for (int i = 0; i < Devices.Length; i++)
                if (Devices[i] != null && Devices[i].Section == ActiveSection) ResetDevice(i);
        }
        for (int i = 0; i < 24; i++) Projectiles.Set(i, default);
    }

    private void ApplyPresentation(bool forcePose)
    {
        bool reset = forcePose || lastPresentedEpoch != ResetEpoch;
        for (int i = 0; i < Devices.Length; i++)
        {
            PcsPuzzleDevice device = Devices[i];
            if (device == null || !device.isActiveAndEnabled) continue;
            PcsDeviceState state = States[i];
            if (Moves(device) && (!HasStateAuthority || reset)) device.ApplyPose(state.Position);
            bool hidden = (device.Kind == PcsDeviceKind.Enemy || device.Kind == PcsDeviceKind.Dummy) && state.Phase == 4 ||
                device.Kind == PcsDeviceKind.Battery && (state.Phase == 4 || state.Active == 0);
            device.Present(state.Active != 0, hidden);
            if (device.EntryOpenBeforeShaftStart && device.Solid != null)
                device.Solid.enabled = ShaftStarted && (!device.DisableColliderWhenOpen || state.Active == 0 ||
                    device.UpperStop == null || Vector2.Distance(device.transform.position, device.UpperStop.position) > 0.04f);
            if (device.Kind == PcsDeviceKind.Dummy && device.Solid != null && (state.Phase == 1 || state.Phase == 2)) device.Solid.enabled = false;
        }
        lastPresentedEpoch = ResetEpoch;
        for (int i = 0; i < 24; i++)
        {
            PcsDeviceState state = Projectiles[i];
            if (state.Active != 0 && projectileViews[i] == null)
            {
                GameObject view = new GameObject("Puzzle stone " + i);
                projectileViews[i] = view.AddComponent<PcsProjectile>();
                projectileViews[i].Initialize(ProjectileSprite);
            }
            if (projectileViews[i] != null) projectileViews[i].Present(state.Active != 0, state.Position);
        }
    }

    private static bool Moves(PcsPuzzleDevice device) => device.Kind == PcsDeviceKind.Elevator || device.Kind == PcsDeviceKind.SlidingWall ||
        device.Kind == PcsDeviceKind.Dummy || device.Kind == PcsDeviceKind.Enemy || device.Kind == PcsDeviceKind.Battery ||
        (device.Kind == PcsDeviceKind.Ladder && device.DeployableLadder);

    public static string RoleDisplayName(MyEnum.CharacterType role)
    {
        switch (role)
        {
            case MyEnum.CharacterType.Rabbit: return "토끼";
            case MyEnum.CharacterType.Bear: return "곰";
            case MyEnum.CharacterType.Mouse: return "쥐";
            case MyEnum.CharacterType.Frog: return "개구리";
            default: return "관전자";
        }
    }

    private void OnGUI()
    {
        if (!CanSpawnPlayers || !Runner.TryGetPlayerObject(Runner.LocalPlayer, out NetworkObject local)) return;
        PcsPlayerAbilities ability = local.GetComponent<PcsPlayerAbilities>();
        if (ability == null) return;
        if (hudLabel == null)
        {
            hudLabel = new GUIStyle(GUI.skin.label) { fontSize = 17, wordWrap = true, richText = false };
            hudBox = new GUIStyle(GUI.skin.box) { fontSize = 17, wordWrap = true, richText = false,
                alignment = TextAnchor.MiddleLeft, padding = new RectOffset(12, 12, 8, 8) };
        }
        hudLabel.font = HudFont;
        hudBox.font = HudFont;
        float width = Mathf.Max(120f, Mathf.Min(Screen.width - 24f, 780f));
        float contentWidth = width - 24f;
        bool waitingForNewTeam = !PcsPlayerAbilities.CanParticipate(local);
        bool showChannels = !waitingForNewTeam && ActiveSection == 2 && ability.CharacterType == MyEnum.CharacterType.Mouse;
        string heading = (ActiveSection == 0 ? "튜토리얼" : ActiveSection == 1 ? "1-1 협동 퍼즐" :
            ActiveSection == 2 ? "1-2 상승 통로" : "완료") + "  |  " + RoleDisplayName(ability.CharacterType) +
            "  |  체력 " + GetHealth(ability.CharacterType);
        string controls = waitingForNewTeam ? "대기 중: 이 팀은 이미 출발했습니다." :
            ActiveSection == 3 ? "네 명 모두 출구에 도착했습니다." :
            "E: 조작·해킹  F: 능력  G: 운반  Backspace: 재시작";
        string progress = waitingForNewTeam ? "플레이하려면 새 방에서 네 역할이 함께 시작하세요." :
            ActiveSection == 3 ? "Backspace: 1-2 재시작 · 새 방: 튜토리얼부터 시작" :
            ActiveSection == 2 ? "전력 " + Energy + "/" + MaximumEnergy + "  배터리 " + Batteries +
                "  |  1~4: 색 채널  R: 충전" :
            ActiveSection == 0 && (CompletedTutorialMask & (1 << (int)ability.CharacterType)) != 0 ?
                "개인 연습 완료 · 모임 장소에서 동료를 기다려 주세요." : "다음 모임 장소에 동료들과 함께 모여 주세요.";
        float headingHeight = hudLabel.CalcHeight(new GUIContent(heading), contentWidth);
        float controlHeight = hudLabel.CalcHeight(new GUIContent(controls), contentWidth);
        float progressHeight = hudLabel.CalcHeight(new GUIContent(progress), contentWidth);
        int channelColumns = width < 580f ? 2 : 4;
        float channelHeight = showChannels ? (4 / channelColumns) * 25f : 0f;
        float panelHeight = headingHeight + controlHeight + progressHeight + channelHeight + 20f;
        GUI.Box(new Rect(12f, 12f, width, panelHeight), GUIContent.none, hudBox);
        float y = 20f;
        GUI.Label(new Rect(24f, y, contentWidth, headingHeight), heading, hudLabel);
        y += headingHeight;
        GUI.Label(new Rect(24f, y, contentWidth, controlHeight), controls, hudLabel);
        y += controlHeight;
        GUI.Label(new Rect(24f, y, contentWidth, progressHeight), progress, hudLabel);
        y += progressHeight;
        if (showChannels)
        {
            float cellWidth = contentWidth / channelColumns;
            for (int i = 0; i < 4; i++)
                GUI.Label(new Rect(24f + i % channelColumns * cellWidth, y + i / channelColumns * 25f, cellWidth, 25f),
                    (i + 1) + " " + ChannelNames[i] + " " + ChannelRemaining(i).ToString("0.0") + "초", hudLabel);
        }
        if (!string.IsNullOrEmpty(ability.Feedback))
        {
            string feedback = "안내: " + ability.Feedback;
            float height = hudBox.CalcHeight(new GUIContent(feedback), width);
            GUI.Box(new Rect(12f, 18f + panelHeight, width, height), feedback, hudBox);
        }
        if (waitingForNewTeam || ActiveSection == 3) return;
        PcsPuzzleDevice nearest = null;
        float distance = 3f;
        for (int i = 0; i < Devices.Length; i++)
        {
            PcsPuzzleDevice device = Devices[i];
            if (device == null || !device.isActiveAndEnabled || !device.Available ||
                (device.Section >= 0 && device.Section != ActiveSection) ||
                (device.RequiredRole != MyEnum.CharacterType.None && device.RequiredRole != ability.CharacterType) ||
                string.IsNullOrEmpty(device.Instruction)) continue;
            float value = Vector2.Distance(local.transform.position, device.InteractionPoint);
            if (value < distance) { distance = value; nearest = device; }
        }
        if (nearest == null) return;
        string instruction = nearest.Instruction;
        if (nearest.Kind == PcsDeviceKind.HackConsole)
        {
            PcsDeviceState console = States[nearest.DeviceId];
            instruction += "\n" + (console.Active != 0 ? "해킹 완료" :
                "E 해킹 " + console.Counter + "/" + nearest.RequiredInputs + " · 멀어지면 진행이 초기화됩니다.");
            if (nearest.Section == 2 && !ShaftStarted)
                instruction += "\n마지막 E 입력 전: 토끼는 우측, 곰·쥐·개구리는 좌측 승강기에 탑승하세요.";
        }
        if (nearest.IsLadder && nearest.DeployableLadder)
            instruction = !nearest.Active ? "두 번째 콘솔을 해킹하면 사다리가 내려옵니다." :
                nearest.CanClimb ? "사다리가 전개되었습니다. W/S로 오르내리세요." : "사다리가 내려오는 중입니다. 전개가 끝날 때까지 기다리세요.";
        float instructionHeight = hudBox.CalcHeight(new GUIContent(instruction), width);
        GUI.Box(new Rect(12f, Screen.height - instructionHeight - 12f, width, instructionHeight), instruction, hudBox);
    }
}

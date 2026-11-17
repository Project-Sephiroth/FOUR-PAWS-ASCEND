using Fusion;
using UnityEngine;

/// <summary>
/// TODO : 퍼즐 진행도를 추적하는 스크립트 같은데 나중에 다르게 매니징 해볼 것
/// </summary>
public class BranchPuzzle : NetworkBehaviour
{
    [System.Serializable]
    private struct FloorButtonPair
    {
        public MovingFloor floor;
        public FloorStepButton positive;
        public FloorStepButton negative;
    }

    [SerializeField] private FloorButtonPair[] floorButtons = System.Array.Empty<FloorButtonPair>();

    private StagePuzzleDirector director;
    private byte lastRenderedButtons;
    internal bool UsesContactDrones => UsesPressurePads;
    private bool UsesPressurePads => director != null && director.UsesPressurePads;

    [Networked] private NetworkBool InnerHackDone { get; set; }
    [Networked] private NetworkBool ExitHackDone { get; set; }
    [Networked] private Generate.State GeneratorState { get; set; }
    [Networked] private int HackStagesCompleted { get; set; }
    [Networked] private TickTimer CrusherActive { get; set; }
    [Networked] private TickTimer CrusherCooldown { get; set; }
    [Networked] private NetworkBool HodPadsActivated { get; set; }
    private const int BarrierPressesRequired = 8;
    private const int BarrierMaxUses = 2;
    [Networked] private byte BarrierPresses { get; set; }
    [Networked] private byte BarrierUses { get; set; }
    [Networked] private byte LeverPressSequence { get; set; }
    [Networked] private int ActiveHodTerminal { get; set; }
    [Networked] private int HodWaveSequence { get; set; }
    [Networked] private NetworkBool NetzachDefenseStarted { get; set; }
    [Networked] private byte PressedButtons { get; set; }

    internal int HodDroneWave => ActiveHodTerminal == 0 ? 0 : HodWaveSequence;
    public bool FirstTerminalDone => InnerHackDone;
    internal bool HackSpawnsEnabled => UsesPressurePads ? HodDroneWave != 0 : NetzachDefenseStarted && !PuzzleReady;

    public bool FinalTerminalDone => ExitHackDone;
    public float GeneratorProgress => GeneratorState.Progress;
    internal float HackProgress => ExitHackDone ? 1f :
        (float)HackStagesCompleted / Mathf.Max(1, director.NetzachHackStageCount);
    private bool GeneratorCharged => GeneratorState.IsCharged;
    private bool PuzzleReady => ExitHackDone;

    private bool IsBarrierRaised => BarrierPresses == BarrierPressesRequired;
    private bool CanRaiseBarrier => BarrierUses < BarrierMaxUses && !IsBarrierRaised;

    internal bool TryUseDrumBarrier()
    {
        if (!HasStateAuthority || !IsBarrierRaised)
            return false;

        BarrierUses++;
        BarrierPresses = 0;
        UpdateDrumBarrier();
        return true;
    }

    private void UpdateDrumBarrier() =>
        director.ApplyDrumBarrier(IsBarrierRaised, (float)BarrierPresses / BarrierPressesRequired, CanRaiseBarrier);

    private bool transitionSent;
    private byte lastRenderedLeverPress;
    private readonly System.Collections.Generic.HashSet<NetworkObject> crusherVictims = new();

    public override void Spawned()
    {
        director = GetComponent<StagePuzzleDirector>();
        lastRenderedLeverPress = LeverPressSequence;
        UpdateDrumBarrier();
        ApplyButtonVisuals(playSound: false);
        ApplyStage();
    }

    public bool CanHack(int terminal)
    {
        if (director == null)
            return false;
        if (UsesPressurePads)
            return director.CanHack(terminal, InnerHackDone, ExitHackDone, HodPadsActivated);
        return terminal == 2 && GeneratorProgress > 0f && !ExitHackDone;
    }

    internal bool ShouldCloseHack(int terminal) =>
        terminal == 2 && Object != null && Object.IsValid && GeneratorProgress <= 0f;

    internal void SetHackActive(int terminal, bool active)
    {
        if (Object == null || !Object.IsValid)
            return;

        if (UsesPressurePads)
            RPC_SetHodHackActive(terminal, active);
        else if (terminal == 2)
            RPC_ReportHackStage(0);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_SetHodHackActive(int terminal, bool active, RpcInfo info = default)
    {
        var manager = NetworkGameManager.Instance;
        PlayerRef source = Runner.IsSinglePlayer ? Runner.LocalPlayer : info.Source;
        if (!UsesPressurePads || manager == null ||
            manager.GetGameplayCharacter(source) != MyEnum.CharacterType.Mouse)
            return;

        if (!active)
        {
            if (ActiveHodTerminal == terminal + 1)
                ActiveHodTerminal = 0;
            return;
        }

        if (ActiveHodTerminal == 0 && CanHack(terminal))
        {
            HodWaveSequence++;
            ActiveHodTerminal = terminal + 1;
        }
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    internal void RPC_ReportHackStage(int completed, RpcInfo info = default)
    {
        var manager = NetworkGameManager.Instance;
        PlayerRef source = Runner.IsSinglePlayer ? Runner.LocalPlayer : info.Source;
        if (director == null || UsesPressurePads || ExitHackDone || manager == null ||
            manager.GetGameplayCharacter(source) != MyEnum.CharacterType.Mouse)
            return;

        if (completed == 0 || (GeneratorProgress > 0f && completed == HackStagesCompleted + 1 &&
                               completed < director.NetzachHackStageCount))
            HackStagesCompleted = completed;
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_HackCompleted(int terminal, RpcInfo info = default)
    {
        if (NetworkGameManager.Instance == null ||
            NetworkGameManager.Instance.GetGameplayCharacter(Runner.IsSinglePlayer ? Runner.LocalPlayer : info.Source) != MyEnum.CharacterType.Mouse ||
            !CanHack(terminal) || (UsesPressurePads && ActiveHodTerminal != terminal + 1))
            return;

        CompleteTerminal(terminal);
    }

    private void CompleteTerminal(int terminal)
    {
        ActiveHodTerminal = 0;
        if (UsesPressurePads && terminal == 0)
            InnerHackDone = true;
        else
            ExitHackDone = true;
    }

    internal void DebugCompleteTerminal(int terminal)
    {
        if (!NetworkGameManager.CanUseStageDebug || Object == null || !Object.IsValid || !HasStateAuthority)
            return;
        CompleteTerminal(terminal);
        if (!UsesPressurePads && director?.Generator != null)
            GeneratorState = director.Generator.Complete(GeneratorState);
        ApplyStage();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_Operate(int control, bool viaTongue, RpcInfo info = default)
    {
        var manager = NetworkGameManager.Instance;
        if (director == null || UsesPressurePads || PuzzleReady || manager == null || !manager.TryGetGameplayPlayer(Runner.IsSinglePlayer ? Runner.LocalPlayer : info.Source, out var player))
            return;

        var type = manager.GetGameplayCharacter(Runner.IsSinglePlayer ? Runner.LocalPlayer : info.Source);
        Vector2 controlPosition = director.GetControlPosition(control);
        if (viaTongue && (control != 1 || type != MyEnum.CharacterType.Frog))
            return;
        if (!Near(player, controlPosition, viaTongue ? 7f : 2f))
            return;

        if (control == 0 && type == MyEnum.CharacterType.Rabbit && director.Generator != null)
        {
            GeneratorState = director.Generator.Charge(GeneratorState, Runner);
        }
        else if (control == BranchControl.BarrierSwitch && CanRaiseBarrier)
        {
            BarrierPresses++;
            LeverPressSequence++;
            UpdateDrumBarrier();
        }
        else if (control == 1 && director.Crusher != null && director.Crusher.Duration > 0f &&
                 CrusherCooldown.ExpiredOrNotRunning(Runner))
        {
            float duration = director.Crusher.Duration;
            LeverPressSequence++;
            crusherVictims.Clear();
            CrusherActive = TickTimer.CreateFromSeconds(Runner, duration);
            CrusherCooldown = TickTimer.CreateFromSeconds(Runner, duration + 2f);
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority)
            return;

        if (director?.Generator != null && GeneratorState.Progress > 0f && !GeneratorCharged)
            GeneratorState = director.Generator.Tick(GeneratorState, Runner);

        if (UsesPressurePads)
            ReadFloorButtons();

        if (!UsesPressurePads && GeneratorState.Started && !NetzachDefenseStarted && !ExitHackDone &&
            FindClosestPlayer(Runner, transform.position, hackingOnly: true) != null)
            NetzachDefenseStarted = true;

        ApplyStage();
        HitPlayersWithCrusher();
        if (director == null || transitionSent || !PuzzleReady)
            return;

        transitionSent = UsesPressurePads ? director.TryExitByElevator(Runner) : director.TryCompleteStage();
    }

    private void ReadFloorButtons()
    {
        byte mask = 0;
        foreach (var player in NetworkGameManager.GetPlayerObjects(Runner))
        {
            if (player == null || !player.IsValid || !player.TryGetComponent(out PlayerHealth health) || health.Health <= 0)
                continue;

            int playerMask = 0;
            for (int i = 0; i < floorButtons.Length; i++)
            {
                var pair = floorButtons[i];
                if (pair.positive.IsPressedBy(player))
                    playerMask |= 1 << (i * 2);
                if (pair.negative.IsPressedBy(player))
                    playerMask |= 2 << (i * 2);
            }

            mask |= (byte)playerMask;
        }

        byte previous = PressedButtons;
        PressedButtons = mask;
        if ((mask & 0b0011) != 0 && (mask & 0b1100) != 0)
            HodPadsActivated = true;

        for (int i = 0; i < floorButtons.Length; i++)
        {
            int shift = i * 2;
            int direction = (mask >> shift) & 3;
            if (direction != ((previous >> shift) & 3))
                floorButtons[i].floor.SetPressureDirection((direction & 1) - (direction >> 1));
        }
    }

    private void ApplyButtonVisuals(bool playSound)
    {
        byte pressed = PressedButtons;
        int newlyPressed = pressed & ~lastRenderedButtons;
        for (int i = 0; i < floorButtons.Length; i++)
        {
            var pair = floorButtons[i];
            int shift = i * 2;
            pair.positive.ShowPressed((pressed & (1 << shift)) != 0);
            pair.negative.ShowPressed((pressed & (2 << shift)) != 0);
        }

        if (playSound && newlyPressed != 0)
            SoundManager.Play(SoundEffect.FloorButton);
        lastRenderedButtons = pressed;
    }

    public override void Render()
    {
        UpdateDrumBarrier();
        if (lastRenderedButtons != PressedButtons)
            ApplyButtonVisuals(playSound: true);
        ApplyStage();
        if (LeverPressSequence != lastRenderedLeverPress)
        {
            lastRenderedLeverPress = LeverPressSequence;
            SoundManager.Play(SoundEffect.Lever);
        }
    }

    private void ApplyStage()
    {
        if (director == null)
            return;
        if (UsesPressurePads)
            director.ApplyGates(InnerHackDone, ExitHackDone);

        var crusher = director.Crusher;
        if (crusher != null)
        {
            float remaining = CrusherActive.RemainingTime(Runner) ?? 0f;
            crusher.SamplePlate(remaining > 0f ? crusher.Duration - remaining : 0f);
        }
    }

    public bool EnemyEnabled()
    {
        if (PuzzleReady)
            return false;

        if (UsesPressurePads)
            return HodDroneWave != 0;

        return NetzachDefenseStarted;
    }

    private void HitPlayersWithCrusher()
    {
        var crusher = director?.Crusher;
        if (crusher == null || CrusherActive.ExpiredOrNotRunning(Runner))
            return;

        foreach (var player in NetworkGameManager.GetPlayerObjects(Runner))
            if (player != null && player.IsValid && player.TryGetComponent(out PlayerHealth health) &&
                health.Health > 0 && player.TryGetComponent(out Collider2D body) &&
                crusher.Hits(body.bounds) && crusherVictims.Add(player))
                health.RPC_Squash(this);
    }

    public bool CrusherHits(Vector3 position)
    {
        var crusher = director != null ? director.Crusher : null;
        if (crusher == null || !CrusherActive.IsRunning || CrusherActive.Expired(Runner))
            return false;

        return crusher.Hits(position);
    }

    internal static PlayerHealth FindClosestPlayer(NetworkRunner runner, Vector2 origin, bool hackingOnly)
    {
        PlayerHealth closest = null;
        float bestDistance = float.PositiveInfinity;
        foreach (var player in NetworkGameManager.GetPlayerObjects(runner))
        {
            if (player == null || !player.IsValid || !player.TryGetComponent(out PlayerHealth health) ||
                health.Health <= 0 || (hackingOnly && !health.IsHacking))
                continue;

            float distance = ((Vector2)player.transform.position - origin).sqrMagnitude;
            if (distance < bestDistance)
            {
                closest = health;
                bestDistance = distance;
            }
        }
        return closest;
    }

    private static bool Near(NetworkObject player, Vector2 position, float distance)
    {
        return player != null && Vector2.Distance(player.transform.position, position) <= distance;
    }
}

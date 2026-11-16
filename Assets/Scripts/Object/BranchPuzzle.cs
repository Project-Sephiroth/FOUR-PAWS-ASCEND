using Fusion;
using UnityEngine;

/// <summary>
/// TODO : 퍼즐 진행도를 추적하는 스크립트 같은데 나중에 다르게 매니징 해볼 것
/// </summary>
public class BranchPuzzle : NetworkBehaviour
{
    private StagePuzzleDirector director;
    private bool UsesPressurePads => director != null && director.UsesPressurePads;

    [Networked] private NetworkBool InnerHackDone { get; set; }
    [Networked] private NetworkBool ExitHackDone { get; set; }
    // 퍼센트의 0~1 범위에서 초당 얼마나 빠지는지 정합니다. 0.055면 1초에 5.5%p입니다.
    [SerializeField, Min(0f)] private float generatorDecayPerSecond = 0.055f;
    private const float GeneratorIdleSeconds = 0.5f;
    [Networked] private TickTimer GeneratorIdleWait { get; set; }
    [Networked] private float Power { get; set; }
    [Networked] private NetworkBool DefenseStarted { get; set; }
    [Networked] private TickTimer CrusherActive { get; set; }
    public bool FirstTerminalDone => InnerHackDone;
    public bool FinalTerminalDone => ExitHackDone;
    public float GeneratorProgress => Power;
    private bool GeneratorCharged => Power >= 1f;
    private bool PuzzleReady => UsesPressurePads ? ExitHackDone : GeneratorCharged;
    private Vector3 crusherStart;
    private bool transitionSent;

    public override void Spawned()
    {
        director = GetComponent<StagePuzzleDirector>();
        if (director != null && director.Crusher != null)
            crusherStart = director.Crusher.position;
        ApplyStage();
    }

    public bool CanHack(int terminal)
    {
        if (director == null)
            return false;
        if (UsesPressurePads)
            return director.CanHack(terminal, InnerHackDone, ExitHackDone, Runner);
        return terminal == 2 && DefenseStarted && Power >= director.RequiredPower && !GeneratorCharged && !ExitHackDone;
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_HackCompleted(int terminal, RpcInfo info = default)
    {
        if (NetworkGameManager.Instance == null ||
            NetworkGameManager.Instance.GetGameplayCharacter(Runner.IsSinglePlayer ? Runner.LocalPlayer : info.Source) != MyEnum.CharacterType.Mouse ||
            director == null || (UsesPressurePads ? !CanHack(terminal) : terminal != 2 || !DefenseStarted || GeneratorCharged || ExitHackDone))
            return;

        if (UsesPressurePads && terminal == 0)
            InnerHackDone = true;
        else
            ExitHackDone = true;
    }

    internal void DebugCompleteTerminal(int terminal)
    {
        if (!NetworkGameManager.CanUseStageDebug || Object == null || !Object.IsValid || !HasStateAuthority)
            return;
        if (UsesPressurePads && terminal == 0)
            InnerHackDone = true;
        else
        {
            ExitHackDone = true;
            if (!UsesPressurePads)
                Power = 1f;
        }
        ApplyStage();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_Operate(int control, RpcInfo info = default)
    {
        var manager = NetworkGameManager.Instance;
        if (director == null || UsesPressurePads || PuzzleReady || manager == null || !manager.TryGetGameplayPlayer(Runner.IsSinglePlayer ? Runner.LocalPlayer : info.Source, out var player))
            return;

        var type = manager.GetGameplayCharacter(Runner.IsSinglePlayer ? Runner.LocalPlayer : info.Source);
        Vector2 controlPosition = director.GetControlPosition(control);
        if (!Near(player, controlPosition, 2f))
            return;

        if (control == 0 && type == MyEnum.CharacterType.Rabbit)
        {
            DefenseStarted = true;
            Power = Mathf.Min(1f, Power + 0.22f);
            // 누를 때마다 기다리는 시간을 다시 셉니다. 연타를 멈추면 그때부터 서서히 내려갑니다.
            GeneratorIdleWait = TickTimer.CreateFromSeconds(Runner, GeneratorIdleSeconds);
        }
        else if (control == 1 && CrusherActive.ExpiredOrNotRunning(Runner))
        {
            float duration = director.CrusherMotion != null ? director.CrusherMotion.length : 1f;
            CrusherActive = TickTimer.CreateFromSeconds(Runner, duration);
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority)
            return;

        if (!UsesPressurePads && DefenseStarted && !GeneratorCharged && GeneratorIdleWait.ExpiredOrNotRunning(Runner))
            Power = Mathf.MoveTowards(Power, 0f, Runner.DeltaTime * generatorDecayPerSecond);

        ApplyStage();
        if (director == null || transitionSent || !PuzzleReady)
            return;

        transitionSent = director.TryExitByElevator(Runner);
    }

    public override void Render()
    {
        ApplyStage();
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
            bool pressed = CrusherActive.IsRunning && !CrusherActive.Expired(Runner);
            var motion = director.CrusherMotion;
            if (motion != null)
                motion.SampleAnimation(crusher.gameObject,
                    pressed ? motion.length - (CrusherActive.RemainingTime(Runner) ?? 0f) : 0f);
            else
                crusher.position = crusherStart + (pressed ? Vector3.down * director.CrusherTravel : Vector3.zero);
        }
    }

    public bool EnemyEnabled()
    {
        if (PuzzleReady)
            return false;

        if (UsesPressurePads)
            return !InnerHackDone;

        return DefenseStarted;
    }

    public bool CrusherHits(Vector3 position)
    {
        var crusher = director != null ? director.Crusher : null;
        if (crusher == null || !CrusherActive.IsRunning || CrusherActive.Expired(Runner))
            return false;

        var bounds = director.CrusherPlateBounds;
        bounds.Expand(new Vector3(0.3f, 0.4f, 2f));
        return bounds.Contains(position);
    }

    private static bool Near(NetworkObject player, Vector2 position, float distance)
    {
        return player != null && Vector2.Distance(player.transform.position, position) <= distance;
    }
}

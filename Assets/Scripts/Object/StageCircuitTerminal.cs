using Fusion;
using FourPaws.PCS.CircuitHack;
using FourPaws.PCS.Hacking;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

public class StageCircuitTerminal : MonoBehaviour
{
    public enum HackType
    {
        Circuit,
        Timing,
        Rotation
    }

    [SerializeField] private HackType hackType;
    [SerializeField] private bool playAllCircuitStages;
    [SerializeField] private CircuitHackManager manager;
    [SerializeField] private PCS_HackingSession session;
    [SerializeField] private PCS_TimingStageData timingStage;
    [SerializeField] private PCS_RotationStageData rotationStage;
    [SerializeField] private BranchPuzzle branch;
    [SerializeField] private MovingFloor requiredArrival;
    [SerializeField] private int terminal;
    [SerializeField] private UnityEvent onCompleted;

    internal int CircuitStageCount => manager == null ? 0 :
        playAllCircuitStages ? (manager.layouts?.Length > 0 ? manager.layouts.Length : manager.stages?.Length ?? 0) : 1;

    internal void SetBranch(BranchPuzzle puzzle, int index)
    {
        branch = puzzle;
        terminal = index;
    }

    internal void AddCompletionAction(UnityAction action)
    {
        onCompleted ??= new UnityEvent();
        onCompleted.AddListener(action);
    }

    private long run = -1;
    private bool completionApplied;

    public void DebugComplete()
    {
        if (!NetworkGameManager.CanUseStageDebug || completionApplied)
            return;
        completionApplied = true;
        if (run >= 0)
        {
            run = -1;
            CloseHack();
        }

        branch?.DebugCompleteTerminal(terminal);
        onCompleted?.Invoke();
    }

    private void OnEnable()
    {
        if (manager != null)
        {
            manager.OnStageCompleted += CircuitStageCompleted;
            manager.OnHackCompleted += CircuitCompleted;
            manager.OnHackCancelled += CircuitCancelled;
        }
        if (session != null)
        {
            session.OnHackCompleted += SessionCompleted;
            session.OnHackCancelled += SessionCancelled;
        }
    }

    private void OnDisable()
    {
        if (manager != null)
        {
            manager.OnStageCompleted -= CircuitStageCompleted;
            manager.OnHackCompleted -= CircuitCompleted;
            manager.OnHackCancelled -= CircuitCancelled;
        }
        if (session != null)
        {
            session.OnHackCompleted -= SessionCompleted;
            session.OnHackCancelled -= SessionCancelled;
        }

        if (run >= 0)
        {
            run = -1;
            branch?.SetHackActive(terminal, false);
            CloseHack();
        }
    }

    private void Update()
    {
        if (run >= 0 && branch != null && branch.ShouldCloseHack(terminal))
        {
            run = -1;
            branch.SetHackActive(terminal, false);
            CloseHack();
        }

        //해킹 콘솔 클릭을 다른 2D 컴포넌트가 막지는 않는지 검사
        var camera = Camera.main;
        if (camera == null || !camera.pixelRect.Contains(Input.mousePosition))
        {
            InteractableOutline.SetTarget(gameObject, false);
            return;
        }
        var ray = camera.ScreenPointToRay(Input.mousePosition);
        bool hovered = false;
        var shape = GetComponent<Collider>();
        if (shape != null)
            hovered = shape.Raycast(ray, out _, 100f);
        if (!hovered && TryGetComponent(out SpriteRenderer sprite))
            hovered = sprite.bounds.IntersectRay(ray);
        bool available = hovered && CanInteract();
        InteractableOutline.SetTarget(gameObject, available);
        if (available && Input.GetMouseButtonDown(0))
            StartTerminal();
    }

    private bool CanInteract()
    {
        if (completionApplied)
            return false;

        if (LocalMultiplayerDebug.BlocksWorldClick || GameMenu.BlocksWorldClick ||
            (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()))
            return false;

        if (requiredArrival != null && (requiredArrival.Object == null ||
            !requiredArrival.Object.IsValid || !requiredArrival.HasReachedDestination))
            return false;

        var network = NetworkGameManager.Instance;
        if (!HackingConsole.CanLocalPlayerHack || network == null ||
            !network.TryGetGameplayPlayer(network.Runner.LocalPlayer, out NetworkObject player) ||
            player == null || Vector2.Distance(player.transform.position, transform.position) > 2.25f ||
            run >= 0 || (branch != null && !branch.CanHack(terminal)))
            return false;

        return hackType switch
        {
            HackType.Circuit => manager != null && manager.State == CircuitHackManager.HackState.Closed &&
                (manager.layouts?.Length > 0 || manager.stages?.Length > 0),
            HackType.Timing => session != null && session.State == PCS_HackingSession.SessionState.Closed && timingStage != null,
            HackType.Rotation => session != null && session.State == PCS_HackingSession.SessionState.Closed && rotationStage != null,
            _ => false
        };
    }

    private void StartTerminal()
    {
        InteractableOutline.SetTarget(gameObject, false);

        bool started = hackType switch
        {
            HackType.Circuit when playAllCircuitStages => manager.StartAllStages(),
            HackType.Circuit when manager.layouts?.Length > 0 =>
                manager.StartSingleStage(manager.layouts[0]),
            HackType.Circuit =>
                manager.StartSingleStage(manager.stages[0]),
            HackType.Timing => session.StartSingleStage(timingStage),
            HackType.Rotation => session.StartSingleStage(rotationStage),
            _ => false
        };
        if (started)
        {
            run = hackType == HackType.Circuit ? manager.RunId : session.SessionId;
            UIManager.EnsureLocalEventSystem();
            PlayerHealth.SetLocalHacking(true);
            branch?.SetHackActive(terminal, true);
        }
    }

    private void CloseHack()
    {
        if (hackType == HackType.Circuit)
            manager?.CloseHack();
        else
            session?.CloseHack();
        PlayerHealth.SetLocalHacking(false);
    }

    private void CircuitStageCompleted(int id, int attempt, string stage)
    {
        if (id == run && terminal == 2 && branch != null && branch.Object != null &&
            branch.Object.IsValid && manager.StageIndex + 1 < manager.StageCount)
            branch.RPC_ReportHackStage(manager.StageIndex + 1);
    }

    private void CircuitCompleted(int id)
    {
        if (hackType == HackType.Circuit)
            Completed(id);
    }

    private void CircuitCancelled(int id)
    {
        if (hackType == HackType.Circuit)
            Cancelled(id);
    }

    private void SessionCompleted(long id)
    {
        if (hackType != HackType.Circuit)
            Completed(id);
    }

    private void SessionCancelled(long id)
    {
        if (hackType != HackType.Circuit)
            Cancelled(id);
    }

    private void Completed(long id)
    {
        if (run != id)
            return;

        completionApplied = branch == null;
        run = -1;
        SoundManager.Play(SoundEffect.HackingComplete);
        CloseHack();
        if (branch != null)
            branch.RPC_HackCompleted(terminal);
        onCompleted?.Invoke();
    }

    private void Cancelled(long id)
    {
        if (run != id)
            return;

        run = -1;
        branch?.SetHackActive(terminal, false);
        PlayerHealth.SetLocalHacking(false);
    }
}

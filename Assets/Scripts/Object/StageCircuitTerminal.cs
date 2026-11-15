using Fusion;
using FourPaws.PCS.CircuitHack;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

public class StageCircuitTerminal : MonoBehaviour
{
    [SerializeField] private CircuitHackManager manager;
    [SerializeField] private BranchPuzzle branch;
    [SerializeField] private MovingFloor requiredArrival;
    [SerializeField] private int terminal;
    [SerializeField] private UnityEvent onCompleted;

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

    private int run = -1;
    private bool closeAfterResult;
    private bool completionApplied;

    public void DebugComplete()
    {
        if (!NetworkGameManager.CanUseStageDebug || completionApplied)
            return;
        completionApplied = true;
        branch?.DebugCompleteTerminal(terminal);
        onCompleted?.Invoke();
        if (run >= 0)
        {
            run = -1;
            PlayerHealth.SetLocalHacking(false);
            closeAfterResult = true;
        }
    }

    private void OnEnable()
    {
        if (manager == null)
            return;

        manager.OnHackCompleted += Completed;
        manager.OnHackCancelled += Cancelled;
    }

    private void OnDisable()
    {
        if (manager != null)
        {
            manager.OnHackCompleted -= Completed;
            manager.OnHackCancelled -= Cancelled;
        }
        if (run >= 0)
            PlayerHealth.SetLocalHacking(false);
    }

    private void Update()
    {
        if (closeAfterResult)
        {
            closeAfterResult = false;
            if (manager != null)
                manager.CloseHack();
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
            manager == null || ((manager.layouts == null || manager.layouts.Length == 0) &&
                (manager.stages == null || manager.stages.Length == 0)) ||
            manager.State != CircuitHackManager.HackState.Closed || (branch != null && !branch.CanHack(terminal)))
            return false;

        return true;
    }

    private void StartTerminal()
    {
        InteractableOutline.SetTarget(gameObject, false);

        bool started = manager.layouts != null && manager.layouts.Length > 0
            ? manager.StartSingleStage(manager.layouts[Mathf.Clamp(terminal, 0, manager.layouts.Length - 1)])
            : manager.StartSingleStage(manager.stages[Mathf.Clamp(terminal, 0, manager.stages.Length - 1)]);
        if (started)
        {
            run = manager.RunId;
            PlayerHealth.SetLocalHacking(true);
        }
    }

    private void Completed(int id)
    {
        if (run != id)
            return;

        completionApplied = branch == null;
        run = -1;
        PlayerHealth.SetLocalHacking(false);
        if (branch != null)
            branch.RPC_HackCompleted(terminal);
        onCompleted?.Invoke();
        closeAfterResult = true;
    }

    private void Cancelled(int id)
    {
        if (run != id)
            return;

        run = -1;
        PlayerHealth.SetLocalHacking(false);
    }
}

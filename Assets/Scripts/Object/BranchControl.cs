using UnityEngine;

/// <summary>
/// TO DO : UI 의 order 제어를 하는 것 같은데 UI Manager 를 둘 필요가 있어보임
/// </summary>
public class BranchControl : MonoBehaviour
{
    [SerializeField] private BranchPuzzle puzzle;
    [SerializeField] private int control;

    internal void SetPuzzle(BranchPuzzle branch, int index)
    {
        puzzle = branch;
        control = index;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (control != 1 || other.isTrigger || other.attachedRigidbody == null ||
            other.gameObject != other.attachedRigidbody.gameObject ||
            !other.attachedRigidbody.TryGetComponent(out Fusion.NetworkObject player) ||
            !player.CompareTag("Player") || !player.IsValid || !player.HasStateAuthority)
            return;

        if (puzzle != null && puzzle.Object != null && puzzle.Object.IsValid)
            puzzle.RPC_Operate(control);
    }

    private void OnMouseDown()
    {
        if (LocalMultiplayerDebug.BlocksWorldClick || GameMenu.BlocksWorldClick)
            return;
        if (puzzle != null && puzzle.Object != null && puzzle.Object.IsValid)
            puzzle.RPC_Operate(control);
    }
}

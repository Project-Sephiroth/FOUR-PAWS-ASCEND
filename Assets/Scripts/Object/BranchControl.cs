using UnityEngine;

/// <summary>
/// TO DO : UI 의 order 제어를 하는 것 같은데 UI Manager 를 둘 필요가 있어보임
/// </summary>
public class BranchControl : MonoBehaviour
{
    internal const int BarrierSwitch = 2;
    [SerializeField] private BranchPuzzle puzzle;
    [SerializeField] private int control;
    [SerializeField] private Transform leverHandle;
    [SerializeField] private Collider leverClickBox;

    internal void SetLeverPosition(float progress, bool canOperate)
    {
        leverHandle.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(45f, -45f, progress));
        leverClickBox.enabled = canOperate;
    }

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

        Operate(viaTongue: false);
    }

    internal void OperateByTongue()
    {
        if (control == 1)
            Operate(viaTongue: true);
    }

    private void OnMouseDown()
    {
        if (LocalMultiplayerDebug.BlocksWorldClick || GameMenu.BlocksWorldClick)
            return;
        var manager = NetworkGameManager.Instance;
        if (control == 1 && manager != null && manager.Runner != null &&
            manager.GetGameplayCharacter(manager.Runner.LocalPlayer) == MyEnum.CharacterType.Frog)
            return;
        Operate(viaTongue: false);
    }

    private void Operate(bool viaTongue)
    {
        if (puzzle != null && puzzle.Object != null && puzzle.Object.IsValid)
            puzzle.RPC_Operate(control, viaTongue);
    }
}

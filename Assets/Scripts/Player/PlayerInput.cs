using Fusion;
using UnityEngine;

//각 로컬에서 키보드 입력을 감지합니다
public class PlayerInput : NetworkBehaviour
{
    public Vector2 MoveInput;
    public bool JumpInput;

    public bool CanMoveInput = true;
    public bool CanJumpInput = true;

    private PlayerHealth health;
    internal bool IsHacking => health != null && health.IsHacking;

    private void Awake() => health = GetComponent<PlayerHealth>();

    internal void ClearMovementInput()
    {
        MoveInput = Vector2.zero;
        JumpInput = false;
    }

    private void Update()
    {
        if (Object == null || !Object.IsValid || !HasStateAuthority)
            return;

        if (IsHacking || !LocalMultiplayerDebug.AllowsInput(Object))
        {
            ClearMovementInput();
            return;
        }
        if (CanMoveInput)
            MoveInput.x = Input.GetAxis("Horizontal");
        else
            MoveInput = Vector2.zero;

        if (CanJumpInput)
            GetJumpInput();
        else
            JumpInput = false;
    }

    void GetJumpInput()
    {
        if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W))
            JumpInput = true;
    }
}

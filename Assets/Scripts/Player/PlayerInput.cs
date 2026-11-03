using Fusion;
using UnityEngine;

//각 로컬에서 키보드 입력을 감지합니다
public class PlayerInput : NetworkBehaviour
{
    public Vector2 MoveInput;
    public bool JumpInput;

    public bool CanMoveInput = true;
    public bool CanJumpInput = true;

    private void Update()
    {
        if (!HasStateAuthority)
            return;

        if (CanMoveInput)
            GetMoveInput();

        if (CanJumpInput)
            GetJumpInput();
    }

    void GetMoveInput()
    {
        MoveInput = Vector2.zero;

        if (Input.GetKey(KeyCode.LeftArrow))
            MoveInput.x -= 1;

        if (Input.GetKey(KeyCode.RightArrow))
            MoveInput.x += 1;
    }

    void GetJumpInput()
    {
        if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.UpArrow))
            JumpInput = true;
    }
}
using Fusion;
using UnityEngine;

/// <summary>
/// 플레이어를 움직입니다.
/// </summary>
public class Mover : NetworkBehaviour
{
    #region Variables

    private PlayerInput input;
    private Rigidbody2D rb;

    [Header("이동")]
    [SerializeField] private float speed = 2f;

    [Header("점프")]
    [SerializeField] private float jumpForce = 5f;

    public bool CanJump = true;

    [SerializeField] private bool canDoubleJump = false;
    private bool doubleJumped = false;

    [Header("땅 체크")]
    [SerializeField] private Transform groundCheck;
    [SerializeField] private float groundCheckRadius = 0.15f;
    [SerializeField] private LayerMask groundLayer;

    #endregion


    #region Cycle

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        input = GetComponent<PlayerInput>();
    }

    public override void Spawned()
    {
        input.enabled = HasStateAuthority;
    }

    public override void FixedUpdateNetwork()
    {
        //아직 네트워크 Spawn 전이면 실행하지 않음
        if (Object == null)
            return;

        //내 캐릭터만 조작
        if (!HasStateAuthority)
            return;

        if (input == null)
            return;

        Move(input.MoveInput);
        Jump(input.JumpInput);
    }

    #endregion


    #region Movement

    private void Move(Vector2 moveInput)
    {
        rb.linearVelocity = new Vector2(moveInput.x * speed, rb.linearVelocity.y);
    }

    private void Jump(bool jumpInput)
    {
        bool isGrounded = IsGrounded();

        //착지했다면 더블점프 초기화
        if (isGrounded)
            doubleJumped = false;

        if (!jumpInput)
            return;

        input.JumpInput = false;

        if (!CanJump)
            return;

        // 공중에 있는 경우
        if (!isGrounded)
        {
            //더블 점프를 할 수 없거나 이미 더블 점프 중이라면 리턴
            if (!canDoubleJump || doubleJumped)
                return;

            doubleJumped = true;
        }

        //점프
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
    }

    /// <summary>
    /// 땅에 착지했는지를 판가름하기 위해 발쪽에 groundCheck 를 두고 해당 기준으로 레이어를 감지하는 동그란 레이저를 쏩니다
    /// </summary>
    /// <returns></returns>
    private bool IsGrounded()
    {
        return Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer) != null;
    }

#if UNITY_EDITOR_WIN

    /// <summary>
    /// 땅 체크를 위한 레이저를 시각화
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        if (groundCheck == null)
            return;

        Gizmos.DrawWireSphere(
            groundCheck.position,
            groundCheckRadius
        );
    }

#endif

    #endregion
}
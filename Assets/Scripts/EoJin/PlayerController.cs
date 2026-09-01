using Fusion;
using UnityEngine;

public class PlayerController : NetworkBehaviour
{
    #region Variables
    [SerializeField] float speed = 2f;
    [SerializeField] float JumpForce = 50f;
    [SerializeField] float GravityValue = -9.81f;

    [SerializeField] Transform groundCheck;
    [SerializeField] float groundCheckRadius = 0.15f;
    [SerializeField] LayerMask groundLayer;

    [SerializeField] SpriteRenderer model;

    private Rigidbody2D rb;
    Vector2 moveInput;
    bool jumpInput;

    Vector2 velocity;
    #endregion

    #region Cycle
    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    private void Update()
    {
        //입력은 업데이트에서 매 프레임마다 받습니다
        GetInput();
    }

    public override void FixedUpdateNetwork()
    {
        //해당 객체에 권한이 있는지
        if (HasStateAuthority == false)
            return;

        Move(moveInput);

        if (jumpInput)
            Jump();
    }
    #endregion

    #region Movement
    void GetInput()
    {
        moveInput = Vector2.zero;

        if (Input.GetKey(KeyCode.LeftArrow))
            moveInput.x -= 1;

        if (Input.GetKey(KeyCode.RightArrow))
            moveInput.x += 1;

        if (Input.GetKeyDown(KeyCode.Space))
            jumpInput = true;
    }

    void Move(Vector2 input)
    {
        rb.linearVelocity = new Vector2(input.x * speed, rb.linearVelocity.y);
    }

    void Jump()
    {
        if (!IsGrounded())
        {
            jumpInput = false;
            return;
        }

        rb.linearVelocity = new Vector2(rb.linearVelocity.x, JumpForce);

        jumpInput = false;
    }

    bool IsGrounded()
    {
        return Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, groundLayer) != null;
    }

    private void OnDrawGizmosSelected()
    {
        if (groundCheck == null)
            return;

        Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
    }
    #endregion
}
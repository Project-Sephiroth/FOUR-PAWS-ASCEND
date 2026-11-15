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
    private Animator anim;
    private Passenger passenger;

    [Networked] private NetworkBool FacesLeft { get; set; }
    [Networked] private NetworkBool IsWalking { get; set; }
    [Networked] private NetworkBool IsJumping { get; set; }
    public bool FacesLeftNow => FacesLeft;
    public void SetDoubleJump(bool enabled) => canDoubleJump = enabled;

    [Header("이동")]
    [SerializeField] private float speed = 2f;

    public bool CanMove = true;

    [Header("점프")]
    [SerializeField] private float jumpForce = 5f;

    public bool CanJump = true;

    [SerializeField] private bool canDoubleJump = false;
    private bool doubleJumped = false;

    [Header("땅 체크")]
    [SerializeField] private Transform groundCheck;
    [SerializeField] private float groundCheckRadius = 0.15f;
    [SerializeField] private LayerMask groundLayer;

    [Header("애니메이션")]
    [SerializeField] private SpriteRenderer model;

    #endregion

    #region Cycle
    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        input = GetComponent<PlayerInput>();
        anim = GetComponent<Animator>();
        passenger = GetComponent<Passenger>();
        tongue = GetComponentInChildren<Tongue>(true);
        thrower = GetComponentInChildren<Thrower>(true);
    }

    private Tongue tongue;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ConfigurePlayerContacts()
    {
        int bodyLayer = LayerMask.NameToLayer("Player");
        int headLayer = LayerMask.NameToLayer("Head");
        if (bodyLayer < 0 || headLayer < 0)
            return;

        Physics2D.IgnoreLayerCollision(bodyLayer, bodyLayer);
        Physics2D.IgnoreLayerCollision(bodyLayer, headLayer);
        Physics2D.IgnoreLayerCollision(headLayer, headLayer);
    }
    private Thrower thrower;

    public override void Spawned()
    {
        if (input != null)
            input.enabled = HasStateAuthority;
    }

    public override void FixedUpdateNetwork()
    {
        //아직 네트워크 Spawn 전이면 실행하지 않음
        if (Object == null || !HasStateAuthority || input == null || rb == null)
            return;

        if (passenger != null && (passenger.IsCarried || passenger.IsFlying))
        {
            IsWalking = false;
            IsJumping = false;
            return;
        }

        bool actionLocksMovement = (tongue != null && tongue.IsTongueActive) ||
            (thrower != null && thrower.LocksMovement);
        Move(CanMove && !actionLocksMovement ? input.MoveInput : Vector2.zero);
        if (CanJump && !actionLocksMovement)
            Jump(input.JumpInput);
        else
            input.JumpInput = false;
    }

    #endregion

    #region Movement

    private void Move(Vector2 moveInput)
    {
        IsWalking = Mathf.Abs(moveInput.x) > 0.01f;
        if (IsWalking)
            FacesLeft = moveInput.x < 0f;

        rb.linearVelocity = new Vector2(moveInput.x * speed, rb.linearVelocity.y);
    }

    public override void Render()
    {
        if (model != null)
            model.flipX = FacesLeft;
        if (anim != null)
        {
            anim.SetBool("IsWalk", IsWalking);
            anim.SetBool("IsJump", IsJumping);
        }
    }

    private void Jump(bool jumpInput)
    {
        bool isGrounded = IsGrounded();

        IsJumping = !isGrounded;
        if (isGrounded)
            doubleJumped = false;

        if (!jumpInput)
            return;

        input.JumpInput = false;

        if (!isGrounded)
        {
            //더블 점프를 할 수 없거나 이미 더블 점프 중이라면 리턴
            if (!canDoubleJump || doubleJumped)
                return;

            doubleJumped = true;
        }

        IsJumping = true;
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
    }

    /// <summary>
    /// 땅에 착지했는지를 판가름하기 위해 발쪽에 groundCheck 를 두고 해당 기준으로 레이어를 감지하는 동그란 레이저를 쏩니다
    /// </summary>
    /// <returns></returns>
    private bool IsGrounded()
    {
        int walkableLayers = groundLayer.value & ~LayerMask.GetMask("Head", "Player");
        return groundCheck != null && rb.linearVelocity.y <= 0.01f &&
            Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, walkableLayers) != null;
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
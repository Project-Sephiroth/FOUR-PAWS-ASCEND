using System.Collections.Generic;
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
    private Collider2D bodyCollider;
    private Animator anim;
    private const float PlatformFootTolerance = 0.12f;
    private static double lastPhysicsSyncTime = double.NegativeInfinity;
    private readonly List<Collider2D> footContacts = new();
    private Collider2D ridingSurface;
    private Bounds lastSurfaceBounds;
    private Passenger passenger;
    private MovingObject tonguePassenger;

    [Networked] private NetworkBool FacesLeft { get; set; }
    [Networked] private NetworkBool IsWalking { get; set; }
    [Networked] private NetworkBool IsJumping { get; set; }
    [Networked] private byte JumpSequence { get; set; }
    private const float OtherPlayerJumpVolume = 0.35f;
    private byte lastRenderedJump;
    private float nextStepIn;
    [SerializeField, Min(0.05f)] private float footstepInterval = 0.3f;
    public bool FacesLeftNow => FacesLeft;

    internal void FaceWorldX(float worldX)
    {
        float offset = worldX - transform.position.x;
        if (HasStateAuthority && !Mathf.Approximately(offset, 0f))
            FacesLeft = offset < 0f;
    }

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
    internal Vector3 FeetPosition => groundCheck != null ? groundCheck.position : transform.position;
    [SerializeField] private float groundCheckRadius = 0.15f;
    [SerializeField] private LayerMask groundLayer;

    [Header("애니메이션")]
    [SerializeField] private SpriteRenderer model;

    #endregion

    #region Cycle
    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        bodyCollider = GetComponent<Collider2D>();
        input = GetComponent<PlayerInput>();
        anim = GetComponent<Animator>();
        passenger = GetComponent<Passenger>();
        tonguePassenger = GetComponent<MovingObject>();
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
        lastRenderedJump = JumpSequence;
        if (input != null)
            input.enabled = HasStateAuthority;
    }

    public override void FixedUpdateNetwork()
    {
        //아직 네트워크 Spawn 전이면 실행하지 않음
        if (Object == null || !HasStateAuthority || input == null || rb == null)
            return;

        bool hacking = input.IsHacking;
        if (hacking)
            input.ClearMovementInput();

        bool heldByTongue = tonguePassenger != null && tonguePassenger.IsHeldByTongue;
        if (heldByTongue && CanJump && input.JumpInput)
            heldByTongue = !tonguePassenger.ReleaseAllyFromTongue();
        if ((passenger != null && (passenger.IsCarried || passenger.IsFlying)) || heldByTongue)
        {
            ridingSurface = null;
            IsWalking = false;
            IsJumping = false;
            return;
        }

        bool actionLocksMovement = hacking || (tongue != null && tongue.IsTongueActive) ||
            (thrower != null && thrower.LocksMovement);
        Move(CanMove && !actionLocksMovement ? input.MoveInput : Vector2.zero);
        if (CanJump && !actionLocksMovement)
            Jump(input.JumpInput);
        else
            input.JumpInput = false;
    }

    #endregion

    #region Movement

    private void FixedUpdate()
    {
        if (Object == null || !Object.IsValid || !HasStateAuthority || rb == null)
            return;

        FollowMovingSurface();
    }

    private void FollowMovingSurface()
    {
        if (bodyCollider == null || !bodyCollider.isActiveAndEnabled || !rb.simulated || rb.linearVelocity.y > 0.15f ||
            (passenger != null && (passenger.IsCarried || passenger.IsFlying)) ||
            (tonguePassenger != null && tonguePassenger.IsHeldByTongue))
        {
            ridingSurface = null;
            return;
        }

        // 원격 발판의 보간된 Transform을 2D 물리 쿼리에도 같은 스텝에서 사용합니다.
        if (lastPhysicsSyncTime != Time.fixedTimeAsDouble)
        {
            Physics2D.SyncTransforms();
            lastPhysicsSyncTime = Time.fixedTimeAsDouble;
        }

        Bounds body = bodyCollider.bounds;
        if (ridingSurface != null && ridingSurface.isActiveAndEnabled && !ridingSurface.isTrigger &&
            FeetOnSurface(body, lastSurfaceBounds))
        {
            Bounds surface = ridingSurface.bounds;
            Vector2 delta = surface.center - lastSurfaceBounds.center;
            if (delta != Vector2.zero)
            {
                // 물리 충돌에 의해 발이 이미 움직였어도 현재 발판 윗면에 한 번만 맞춥니다.
                delta.y = surface.max.y - body.min.y;
                rb.position += delta;
                transform.position = new Vector3(rb.position.x, rb.position.y, transform.position.z);
            }

            lastSurfaceBounds = surface;
            return;
        }

        ridingSurface = null;
        var filter = new ContactFilter2D { useTriggers = false };
        filter.SetLayerMask(Physics2D.GetLayerCollisionMask(gameObject.layer) & ~LayerMask.GetMask("Player", "Head"));
        var center = new Vector2(body.center.x, body.min.y);
        var size = new Vector2(body.size.x, PlatformFootTolerance * 2f);
        Physics2D.OverlapBox(center, size, 0f, filter, footContacts);

        float highest = float.NegativeInfinity;
        foreach (var surface in footContacts)
        {
            Bounds top = surface.bounds;
            if (top.max.y <= highest || !FeetOnSurface(body, top) ||
                surface.GetComponentInParent<MovingObject>() == null)
                continue;

            highest = top.max.y;
            ridingSurface = surface;
            lastSurfaceBounds = top;
        }
    }

    private static bool FeetOnSurface(Bounds body, Bounds surface) =>
        body.max.x > surface.min.x && body.min.x < surface.max.x &&
        Mathf.Abs(body.min.y - surface.max.y) <= PlatformFootTolerance;

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
        bool thrownByBear = passenger != null && passenger.IsFlying;
        bool jumping = thrownByBear || IsJumping;
        if (anim != null)
        {
            anim.SetBool("IsWalk", IsWalking && !thrownByBear);
            anim.SetBool("IsJump", jumping);
        }

        if (JumpSequence != lastRenderedJump)
        {
            lastRenderedJump = JumpSequence;
            SoundManager.Play(SoundEffect.Jump, IsLocalCharacter() ? 1f : OtherPlayerJumpVolume);
        }
        UpdateFootsteps(jumping);
    }

    private bool IsLocalCharacter()
    {
        if (LocalMultiplayerDebug.GetPlayers(Runner) != null)
            return LocalMultiplayerDebug.GetSelected(Runner) == Object;

        return Runner.TryGetPlayerObject(Runner.LocalPlayer, out var localPlayer)
            ? localPlayer == Object
            : HasStateAuthority;
    }

    private void UpdateFootsteps(bool jumping)
    {
        if (!IsWalking || jumping)
        {
            nextStepIn = 0f;
            return;
        }

        nextStepIn -= Time.deltaTime;
        if (nextStepIn > 0f)
            return;

        int footstep = Random.Range((int)SoundEffect.FootStepA, (int)SoundEffect.FootStepC + 1);
        SoundManager.Play((SoundEffect)footstep);
        nextStepIn = footstepInterval;
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
        if (SidePortal.TryUse(rb))
        {
            ridingSurface = null;
            IsJumping = false;
            return;
        }

        if (!isGrounded)
        {
            //더블 점프를 할 수 없거나 이미 더블 점프 중이라면 리턴
            if (!canDoubleJump || doubleJumped)
                return;

            doubleJumped = true;
        }

        ridingSurface = null;
        IsJumping = true;
        JumpSequence++;
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
    }

    /// <summary>
    /// 땅에 착지했는지를 판가름하기 위해 발쪽에 groundCheck 를 두고 해당 기준으로 레이어를 감지하는 동그란 레이저를 쏩니다
    /// </summary>
    /// <returns></returns>
    private bool IsGrounded()
    {
        int walkableLayers = groundLayer.value & ~LayerMask.GetMask("Head", "Player");
        return rb.linearVelocity.y <= 0.01f &&
            (ridingSurface != null || (groundCheck != null &&
                Physics2D.OverlapCircle(groundCheck.position, groundCheckRadius, walkableLayers) != null));
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
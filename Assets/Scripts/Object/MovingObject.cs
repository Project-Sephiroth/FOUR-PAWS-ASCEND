using Fusion;
using UnityEngine;
using static IMoveable;

[RequireComponent(typeof(Rigidbody2D))]
// 열쇠, 잡을 수 있는 아군과 로봇에 공통으로 붙습니다. 누가 들고 있는지와 물체가 어떻게 움직이는지를 한곳에서 관리합니다.
public class MovingObject : NetworkBehaviour, IInteractable, IMoveable
{
    // canInteract는 평상시 상호작용 허용 여부, moveSpeed와 arcHeight는 기본 이동 속도와 포물선의 높이입니다.
    [SerializeField] private bool canInteract = true;
    [SerializeField] private float moveSpeed = 3f;
    [SerializeField] private float arcHeight = 2f;
    [SerializeField] private InteractableOutline outline;
    [SerializeField] private Transform holdPivot;
    [SerializeField] private bool tongueAlly;
    [SerializeField] private bool tongueOnly;
    internal bool AllowsHolder(Interactor interactor) =>
        interactor is Tongue || (!tongueOnly && (robot == null || interactor is not Thrower));
    private Passenger passenger;
    private PlayerHealth playerHealth;

    // IsMoving은 정해진 경로를 따라 움직이는 중인지, Holder는 지금 누가 들고 있는지 나타냅니다.
    // StartPosition과 Destination은 경로의 양 끝, Path는 직선이나 포물선입니다. Duration은 전체 이동 시간이고 Elapsed는 지금까지 지난 시간입니다.
    [Networked] public NetworkBool IsMoving { get; private set; }
    [Networked] public Interactor Holder { get; private set; }
    [Networked] private Vector2 StartPosition { get; set; }
    [Networked] private Vector2 Destination { get; set; }
    [Networked] private MoveType Path { get; set; }
    [Networked] private float Duration { get; set; }
    [Networked] private float Elapsed { get; set; }
    [Networked] private float ArcHeight { get; set; }
    public bool IsHeld => Holder != null;
    // ReleasedByTongue는 마지막으로 혀에서 놓였는지를 기억합니다. 열쇠문에서 혀로 놓인 열쇠를 구분할 때도 사용합니다.
    // RobotAirborne은 로봇이 던져졌거나 놓여 착지를 기다리는 상태입니다. RobotFallSpeed는 그때의 세로 속도입니다. groundHits에는 바닥 검사 결과를 담습니다.
    [Networked] public NetworkBool ReleasedByTongue { get; private set; }
    // 곰 전용 문은 마지막으로 곰이 던진 인형만 받습니다.
    [Networked] public NetworkBool ReleasedByThrower { get; private set; }
    [Networked] internal NetworkBool RobotAirborne { get; private set; }
    [Networked] private float RobotFallSpeed { get; set; }
    private readonly RaycastHit2D[] groundHits = new RaycastHit2D[4];
    private Key key;
    private RobotBase robot;
    // 클릭과 외곽선이 같은 조건을 사용해야 잡을 수 없는 물체를 표시하지 않습니다.
    public bool CanInteract => Object != null && Object.IsValid && canInteract && !IsHeld && !IsMoving &&
        !RobotAirborne && (robot == null || !robot.SuppressBody);
    internal Vector2 TongueGripPosition => holdPivot != null ? holdPivot.position : transform.position;
    internal bool CanBeTongueHeld => CanInteract && !TongueTargetUnavailable;
    internal bool TongueTargetUnavailable => Object == null || !Object.IsValid ||
        (robot != null && robot.SuppressBody) ||
        (tongueAlly && !CanGripAlly);
    internal bool IsHeldByTongue => Object != null && Object.IsValid && Holder is Tongue;
    private bool CanGripAlly => passenger != null && !passenger.IsCarried &&
        playerHealth != null && playerHealth.Health > 0;
    public bool IsCrushed => robot != null && robot.IsCrushed;

    // rb는 물리로 움직이는 몸체, col은 부딪히는 모양입니다. original로 시작하는 값들은 잡기 전의 물리 설정으로 돌려놓을 때 사용합니다.
    // suppressingPhysics는 물리를 이미 멈춘 상태인지 기억해 매번 설정을 덮어쓰지 않게 합니다.
    private Rigidbody2D rb;
    private Collider2D col;
    private bool suppressingPhysics;
    private bool originalSimulation;
    private bool originalColliderEnabled;
    private bool originalColliderTrigger;
    private bool tongueHitboxActive;
    private RigidbodyType2D originalBodyType;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        key = GetComponent<Key>();
        robot = GetComponent<RobotBase>();
        if (tongueAlly)
        {
            passenger = GetComponent<Passenger>();
            playerHealth = GetComponent<PlayerHealth>();
        }
        originalSimulation = rb.simulated;
        originalBodyType = rb.bodyType;
        originalColliderEnabled = col != null && col.enabled;
        originalColliderTrigger = col != null && col.isTrigger;
    }

    // 매 틱의 처리 순서
    // 1. 물체를 맡은 쪽에서만 이동을 계산하고, 이미 사용한 열쇠는 건너뜁니다.
    // 2. 제거된 로봇의 비행을 끝내고, 현재 잡힌 상태에 맞춰 물리 설정을 적용합니다.
    // 3. 경로를 따라 움직이는 중이면 Move, 경로가 끝났지만 로봇이 공중에 있으면 FallRobot을 실행합니다.
    // 4. 둘 다 아니면서 누가 잡고 있다면 혀 끝이나 손의 잡는 위치를 따라갑니다.
    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority || (key != null && key.IsConsumed))
            return;

        ApplyHeldPhysics();
        if (IsCrushed)
        {
            if (RobotAirborne)
                FallRobot();
            return;
        }
        if (IsMoving)
            Move();
        else if (RobotAirborne)
            FallRobot();
        else if (Holder is Tongue tongue)
        {
            if (tongueAlly && !CanGripAlly)
            {
                ReleaseAllyFromTongue();
                return;
            }

            if (tongue.State == Tongue.TongueState.Holding)
                FollowTongue(tongue);
        }
        else if (Holder is Thrower bear)
            FollowBear(bear);
    }

    internal bool ReleaseAllyFromTongue()
    {
        if (!HasStateAuthority || !tongueAlly || Holder is not Tongue tongue)
            return false;

        Holder = null;
        ReleasedByTongue = true;
        ApplyHeldPhysics();
        tongue.RPC_AllyReleased(this);
        return true;
    }

    private void FollowBear(Thrower bear)
    {
        Transform anchor = key != null ? bear.PuppetKeyHoldPoint : bear.HoldPoint;
        if (anchor != null)
            transform.position = anchor.position;
    }

    // 혀의 목표점을 피벗에 맞춥니다. Rigidbody와 Transform은 같은 위치에 둡니다.
    private void FollowTongue(Tongue tongue)
    {
        Vector2 gripOffset = TongueGripPosition - (Vector2)transform.position;
        SetBodyPosition(tongue.GetFollowPosition() - gripOffset);
    }

    public override void Render()
    {
        if (HasStateAuthority && Holder is Thrower bear)
            FollowBear(bear);
        if ((robot != null || !HasStateAuthority) && (key == null || !key.IsConsumed))
            ApplyHeldPhysics();
    }

    // 먼저 지금 잡을 수 있는 물체인지 확인합니다. 그다음 열쇠 전용 동작, 곰의 던지기, 개구리 혀 중 해당 동작을 호출합니다.
    public void OnInteract(Interactor interactor)
    {
        if (Object == null || !Object.IsValid || !canInteract || IsCrushed || IsHeld || IsMoving || RobotAirborne || (robot != null && robot.SuppressBody))
            return;

        if (!AllowsHolder(interactor) || (tongueAlly && interactor is not Tongue))
            return;

        if (key != null && interactor is Thrower)
            key.OnInteract(interactor);
        else if (interactor is Thrower thrower)
            thrower.TryThrow(this);
        else if (interactor is Tongue tongue)
            tongue.TryTongue(this);
    }

    // 잡기 순서
    // 1. 온라인에서는 요청을 보낸 사람이 조작하는 캐릭터인지 확인합니다.
    // 2. accepted에 잡기 가능 여부를 담습니다. 이미 잡혔거나 날아가는 물체는 잡을 수 없습니다.
    // 3. 허용하면 Holder를 설정하고 물리를 끈 뒤, 곰이 잡은 물체를 손 위치에 둡니다.
    // 4. 결과를 곰이나 개구리에게 돌려줘서 다음 동작을 시작할 수 있게 합니다.
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestHold(Interactor interactor, RpcInfo info = default)
    {
        if (interactor == null || (!Runner.IsSinglePlayer && info.Source != interactor.Object.StateAuthority))
            return;

        bool accepted = canInteract && !IsCrushed && !IsHeld && !IsMoving && !RobotAirborne &&
            (robot == null || !robot.SuppressBody) && (key == null || key.CanPickUp);
        accepted &= AllowsHolder(interactor) && (!tongueAlly || (interactor is Tongue && CanGripAlly));
        if (accepted)
        {
            ReleasedByTongue = false;
            ReleasedByThrower = false;
            Holder = interactor;

            if (interactor is Tongue)
            {
                passenger?.OnTongueGrabbed();
                robot?.StartHeldStruggle();
            }

            ApplyHeldPhysics();

            if (interactor is Thrower bear)
                FollowBear(bear);
        }

        if (interactor is Tongue tongue)
            tongue.RPC_HoldResult(this, accepted);
        else if (interactor is Thrower thrower)
            thrower.RPC_HoldResult(this, accepted);
    }

    // 놓기 순서
    // 1. 현재 잡고 있는 캐릭터의 요청인지 확인합니다.
    // 2. 손에 붙여 둔 물체를 떼고 지정 위치에 놓은 뒤 Holder를 비웁니다.
    // 3. 혀에서 놓은 로봇은 낙하를 시작하게 표시하고, 잡는 동안 껐던 물리를 다시 적용합니다.
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestRelease(Interactor interactor, Vector2 position, RpcInfo info = default)
    {
        if (interactor == null || Holder != interactor || (!Runner.IsSinglePlayer && info.Source != interactor.Object.StateAuthority))
            return;

        ReleasedByTongue = interactor is Tongue;
        if (ReleasedByTongue)
        {
            position -= TongueGripPosition - (Vector2)transform.position;
            SetBodyPosition(position);
        }
        else
            transform.position = position;
        Holder = null;
        if (robot != null && ReleasedByTongue && !IsCrushed)
        {
            RobotAirborne = true;
            RobotFallSpeed = 0f;
            rb.position = position;
        }
        ApplyHeldPhysics();
    }

    public void ConsumeKey()
    {
        if (HasStateAuthority)
            Holder = null;
    }

    // 잡힌 동안에는 물체가 손이나 혀와 함께 움직이므로 중력과 충돌체를 잠깐 끕니다.
    // 놓을 때는 Awake에서 저장한 설정을 되돌립니다. 동적 몸체의 이전 속도는 지워서 잡히기 전 속도로 튀지 않게 합니다.
    private void ApplyHeldPhysics()
    {
        bool suppress = IsHeld || (robot != null && robot.SuppressBody);
        // 혀에 잡힌 로봇은 물리 반응 없이 따라가지만 돌의 충돌 검사에는 남겨 둡니다.
        bool tongueHitbox = robot != null && Holder is Tongue && !robot.SuppressBody && originalColliderEnabled;
        if (suppressingPhysics == suppress && tongueHitboxActive == tongueHitbox)
            return;

        if (tongueHitboxActive != tongueHitbox)
        {
            rb.bodyType = tongueHitbox ? RigidbodyType2D.Kinematic : originalBodyType;
            if (col != null)
                col.isTrigger = tongueHitbox || originalColliderTrigger;
        }
        suppressingPhysics = suppress;
        tongueHitboxActive = tongueHitbox;
        rb.simulated = (tongueHitbox || !suppress) && originalSimulation;
        if (col != null)
            col.enabled = (tongueHitbox || !suppress) && originalColliderEnabled;

        if (HasStateAuthority && rb.bodyType == RigidbodyType2D.Dynamic)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }
    }

    // 던지기 순서
    // 1. 현재 잡고 있는 캐릭터의 요청인지 확인하고 물체를 손에서 분리합니다.
    // 2. 손 위치로 출발점을 맞춘 뒤 잡은 상태를 풀고 물리를 되돌립니다.
    // 3. BeginMove로 경로를 준비합니다.
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_RequestMove(Interactor interactor, MoveType type, Vector2 origin, Vector2 target,
        float speed, float height, RpcInfo info = default)
    {
        if (robot != null || interactor == null || Holder != interactor ||
            (!Runner.IsSinglePlayer && info.Source != interactor.Object.StateAuthority))
            return;

        SetBodyPosition(origin);

        Holder = null;
        ApplyHeldPhysics();
        BeginMove(type, target, speed, height);
        ReleasedByThrower = key != null && interactor is Thrower && IsMoving;
    }

    public void TryMove(MoveType type, Vector2 targetPosition)
    {
        if (!HasStateAuthority || IsMoving || IsHeld)
            return;

        BeginMove(type, targetPosition, moveSpeed, arcHeight);
    }

    // 이동 거리와 속도로 전체 시간을 정합니다. 경로를 코드에서 그대로 따라가게 이동 중에는 물리 몸체를 Kinematic으로 바꿉니다.
    private void BeginMove(MoveType type, Vector2 target, float speed, float height)
    {
        StartPosition = transform.position;
        Destination = target;
        float distance = Vector2.Distance(StartPosition, target);
        if (distance <= 0.001f || speed <= 0f)
            return;

        Path = type;
        ArcHeight = type == MoveType.Parabola && key != null ? Mathf.Max(0.35f, height) : height;
        Duration = distance / speed;
        Elapsed = 0f;
        IsMoving = true;
        rb.bodyType = RigidbodyType2D.Kinematic;
    }

    // 이동 순서
    // 경로를 따라 이동하며 로봇의 착지를 확인하고, 끝나면 원래 물리 타입으로 복구합니다.
    public void Move()
    {
        if (!HasStateAuthority || !IsMoving)
            return;

        // 마지막 MovePosition이 물리 스텝에 반영된 뒤 원래 물리 타입을 복구합니다.
        if (Elapsed >= Duration)
        {
            IsMoving = false;
            rb.bodyType = originalBodyType;
            return;
        }

        Elapsed = Mathf.Min(Elapsed + Runner.DeltaTime, Duration);
        Vector2 next = SamplePath(Elapsed);

        if (RobotAirborne)
        {
            if (!TryLandRobot(next))
                SetBodyPosition(next);
            return;
        }

        if (key != null)
            SetBodyPosition(next);
        else
            rb.MovePosition(next);
    }

    private Vector2 SamplePath(float elapsed)
    {
        float progress = elapsed / Duration;
        Vector2 position = Vector2.Lerp(StartPosition, Destination, progress);
        if (Path == MoveType.Parabola)
            position.y += 4f * ArcHeight * progress * (1f - progress);
        return position;
    }

    // 던지기 경로가 끝났거나 혀에서 놓인 로봇은 매 틱 아래로 빨라집니다. 먼저 바닥 충돌을 보고, 월드 아래로 빠지면 제거를 요청합니다.
    private void FallRobot()
    {
        RobotFallSpeed -= 24f * Runner.DeltaTime;
        Vector2 next = rb.position + Vector2.up * (RobotFallSpeed * Runner.DeltaTime);
        if (!TryLandRobot(next))
        {
            if (next.y < -35f)
            {
                EndRobotFlight();
                robot.HandleOutOfBounds();
                return;
            }
            SetBodyPosition(next);
        }
    }

    // 물리 몸체 위치와 화면에 보이는 Transform 위치를 함께 맞춥니다. 둘이 다른 위치를 가리키면 착지 계산과 보이는 위치가 어긋날 수 있습니다.
    private void SetBodyPosition(Vector2 position)
    {
        rb.position = position;
        transform.position = new Vector3(position.x, position.y, transform.position.z);
    }

    // 착지 순서
    // 1. 드론은 모든 방향에서 Ground 충돌을 검사하고, 다른 로봇은 아래로 움직일 때만 바닥을 검사합니다.
    // 2. 상자 충돌체는 실제 물리 위치, 크기, 각도를 사용해 이동 구간을 BoxCast로 검사합니다.
    // 3. 가장 가까운 충돌 지점 앞에서 로봇을 멈추고 공중 상태를 끝냅니다.
    private bool TryLandRobot(Vector2 next)
    {
        Vector2 displacement = next - rb.position;
        var drone = robot as DroneRobot;
        if (col == null || displacement.sqrMagnitude <= 0.00000001f || (drone == null && displacement.y >= 0f))
            return false;

        var filter = new ContactFilter2D { useTriggers = false };
        filter.SetLayerMask(LayerMask.GetMask("Ground"));
        var bounds = col.bounds;
        Vector2 center = bounds.center;
        Vector2 size = bounds.size;
        float angle = 0f;
        if (col is BoxCollider2D box)
        {
            Vector3 scale = transform.lossyScale;
            center = rb.position + (Vector2)transform.TransformVector(box.offset);
            size = Vector2.Scale(box.size, new Vector2(Mathf.Abs(scale.x), Mathf.Abs(scale.y)));
            angle = rb.rotation;
        }
        float distance = displacement.magnitude;
        Vector2 direction = displacement / distance;
        int count = Physics2D.BoxCast(center, size, angle, direction, filter, groundHits, distance + 0.02f);
        float travel = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
            if (drone != null || groundHits[i].normal.y > 0.3f)
                travel = Mathf.Min(travel, groundHits[i].distance);
        if (float.IsPositiveInfinity(travel))
            return false;

        SetBodyPosition(rb.position + direction * Mathf.Max(0f, travel - 0.02f));
        EndRobotFlight();
        if (drone != null)
            drone.HitGround();
        else if (!ReleasedByTongue && !robot.RequiresCrushing)
            robot.DefeatOnLanding();
        return true;
    }

    private void EndRobotFlight()
    {
        IsMoving = false;
        RobotAirborne = false;
        RobotFallSpeed = 0f;
        rb.bodyType = originalBodyType;
    }

    // 압착처럼 공중 이동이나 잡기가 끝나기 전에 제거될 수도 있습니다. 이때 손에서 떼고 진행 중이던 이동 상태를 정리합니다.
    internal void ReleaseRobotForRemoval()
    {
        bool releasedFromTongue = Holder is Tongue;
        if (Holder != null)
        {
            transform.SetParent(null, true);
            Holder = null;
        }
        if (releasedFromTongue)
        {
            rb.position = transform.position;
            RobotAirborne = true;
            RobotFallSpeed = 0f;
        }
        else if (!RobotAirborne)
            EndRobotFlight();
    }

    public void SetOutlineMat(bool isOn)
    {
        if (outline != null)
            outline.SetOutline(isOn);
    }
}

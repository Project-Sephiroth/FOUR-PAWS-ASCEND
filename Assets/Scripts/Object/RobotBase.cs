using Fusion;
using UnityEngine;

/// <summary>
/// 드론 로봇, 필드 로봇은 해당 클래스를 베이스로 둡니다 (지상-공중 이동만 따로 처리)
/// 로봇은 가까이 있는 대상을 1순위로, 2순위로 해킹 중인 대상을 목표로 잡습니다
/// </summary>
public abstract class RobotBase : NetworkBehaviour
{
    [SerializeField] private BranchPuzzle encounter; //로봇은 해킹 진행에 따라 움직이지 못합니다
    [SerializeField] private Switch activationSwitch; //로봇 등장 스위치
    [Networked] private NetworkBool Activated { get; set; } //등장을 네트워크 동기화

    [Header("로봇 설정")]
    [SerializeField] protected Vector2 patrolX = new(-2f, 4f); //가로 순찰 구간
    [SerializeField] protected float speed = 2f; 
    [SerializeField] private float detectionRange = 5f; //플레이어 감지 범위
    [SerializeField, Range(1, 3)] private int damage = 1; //플레이어에게 줄 피해량
    [SerializeField, Min(1)] private int maxHealth = 2; 
    [SerializeField] private RobotSpawner spawner;
    [SerializeField] private bool disableRespawn;
    [SerializeField, Min(0)] private int spawnerSlot;
    [Networked] private TickTimer FirstSpawnTimer { get; set; }
    [Networked] private NetworkBool RuntimePooled { get; set; }
    [Networked] private NetworkBool HodPoolInactive { get; set; }

    internal void DeactivateHodPoolSlot()
    {
        if (HodPoolInactive)
            return;

        HodPoolInactive = true;
        AimTimer = default;
        BulletActive = false;
        ReleaseForDespawn();
        ApplyAppearance();
    }

    internal void ReleaseForDespawn()
    {
        if (movingObject != null && movingObject.IsHeld)
            movingObject.ReleaseRobotForRemoval();
    }

    /// <summary>
    /// 로봇 스포너에서 호출
    /// </summary>
    internal void ConfigurePool(RobotSpawner home, int slot, BranchPuzzle puzzle, Vector2 patrol, float movementSpeed)
    {
        spawner = home;
        spawnerSlot = slot;
        encounter = puzzle;
        patrolX = patrol;
        speed = movementSpeed;
        RuntimePooled = true;
    }

    [Networked] protected NetworkBool GoingRight { get; set; } //다음 순찰 방향
    [Networked] private TickTimer RespawnTimer { get; set; } //리스폰 쿨타임
    [Networked] private TickTimer RemovalTimer { get; set; } //사라지는 애니메이션 시간
    [Networked] private TickTimer HitFlash { get; set; } //붉은 효과 조절
    [Networked] public int Health { get; private set; }
    [Networked] private Vector3 SpawnPosition { get; set; }
    protected Vector3 spawnPosition => SpawnPosition;



    private const float FlashSeconds = 0.22f;
    private const float RemovalSeconds = 0.78f;
    protected virtual float RemovalDuration => RemovalSeconds;
    private static readonly Color HitColor = new(1f, 0.17f, 0.2f);
    [Networked] private TickTimer AttackCooldown { get; set; }

    protected enum RobotState { Idle, Patrol, Chase, Struggling, Dead, Aim, Held }

    [Header("로봇 팔 회전 및 총 설정")]
    [SerializeField] private Animator robotAnimator;
    [SerializeField] private AnimationClip heldStruggleClip;
    [Networked] private TickTimer HeldStruggle { get; set; }

    internal void StartHeldStruggle()
    {
        if (!HasStateAuthority)
            return;

        float seconds = heldStruggleClip != null ? heldStruggleClip.length : 0f;
        HeldStruggle = seconds > 0f ? TickTimer.CreateFromSeconds(Runner, seconds) : default;
        State = seconds > 0f ? RobotState.Struggling : RobotState.Held;
    }
    [SerializeField] private SpriteRenderer gunArm;
    [SerializeField] private SpriteRenderer bulletVisual;
    [SerializeField] private bool shootsProjectiles;
    [SerializeField] private float muzzleDistance = 0.24f;
    [SerializeField, Min(0f)] private float closeTargetRange = 2.5f; //얼마나 가까운 적을 공격할 수 있는가
    [SerializeField] private float aimSeconds = 0.45f;
    [SerializeField] private float bulletSpeed = 9f;
    [Networked] private TickTimer AimTimer { get; set; }
    [Networked] private Vector2 AimDirection { get; set; }
    [Networked] private NetworkBool BulletActive { get; set; }
    [Networked] private Vector2 BulletPosition { get; set; }
    [Networked] private Vector2 BulletDirection { get; set; }
    [Networked] private TickTimer BulletLife { get; set; }

    protected bool UsesRangedAttack => bulletVisual != null && (shootsProjectiles || gunArm != null);
    protected BranchPuzzle Encounter => encounter;

    private ContactFilter2D sightFilter;
    private ContactFilter2D bulletFilter;
    private readonly RaycastHit2D[] sightHits = new RaycastHit2D[1];

    protected bool CanShootFromHere(PlayerHealth target) => TryGetShot(target, out _, out _, out _);

    private bool TryGetShot(PlayerHealth target, out Vector2 shoulder, out Vector2 delta, out bool facingLeft)
    {
        Vector2 point = GetTargetPoint(target);
        facingLeft = point.x < transform.position.x;
        shoulder = gunArm == null ? transform.position : gunArm.transform.parent.TransformPoint(GetShoulderOffset(facingLeft));
        delta = point - shoulder;
        return Physics2D.Linecast(shoulder, point, sightFilter, sightHits) == 0;
    }

    private Vector3 shoulderOffset;
    private Vector3 GetShoulderOffset(bool facingLeft) => new Vector3(
        facingLeft ? -shoulderOffset.x : shoulderOffset.x, shoulderOffset.y, shoulderOffset.z);

    [Networked] protected RobotState State { get; set; }
    [Networked] private NetworkBool Crushed { get; set; }
    [Networked] protected NetworkBool FacingLeft { get; set; }
    public bool IsCrushed => Crushed;
    internal virtual bool RequiresCrushing => false;
    public bool IsTongueHeld => movingObject != null && movingObject.Holder is Tongue;
    private MovingObject movingObject; //로봇도 개구리가 잡거나 곰이 던질 수 있게 하기 위함

    [SerializeField] private SpriteRenderer model;
    private Collider2D hitbox;
    private bool startsWithHitbox;

    //스턴이나 죽을 때 깜박거리는 효과를 주기 위해 origin 컬러 기억
    private Color modelColor = Color.white;
    private Color armColor = Color.white;

    internal bool SuppressBody => IsCrushed || !HasAppeared;

    protected virtual void Awake()
    {
        sightFilter = new ContactFilter2D { useTriggers = false };
        sightFilter.SetLayerMask(LayerMask.GetMask("Ground"));
        bulletFilter = new ContactFilter2D { useTriggers = false };
        bulletFilter.SetLayerMask(LayerMask.GetMask("Ground", "Player", "Head"));
        movingObject = GetComponent<MovingObject>();
        if (model == null)
            model = GetComponent<SpriteRenderer>();
        hitbox = GetComponent<Collider2D>();
        startsWithHitbox = hitbox != null && hitbox.enabled;
        if (model != null)
            modelColor = model.color;
        if (gunArm != null)
        {
            armColor = gunArm.color;
            shoulderOffset = gunArm.transform.localPosition;
            if (model != null)
            {
                gunArm.sortingLayerID = model.sortingLayerID;
                gunArm.sortingOrder = model.sortingOrder + 1;
            }
        }
    }

    //해킹 콘솔 또는 스위치 on off 로 로봇들이 활성/비활성화 되기 때문에 아래 변수들 세팅
    private bool HasAppeared => !HodPoolInactive && (spawner == null || spawner.AllowsSlot(spawnerSlot)) &&
        !FirstSpawnTimer.IsRunning && (activationSwitch == null || Activated);
    private bool IsActive => !SuppressBody && (RuntimePooled || encounter == null || encounter.EnemyEnabled());
    public int Damage => Mathf.Clamp(damage, 1, 3);

    /// <summary>
    /// 스포너에 의해 또는 씬 배치로 스폰이 정해짐
    /// </summary>
    public override void Spawned()
    {
        if (HasStateAuthority)
        {
            Health = Mathf.Max(1, maxHealth);
            SpawnPosition = spawner != null ? spawner.SpawnPosition : transform.position;
            if (spawner != null && !RuntimePooled)
                FirstSpawnTimer = spawner.StartFirstSpawnWait(Runner, spawnerSlot);
            GoingRight = spawner == null || !spawner.StartPatrolLeft;
            FacingLeft = !GoingRight;
        }

        ApplyAppearance();
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority)
            return;

        if (HodPoolInactive || (spawner != null && !spawner.AllowsSlot(spawnerSlot)))
            return;

        if (FirstSpawnTimer.IsRunning) //첫 스폰 
        {
            if (!FirstSpawnTimer.Expired(Runner))
                return;

            FirstSpawnTimer = default;
            MoveToSpawn();
        }

        //스위치 또는 기타로 인해 로봇 활성/비활성이 되었는가
        if (activationSwitch != null && !Activated && activationSwitch.Object != null && activationSwitch.Object.IsValid && activationSwitch.isOn)
        {
            Activated = true;
            ApplyAppearance();
        }
        
        if (UsesRangedAttack) //총알을 쏘는가
            AdvanceBullet();

        //없어지는 중이라면 리스폰 타이머 설정
        if (RemovalTimer.Expired(Runner) && (movingObject == null || !movingObject.RobotAirborne))
        {
            RemovalTimer = default;
            if (!disableRespawn)
                RespawnTimer = spawner != null ? spawner.StartRespawnWait(Runner) : TickTimer.CreateFromSeconds(Runner, RobotSpawner.DefaultDelay);
        }

        //없어졌다면 세팅 값 초기화
        if (RespawnTimer.Expired(Runner))
        {
            MoveToSpawn();
            Crushed = false;
            RespawnTimer = default;
            HitFlash = default;

            AimTimer = default;
            AttackCooldown = default;
            Health = Mathf.Max(1, maxHealth);
            GoingRight = spawner == null || !spawner.StartPatrolLeft;
            FacingLeft = !GoingRight;
            State = RobotState.Idle;
            return;
        }

        //외부 요인에 의해 로봇이 죽을 경우(압착기라던가)
        if (!SuppressBody && encounter != null && encounter.CrusherHits(transform.position))
        {
            BeginRemoval(); //사라지기 시작
            return;
        }

        if (!IsActive)
        {
            AimTimer = default;
            return;
        }

        //누군가 로봇을 움직인다면 (개구리나 곰이)
        if (movingObject != null && (movingObject.IsMoving || movingObject.RobotAirborne))
        {
            AimTimer = default;
            State = RobotState.Idle;
            return;
        }

        if (movingObject != null && movingObject.IsHeld)
        {
            AimTimer = default;
            State = !IsTongueHeld ? RobotState.Idle :
                HeldStruggle.ExpiredOrNotRunning(Runner) ? RobotState.Held : RobotState.Struggling;
            return;
        }



        UpdateMovement();
    }

    private void MoveToSpawn()
    {
        if (TryGetComponent(out NetworkTransform networkTransform))
            networkTransform.Teleport(spawnPosition);
        else
            transform.position = spawnPosition;
        if (TryGetComponent(out Rigidbody2D body))
            body.position = spawnPosition;
    }

    protected abstract void UpdateMovement();

    // 조준과 발사 순서
    // 1. 표적이 없거나 총알이 이미 날고 있거나 공격 대기 중이면 조준을 취소합니다.
    // 2. delta에 총이 달린 어깨부터 표적까지의 방향과 거리를 담습니다. 지형에 막히면 조준을 취소합니다.
    // 3. 조준 시간을 처음 한 번만 시작합니다. 시간이 끝나면 그 순간의 방향으로 총알을 만들고 다음 공격 대기 시간을 시작합니다.
    // 4. true를 돌려주면 지상 로봇이 이번 틱에는 이동을 건너뜁니다.
    protected bool UpdateRangedAttack(PlayerHealth target)
    {
        if (target == null || BulletActive || !AttackCooldown.ExpiredOrNotRunning(Runner))
        {
            AimTimer = default;
            return false;
        }

        if (!TryGetShot(target, out Vector2 shoulder, out Vector2 delta, out bool targetOnLeft) ||
            delta.sqrMagnitude < 0.001f)
        {
            AimTimer = default;
            return false;
        }

        FacingLeft = targetOnLeft;
        State = RobotState.Aim;
        AimDirection = delta.normalized;

        if (!AimTimer.IsRunning)
            AimTimer = TickTimer.CreateFromSeconds(Runner, aimSeconds);

        else if (AimTimer.Expired(Runner))
        {
            BulletPosition = shoulder;
            BulletDirection = AimDirection;
            float lifetime = 1.4f;
            if (target.IsHacking && bulletSpeed > 0f)
                lifetime = Mathf.Max(lifetime, delta.magnitude / bulletSpeed + Runner.DeltaTime * 2f);
            BulletLife = TickTimer.CreateFromSeconds(Runner, lifetime);
            BulletActive = true;
            AimTimer = default;
            AttackCooldown = TickTimer.CreateFromSeconds(Runner, 1f);
            AdvanceBullet(muzzleDistance);
        }

        return true;
    }

    private void AdvanceBullet(float initialStep = 0f)
    {
        if (!BulletActive)
            return;

        if (BulletLife.Expired(Runner))
        {
            BulletActive = false;
            return;
        }

        float distance = initialStep > 0f ? initialStep : bulletSpeed * Runner.DeltaTime;

        if (Physics2D.Raycast(BulletPosition, BulletDirection, bulletFilter, bulletHits, distance) > 0)
        {
            Collider2D hit = bulletHits[0].collider;
            if (hit.GetComponentInParent<PlayerHealth>() is PlayerHealth player)
                player.RPC_TakeHit(this);

            BulletActive = false;
            return;
        }

        BulletPosition += BulletDirection * distance;
    }

    private readonly RaycastHit2D[] bulletHits = new RaycastHit2D[1];

    protected static Vector2 GetTargetPoint(PlayerHealth target) =>
        target.TryGetComponent(out Collider2D body) && body.bounds.size.sqrMagnitude > 0f
            ? (Vector2)body.bounds.center : (Vector2)target.transform.position + Vector2.up * 0.5f;

    protected PlayerHealth FindTarget()
    {
        PlayerHealth best = null;
        float closest = float.PositiveInfinity;
        int bestPriority = int.MaxValue;

        foreach (var obj in NetworkGameManager.GetPlayerObjects(Runner))
        {
            //살아있는 플레이어만 후보자로 설정
            //TO DO : 현재 플레이어 한 명이 죽으면 다같이 리스폰이라 이 부분 없애기
            if (!obj.TryGetComponent(out PlayerHealth candidate) || candidate.Health <= 0)
                continue;

            Vector2 delta;
            if (UsesRangedAttack)
            {
                if (!TryGetShot(candidate, out _, out delta, out _))
                    continue;
            }
            else
            {
                delta = obj.transform.position - transform.position;
                if (Mathf.Abs(delta.y) > 1.4f)
                    continue;
            }

            float distance = delta.magnitude;
            if (!candidate.IsHacking && distance > detectionRange)
                continue;

            // 사격할 수 있는 해킹 대상을 먼저 고르고, 일반 대상은 기존 근거리 우선순위로 비교합니다.
            int priority = candidate.IsHacking ? 0 : UsesRangedAttack && distance > closeTargetRange ? 2 : 1;
            if (priority < bestPriority || (priority == bestPriority && distance < closest))
            {
                closest = distance;
                best = candidate;
                bestPriority = priority;
            }
        }
        return best;
    }

    protected void TakeGroundImpact()
    {
        if (!HasStateAuthority || SuppressBody || Health <= 0)
            return;

        Health--;
        if (Health == 0)
            BeginRemoval();
        else
            HitFlash = TickTimer.CreateFromSeconds(Runner, FlashSeconds);
    }

    internal bool CanBeHitByRock => Object != null && Object.IsValid && !SuppressBody && Health > 0 &&
        (movingObject == null || !movingObject.IsHeld || IsTongueHeld);

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    internal void RPC_HitByRock(Rock rock, RpcInfo info = default)
    {
        if (rock == null || rock.Object == null || !rock.Object.IsValid ||
            (!Runner.IsSinglePlayer && info.Source != rock.Object.StateAuthority) || !CanBeHitByRock)
            return;

        if (RequiresCrushing)
            HitFlash = TickTimer.CreateFromSeconds(Runner, FlashSeconds);
        else
            BeginRemoval();
    }

    /// <summary>
    /// 압착기에 의해 또는 떨어져서 땅바닥에 박힐 경우
    /// </summary>
    internal void DefeatOnLanding()
    {
        if (Object != null && Object.IsValid && HasStateAuthority)
            BeginRemoval();
    }


    internal void HandleOutOfBounds()
    {
        if (IsCrushed)
            return;
        if (RequiresCrushing)
            MoveToSpawn();
        else
            DefeatOnLanding();
    }

    public void Crush()
    {
        if (Object != null && Object.IsValid)
            RPC_Crush();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_Crush() => BeginRemoval();

    /// <summary>
    /// 죽기 전에 초기화
    /// </summary>
    private void BeginRemoval()
    {
        if (IsCrushed) //압착기로 죽는 경우 예외
            return;

        Health = 0;
        Crushed = true;

        movingObject?.ReleaseRobotForRemoval();
        AimTimer = default;
        BulletActive = false;
        State = RobotState.Dead;
        HitFlash = TickTimer.CreateFromSeconds(Runner, FlashSeconds);
        RemovalTimer = TickTimer.CreateFromSeconds(Runner, RemovalDuration);
    }

    public override void Render() => ApplyAppearance();

    protected virtual bool FlipModel => false;

    /// <summary>
    /// 네트워크 렌더링
    /// </summary>
    private void ApplyAppearance()
    {
        if (movingObject == null && hitbox != null)
            hitbox.enabled = startsWithHitbox && !SuppressBody;

        float flash = Mathf.Clamp01((HitFlash.RemainingTime(Runner) ?? 0f) / FlashSeconds);
        float removal = RemovalTimer.RemainingTime(Runner) ?? 0f;
        bool falling = IsCrushed && movingObject != null && movingObject.RobotAirborne;
        bool removing = Crushed && (removal > 0f || falling);

        bool visible = HasAppeared && (!IsCrushed || (removing && (flash > 0f || Mathf.FloorToInt((RemovalDuration - removal) * 14f) % 2 == 0)));

        if (spawner != null)
            spawner.ShowWaiting(spawnerSlot, FirstSpawnTimer.IsRunning || RespawnTimer.IsRunning);

        if (model != null)
        {
            if (model.gameObject != gameObject)
            {
                bool inUse = !RuntimePooled || (HasAppeared && (!IsCrushed || removing));

                if (model.gameObject.activeSelf != inUse)
                    model.gameObject.SetActive(inUse);
            }

            model.enabled = visible; //모델 활성화

            if (robotAnimator != null || FlipModel)
                model.flipX = FacingLeft;

            //깜박이는 연출
            model.color = Color.Lerp(modelColor, HitColor, flash);

        }

        if (robotAnimator != null && robotAnimator.isActiveAndEnabled)
            robotAnimator.SetInteger("State", (int)State);

        if (!UsesRangedAttack)
            return;

        if (gunArm != null)
        {
            gunArm.enabled = visible && !removing && (movingObject == null || !movingObject.IsHeld);
            gunArm.color = Color.Lerp(armColor, HitColor, flash);
        }

        //조준에 따른 팔의 회전
        if (gunArm != null && gunArm.enabled)
        {
            Vector2 facing = FacingLeft ? Vector2.left : Vector2.right;
            Vector2 direction = State == RobotState.Aim && AimDirection.sqrMagnitude > 0f ? AimDirection : facing;

            gunArm.transform.localPosition = GetShoulderOffset(FacingLeft);
            gunArm.flipY = direction.x < 0f;
            gunArm.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
        }

        bulletVisual.enabled = BulletActive;
        if (BulletActive)
        {
            bulletVisual.transform.position = new Vector3(BulletPosition.x, BulletPosition.y, transform.position.z);
            bulletVisual.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(BulletDirection.y, BulletDirection.x) * Mathf.Rad2Deg);
        }
    }


    protected void OnTriggerStay2D(Collider2D other)
    {
        if (Object == null || !Object.IsValid || !HasStateAuthority || UsesRangedAttack || !IsActive ||
            (movingObject != null && (movingObject.IsHeld || movingObject.IsMoving || movingObject.RobotAirborne)))
            return;

        var body = other.attachedRigidbody;
        if (body == null || !body.CompareTag("Player") || !body.TryGetComponent(out PlayerHealth target))
            return;

        TryContactAttack(target);
    }

    protected void TryContactAttack(PlayerHealth target)
    {
        if (!AttackCooldown.ExpiredOrNotRunning(Runner))
            return;

        target.RPC_TakeHit(this);
        AttackCooldown = TickTimer.CreateFromSeconds(Runner, 1f);
    }

}

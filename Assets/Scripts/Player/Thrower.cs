using Fusion;
using UnityEngine;
using static IMoveable;

public class Thrower : Interactor
{
    [SerializeField] private Mover mover;
    [SerializeField] private BearCarryPresentation carryPresentation;
    [SerializeField] private SpriteRenderer model; //모델이 바라보는 방향에 따라 던지기 때문에 참조

    [Header("던지기 설정")]
    [SerializeField] private float minDistance = 1f;
    [SerializeField] private float maxDistance = 3f;
    [SerializeField] private float minAngle = 0f;
    [SerializeField, Range(-89f, 0f)] private float rockMinAngle = -25f;
    [SerializeField] private float maxAngle = 60f;
    [SerializeField] private float throwSpeed = 5f;
    [SerializeField] private float cycleTime = 2f;

    [Header("미리보기")]
    [SerializeField] private Transform throwArrow;
    [SerializeField] private Transform puppetKeyHoldPoint;
    [SerializeField] private float arrowBaseLength = 1f;

    private SpriteRenderer arrowSprite;
    private Vector3 arrowOriginalScale;
    private Vector3 arrowOriginalPosition;
    private Vector3 originalKeyHoldPosition;
    private Vector3 originalHoldPosition; //애니메이션 플립에 대비

    [Networked] private float ThrowDistance { get; set; }
    [Networked] private float ThrowAngle { get; set; }
    private float aimDirection;

    private float aimTimer;
    [SerializeField] private RockMaker rockMaker;
    private bool waitingForHold;
    private bool wasThrowing;
    private bool wasGrabbingAlly;
    private static readonly int GrabState = Animator.StringToHash("Base Layer.Bear_Grab");
    private static readonly int ThrowState = Animator.StringToHash("Base Layer.Bear_Throw");
    [Networked] private TickTimer ThrowRecovery { get; set; }
    public bool LocksMovement => waitingForHold || aimState != AimState.None || LiftedAlly != null ||
        !ThrowRecovery.ExpiredOrNotRunning(Runner) || (rockMaker != null && rockMaker.IsPreparing);
    [Networked] private NetworkBool IsAiming { get; set; }
    private enum AimState
    {
        None,
        Distance,
        Angle
    }

    private AimState aimState = AimState.None;
    private MovingObject curMovingObject;
    [Networked] public MovingObject CarriedKey { get; private set; }
    [Networked] private Passenger LiftedAlly { get; set; }
    [Networked] private NetworkBool HoldingRock { get; set; }
    private float MinimumAimAngle => HoldingRock ? rockMinAngle : minAngle;
    private float keyHoldDeadline;

    public void TryCarryKey(MovingObject key)
    {
        if (!HasStateAuthority || CarriedKey != null || LiftedAlly != null ||
            curMovingObject != null || key == null || holdPoint == null)
            return;

        CarriedKey = key;
        keyHoldDeadline = Time.time + 3f;
        key.RPC_RequestHold(this);
    }

    public void TryLift(Passenger passenger)
    {
        if (!HasStateAuthority || CarriedKey != null || LiftedAlly != null ||
            curMovingObject != null || passenger == null || passenger.IsCarried ||
            !passenger.CanBeLifted || holdPoint == null)
            return;

        LiftedAlly = passenger;
        passenger.RPC_Lift(this);
        BeginAim();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_ClearLift(Passenger passenger, RpcInfo info = default)
    {
        if (passenger != null && LiftedAlly == passenger && (Runner.IsSinglePlayer || info.Source == passenger.Object.StateAuthority))
            LiftedAlly = null;
    }

    private float throwRecoverySeconds = 0.45f;
    [SerializeField] private Animator anim; 

    #region Cycle
    private void Awake()
    {
        if (anim != null && anim.runtimeAnimatorController != null)
        {
            foreach (var clip in anim.runtimeAnimatorController.animationClips)
                if (clip.name == "Bear_Throw")
                    throwRecoverySeconds = clip.length;
        }
        if (rockMaker == null)
            rockMaker = GetComponentInChildren<RockMaker>(true);
        if (holdPoint != null)
            originalHoldPosition = holdPoint.localPosition;

        if (puppetKeyHoldPoint != null)
            originalKeyHoldPosition = puppetKeyHoldPoint.localPosition;

        if (throwArrow != null)
        {
            arrowSprite = throwArrow.GetComponent<SpriteRenderer>();
            arrowOriginalScale = throwArrow.localScale;
            arrowOriginalPosition = throwArrow.localPosition;
            throwArrow.gameObject.SetActive(false);
        }

        interactWithClick = true;
        interactWithRange = true;
    }

    public override void Update()
    {
        if (Object == null || !Object.IsValid || !HasStateAuthority)
            return;

        if (CarriedKey != null && (CarriedKey.GetComponent<Key>().IsConsumed ||
            (CarriedKey.Holder != this && Time.time >= keyHoldDeadline)))
            CarriedKey = null;

        if ((rockMaker != null && rockMaker.IsPreparing) || !ThrowRecovery.ExpiredOrNotRunning(Runner))
            return;

        //조준 중이라면 두 번째 클릭에 던지기
        if (aimState == AimState.None)
        {
            if (waitingForHold && curMovingObject == null)
                CancelAim();

            if (!waitingForHold)
                base.Update();
            return;
        }

        if (!HoldingRock && curMovingObject == null && LiftedAlly == null)
        {
            CancelAim();
            return;
        }

        aimTimer += Time.deltaTime;

        float progress = Mathf.PingPong(aimTimer / Mathf.Max(0.01f, cycleTime), 1f);
        if (aimState == AimState.Distance)
            ThrowDistance = Mathf.Lerp(minDistance, maxDistance, progress);
        else
            ThrowAngle = Mathf.Lerp(MinimumAimAngle, maxAngle, progress);

        //클릭 시 현재 단계 확정
        if (WorldClickPressed())
            ConfirmAim();
    }

    protected override void TryIntetact()
    {
        if (CarriedKey != null)
            return;

        if (curClick == null && Camera.main != null && rockMaker != null && rockMaker.TryMakeRock(mousePos))
            return;

        base.TryIntetact();
    }

    // 제작과 조준 중에는 새로 집을 물체를 표시하지 않습니다.
    protected override bool CanShowInteractionOutline => !waitingForHold && aimState == AimState.None &&
        CarriedKey == null && LiftedAlly == null && (rockMaker == null || !rockMaker.IsPreparing);

    protected override void LateUpdate()
    {
        base.LateUpdate();
        if (Object == null || !Object.IsValid)
            return;

        UpdateHoldPoint();
        if (aimState != AimState.None)
            aimDirection = FacesLeft() ? -1f : 1f;

        if (IsAiming)
            UpdateArrow();
        else
            HideArrow();
    }

    private bool FacesLeft() => mover != null ? mover.FacesLeftNow : model != null && model.flipX;

    private void UpdateHoldPoint()
    {
        if (holdPoint == null)
            return;

        holdPoint.localPosition = FaceOffset(originalHoldPosition);
        if (puppetKeyHoldPoint != null)
        {
            Vector3 keyPosition = originalKeyHoldPosition;
            if (FacesLeft())
                keyPosition.x = -keyPosition.x;
            puppetKeyHoldPoint.localPosition = keyPosition;
        }
    }

    internal Transform PuppetKeyHoldPoint => puppetKeyHoldPoint != null ? puppetKeyHoldPoint : holdPoint;

    private Vector3 FaceOffset(Vector3 position)
    {
        position.x = Mathf.Abs(position.x) * (FacesLeft() ? -1f : 1f);
        return position;
    }

    private void OnDisable()
    {
        HideArrow();
    }
    #endregion

    #region Throw
    public void TryThrow(MovingObject movingObject)
    {
        if (!HasStateAuthority || movingObject == null || !movingObject.AllowsHolder(this) || holdPoint == null ||
            CarriedKey != null || LiftedAlly != null || waitingForHold || aimState != AimState.None)
            return;

        curMovingObject = movingObject;
        waitingForHold = true;
        movingObject.RPC_RequestHold(this);
    }

    // 물체가 손에 들어왔다는 응답을 받고 나서야 조준을 시작합니다. 다른 물체의 응답이나 거절된 요청은 조준 상태로 넘어가지 않습니다.
    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    public void RPC_HoldResult(MovingObject movingObject, bool accepted, RpcInfo info = default)
    {
        if (movingObject == null || (!Runner.IsSinglePlayer && info.Source != movingObject.Object.StateAuthority))
            return;

        if (movingObject == CarriedKey)
        {
            if (!accepted)
                CarriedKey = null;
            return;
        }

        if (!waitingForHold || curMovingObject != movingObject)
            return;

        waitingForHold = false;
        if (!accepted)
        {
            curMovingObject = null;
            return;
        }

        BeginAim();
    }

    internal void BeginRockAim()
    {
        HoldingRock = true;
        BeginAim();
    }

    private void BeginAim()
    {
        aimDirection = FacesLeft() ? -1f : 1f;
        ThrowDistance = minDistance;
        ThrowAngle = MinimumAimAngle;
        aimTimer = 0f;
        aimState = AimState.Distance;
        IsAiming = true;
        UpdateArrow();
    }

    private void Throw()
    {
        aimDirection = FacesLeft() ? -1f : 1f;
        UpdateArrow();
        Vector2 origin = arrowSprite != null && arrowSprite.sprite != null
            ? throwArrow.TransformPoint(arrowSprite.sprite.bounds.center)
            : GetArrowAnchor();
        float range = aimDirection * ThrowDistance;
        if (HoldingRock)
        {
            rockMaker.ThrowRock(origin, range, ThrowAngle, throwSpeed);
        }
        else
        {
            Vector2 destination = origin + Vector2.right * range;
            float height = GetArcHeight();
            if (LiftedAlly != null)
                LiftedAlly.RPC_Throw(this, origin, destination, throwSpeed, height);
            else if (curMovingObject != null)
                curMovingObject.RPC_RequestMove(this, MoveType.Parabola, origin, destination, throwSpeed, height);
        }
        CancelAim();
        ThrowRecovery = TickTimer.CreateFromSeconds(Runner, throwRecoverySeconds);
    }

    internal bool HasAllyCarryPose(MyEnum.CharacterType character) =>
        carryPresentation != null && carryPresentation.HasAllyPose(character);

    public override void Render()
    {
        bool throwing = !ThrowRecovery.ExpiredOrNotRunning(Runner);
        var ally = LiftedAlly;
        bool grabbingAlly = !throwing && ally != null && ally.CurrentLifter == this;
        bool showGrab = !throwing && (grabbingAlly || IsAiming || (CarriedKey != null && CarriedKey.Holder == this));
        if (carryPresentation != null)
        {
            var allyType = grabbingAlly ? ally.BearCarryCharacter : MyEnum.CharacterType.None;
            carryPresentation.Show(showGrab && ally == null, HoldingRock, allyType, FacesLeft());
        }

        if (throwing && !wasThrowing)
            SoundManager.Play(SoundEffect.Throw);

        if (anim != null)
        {
            anim.SetBool("IsGrab", showGrab);
            anim.SetBool("IsThrowing", throwing);
            // 모든 화면에서 네트워크 던지기 상태가 시작할 때 첫 프레임부터 재생합니다.
            if (throwing && !wasThrowing)
                anim.Play(ThrowState, 0, 0f);
            else if (grabbingAlly && !wasGrabbingAlly)
                anim.Play(GrabState, 0, 0f);
        }
        wasThrowing = throwing;
        wasGrabbingAlly = grabbingAlly;
    }

    private Vector3 GetArrowAnchor()
    {
        if (throwArrow == null)
            return holdPoint.position;

        Vector3 position = FaceOffset(arrowOriginalPosition);
        return throwArrow.parent != null ? throwArrow.parent.TransformPoint(position) : position;
    }

    /// <summary>
    /// 던지는 각도에 따른 포물선 높이 계산
    /// </summary>
    /// <returns></returns>
    private float GetArcHeight()
    {
        float rad = Mathf.Clamp(ThrowAngle, 0f, 89f) * Mathf.Deg2Rad;

        return ThrowDistance * Mathf.Tan(rad) / 4f;
    }

    #endregion

    #region Aim
    /// <summary>
    /// 클릭할 때마다 조준 단계 전환
    /// </summary>
    private void ConfirmAim()
    {
        switch (aimState)
        {
            case AimState.Distance: //현재 거리 확정 후 각도 조절 시작
                aimState = AimState.Angle;
                aimTimer = 0f;
                ThrowAngle = MinimumAimAngle;
                break;

            case AimState.Angle: //현재 각도 확정 후 던지기
                Throw();
                break;
        }
    }
    
    /// <summary>
    /// 조준 단계 해제
    /// </summary>
    private void CancelAim()
    {
        curMovingObject = null;
        HoldingRock = false;
        waitingForHold = false;
        aimState = AimState.None;
        IsAiming = false;
        aimTimer = 0f;
        HideArrow();
    }

    private void UpdateArrow()
    {
        if (throwArrow == null || holdPoint == null)
            return;

        if (!throwArrow.gameObject.activeSelf)
            throwArrow.gameObject.SetActive(true);

        throwArrow.position = GetArrowAnchor();

        //각도 조절
        float angle = FacesLeft() ? 180f - ThrowAngle : ThrowAngle;

        throwArrow.rotation = Quaternion.Euler(0f, 0f, angle);

        //X축 스케일로 길이 조절
        float scaleX = ThrowDistance / Mathf.Max(0.01f, arrowBaseLength);

        throwArrow.localScale = new Vector3(arrowOriginalScale.x * scaleX, arrowOriginalScale.y, arrowOriginalScale.z);
    }


    private void HideArrow()
    {
        if (throwArrow != null)
            throwArrow.gameObject.SetActive(false);
    }
    #endregion

}

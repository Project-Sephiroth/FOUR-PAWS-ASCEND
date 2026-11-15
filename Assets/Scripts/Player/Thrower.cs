using Fusion;
using UnityEngine;
using static IMoveable;

public class Thrower : Interactor
{
    [SerializeField] private Mover mover;
    [SerializeField] private SpriteRenderer model; //모델이 바라보는 방향에 따라 던지기 때문에 참조

    [Header("던지기 설정")]
    [SerializeField] private float minDistance = 1f;
    [SerializeField] private float maxDistance = 3f;
    [SerializeField] private float minAngle = 0f;
    [SerializeField] private float maxAngle = 60f;
    [SerializeField] private float throwSpeed = 5f;
    [SerializeField] private float cycleTime = 2f;

    [Header("미리보기")]
    [SerializeField] private Transform throwArrow;
    [SerializeField] private float arrowBaseLength = 1f;

    private Vector3 arrowOriginalScale;
    private Vector3 originalHoldPosition; //애니메이션 플립에 대비

    private float throwDistance;
    private float throwAngle;
    private float aimDirection;

    private float aimTimer;
    [SerializeField] private RockMaker rockMaker;
    private bool waitingForHold;
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
            !passenger.CanBeCarried || holdPoint == null)
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

        if (throwArrow != null)
        {
            arrowOriginalScale = throwArrow.localScale;
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

        if (curMovingObject == null && LiftedAlly == null)
        {
            CancelAim();
            return;
        }

        aimTimer += Time.deltaTime;

        switch (aimState)
        {
            case AimState.Distance:
                UpdateDistance();
                break;

            case AimState.Angle:
                UpdateAngle();
                break;
        }

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

        if (Object == null || !Object.IsValid || !HasStateAuthority)
            return;

        if (aimState != AimState.None)
            UpdateArrow();
    }

    private bool FacesLeft() => mover != null ? mover.FacesLeftNow : model != null && model.flipX;

    private void UpdateHoldPoint()
    {
        if (holdPoint == null)
            return;

        Vector3 pos = originalHoldPosition;
        pos.x = Mathf.Abs(pos.x) * (FacesLeft() ? -1f : 1f);
        holdPoint.localPosition = pos;
    }

    private void OnDisable()
    {
        HideArrow();
    }
    #endregion

    #region Throw
    public void TryThrow(MovingObject movingObject)
    {
        if (!HasStateAuthority || movingObject == null || holdPoint == null ||
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

    private void BeginAim()
    {
        bool facesLeft = mover != null ? mover.FacesLeftNow : model != null && model.flipX;
        aimDirection = facesLeft ? -1f : 1f;
        throwDistance = minDistance;
        throwAngle = minAngle;
        aimTimer = 0f;
        aimState = AimState.Distance;
        IsAiming = true;
        UpdateArrow();
    }

    private void Throw()
    {
        aimDirection = FacesLeft() ? -1f : 1f;
        UpdateHoldPoint();
        if (LiftedAlly != null)
            LiftedAlly.RPC_Throw(this, GetDestination(), throwSpeed, GetArcHeight());
        else if (curMovingObject != null)
            curMovingObject.RPC_RequestMove(this, MoveType.Parabola, GetDestination(), throwSpeed, GetArcHeight());
        CancelAim();
        ThrowRecovery = TickTimer.CreateFromSeconds(Runner, throwRecoverySeconds);
        RPC_PlayThrow();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_PlayThrow()
    {
        if (anim != null)
            anim.SetTrigger("Throw");
    }

    public override void Render()
    {
        if (anim != null)
            anim.SetBool("IsGrab", IsAiming || CarriedKey != null || LiftedAlly != null);
    }

    private void UpdateDistance()
    {
        float duration = Mathf.Max(0.01f, cycleTime);

        float t = Mathf.PingPong(aimTimer / duration, 1f);

        throwDistance = Mathf.Lerp(minDistance, maxDistance, t);
    }

    private void UpdateAngle()
    {
        float duration = Mathf.Max(0.01f, cycleTime);

        float t = Mathf.PingPong(aimTimer / duration, 1f);

        throwAngle = Mathf.Lerp(minAngle, maxAngle, t);
    }

    /// <summary>
    /// 잡고 있는 오브젝트가 던져질 위치를 계산
    /// </summary>
    /// <returns></returns>
    private Vector2 GetDestination()
    {
        Vector2 startPos = holdPoint.position;

        return startPos + new Vector2(aimDirection * throwDistance, 0f);
    }

    /// <summary>
    /// 던지는 각도에 따른 포물선 높이 계산
    /// </summary>
    /// <returns></returns>
    private float GetArcHeight()
    {
        float rad = Mathf.Clamp(throwAngle, 0f, 89f) * Mathf.Deg2Rad;

        return throwDistance * Mathf.Tan(rad) / 4f;
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
                throwAngle = minAngle;
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

        //화살표 꼬리를 손 위치에 고정
        throwArrow.position = holdPoint.position;

        //각도 조절
        float angle = aimDirection > 0f ? throwAngle : 180f - throwAngle;

        throwArrow.rotation = Quaternion.Euler(0f, 0f, angle);

        //X축 스케일로 길이 조절
        float scaleX = throwDistance / Mathf.Max(0.01f, arrowBaseLength);

        throwArrow.localScale = new Vector3(arrowOriginalScale.x * scaleX, arrowOriginalScale.y, arrowOriginalScale.z);
    }


    private void HideArrow()
    {
        if (throwArrow != null)
            throwArrow.gameObject.SetActive(false);
    }
    #endregion

}

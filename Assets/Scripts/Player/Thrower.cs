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
    [SerializeField] private float maxAngle = 45f;
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
    private enum AimState
    {
        None,
        Distance,
        Angle
    }

    private AimState aimState = AimState.None;
    private MovingObject selectedObject;

    [SerializeField] private Animator anim; 

    #region Cycle
    private void Awake()
    {
        if (holdPoint != null)
            originalHoldPosition = holdPoint.localPosition;

        if (throwArrow != null)
        {
            arrowOriginalScale = throwArrow.localScale;
            throwArrow.gameObject.SetActive(false);
        }
    }

    public override void Update()
    {
        if (Object == null || !Object.IsValid || !HasStateAuthority)
            return;

        //조준 중이라면 두 번째 클릭에 던지기
        if (aimState == AimState.None)
        {
            base.Update();
            return;
        }

        if (selectedObject == null)
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
        if (Input.GetMouseButtonDown(0))
            ConfirmAim();
    }

    private void LateUpdate()
    {
        if (holdPoint != null && model != null)
        {
            Vector3 pos = originalHoldPosition;

            if (model.flipX)
                pos.x = -pos.x;

            holdPoint.localPosition = pos;
        }

        if (Object == null || !Object.IsValid || !HasStateAuthority)
            return;

        if (aimState != AimState.None)
            UpdateArrow();
    }

    private void OnDisable()
    {
        HideArrow();
    }
    #endregion

    #region Throw
    public void TryThrow(MovingObject movingObject)
    {
        //권한이 없거나 던질 오브젝트가 없으면 리턴
        if (!HasStateAuthority || movingObject == null)
            return;

        //조준 중이거나 이미 잡고 이동 중인 오브젝트라면 던지기 시도 X
        if (aimState != AimState.None || movingObject.IsMoving || movingObject.IsHeld)
            return;

        if (mover != null) //들고 있을 때 이동 제한
        {
            mover.CanMove = false;
            mover.CanJump = false;
        }

        selectedObject = movingObject;

        aimDirection = model.flipX ? -1f : 1f;

        throwDistance = minDistance;
        throwAngle = minAngle;

        aimTimer = 0f;
        aimState = AimState.Distance;

        // 물체를 곰의 손에 들도록 요청
        TryHold(movingObject.GetComponent<NetworkObject>());

        if (anim != null)
            anim.SetBool("IsGrab", true);

        UpdateArrow();
    }

    private void Throw()
    {
        selectedObject.RPC_RequestMove(MoveType.Parabola, GetDestination(), throwSpeed, GetArcHeight());
        selectedObject.RPC_RequestRelease();

        CancelAim();

        if (anim != null)
            anim.SetTrigger("Throw");

        if (mover != null) //이동 재설정
        {
            mover.CanMove = true;
            mover.CanJump = true;
        }
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
        float rad = throwAngle * Mathf.Deg2Rad;

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
    /// <param name="releaseObject"></param>
    private void CancelAim(bool releaseObject = true)
    {
        if (releaseObject && selectedObject != null && Object != null && Object.IsValid)
            selectedObject.RPC_RequestRelease();

        selectedObject = null;
        aimState = AimState.None;
        aimTimer = 0f;

        HideArrow();

        if (anim != null)
            anim.SetBool("IsGrab", false);
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

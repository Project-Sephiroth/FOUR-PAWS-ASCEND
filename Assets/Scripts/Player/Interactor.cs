using Fusion;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 가까운 범위 내의 오브젝트를 클릭 시 상호작용을 시도합니다.
/// </summary>
public class Interactor : NetworkBehaviour
{
    protected bool interactWithClick = true;
    protected bool interactWithRange = true;

    [SerializeField] private InteractRange interactRange;
    [SerializeField] private LayerMask detectLayer;

    protected GameObject curClick;
    private GameObject curRange;
    private GameObject curHover;

    protected IInteractable curInteratcble;
    protected Vector2 mousePos;

    [SerializeField] protected Transform holdPoint;
    public Transform HoldPoint => holdPoint;

    [Networked]
    public NetworkObject CurInteractObject { get; set; }

    public override void Spawned()
    {
        if (!HasStateAuthority)
            return;

        if (interactRange != null)
            interactRange.OnDetect += OnDetect;
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (interactRange != null)
            interactRange.OnDetect -= OnDetect;
    }

    public virtual void Update()
    {
        if (Object == null || !Object.IsValid || !HasStateAuthority)
            return;

        if (Input.GetMouseButtonDown(0))
            OnClick();

        TryIntetact();
    }

    /// <summary>
    /// 모든 IInteractable의 외곽선 상태 변경
    /// </summary>
    private void SetOutline(GameObject target, bool enabled)
    {
        if (target == null)
            return;

        IInteractable interactable = target.GetComponent<IInteractable>();

        interactable?.SetOutlineMat(enabled);
    }

    /// <summary>
    /// 마우스로 클릭한 월드 오브젝트를 감지합니다.
    /// </summary>
    protected virtual void OnClick()
    {
        mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);

        Collider2D hit = Physics2D.OverlapPoint(mousePos, detectLayer);

        if (hit == null)
            return;

        Debug.Log($"Interactor: {hit.gameObject.name} 을 클릭");

        curClick = hit.gameObject;
    }

    /// <summary>
    /// 상호작용 조건을 확인하고 모든 IInteractable을 호출합니다.
    /// </summary>
    protected virtual void TryIntetact()
    {
        if (!HasStateAuthority)
            return;

        if (!Input.GetMouseButtonDown(0))
            return;

        GameObject target = null;

        //범위 감지 및 클릭이 모두 필요할 경우
        if (interactWithRange && interactWithClick && curClick == curRange)
            target = curClick;
        else
            target = curClick;

        if (target == null)
            return;

        IInteractable interactable = target.GetComponent<IInteractable>();

        CurInteractObject = target.GetComponent<NetworkObject>();

        if (interactable == null)
            return;

        curInteratcble = interactable;

        curInteratcble.OnInteract(this);

        Debug.Log($"Interactor 가 {target.name} 에 OnInteract");
    }

    protected virtual void TryHold(NetworkObject target)
    {
        if (target == null)
            return;

        CurInteractObject = target;

        CurInteractObject.transform.SetParent(holdPoint, true);
        CurInteractObject.transform.position = holdPoint.position;
    }

    protected virtual void ReleaseHold()
    {
        if (CurInteractObject == null)
            return;

        CurInteractObject.transform.SetParent(null, true);
    }

    protected void OnDetect(GameObject detectTarget)
    {
        if (detectTarget == curRange)
            return;

        GameObject previous = curRange;
        curRange = detectTarget;

        //마우스가 올라가 있다면 외곽선 유지
        if (previous != null && previous != curHover)
            SetOutline(previous, false);

        if (curRange != null)
            SetOutline(curRange, true);

        Debug.Log($"Interactor 에 감지 된 target 을 {detectTarget} 으로 설정");
    }

}

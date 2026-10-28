using Fusion;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 가까운 범위 내에 있는 오브젝트를 클릭 시 인터랙션을 시도합니다
/// </summary>
public class Interactor : NetworkBehaviour
{
    //클릭 or 범위 감지로 인터랙션을 시도
    protected bool interactWithClick = true;
    protected bool interactWithRange = true;

    [SerializeField] private InteractRange interactRange;
    [SerializeField] LayerMask detectLayer;

    private GameObject curClick; //현재 클릭한 대상
    private GameObject curRange; //현재 범위 내에 있는 대상

    public event UnityAction<Vector2> OnClick;

    private IInteractable curInteratcble;
    public IInteractable CurInteractable => curInteratcble;

    protected Vector2 mousePos;

    public override void Spawned()
    {
        if (!HasStateAuthority)
            return;

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

        GetMouseClickRay();
        TryIntetact();
    }

    /// <summary>
    /// 스크린의 마우스 포지션을 받아 얼드 내 게임 오브젝트를 반환합니다
    /// </summary>
    protected virtual void GetMouseClickRay()
    {
        if (!Input.GetMouseButtonDown(0))
            return;

        //이전 클릭 초기화
        curClick = null;

        if (Camera.main == null)
            return;

        mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);

        Collider2D hit = Physics2D.OverlapPoint(mousePos, detectLayer);

        if (hit != null)
            TryClick(hit.gameObject);
    }

    protected virtual void TryIntetact()
    {
        if (!HasStateAuthority)
            return;

        //클릭 타겟과 범위 타겟 둘 중 하나가 null 이면 return
        if (curClick == null || curRange == null)
            return;

        //상호작용 하는 데에 클릭과 범위 감지 모두 필요하다면
        if (interactWithClick && interactWithRange)
        {
            //클릭 타겟과 범위 타겟이 같을 경우에만 curInteractable 설정
            curInteratcble = curClick == curRange ? curClick.GetComponent<IInteractable>() : null;
        }
        else
        {
            if (interactWithRange)
                curInteratcble = curRange.GetComponent<IInteractable>();
            if (interactWithClick)
                curInteratcble = curClick.GetComponent<IInteractable>();
        }

        curInteratcble.OnInteract(this);
        Debug.Log($"Interactor 가 {curInteratcble} 에 OnInteract");

        //클릭 초기화
        curClick = null;
    }

    protected void TryClick(GameObject clickTarget) 
    { 
        IInteractable interactable = clickTarget.GetComponent<IInteractable>();

        if (interactable == null)
            return;

        curClick = clickTarget;

        Debug.Log($"Interactor 에 클릭 된 target 을 {clickTarget} 으로 설정");
    }

    protected void OnDetect(GameObject detectTarget)
    {
        //이미 캐싱 됐다면 리턴
        if (detectTarget == curRange)
            return;

        Debug.Log($"Interactor 에 감지 된 target 을 {detectTarget} 으로 설정");

        curRange = detectTarget;
    }
}

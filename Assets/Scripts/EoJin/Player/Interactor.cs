using Fusion;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 가까운 범위 내에 있는 오브젝트를 클릭 시 인터랙션을 시도합니다
/// </summary>
public class Interactor : NetworkBehaviour
{
    [SerializeField] private InteractRange interactRange;
    [SerializeField] LayerMask detectLayer;

    private GameObject curClick; //현재 클릭한 대상
    private GameObject curRange; //현재 범위 내에 있는 대상

    public event UnityAction<GameObject> OnTryInteract;

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

    public void Update()
    {
        if (Object == null || !Object.IsValid || !HasStateAuthority)
            return;

        GetMouseClickRay();
    }

    /// <summary>
    /// 스크린의 마우스 포지션을 받아 얼드 내 게임 오브젝트를 반환합니다
    /// </summary>
    void GetMouseClickRay()
    {
        if (!Input.GetMouseButtonDown(0))
            return;

        //이전 클릭 초기화
        curClick = null;

        if (Camera.main == null)
            return;

        Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);

        Collider2D hit = Physics2D.OverlapPoint(mousePos, detectLayer);

        if (hit != null)
            OnClick(hit.gameObject);

        TryIntetact();
    }

    void TryIntetact()
    {
        if (!HasStateAuthority)
            return;

        if (curClick == null)
            return;

        if (!interactRange.Contains(curClick))
            return;

        OnTryInteract?.Invoke(curRange);

        //클릭 초기화
        curClick = null;
    }

    void OnClick(GameObject clickTarget) 
    { 
        IInteractable interactable = clickTarget.GetComponent<IInteractable>();

        if (interactable == null)
            return;

        curClick = clickTarget;

        Debug.Log($"Interactor 에 클릭 된 target 을 {clickTarget} 으로 설정");
    }

    void OnDetect(GameObject detectTarget)
    {
        //이미 캐싱 됐다면 리턴
        if (detectTarget == curRange)
            return;

        Debug.Log($"Interactor 에 감지 된 target 을 {detectTarget} 으로 설정");

        curRange = detectTarget;
    }
}

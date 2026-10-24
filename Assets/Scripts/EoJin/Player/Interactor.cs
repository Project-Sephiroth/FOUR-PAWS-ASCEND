using Fusion;
using UnityEngine;
using UnityEngine.Events;

public class Interactor : NetworkBehaviour
{
    [SerializeField] private InteractRange interactRange;
    [SerializeField] LayerMask detectLayer;

    public GameObject curClick;
    public GameObject curRange;

    public event UnityAction<GameObject> OnTryInteract;

    private void Awake()
    {
        if (!HasStateAuthority)
            return;

        interactRange.OnDetect += OnDetect;
    }

    private void OnDestroy()
    {
        if (!HasStateAuthority)
            return;

        interactRange.OnDetect -= OnDetect;
    }

    public void Update()
    {
        if (!HasStateAuthority)
            return;

        GetMouseClickRay();
    }

    /// <summary>
    /// 스크린의 마우스 포지션을 받아 얼드 내 게임 오브젝트를 반환합니다
    /// </summary>
    void GetMouseClickRay()
    {
        if (!HasStateAuthority)
            return;

        if (Input.GetMouseButtonDown(0))
        {
            Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            Collider2D hit = Physics2D.OverlapPoint(mousePos, detectLayer);

            if (hit != null)
                OnClick(hit.gameObject);

            //클릭이 있을 때 상호작용 시도
            TryIntetact();
        }
    }

    void TryIntetact()
    {
        //현재 클릭한 개체와 닿아있는 개체가 다르면 리턴
        if (curClick != curRange)
            return;

        OnTryInteract(curRange);

        //클릭 초기화
        curClick = null;
    }

    void OnClick(GameObject clickTarget) 
    { 
        IInteractable interactable = clickTarget.GetComponent<IInteractable>();

        if (interactable == null)
            return;

        curClick = clickTarget;
    }

    void OnDetect(GameObject detectTarget)
    {
        //이미 캐싱 됐다면 리턴
        if (detectTarget == curRange)
            return;

        curRange = detectTarget;
    }
}

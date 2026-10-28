
using FourPaws.PCS.CircuitHack;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

[RequireComponent(typeof(Collider))]
public class PCS_CubeCircuitLauncher : MonoBehaviour
{
    [Header("P4 퍼즐 연결")]
    [SerializeField] private CircuitHackManager manager;
    [SerializeField] private CircuitStageLayout stage;

    [Header("P4 UI 표시 제어")]
    [SerializeField] private GameObject circuitOverlay;
    [SerializeField] private Canvas editableLayoutsCanvas;

    [Header("퍼즐 완료 후 실행")]
    [SerializeField] private UnityEvent onPuzzleCompleted;

    [Header("퍼즐 취소 시 실행")]
    [SerializeField] private UnityEvent onPuzzleCancelled;

    [Header("완료 후 UI 자동 닫기")]
    [SerializeField] private bool autoCloseOnComplete = true;

    private int activeRunId = -1;
    private bool waitingForResult;

    private GraphicRaycaster layoutsRaycaster;

    private void Awake()
    {
        if (editableLayoutsCanvas != null)
        {
            layoutsRaycaster =
                editableLayoutsCanvas.GetComponent<GraphicRaycaster>();
        }

        SetPuzzleVisible(false);
    }

    private void OnEnable()
    {
        if (manager == null) return;

        manager.OnHackCompleted += HandleCompleted;
        manager.OnHackCancelled += HandleCancelled;
    }

    private void OnDisable()
    {
        if (manager == null) return;

        manager.OnHackCompleted -= HandleCompleted;
        manager.OnHackCancelled -= HandleCancelled;
    }

    private void Start()
    {
        // 초기 데모 UI가 표시되지 않도록 정리
        if (!waitingForResult)
            SetPuzzleVisible(false);
    }

    private void OnMouseDown()
    {
        if (waitingForResult) return;

        if (manager == null || stage == null ||
            circuitOverlay == null || editableLayoutsCanvas == null)
        {
            Debug.LogError("[PCS P4] Inspector 참조 누락!");
            return;
        }

        if (!manager.isActiveAndEnabled)
        {
            Debug.LogWarning("[PCS P4] Manager가 비활성 상태입니다.");
            return;
        }

        // UI 활성화
        SetPuzzleVisible(true);

        // 퍼즐 시작은 한 번만 요청
        bool started = manager.StartSingleStage(stage);

        if (!started)
        {
            SetPuzzleVisible(false);

            Debug.LogWarning(
                "[PCS P4] 퍼즐 시작 실패!\n" +
                $"State: {manager.State}\n" +
                $"Error: {manager.Error}"
            );

            return;
        }

        activeRunId = manager.RunId;
        waitingForResult = true;

        Debug.Log("[PCS P4] 퍼즐 시작 성공!");
    }

    private void HandleCompleted(int runId)
    {
        if (!waitingForResult || runId != activeRunId)
            return;

        waitingForResult = false;
        activeRunId = -1;

        Debug.Log("[PCS P4] 퍼즐 완료!");

        if (autoCloseOnComplete)
        {
            manager.CloseHack();
            SetPuzzleVisible(false);
        }

        onPuzzleCompleted?.Invoke();
    }

    private void HandleCancelled(int runId)
    {
        if (!waitingForResult || runId != activeRunId)
            return;

        waitingForResult = false;
        activeRunId = -1;

        Debug.Log("[PCS P4] 퍼즐 취소!");

        SetPuzzleVisible(false);

        onPuzzleCancelled?.Invoke();
    }

    private void SetPuzzleVisible(bool visible)
    {
        // 퍼즐 UI
        if (circuitOverlay != null)
            circuitOverlay.SetActive(visible);

        // 스테이지 미로 표시
        if (editableLayoutsCanvas != null)
            editableLayoutsCanvas.enabled = visible;

        // 숨겨진 UI가 마우스 입력을 가로채지 않도록 처리
        if (layoutsRaycaster != null)
            layoutsRaycaster.enabled = visible;
    }
}

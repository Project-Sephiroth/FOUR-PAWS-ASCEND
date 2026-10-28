
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using FourPaws.PCS.Hacking;
using FourPaws.PCS.CircuitHack;

[RequireComponent(typeof(Collider))]
public class PCS_AllPuzzleSequenceLauncher : MonoBehaviour
{
    public enum PuzzleMode
    {
        P1_ThreeStages,
        P2_ThreeStages,
        P4_ThreeStages,
        All_P1_P2_P4
    }

    private enum PuzzlePart
    {
        None,
        P1,
        P2,
        P4
    }

    [Header("실행 방식")]
    [SerializeField]
    private PuzzleMode mode =
        PuzzleMode.All_P1_P2_P4;

    [Header("P1 / P2 공통 Session")]
    [SerializeField] private PCS_HackingSession session;
    [SerializeField] private Button closeButton;

    [Header("P1 타이밍 해킹 - 3단계")]
    [SerializeField]
    private PCS_TimingStageData[] p1Stages =
        new PCS_TimingStageData[3];

    [Header("P2 회전 해킹 - 3단계")]
    [SerializeField]
    private PCS_RotationStageData[] p2Stages =
        new PCS_RotationStageData[3];

    [SerializeField] private int p2Seed = 20261015;

    [Header("P4 회로 해킹 - 3단계")]
    [SerializeField] private CircuitHackManager manager;

    [SerializeField]
    private CircuitStageLayout[] p4Stages =
        new CircuitStageLayout[3];

    [Header("P4 화면 표시 - 기존 오브젝트 연결")]
    [SerializeField] private GameObject p4CircuitOverlay;
    [SerializeField] private GameObject p4EditableLayouts;

    [Header("개별 퍼즐 완료 이벤트")]
    [SerializeField] private UnityEvent onP1Completed;
    [SerializeField] private UnityEvent onP2Completed;
    [SerializeField] private UnityEvent onP4Completed;

    [Header("전체 과정 완료")]
    [SerializeField] private UnityEvent onSequenceCompleted;

    [Header("퍼즐 취소")]
    [SerializeField] private UnityEvent onPuzzleCancelled;

    private PuzzlePart currentPart = PuzzlePart.None;

    private long activeSessionId = -1;
    private int activeRunId = -1;

    private bool isRunning;

    private Canvas layoutsCanvas;
    private GraphicRaycaster layoutsRaycaster;

    private void Awake()
    {
        // 기존 EditableLayouts의 컴포넌트 찾기
        if (p4EditableLayouts != null)
        {
            layoutsCanvas =
                p4EditableLayouts.GetComponent<Canvas>();

            layoutsRaycaster =
                p4EditableLayouts.GetComponent<GraphicRaycaster>();

            if (layoutsCanvas == null)
            {
                Debug.LogWarning(
                    "[PCS] EditableLayouts에 Canvas가 없습니다.");
            }
        }

        // 게임 시작 시 P4 화면 숨기기
        SetP4Visible(false);
    }

    private void Start()
    {
        if (!isRunning)
        {
            SetP4Visible(false);
        }
    }

    private void OnEnable()
    {
        if (session != null)
        {
            session.OnHackCompleted += HandleSessionCompleted;
            session.OnHackCancelled += HandleSessionCancelled;
        }

        if (manager != null)
        {
            manager.OnHackCompleted += HandleCircuitCompleted;
            manager.OnHackCancelled += HandleCircuitCancelled;
        }
    }

    private void OnDisable()
    {
        if (session != null)
        {
            session.OnHackCompleted -= HandleSessionCompleted;
            session.OnHackCancelled -= HandleSessionCancelled;
        }

        if (manager != null)
        {
            manager.OnHackCompleted -= HandleCircuitCompleted;
            manager.OnHackCancelled -= HandleCircuitCancelled;
        }
    }

    private void OnMouseDown()
    {
        StartPuzzles();
    }

    // 외부 스크립트 / UnityEvent 호출 가능
    public void StartPuzzles()
    {
        if (isRunning)
        {
            Debug.LogWarning("[PCS] 이미 퍼즐 진행 중!");
            return;
        }

        PuzzlePart firstPart = GetFirstPart();

        isRunning = true;

        if (!TryStartPart(firstPart))
        {
            StopAfterError();
        }
    }

    private PuzzlePart GetFirstPart()
    {
        switch (mode)
        {
            case PuzzleMode.P1_ThreeStages:
            case PuzzleMode.All_P1_P2_P4:
                return PuzzlePart.P1;

            case PuzzleMode.P2_ThreeStages:
                return PuzzlePart.P2;

            case PuzzleMode.P4_ThreeStages:
                return PuzzlePart.P4;

            default:
                return PuzzlePart.None;
        }
    }

    private bool TryStartPart(PuzzlePart part)
    {
        currentPart = part;

        switch (part)
        {
            case PuzzlePart.P1:
                return StartP1();

            case PuzzlePart.P2:
                return StartP2();

            case PuzzlePart.P4:
                return StartP4();

            default:
                return false;
        }
    }

    // ================================
    // P1 타이밍 해킹
    // ================================

    private bool StartP1()
    {
        if (!CheckSession() || !CheckStages(p1Stages))
            return false;

        bool started = session.StartSequence(p1Stages);

        if (!started)
        {
            Debug.LogWarning(
                "[PCS P1] 시작 실패: " + session.Error);
            return false;
        }

        activeSessionId = session.SessionId;

        Debug.Log("[PCS] P1 3단계 시작!");

        return true;
    }

    // ================================
    // P2 육각 회전 해킹
    // ================================

    private bool StartP2()
    {
        if (!CheckSession() || !CheckStages(p2Stages))
            return false;

        bool started = session.StartSequence(p2Stages, p2Seed);

        if (!started)
        {
            Debug.LogWarning(
                "[PCS P2] 시작 실패: " + session.Error);
            return false;
        }

        activeSessionId = session.SessionId;

        Debug.Log("[PCS] P2 3단계 시작!");

        return true;
    }

    // ================================
    // P4 서킷 해킹
    // ================================

    private bool StartP4()
    {
        if (manager == null || !manager.isActiveAndEnabled)
        {
            Debug.LogError(
                "[PCS P4] CircuitHackManager 누락 또는 비활성!");
            return false;
        }

        if (!CheckStages(p4Stages))
            return false;

        // 기존 UI 및 미로 표시
        SetP4Visible(true);

        bool started = manager.StartSequence(p4Stages);

        if (!started)
        {
            Debug.LogWarning(
                "[PCS P4] 시작 실패: " + manager.Error);

            SetP4Visible(false);

            return false;
        }

        activeRunId = manager.RunId;

        Debug.Log("[PCS] P4 3단계 시작!");

        return true;
    }

    // ================================
    // 공통 검사
    // ================================

    private bool CheckSession()
    {
        if (session == null || !session.isActiveAndEnabled)
        {
            Debug.LogError(
                "[PCS] Session 참조 누락 또는 비활성!");
            return false;
        }

        if (closeButton == null)
        {
            Debug.LogError(
                "[PCS] Close Button 참조 누락!");
            return false;
        }

        return true;
    }

    private bool CheckStages<T>(T[] stages)
        where T : UnityEngine.Object
    {
        if (stages == null || stages.Length != 3)
        {
            Debug.LogError(
                "[PCS] Stage는 정확히 3개 필요!");
            return false;
        }

        for (int i = 0; i < stages.Length; i++)
        {
            if (stages[i] == null)
            {
                Debug.LogError(
                    $"[PCS] Stage {i + 1} 참조 누락!");
                return false;
            }
        }

        return true;
    }

    // ================================
    // P1 / P2 완료
    // ================================

    private void HandleSessionCompleted(long sessionId)
    {
        if (!isRunning || sessionId != activeSessionId)
            return;

        if (currentPart != PuzzlePart.P1 &&
            currentPart != PuzzlePart.P2)
            return;

        PuzzlePart finishedPart = currentPart;

        currentPart = PuzzlePart.None;
        activeSessionId = -1;

        // 기존 PCS UI 닫기
        if (closeButton != null)
        {
            closeButton.onClick.Invoke();
        }

        Debug.Log("[PCS] " + finishedPart + " 전체 완료!");

        if (finishedPart == PuzzlePart.P1)
        {
            onP1Completed?.Invoke();
        }
        else
        {
            onP2Completed?.Invoke();
        }

        AdvanceAfterCompletion(finishedPart);
    }

    // ================================
    // P4 완료
    // ================================

    private void HandleCircuitCompleted(int runId)
    {
        if (!isRunning ||
            currentPart != PuzzlePart.P4 ||
            runId != activeRunId)
            return;

        currentPart = PuzzlePart.None;
        activeRunId = -1;

        // P4 세션 종료
        if (manager != null)
        {
            manager.CloseHack();
        }

        // P4 화면 숨김
        SetP4Visible(false);

        Debug.Log("[PCS] P4 전체 완료!");

        onP4Completed?.Invoke();

        AdvanceAfterCompletion(PuzzlePart.P4);
    }

    // ================================
    // 다음 퍼즐 자동 실행
    // ================================

    private void AdvanceAfterCompletion(PuzzlePart finished)
    {
        if (mode == PuzzleMode.All_P1_P2_P4)
        {
            if (finished == PuzzlePart.P1)
            {
                StartCoroutine(
                    StartNextPart(PuzzlePart.P2));
                return;
            }

            if (finished == PuzzlePart.P2)
            {
                StartCoroutine(
                    StartNextPart(PuzzlePart.P4));
                return;
            }
        }

        FinishSequence();
    }

    private IEnumerator StartNextPart(PuzzlePart next)
    {
        // 이전 퍼즐 UI 처리 완료 대기
        yield return null;

        if (!isRunning)
            yield break;

        if (!TryStartPart(next))
        {
            StopAfterError();
        }
    }

    // ================================
    // 전체 완료
    // ================================

    private void FinishSequence()
    {
        isRunning = false;
        currentPart = PuzzlePart.None;

        activeSessionId = -1;
        activeRunId = -1;

        Debug.Log("[PCS] 모든 퍼즐 완료!");

        onSequenceCompleted?.Invoke();
    }

    // ================================
    // 취소 이벤트
    // ================================

    private void HandleSessionCancelled(long sessionId)
    {
        if (!isRunning ||
            sessionId != activeSessionId)
            return;

        if (currentPart != PuzzlePart.P1 &&
            currentPart != PuzzlePart.P2)
            return;

        CancelSequence();
    }

    private void HandleCircuitCancelled(int runId)
    {
        if (!isRunning ||
            currentPart != PuzzlePart.P4 ||
            runId != activeRunId)
            return;

        CancelSequence();
    }

    private void CancelSequence()
    {
        isRunning = false;
        currentPart = PuzzlePart.None;

        activeSessionId = -1;
        activeRunId = -1;

        SetP4Visible(false);

        Debug.Log("[PCS] 퍼즐 취소!");

        onPuzzleCancelled?.Invoke();
    }

    private void StopAfterError()
    {
        isRunning = false;
        currentPart = PuzzlePart.None;

        activeSessionId = -1;
        activeRunId = -1;

        SetP4Visible(false);

        Debug.LogError("[PCS] 퍼즐 실행 중단!");
    }

    // ================================
    // P4 화면 표시 제어
    // ================================

    private void SetP4Visible(bool visible)
    {
        // P4 Overlay 표시
        if (p4CircuitOverlay != null)
        {
            p4CircuitOverlay.SetActive(visible);
        }

        // EditableLayouts의 Canvas 표시
        // Stage GameObject 자체는 끄지 않음
        if (layoutsCanvas != null)
        {
            layoutsCanvas.enabled = visible;
        }

        // 숨겨진 UI의 클릭 입력도 비활성화
        if (layoutsRaycaster != null)
        {
            layoutsRaycaster.enabled = visible;
        }
    }
}

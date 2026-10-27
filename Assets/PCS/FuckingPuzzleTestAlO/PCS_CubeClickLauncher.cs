using UnityEngine.UI;
using FourPaws.PCS.Hacking;
using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(Collider))]
public class PCS_CubeClickLauncher : MonoBehaviour
{
    [Header("PCS 퍼즐 연결")]
    [SerializeField] private PCS_HackingSession session;
    [SerializeField] private PCS_TimingStageData timingStage;

    [Header("퍼즐 완료 후 실행")]
    [SerializeField] private UnityEvent onPuzzleCompleted;
    [Header("해킹 UI 닫기")]
    [SerializeField] private Button closeButton;

    [Header("퍼즐 취소 시 실행")]
    [SerializeField] private UnityEvent onPuzzleCancelled;

    private long activeSessionId = -1;
    private bool waitingForResult = false;

    private void OnEnable()
    {
        if (session == null)
        {
            Debug.LogWarning(
                "[PCS] OnEnable: Session이 연결되지 않았습니다.");
            return;
        }

        session.OnHackCompleted += HandleCompleted;
        session.OnHackCancelled += HandleCancelled;
    }

    private void OnDisable()
    {
        if (session == null) return;

        session.OnHackCompleted -= HandleCompleted;
        session.OnHackCancelled -= HandleCancelled;
    }

    private void OnMouseDown()
    {
        Debug.Log("[PCS] Cube 클릭 감지!");

        // 이미 퍼즐 진행 중이면 중복 실행 방지
        if (waitingForResult)
        {
            Debug.LogWarning(
                "[PCS] 이미 퍼즐 진행 중입니다.");
            return;
        }

        // 참조 확인
        if (session == null)
        {
            Debug.LogError(
                "[PCS] Session 참조가 없습니다!");
            return;
        }

        if (timingStage == null)
        {
            Debug.LogError(
                "[PCS] Timing Stage 참조가 없습니다!");
            return;
        }

        // 현재 상태 출력
        Debug.Log(
            "[PCS] 퍼즐 실행 전 상태\n" +
            $"Session Object: {session.gameObject.name}\n" +
            $"Session Active: {session.gameObject.activeInHierarchy}\n" +
            $"Session Enabled: {session.enabled}\n" +
            $"Session ID: {session.SessionId}\n" +
            $"Stage: {timingStage.name}"
        );

        // 퍼즐 실행 요청
        bool started = session.StartSingleStage(timingStage);

        if (!started)
        {
            string error = session.Error;

            if (string.IsNullOrEmpty(error))
            {
                error = "(오류 메시지 없음)";
            }

            Debug.LogWarning(
                "[PCS] 퍼즐 시작 실패!\n" +
                $"Error: {error}\n" +
                $"Session Active: {session.gameObject.activeInHierarchy}\n" +
                $"Session Enabled: {session.enabled}\n" +
                $"Session ID: {session.SessionId}\n" +
                $"Stage: {timingStage.name}"
            );

            return;
        }

        activeSessionId = session.SessionId;
        waitingForResult = true;

        Debug.Log(
            "[PCS] 퍼즐 시작 성공!\n" +
            $"Session ID: {activeSessionId}"
        );
    }


    private void HandleCompleted(long sessionId)
    {
        if (!waitingForResult) return;
        if (sessionId != activeSessionId) return;

        waitingForResult = false;
        activeSessionId = -1;

        Debug.Log("[PCS] 퍼즐 완료!");

        // 기존 PCS 닫기 버튼 호출
        if (closeButton != null)
        {
            closeButton.onClick.Invoke();
            Debug.Log("[PCS] 해킹 UI 닫기 요청!");
        }
        else
        {
            Debug.LogWarning("[PCS] Close Button 참조 누락!");
        }

        // 퍼즐 성공 후 외부 상호작용 실행
        onPuzzleCompleted?.Invoke();
    }


    private void HandleCancelled(long sessionId)
    {
        if (!waitingForResult) return;
        if (sessionId != activeSessionId) return;

        waitingForResult = false;
        activeSessionId = -1;

        Debug.Log("[PCS] 퍼즐 취소!");

        // Inspector에 등록된 취소 이벤트 실행
        onPuzzleCancelled?.Invoke();
    }
}

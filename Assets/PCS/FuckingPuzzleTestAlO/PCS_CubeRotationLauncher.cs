
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using FourPaws.PCS.Hacking;

[RequireComponent(typeof(Collider))]
public class PCS_CubeRotationLauncher : MonoBehaviour
{
    [Header("PCS P2 퍼즐 연결")]
    [SerializeField] private PCS_HackingSession session;
    [SerializeField] private PCS_RotationStageData rotationStage;
    [SerializeField] private int seed = 20261015;

    [Header("퍼즐 완료 후 실행")]
    [SerializeField] private UnityEvent onPuzzleCompleted;

    [Header("해킹 UI 닫기")]
    [SerializeField] private Button closeButton;

    [Header("퍼즐 취소 시 실행")]
    [SerializeField] private UnityEvent onPuzzleCancelled;

    private long activeSessionId = -1;
    private bool waitingForResult;

    private void OnEnable()
    {
        if (session == null) return;

        session.OnHackCompleted += HandleCompleted;
        session.OnHackCancelled += HandleCancelled;
    }

    private void OnDisable()
    {
        if (session == null) return;

        session.OnHackCompleted -= HandleCompleted;
        session.OnHackCancelled -= HandleCancelled;

        waitingForResult = false;
        activeSessionId = -1;
    }

    private void OnMouseDown()
    {
        if (waitingForResult) return;

        if (session == null || rotationStage == null)
        {
            Debug.LogError("[PCS P2] Session 또는 Rotation Stage 누락!");
            return;
        }


        Debug.Log(
            "[PCS P2] 시작 전 상태\n" +
            $"State: {session.State}\n" +
            $"ActiveAndEnabled: {session.isActiveAndEnabled}\n" +
            $"Session ID: {session.SessionId}\n" +
            $"Rotation Stage: {rotationStage.name}\n" +
            $"Seed: {seed}"
        );

        bool started = session.StartSingleStage(rotationStage, seed);

        Debug.Log(
            "[PCS P2] 시작 후 상태\n" +
            $"Started: {started}\n" +
            $"State: {session.State}\n" +
            $"Session Error: {session.Error}\n" +
            $"Rotation Error: {session.rotation?.Error}\n" +
            $"IsRotation: {session.IsRotation}"
        );
        Debug.Log(
    "[PCS P2] Session 활성화 상세 진단\n" +
    $"Object: {session.gameObject.name}\n" +
    $"Component Enabled: {session.enabled}\n" +
    $"GameObject ActiveSelf: {session.gameObject.activeSelf}\n" +
    $"GameObject ActiveInHierarchy: {session.gameObject.activeInHierarchy}\n" +
    $"IsActiveAndEnabled: {session.isActiveAndEnabled}\n"// +
    //$"CanStart: {session.CanStart}"
); 

        if (!started)
        {
            Debug.LogWarning(
                "[PCS P2] 퍼즐 시작 실패: " + session.Error);
            return;
        }

        activeSessionId = session.SessionId;
        waitingForResult = true;

        Debug.Log("[PCS P2] 육각 타일 퍼즐 시작!");
    }

    private void HandleCompleted(long sessionId)
    {
        if (!waitingForResult || sessionId != activeSessionId)
            return;

        waitingForResult = false;
        activeSessionId = -1;

        Debug.Log("[PCS P2] 퍼즐 완료!");

        if (closeButton != null)
            closeButton.onClick.Invoke();

        onPuzzleCompleted?.Invoke();
    }

    private void HandleCancelled(long sessionId)
    {
        if (!waitingForResult || sessionId != activeSessionId)
            return;

        waitingForResult = false;
        activeSessionId = -1;

        Debug.Log("[PCS P2] 퍼즐 취소!");

        onPuzzleCancelled?.Invoke();
    }
}

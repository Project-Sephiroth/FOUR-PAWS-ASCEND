using System.Collections.Generic;
using Fusion;
using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public static CameraFollow Instance { get; private set; }

    [Header("카메라 설정")]
    [SerializeField] private Vector3 offset = new Vector3(0, 0, -10);
    [SerializeField] private float smoothSpeed = 5f;

    [Header("카메라 이동 범위")]
    [SerializeField] private float minX;
    [SerializeField] private float maxX;
    [SerializeField] private float minY;
    [SerializeField] private float maxY;

    [SerializeField, Min(0.1f)] private float holdThreshold = 0.3f;
    private Transform target;
    private NetworkObject selectedView;
    private float tabPressedAt = -1f;
    private bool holdingTab;
    private readonly List<NetworkObject> cameraTargets = new(4);

    public static NetworkObject ViewedPlayer =>
        Instance != null && Instance.selectedView != null && Instance.selectedView.IsValid ? Instance.selectedView : null;
    public static bool IsViewingOther => ViewedPlayer != null;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void LateUpdate()
    {
        if (target == null)
            return;

        UpdateSpectatorInput();
        Transform followed = ViewedPlayer != null ? ViewedPlayer.transform : target;
        Vector3 targetPos = followed.position + offset;

        Vector3 newPos = Vector3.Lerp(
            transform.position,
            targetPos,
            smoothSpeed * Time.deltaTime
        );

        transform.position = ClampPosition(newPos);
    }

    // 짧게 누르면 다음 화면을 유지합니다. 길게 누르면 키를 떼는 순간 자기 화면으로 돌아옵니다.
    private void UpdateSpectatorInput()
    {
        var manager = NetworkGameManager.Instance;
        if (manager == null || manager.Object == null || !manager.Object.IsValid ||
            !manager.TryGetGameplayPlayer(manager.Runner.LocalPlayer, out var local) ||
            local == null || !local.IsValid)
        {
            ResetView();
            return;
        }
        if (GameMenu.BlocksInput || LocalMultiplayerDebug.BlocksWorldClick ||
            HackingConsole.IsLocalHackOpen || ScenePresentation.BlocksInput || !Application.isFocused)
        {
            ResetView();
            return;
        }
        if (selectedView != null && !selectedView.IsValid)
            selectedView = null;

        if (Input.GetKeyDown(KeyCode.Tab))
            tabPressedAt = Time.unscaledTime;
        if (tabPressedAt < 0f)
            return;
        if (!holdingTab && Input.GetKey(KeyCode.Tab) && Time.unscaledTime - tabPressedAt >= holdThreshold)
        {
            holdingTab = true;
            SelectNextView(manager.Runner, local, false);
        }
        if (Input.GetKeyUp(KeyCode.Tab))
        {
            if (holdingTab)
                ResetView();
            else
            {
                tabPressedAt = -1f;
                SelectNextView(manager.Runner, local, true);
            }
        }
    }

    private void SelectNextView(NetworkRunner runner, NetworkObject local, bool includeOwnView)
    {
        cameraTargets.Clear();
        foreach (var player in NetworkGameManager.GetPlayerObjects(runner))
            if (player != null && player.IsValid && player != local)
                cameraTargets.Add(player);
        cameraTargets.Sort((a, b) => a.Id.GetHashCode().CompareTo(b.Id.GetHashCode()));
        int next = cameraTargets.IndexOf(selectedView) + 1;
        selectedView = cameraTargets.Count == 0 || (includeOwnView && next == cameraTargets.Count)
            ? null : cameraTargets[next % cameraTargets.Count];
    }

    private void ResetView()
    {
        selectedView = null;
        tabPressedAt = -1f;
        holdingTab = false;
    }

    private Vector3 ClampPosition(Vector3 pos)
    {
        pos.x = Mathf.Clamp(pos.x, minX, maxX);
        pos.y = Mathf.Clamp(pos.y, minY, maxY);

        return pos;
    }

    public void SetTarget(Transform newTarget)
    {
        if (target != newTarget)
            ResetView();
        target = newTarget;

        if (target != null)
            transform.position = ClampPosition(target.position + offset);
    }

    public void ClearTarget()
    {
        ResetView();
        target = null;
    }
}
using UnityEngine;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public sealed class UIManager : MonoBehaviour
{
    // 레이어 사이에 여유를 두어 한 화면 안의 팝업 캔버스도 자기 UI 범위에서 그릴 수 있게 합니다.
    internal enum Layer
    {
        Health = 40, Puzzle = 41, Shortcut = 45, Pause = 150, Settings = 170,
        Hacking = 190, Debug = 220, Transition = 1000
    }

    private static UIManager instance;
    private EventSystem localEventSystem;

    internal void Initialize(GamePresentationAssets assets)
    {
        instance = this;
        Mount(assets.healthHud, Layer.Health);
        Mount(assets.puzzleProgressHud, Layer.Puzzle);
        gameObject.AddComponent<GameMenu>().Initialize(assets, this);
    }

    internal GameObject Mount(GameObject prefab, Layer layer)
    {
        if (prefab == null)
            return null;
        var view = Instantiate(prefab, transform);
        if (view.TryGetComponent(out Canvas canvas))
        {
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.transform.localScale = Vector3.one;
        }
        SetCanvasOrder(view, layer);
        return view;
    }

    internal T Mount<T>(T prefab, Layer layer) where T : Component
    {
        if (prefab == null)
            return null;
        var view = Instantiate(prefab, transform);
        SetCanvasOrder(view.gameObject, layer);
        return view;
    }

    // 해킹 패널은 씬에 배치된 상태로 둡니다. 표시 여부나 네트워크 권한은 각 퍼즐 스크립트가 관리합니다.
    public static void RegisterHackPanel(GameObject panel)
    {
        if (panel != null && HackingConsole.IsGameplayScene(panel.scene))
            SetCanvasOrder(panel, Layer.Hacking);
    }

    internal static void SetCanvasOrder(GameObject view, Layer layer)
    {
        if (view == null)
            return;

        var root = view.GetComponentInParent<Canvas>(true);
        if (root == null)
            root = view.GetComponentInChildren<Canvas>(true);
        if (root == null)
            return;
        var outer = root.rootCanvas;
        if (outer != null)
            root = outer;
        int previous = root.sortingOrder;
        foreach (var child in root.GetComponentsInChildren<Canvas>(true))
            if (child != root && child.overrideSorting)
                child.sortingOrder = (int)layer + Mathf.Clamp(child.sortingOrder - previous, -5, 5);
        root.sortingOrder = (int)layer;
    }

    internal static void EnsureLocalEventSystem()
    {
        if (instance == null || EventSystem.current != null || instance.localEventSystem != null)
            return;
        var input = new GameObject("UI Event System", typeof(EventSystem), typeof(StandaloneInputModule));
        input.transform.SetParent(instance.transform, false);
        instance.localEventSystem = input.GetComponent<EventSystem>();
    }

    internal static void ReleaseLocalEventSystem()
    {
        if (instance == null || instance.localEventSystem == null)
            return;
        var input = instance.localEventSystem.gameObject;
        instance.localEventSystem = null;
        input.SetActive(false);
        Destroy(input);
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }
}
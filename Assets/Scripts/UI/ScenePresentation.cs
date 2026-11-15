using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using Character = MyEnum.CharacterType;

[DefaultExecutionOrder(-110)]
public sealed class ScenePresentation : MonoBehaviour
{
    private const float FallbackCloseSeconds = 0.45f;
    private static ScenePresentation instance;
    private SceneTransitionView view;
    private bool loading;

    public static float CloseSeconds => instance != null && instance.view != null
        ? instance.view.CoverSeconds
        : FallbackCloseSeconds;

    public static bool BlocksInput => instance != null && instance.view != null && instance.view.BlocksInput;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        var assets = Resources.Load<GamePresentationAssets>("GamePresentation");
        if (assets == null)
            return;
        var root = new GameObject("Game Presentation");
        DontDestroyOnLoad(root);
        instance = root.AddComponent<ScenePresentation>();
        instance.Initialize(assets);
        root.AddComponent<GameMenu>().Initialize(assets);
    }

    private void Initialize(GamePresentationAssets assets)
    {
        if (assets.sceneTransition == null)
            return;

        view = Instantiate(assets.sceneTransition, transform);
        if (!view.Begin())
        {
            Destroy(view.gameObject);
            view = null;
        }
    }

    internal static Canvas CreateOverlay(Transform parent, string name, int order)
    {
        var canvas = new GameObject(name, typeof(RectTransform), typeof(Canvas)).GetComponent<Canvas>();
        canvas.transform.SetParent(parent, false);
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = order;
        return canvas;
    }

    public static Character LocalCharacter()
    {
        var manager = NetworkGameManager.Instance;
        return manager != null && manager.Object != null && manager.Object.IsValid
            ? manager.GetGameplayCharacter(manager.Runner.LocalPlayer)
            : Character.None;
    }

    public static void Cover(bool immediate = false)
    {
        if (instance == null || instance.view == null)
            return;

        GameMenu.CloseForTransition();
        instance.view.Cover(LocalCharacter(), immediate);
    }

    public static void Reveal()
    {
        if (instance != null && instance.view != null)
            instance.view.Reveal();
    }

    public static async Task CoverAsync()
    {
        Cover();
        while (instance != null && instance.view != null && instance.view.IsCovering && !instance.view.IsCovered)
            await Awaitable.NextFrameAsync();
    }

    // 1. 화면이 다 닫힐 때까지 기다립니다. 2. 필요하면 이전 연결을 먼저 정리하고 새 씬을 엽니다. 3. 로드가 끝나면 화면을 다시 엽니다.
    public static async Task LoadLocalAsync(string sceneName, Func<Task> beforeLoad = null)
    {
        if (instance != null && instance.loading)
            return;
        if (instance != null)
            instance.loading = true;
        try
        {
            await CoverAsync();
            if (beforeLoad != null)
                await beforeLoad();
            await SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
        }
        finally
        {
            if (instance != null)
                instance.loading = false;
            Reveal();
        }
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }
}

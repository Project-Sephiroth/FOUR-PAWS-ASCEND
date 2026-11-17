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
    private GameObject demoComplete;
    private bool loading;

    public static float CloseSeconds => instance != null && instance.view != null
        ? instance.view.CoverSeconds
        : FallbackCloseSeconds;

    internal static bool IsDemoCompleteVisible =>
        instance != null && instance.demoComplete != null && instance.demoComplete.activeSelf;

    public static bool BlocksInput => IsDemoCompleteVisible ||
        (instance != null && instance.view != null && instance.view.BlocksInput);

    internal static void ShowDemoComplete(bool visible)
    {
        if (instance != null && instance.demoComplete != null && instance.demoComplete.activeSelf != visible)
            instance.demoComplete.SetActive(visible);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        var assets = Resources.Load<GamePresentationAssets>("GamePresentation");
        if (assets == null)
            return;
        var root = new GameObject("Game Presentation");
        DontDestroyOnLoad(root);
        root.AddComponent<CharacterCursor>();
        instance = root.AddComponent<ScenePresentation>();
        instance.Initialize(assets);
        var ui = root.AddComponent<UIManager>();
        ui.Initialize(assets);
        instance.demoComplete = ui.Mount(assets.demoComplete, UIManager.Layer.DemoComplete);
        SceneManager.activeSceneChanged += instance.OnActiveSceneChanged;
    }

    private void Initialize(GamePresentationAssets assets)
    {
        if (assets.sceneTransition == null)
            return;

        view = Instantiate(assets.sceneTransition, transform);
        UIManager.SetCanvasOrder(view.gameObject, UIManager.Layer.Transition);
        if (!view.Begin())
        {
            Destroy(view.gameObject);
            view = null;
        }
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

    private void OnActiveSceneChanged(Scene previous, Scene current) => ShowDemoComplete(false);

    private void OnDestroy()
    {
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        if (instance == this)
            instance = null;
    }
}

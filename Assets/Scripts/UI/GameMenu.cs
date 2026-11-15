using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DefaultExecutionOrder(-105)]
public sealed class GameMenu : MonoBehaviour
{
    private static GameMenu instance;
    private GameObject pauseMenu;
    private GameObject settingsMenu;
    private EventSystem menuEventSystem;
    private Button settingsButton;
    private Sprite gearSprite;
    // settingsOpenedFromPause는 환경설정을 어디서 열었는지 기억합니다. 닫을 때 일시정지 메뉴로 돌아갈지 게임 화면으로 돌아갈지 정할 때 씁니다.
    private bool settingsOpenedFromPause;
    private PlayerSettings settings;
    private Image[] hearts;
    private Canvas healthCanvas;
    private Sprite heartSprite;

    private float? previousTimeScale;
    private bool isOpen;
    private bool leaving;

    public static bool BlocksInput => instance != null && instance.isOpen;

    public static bool BlocksWorldClick =>
        BlocksInput || ScenePresentation.BlocksInput ||
        (instance != null && instance.settingsButton != null &&
         instance.settingsButton.isActiveAndEnabled &&
         RectTransformUtility.RectangleContainsScreenPoint(
             (RectTransform)instance.settingsButton.transform, Input.mousePosition));

    internal void Initialize(GamePresentationAssets assets)
    {
        instance = this;
        BuildHealthHud();
        if (assets.pauseMenu == null || assets.settingsMenu == null)
            return;

        pauseMenu = Instantiate(assets.pauseMenu, transform);
        settingsMenu = Instantiate(assets.settingsMenu, transform);
        SetCanvasOrder(pauseMenu, 150);
        SetCanvasOrder(settingsMenu, 170);

        FindControl<Button>(pauseMenu, "ResumeButton").onClick.AddListener(Close);
        FindControl<Button>(pauseMenu, "OptionButton").onClick.AddListener(ShowSettings);
        FindControl<Button>(pauseMenu, "TitleButton").onClick.AddListener(ReturnToTitle);
        FindControl<Button>(pauseMenu, "ExitButton").onClick.AddListener(ExitGame);
        // 배경 그림에 그려진 X 위의 투명 버튼도 저장 없이 창을 닫습니다.
        FindControl<Button>(settingsMenu, "CloseSettings").onClick.AddListener(FinishSettings);
        settings = new PlayerSettings(settingsMenu, FinishSettings);
        pauseMenu.SetActive(false);
        settingsMenu.SetActive(false);
        BuildSettingsShortcut();
    }

    private static void SetCanvasOrder(GameObject menu, int order)
    {
        var root = menu.GetComponent<Canvas>();
        root.renderMode = RenderMode.ScreenSpaceOverlay;
        root.transform.localScale = Vector3.one;
        root.sortingOrder = order;
        foreach (var canvas in menu.GetComponentsInChildren<Canvas>(true))
            if (canvas != root && canvas.overrideSorting)
                canvas.sortingOrder = order + 1;
    }

    internal static T FindControl<T>(GameObject root, string name) where T : Component
    {
        foreach (var component in root.GetComponentsInChildren<T>(true))
            if (component.name == name)
                return component;
        throw new InvalidOperationException($"Missing {typeof(T).Name} '{name}' in '{root.name}'.");
    }

    private void Update()
    {
        if (pauseMenu == null || leaving || ScenePresentation.BlocksInput ||
            LocalMultiplayerDebug.BlocksWorldClick || HackingConsole.IsLocalHackOpen || !Input.GetKeyDown(KeyCode.Escape))
            return;
        if (isOpen)
        {
            if (settingsMenu.activeSelf)
                FinishSettings();
            else
                Close();
            return;
        }

        var manager = NetworkGameManager.Instance;
        if (manager != null && manager.Object != null && manager.Object.IsValid)
            Open(manager.Runner.IsSinglePlayer);
    }

    internal static void EnsureLocalEventSystem() => instance?.EnsureEventSystem();

    private void EnsureEventSystem()
    {
        if (EventSystem.current != null || menuEventSystem != null)
            return;

        var system = new GameObject("HUD Event System", typeof(EventSystem), typeof(StandaloneInputModule));
        system.transform.SetParent(transform, false);
        menuEventSystem = system.GetComponent<EventSystem>();
    }

    // 메뉴를 열기 전에 버튼 클릭을 받을 EventSystem을 준비합니다. 로컬 테스트에서만 현재 게임 시간을 기억하고 정지시키므로 온라인의 다른 참가자는 계속 플레이합니다.
    private void Open(bool freezeTime)
    {
        EnsureEventSystem();
        isOpen = true;
        if (freezeTime)
        {
            previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
        }
        ShowPauseMenu();
    }

    private void ShowPauseMenu()
    {
        settingsMenu.SetActive(false);
        pauseMenu.SetActive(true);
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    private void OpenSettingsFromHud()
    {
        if (isOpen || leaving || ScenePresentation.BlocksInput || LocalMultiplayerDebug.BlocksWorldClick)
            return;

        isOpen = true;
        ShowSettings();
    }

    private void FinishSettings()
    {
        if (settingsOpenedFromPause)
            ShowPauseMenu();
        else
            Close();
    }

    private void ShowSettings()
    {
        settingsOpenedFromPause = pauseMenu.activeSelf;
        SoundManager.Play(SoundEffect.UIClick);
        pauseMenu.SetActive(false);
        settings.Reload();
        settingsMenu.SetActive(true);
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    private void Close()
    {
        if (!isOpen)
            return;
        isOpen = false;
        pauseMenu.SetActive(false);
        settingsMenu.SetActive(false);
        if (previousTimeScale is float timeScale)
        {
            Time.timeScale = timeScale;
            previousTimeScale = null;
        }
    }

    private void ReleaseEventSystem()
    {
        if (menuEventSystem == null)
            return;
        menuEventSystem.gameObject.SetActive(false);
        Destroy(menuEventSystem.gameObject);
        menuEventSystem = null;
    }

    public static void CloseForTransition()
    {
        if (instance == null)
            return;
        instance.Close();
        instance.ReleaseEventSystem();
    }

    private async void ReturnToTitle()
    {
        if (leaving)
            return;
        leaving = true;
        SoundManager.Play(SoundEffect.UIClick);
        try
        {
            var manager = NetworkGameManager.Instance;
            var launcher = manager != null && !manager.Runner.IsSinglePlayer
                ? FindAnyObjectByType<NetworkLauncher>()
                : null;
            Func<Task> shutdown = launcher != null ? launcher.LeaveForTitle : LocalMultiplayerDebug.LeaveForTitle;
            await ScenePresentation.LoadLocalAsync(nameof(NetworkGameManager.SceneName.Title), shutdown);
        }
        catch (Exception error)
        {
            Debug.LogException(error);
        }
        finally
        {
            leaving = false;
        }
    }

    private static void ExitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void BuildSettingsShortcut()
    {
        Canvas canvas = ScenePresentation.CreateOverlay(transform, "Settings Shortcut", 45);
        ConfigureHudScale(canvas);
        canvas.gameObject.AddComponent<GraphicRaycaster>();

        var background = new GameObject("Settings Button", typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<Image>();
        background.transform.SetParent(canvas.transform, false);
        var rect = background.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
        rect.anchoredPosition = new Vector2(-20f, -16f);
        rect.sizeDelta = new Vector2(42f, 42f);
        background.color = new Color32(31, 25, 45, 235);
        var outline = background.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color32(177, 156, 202, 255);
        outline.effectDistance = new Vector2(1f, -1f);

        settingsButton = background.GetComponent<Button>();
        settingsButton.targetGraphic = background;
        settingsButton.onClick.AddListener(OpenSettingsFromHud);

        var icon = new GameObject("Gear", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        icon.transform.SetParent(rect, false);
        icon.rectTransform.sizeDelta = new Vector2(28f, 28f);
        icon.raycastTarget = false;
        gearSprite = CreatePixelSprite(new[]
        {
            "......##......",
            "..##..##..##..",
            "..##########..",
            "...########...",
            "..##########..",
            "..###....###..",
            "#####....#####",
            "#####....#####",
            "..###....###..",
            "..##########..",
            "...########...",
            "..##########..",
            "..##..##..##..",
            "......##......"
        });
        icon.sprite = gearSprite;
        icon.color = new Color32(250, 244, 255, 255);
    }

    private static void ConfigureHudScale(Canvas canvas)
    {
        var scaler = canvas.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = 1f;
    }

    private void BuildHealthHud()
    {
        healthCanvas = ScenePresentation.CreateOverlay(transform, "Health HUD", 40);
        ConfigureHudScale(healthCanvas);
        heartSprite = CreatePixelSprite(new[]
        {
            "..###...###..",
            ".#####.#####.",
            "#############",
            "#############",
            "#############",
            ".###########.",
            "..#########..",
            "...#######...",
            "....#####....",
            ".....###.....",
            "......#......"
        });

        hearts = new Image[3];
        for (int i = 0; i < hearts.Length; i++)
        {
            var heart = new GameObject($"Heart {i + 1}", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            heart.transform.SetParent(healthCanvas.transform, false);
            heart.rectTransform.anchorMin = heart.rectTransform.anchorMax = heart.rectTransform.pivot = new Vector2(0f, 1f);
            heart.rectTransform.anchoredPosition = new Vector2(20f + i * 34f, -20f);
            heart.rectTransform.sizeDelta = new Vector2(26f, 22f);
            heart.sprite = heartSprite;
            heart.raycastTarget = false;
            var outline = heart.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.45f, 0.40f, 0.43f, 1f);
            outline.effectDistance = new Vector2(1f, -1f);
            hearts[i] = heart;
        }
        healthCanvas.enabled = false;
    }

    private static Sprite CreatePixelSprite(string[] pixels)
    {
        var texture = new Texture2D(pixels[0].Length, pixels.Length, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };
        for (int y = 0; y < pixels.Length; y++)
            for (int x = 0; x < pixels[y].Length; x++)
                texture.SetPixel(x, pixels.Length - 1 - y, pixels[y][x] == '#' ? Color.white : Color.clear);
        texture.Apply(false, true);
        return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
    }

    private void LateUpdate()
    {
        if (settingsButton != null)
        {
            bool show = !isOpen && !leaving && !ScenePresentation.BlocksInput && !LocalMultiplayerDebug.BlocksWorldClick;
            settingsButton.gameObject.SetActive(show);
            if (show)
                EnsureEventSystem();
        }

        var manager = NetworkGameManager.Instance;
        if (manager == null || manager.Object == null || !manager.Object.IsValid ||
            !manager.TryGetGameplayPlayer(manager.Runner.LocalPlayer, out var player) ||
            player == null || !player.IsValid || !player.TryGetComponent(out PlayerHealth health))
        {
            healthCanvas.enabled = false;
            return;
        }

        healthCanvas.enabled = true;
        int lost = hearts.Length - Mathf.Clamp(health.Health, 0, hearts.Length);
        for (int i = 0; i < hearts.Length; i++)
            hearts[i].color = i < lost ? Color.black : new Color32(240, 50, 81, 255);
    }

    private void OnDestroy()
    {
        Close();
        ReleaseEventSystem();
        if (heartSprite != null)
            Destroy(heartSprite.texture);
        if (gearSprite != null)
            Destroy(gearSprite.texture);
        Destroy(heartSprite);
        Destroy(gearSprite);
        if (instance == this)
            instance = null;
    }
}

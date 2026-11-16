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
    // settingsOpenedFromPause는 환경설정을 어디서 열었는지 기억합니다. 닫을 때 일시정지 메뉴로 돌아갈지 게임 화면으로 돌아갈지 정할 때 씁니다.
    private bool settingsOpenedFromPause;
    private SettingsMenuView settings;
    private SettingsShortcutView shortcut;

    private float? previousTimeScale;
    private bool isOpen;
    private bool leaving;

    public static bool BlocksInput => instance != null && instance.isOpen;

    public static bool BlocksWorldClick =>
        BlocksInput || ScenePresentation.BlocksInput || CameraFollow.IsViewingOther ||
        (instance != null && instance.shortcut != null && instance.shortcut.ContainsPointer);

    internal void Initialize(GamePresentationAssets assets, UIManager ui)
    {
        instance = this;
        if (assets.pauseMenu == null || assets.settingsMenu == null)
            return;

        pauseMenu = ui.Mount(assets.pauseMenu, UIManager.Layer.Pause);
        settingsMenu = ui.Mount(assets.settingsMenu, UIManager.Layer.Settings);
        settings = settingsMenu.GetComponent<SettingsMenuView>();

        FindControl<Button>(pauseMenu, "ResumeButton").onClick.AddListener(Close);
        FindControl<Button>(pauseMenu, "OptionButton").onClick.AddListener(ShowSettings);
        FindControl<Button>(pauseMenu, "TitleButton").onClick.AddListener(ReturnToTitle);
        FindControl<Button>(pauseMenu, "ExitButton").onClick.AddListener(ExitGame);
        settings.Bind(FinishSettings);
        pauseMenu.SetActive(false);
        settingsMenu.SetActive(false);
        shortcut = ui.Mount(assets.settingsShortcut, UIManager.Layer.Shortcut);
        if (shortcut != null)
            shortcut.Bind(OpenSettingsFromHud);
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

    // 메뉴를 열기 전에 버튼 클릭을 받을 EventSystem을 준비합니다. 로컬 테스트에서만 현재 게임 시간을 기억하고 정지시키므로 온라인의 다른 참가자는 계속 플레이합니다.
    private void Open(bool freezeTime)
    {
        UIManager.EnsureLocalEventSystem();
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

    public static void CloseForTransition()
    {
        if (instance == null)
            return;
        instance.Close();
        UIManager.ReleaseLocalEventSystem();
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

    private void LateUpdate()
    {
        if (shortcut == null)
            return;
        bool show = !isOpen && !leaving && !ScenePresentation.BlocksInput && !LocalMultiplayerDebug.BlocksWorldClick;
        shortcut.SetVisible(show);
        if (show)
            UIManager.EnsureLocalEventSystem();
    }

    private void OnDestroy()
    {
        Close();
        if (instance == this)
            instance = null;
    }
}

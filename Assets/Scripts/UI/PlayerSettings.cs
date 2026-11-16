using System;
using UnityEngine;
using UnityEngine.UI;

internal sealed class PlayerSettings
{
    private const string MasterKey = "Settings.MasterVolume";
    private const string MusicKey = "Settings.MusicVolume";
    private const string EffectsKey = "Settings.EffectsVolume";
    private const string DisplayKey = "Settings.DisplayMode";
    private const string ResolutionKey = "Settings.Resolution";

    private static readonly FullScreenMode[] DisplayModes =
        { FullScreenMode.ExclusiveFullScreen, FullScreenMode.Windowed, FullScreenMode.FullScreenWindow };

    private static readonly Vector2Int[] Resolutions =
        { new(1280, 720), new(1600, 900), new(1920, 1080), new(2560, 1440), new(3840, 2160) };

    private readonly Slider master;
    private readonly Slider music;
    private readonly Slider effects;
    private readonly Dropdown display;
    private readonly Dropdown resolution;

    public PlayerSettings(Slider master, Slider music, Slider effects, Dropdown display,
        Dropdown resolution, Button saveButton, Action saved)
    {
        this.master = master;
        this.music = music;
        this.effects = effects;
        this.display = display;
        this.resolution = resolution;
        saveButton.onClick.AddListener(() =>
        {
            Save();
            saved();
        });
    }

    public void Reload()
    {
        master.SetValueWithoutNotify(PlayerPrefs.GetFloat(MasterKey, 1f));
        music.SetValueWithoutNotify(PlayerPrefs.GetFloat(MusicKey, 1f));
        effects.SetValueWithoutNotify(PlayerPrefs.GetFloat(EffectsKey, 1f));
        int mode = Mathf.Max(0, Array.IndexOf(DisplayModes, Screen.fullScreenMode));
        display.SetValueWithoutNotify(PlayerPrefs.GetInt(DisplayKey, mode));
        resolution.SetValueWithoutNotify(PlayerPrefs.GetInt(ResolutionKey, ClosestResolution()));
    }

    // 1. 지금 고른 슬라이더와 드롭다운 값을 각 사용자의 로컬 설정에 저장합니다. 2. 오디오와 화면 값을 게임에 적용합니다. 다음 실행 때도 저장된 값으로 시작합니다.
    private void Save()
    {
        PlayerPrefs.SetFloat(MasterKey, master.value);
        PlayerPrefs.SetFloat(MusicKey, music.value);
        PlayerPrefs.SetFloat(EffectsKey, effects.value);
        PlayerPrefs.SetInt(DisplayKey, display.value);
        PlayerPrefs.SetInt(ResolutionKey, resolution.value);
        PlayerPrefs.Save();
        ApplyAudio();
        ApplyDisplay();
        SoundManager.Play(SoundEffect.UIClick);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RestoreSavedSettings()
    {
        ApplyAudio();
        ApplyDisplay();
    }

    private static void ApplyAudio()
    {
        AudioListener.volume = Mathf.Clamp01(PlayerPrefs.GetFloat(MasterKey, 1f));
        SoundManager.Instance?.SetVolumeLevels(
            Mathf.Clamp01(PlayerPrefs.GetFloat(MusicKey, 1f)),
            Mathf.Clamp01(PlayerPrefs.GetFloat(EffectsKey, 1f)));
    }

    private static void ApplyDisplay()
    {
        if (!PlayerPrefs.HasKey(DisplayKey) || !PlayerPrefs.HasKey(ResolutionKey))
            return;
        var mode = DisplayModes[Mathf.Clamp(PlayerPrefs.GetInt(DisplayKey), 0, DisplayModes.Length - 1)];
        var size = Resolutions[Mathf.Clamp(PlayerPrefs.GetInt(ResolutionKey), 0, Resolutions.Length - 1)];
        Screen.SetResolution(size.x, size.y, mode);
    }

    private static int ClosestResolution()
    {
        int closest = 0;
        var current = new Vector2Int(Screen.width, Screen.height);
        for (int i = 1; i < Resolutions.Length; i++)
            if ((Resolutions[i] - current).sqrMagnitude < (Resolutions[closest] - current).sqrMagnitude)
                closest = i;
        return closest;
    }
}

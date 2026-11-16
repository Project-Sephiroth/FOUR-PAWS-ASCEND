using System;
using UnityEngine;
using UnityEngine.UI;

public sealed class SettingsMenuView : MonoBehaviour
{
    [SerializeField] private Slider master;
    [SerializeField] private Slider music;
    [SerializeField] private Slider effects;
    [SerializeField] private Dropdown display;
    [SerializeField] private Dropdown resolution;
    [SerializeField] private Button saveButton;
    [SerializeField] private Button closeButton;
    private PlayerSettings settings;

    // Save는 값을 적용하고 닫습니다. X는 값을 저장하지 않고 닫는 동작만 호출합니다.
    internal void Bind(Action close)
    {
        settings = new PlayerSettings(master, music, effects, display, resolution, saveButton, close);
        closeButton.onClick.AddListener(() => close());
    }

    internal void Reload() => settings.Reload();
}
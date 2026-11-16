using System;
using UnityEngine;
using UnityEngine.UI;

public sealed class SettingsShortcutView : MonoBehaviour
{
    [SerializeField] private Button button;

    internal bool ContainsPointer => button.isActiveAndEnabled &&
        RectTransformUtility.RectangleContainsScreenPoint((RectTransform)button.transform, Input.mousePosition);

    internal void Bind(Action open) => button.onClick.AddListener(() => open());

    internal void SetVisible(bool visible) => button.gameObject.SetActive(visible);
}
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Button))]
public sealed class ButtonClickSound : MonoBehaviour
{
    private Button button;

    private void Awake()
    {
        button = GetComponent<Button>();
        button.onClick.AddListener(PlayClick);
    }

    private void OnDestroy()
    {
        if (button != null)
            button.onClick.RemoveListener(PlayClick);
    }

    private void PlayClick() => SoundManager.Play(SoundEffect.UIClick);
}

using TMPro;
using UnityEngine;

[RequireComponent(typeof(TextMeshProUGUI))]
public sealed class IntroTypewriter : MonoBehaviour
{
    [SerializeField, Min(1f)] private float charactersPerSecond = 24f;
    private TextMeshProUGUI label;
    private int characterCount;
    private float elapsed;

    public bool IsTyping { get; private set; }

    public void Show(string line)
    {
        if (!label)
            label = GetComponent<TextMeshProUGUI>();

        label.text = line;
        label.maxVisibleCharacters = 0;
        label.ForceMeshUpdate();
        characterCount = label.textInfo.characterCount;
        elapsed = 0f;
        IsTyping = characterCount > 0;
    }

    public void Reveal()
    {
        label.maxVisibleCharacters = characterCount;
        IsTyping = false;
    }

    private void Update()
    {
        if (!IsTyping)
            return;

        elapsed += Time.unscaledDeltaTime;
        int visible = Mathf.FloorToInt(elapsed * charactersPerSecond);
        if (visible >= characterCount)
            Reveal();
        else
            label.maxVisibleCharacters = visible;
    }
}

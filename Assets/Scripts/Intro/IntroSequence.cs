using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class IntroSequence : MonoBehaviour
{
    [SerializeField] private IntroNarration narration;
    [SerializeField] private Canvas overlay;
    [SerializeField] private Image artwork;
    [SerializeField] private AspectRatioFitter artworkAspect;
    [SerializeField] private IntroAdvanceInput advanceInput;
    [SerializeField] private IntroTypewriter typewriter;
    [SerializeField] private Transform sceneCamera;

    private int cutIndex;
    private int lineIndex;
    private bool cameraPlaying;
    private bool loadingTitle;

    private void OnEnable() => advanceInput.Advance += Advance;

    private void OnDisable() => advanceInput.Advance -= Advance;

    private void Start() => ShowCut();

    private void ShowCut()
    {
        while (cutIndex < narration.cuts.Length)
        {
            var cut = narration.cuts[cutIndex];
            if (cut.lines != null && cut.lines.Length > 0)
            {
                var sprite = Resources.Load<Sprite>("CutScene/CutScene" + cut.id);
                artwork.sprite = sprite;
                artwork.enabled = sprite != null;
                if (sprite)
                    artworkAspect.aspectRatio = sprite.rect.width / sprite.rect.height;

                lineIndex = 0;
                typewriter.Show(cut.lines[lineIndex]);
                return;
            }

            cutIndex++;
        }

        cameraPlaying = true;
        overlay.gameObject.SetActive(false);
        StartCoroutine(PlayCamera());
    }

    private void Advance()
    {
        if (cameraPlaying || loadingTitle)
            return;

        if (typewriter.IsTyping)
        {
            typewriter.Reveal();
            return;
        }

        var lines = narration.cuts[cutIndex].lines;
        lineIndex++;
        if (lineIndex < lines.Length)
        {
            typewriter.Show(lines[lineIndex]);
            return;
        }

        cutIndex++;
        ShowCut();
    }

    private IEnumerator PlayCamera()
    {
        Vector3 start = sceneCamera.position;
        Vector3 lowered = start + Vector3.down * 1.5f;

        for (float time = 0f; time < 1.8f; time += Time.unscaledDeltaTime)
        {
            sceneCamera.position = Vector3.Lerp(start, lowered, Mathf.SmoothStep(0f, 1f, time / 1.8f));
            yield return null;
        }

        for (float time = 0f; time < 0.45f; time += Time.unscaledDeltaTime)
        {
            var offset = new Vector3(Mathf.Sin(time * 131f), Mathf.Cos(time * 167f), 0f);
            sceneCamera.position = lowered + offset * 0.24f;
            yield return null;
        }

        for (float time = 0f; time < 0.55f; time += Time.unscaledDeltaTime)
        {
            float progress = time / 0.55f;
            sceneCamera.position = lowered + Vector3.up * (12f * progress * progress);
            yield return null;
        }

        sceneCamera.position = lowered + Vector3.up * 12f;
        FinishIntro();
    }

    private async void FinishIntro()
    {
        if (loadingTitle)
            return;

        loadingTitle = true;
        await ScenePresentation.LoadLocalAsync(nameof(NetworkGameManager.SceneName.Title));
    }

    public void Skip()
    {
        if (loadingTitle)
            return;

        loadingTitle = true;
        SceneManager.LoadScene("Assets/Scenes/Title.unity");
    }
}

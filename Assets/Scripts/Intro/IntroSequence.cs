using System.Collections;
using UnityEngine;
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
    [SerializeField] private Animator[] characters;

    private static readonly int IsJump = Animator.StringToHash("IsJump");

    private int cutIndex;
    private int lineIndex;
    private bool cameraPlaying;
    private bool loadingTitle;

    private void Awake()
    {
        foreach (var character in characters)
        {
            character.GetComponent<Rigidbody2D>().simulated = false;
            character.updateMode = AnimatorUpdateMode.UnscaledTime;
        }
    }

    private void OnEnable() => advanceInput.Advance += Advance;

    private void OnDisable() => advanceInput.Advance -= Advance;

    private void Start() => ShowCut();

    private void ShowCut(bool playTransitionSound = false)
    {
        if (playTransitionSound)
            SoundManager.Play(SoundEffect.CutScene);

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

        SoundManager.Play(SoundEffect.UIClick);
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
        ShowCut(playTransitionSound: true);
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

        var startingPositions = new Vector3[characters.Length];
        for (int i = 0; i < characters.Length; i++)
        {
            startingPositions[i] = characters[i].transform.position;
            characters[i].SetBool(IsJump, true);
        }

        const float fallDuration = 0.9f;
        const float fallDistance = 9.5f;
        for (float time = 0f; time < fallDuration; time += Time.unscaledDeltaTime)
        {
            float progress = time / fallDuration;
            float descent = progress * progress;
            float shakeStrength = 0.24f * (1f - progress);
            var shake = new Vector3(Mathf.Sin(time * 131f), Mathf.Cos(time * 167f), 0f);
            sceneCamera.position = lowered + Vector3.down * (0.5f * descent) + shake * shakeStrength;

            for (int i = 0; i < characters.Length; i++)
                characters[i].transform.position = startingPositions[i] + Vector3.down * (fallDistance * descent);

            yield return null;
        }

        sceneCamera.position = lowered + Vector3.down * 0.5f;
        for (int i = 0; i < characters.Length; i++)
            characters[i].transform.position = startingPositions[i] + Vector3.down * fallDistance;
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

        FinishIntro();
    }
}

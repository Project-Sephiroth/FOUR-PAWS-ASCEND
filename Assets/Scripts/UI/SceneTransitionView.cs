using UnityEngine;
using UnityEngine.UI;
using Character = MyEnum.CharacterType;

[ExecuteAlways]
[RequireComponent(typeof(Animator), typeof(Canvas))]
public sealed class SceneTransitionView : MonoBehaviour
{
    private static readonly int CoverState = Animator.StringToHash("Base Layer.Cover");
    private static readonly int RevealState = Animator.StringToHash("Base Layer.Reveal");
    private static readonly int MaskTexture = Shader.PropertyToID("_MaskTex");
    private static readonly int SpriteRect = Shader.PropertyToID("_SpriteRect");
    private static readonly int HoleSize = Shader.PropertyToID("_HoleSize");
    private static readonly int BlackOpacity = Shader.PropertyToID("_BlackOpacity");

    [SerializeField] private RawImage overlay;
    [SerializeField] private AnimationClip coverClip;
    [SerializeField] private Sprite[] idleSprites;
    // openness는 실루엣 구멍 크기, opacity는 검은 부분의 진하기입니다. 두 값을 Animation 창의 키로 편집할 수 있습니다.
    [SerializeField, Range(0f, 1f)] private float openness;
    [SerializeField, Range(0f, 1f)] private float opacity = 1f;

    private Animator animator;
    private Material maskMaterial;
    private Sprite currentSprite;

    public float CoverSeconds => coverClip.length;
    public bool IsCovering { get; private set; }
    public bool IsCovered => IsCovering && Progress >= 1f;
    public bool BlocksInput => IsCovering || Progress < 1f;
    private float Progress => Mathf.Clamp01(animator.GetCurrentAnimatorStateInfo(0).normalizedTime);

    public bool Begin()
    {
        animator = GetComponent<Animator>();
        if (overlay == null || coverClip == null || animator.runtimeAnimatorController == null ||
            !animator.HasState(0, CoverState) || !animator.HasState(0, RevealState))
        {
            Debug.LogError("The scene transition prefab needs its overlay, cover clip, and animator states.", this);
            return false;
        }
        if (overlay.material != overlay.defaultMaterial)
        {
            maskMaterial = new Material(overlay.material);
            overlay.material = maskMaterial;
        }
        SetCharacter(Character.None);
        Play(RevealState, 0f);
        return true;
    }

    // 캐릭터 이미지를 고르고 닫기 상태를 재생합니다. 열리는 중에 닫기를 요청하면 진행률을 반대로 계산해 현재 화면에서 이어집니다.
    public void Cover(Character character, bool immediate)
    {
        if (IsCovering)
        {
            if (immediate)
                Play(CoverState, 1f);
            return;
        }

        float start = immediate ? 1f : 1f - Progress;
        IsCovering = true;
        SetCharacter(character);
        Play(CoverState, start);
    }

    public void Reveal()
    {
        if (!IsCovering)
            return;

        float start = 1f - Progress;
        IsCovering = false;
        Play(RevealState, start);
    }

    private void Play(int state, float start)
    {
        animator.Play(state, 0, start);
        animator.Update(0f);
        Draw();
    }

    private void LateUpdate()
    {
        if (Application.isPlaying && animator != null)
            Draw();
    }

    private void Draw()
    {
        overlay.enabled = BlocksInput;
        if (!overlay.enabled)
            return;

        if (maskMaterial == null || currentSprite == null)
        {
            overlay.material = null;
            overlay.color = new Color(0f, 0f, 0f, 1f - openness);
            return;
        }

        SetMaskSize(maskMaterial, currentSprite);
    }

    private void SetMaskSize(Material material, Sprite sprite)
    {
        float aspect = sprite.rect.width / sprite.rect.height;
        float screenAspect = (float)Mathf.Max(1, Screen.width) / Mathf.Max(1, Screen.height);
        float height = 5f * Mathf.Max(1f, screenAspect / aspect) * openness;
        material.SetVector(HoleSize, new Vector4(height * aspect / screenAspect, height, 0f, 0f));
        material.SetFloat(BlackOpacity, opacity);
    }

    private void SetCharacter(Character character)
    {
        if (idleSprites == null || idleSprites.Length == 0)
            return;

        int index = (int)character - 1;
        currentSprite = idleSprites[index >= 0 && index < idleSprites.Length
            ? index
            : Random.Range(0, idleSprites.Length)];
        if (maskMaterial == null || currentSprite == null)
            return;

        overlay.material = maskMaterial;
        overlay.color = Color.white;
        SetMaskSprite(maskMaterial, currentSprite);
    }

    private static void SetMaskSprite(Material material, Sprite sprite)
    {
        var texture = sprite.texture;
        Rect rect = sprite.textureRect;
        material.SetTexture(MaskTexture, texture);
        material.SetVector(SpriteRect, new Vector4(rect.x / texture.width, rect.y / texture.height,
            rect.width / texture.width, rect.height / texture.height));
    }

#if UNITY_EDITOR
    private Material previewMaterial;

    private void OnEnable()
    {
        if (!Application.isPlaying)
            Canvas.willRenderCanvases += DrawPreview;
    }

    private void OnDisable()
    {
        Canvas.willRenderCanvases -= DrawPreview;
        ClearPreview();
    }

    private void DrawPreview()
    {
        if (Application.isPlaying || !UnityEditor.AnimationMode.InAnimationMode() ||
            overlay == null || overlay.material == overlay.defaultMaterial ||
            idleSprites == null || idleSprites.Length == 0 || idleSprites[0] == null)
        {
            ClearPreview();
            return;
        }

        if (previewMaterial == null)
        {
            previewMaterial = new Material(overlay.material) { hideFlags = HideFlags.HideAndDontSave };
            SetMaskSprite(previewMaterial, idleSprites[0]);
        }
        SetMaskSize(previewMaterial, idleSprites[0]);
        overlay.canvasRenderer.SetMaterial(previewMaterial, 0);
    }

    private void ClearPreview()
    {
        if (previewMaterial == null)
            return;
        DestroyImmediate(previewMaterial);
        if (overlay != null)
            overlay.SetMaterialDirty();
    }
#endif

    private void OnDestroy()
    {
        if (maskMaterial != null)
            Destroy(maskMaterial);
    }
}

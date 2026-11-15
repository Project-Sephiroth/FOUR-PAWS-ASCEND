using UnityEngine;

public class InteractableOutline : MonoBehaviour
{
    [SerializeField] private SpriteRenderer spriteRenderer;
    private Material previousMaterial;
    private MaterialPropertyBlock propertyBlock;
    private bool isOutlined;
    private static Material outlineMaterial;
    private static readonly int OutlineEnabled = Shader.PropertyToID("_OutlineEnabled");
    private static readonly int OutlineColor = Shader.PropertyToID("_OutlineColor");

    // 일부 씬 오브젝트에는 외곽선 컴포넌트가 없습니다. 처음 외곽선을 요청할 때만 붙입니다.
    public static void SetTarget(GameObject target, bool enabled)
    {
        if (target == null)
            return;
        var outline = target.GetComponentInChildren<InteractableOutline>();
        if (outline == null && enabled)
            outline = target.AddComponent<InteractableOutline>();
        if (outline != null)
            outline.SetOutline(enabled);
    }

    public void SetOutline(bool enabled)
    {
        if (isOutlined == enabled)
            return;
        if (spriteRenderer == null)
            spriteRenderer = FindModel();
        if (spriteRenderer == null)
            return;

        propertyBlock ??= new MaterialPropertyBlock();
        if (enabled)
        {
            outlineMaterial ??= Resources.Load<Material>("InteractionOutline");
            if (outlineMaterial == null)
                return;
            previousMaterial = spriteRenderer.sharedMaterial;
            spriteRenderer.sharedMaterial = outlineMaterial;
        }

        // 외곽선 설정만 바꿉니다. 다른 효과가 넣은 렌더러 설정은 그대로 둡니다.
        spriteRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetFloat(OutlineEnabled, enabled ? 1f : 0f);
        propertyBlock.SetColor(OutlineColor, new Color(0.15f, 1f, 1f));
        spriteRenderer.SetPropertyBlock(propertyBlock);
        if (!enabled)
            spriteRenderer.sharedMaterial = previousMaterial;
        isOutlined = enabled;
    }

    private SpriteRenderer FindModel()
    {
        var direct = GetComponent<SpriteRenderer>();
        if (direct != null)
            return direct;
        SpriteRenderer first = null;
        foreach (var candidate in GetComponentsInChildren<SpriteRenderer>())
        {
            if (candidate.sprite == null)
                continue;
            if (candidate.name.StartsWith("model", System.StringComparison.OrdinalIgnoreCase))
                return candidate;
            first ??= candidate;
        }
        return first;
    }

    private void OnDisable() => SetOutline(false);
}

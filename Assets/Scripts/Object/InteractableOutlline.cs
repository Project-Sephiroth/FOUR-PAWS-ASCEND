using System;
using UnityEngine;

public class InteractableOutline : MonoBehaviour
{
    [SerializeField] private SpriteRenderer spriteRenderer;

    private MaterialPropertyBlock propertyBlock;

    private static readonly int OutlineEnabled = Shader.PropertyToID("_OutlineEnabled");

    private void Awake()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();

        propertyBlock = new MaterialPropertyBlock();

        SetOutline(false);
    }

    public void SetOutline(bool enabled)
    {
        spriteRenderer.GetPropertyBlock(propertyBlock);

        propertyBlock.SetFloat(OutlineEnabled, enabled ? 3f : 0f);

        spriteRenderer.SetPropertyBlock(propertyBlock);
    }
}
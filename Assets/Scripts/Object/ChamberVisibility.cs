using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 유리창에 쓰이는 투명/불투명 제어
/// </summary>
public class ChamberVisibility : MonoBehaviour
{
    [SerializeField] private Vector2 chamberSize;
    [SerializeField] private GameObject opaqueGlass;
    private readonly HashSet<SpriteRenderer> hidden = new();

    private void LateUpdate()
    {
        var network = NetworkGameManager.Instance;
        if (network == null || network.Object == null || !network.Object.IsValid || !network.TryGetGameplayPlayer(network.Runner.LocalPlayer, out var local) || local == null)
            return;

        bool localInside = Contains(local.transform.position);
        if (opaqueGlass != null && opaqueGlass.activeSelf == localInside)
            opaqueGlass.SetActive(!localInside);
        foreach (var other in NetworkGameManager.GetPlayerObjects(network.Runner))
        {
            bool hide = other != local && !localInside && Contains(other.transform.position);
            foreach (var sprite in other.GetComponentsInChildren<SpriteRenderer>())
            {
                if (hide && !sprite.forceRenderingOff)
                {
                    hidden.Add(sprite);
                    sprite.forceRenderingOff = true;
                }
                else if (!hide && hidden.Remove(sprite))
                    sprite.forceRenderingOff = false;
            }
        }
    }

    private bool Contains(Vector3 position)
    {
        Vector3 delta = position - transform.position;
        return Mathf.Abs(delta.x) <= chamberSize.x * 0.5f && Mathf.Abs(delta.y) <= chamberSize.y * 0.5f;
    }

    private void OnDisable()
    {
        foreach (var sprite in hidden)
            if (sprite != null)
                sprite.forceRenderingOff = false;
        hidden.Clear();
    }
}

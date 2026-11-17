using System.Collections.Generic;
using Fusion;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class SidePortal : MonoBehaviour
{
    [SerializeField] private Vector2 destination;
    private static readonly HashSet<SidePortal> Portals = new();
    private Collider2D trigger;

    private void OnEnable()
    {
        trigger = GetComponent<Collider2D>();
        Portals.Add(this);
    }

    private void OnDisable() => Portals.Remove(this);

    internal static bool TryUse(Rigidbody2D body)
    {
        if (body == null || !body.TryGetComponent(out Collider2D playerCollider) ||
            GameMenu.BlocksWorldClick || LocalMultiplayerDebug.BlocksWorldClick || HackingConsole.IsLocalHackOpen)
            return false;

        // Mover가 점프 입력을 한 번 소비합니다. 반대편에 도착한 뒤 다시 누르면 돌아갈 수 있습니다.
        foreach (var portal in Portals)
        {
            if (portal.gameObject.scene != body.gameObject.scene || !portal.trigger.enabled ||
                !portal.trigger.Distance(playerCollider).isOverlapped)
                continue;

            Vector2 target = portal.destination;
            body.linearVelocity = Vector2.zero;
            body.position = target;
            Vector3 position = new(target.x, target.y, body.transform.position.z);
            if (body.TryGetComponent(out NetworkTransform sync))
                sync.Teleport(position);
            body.transform.position = position;
            return true;
        }
        return false;
    }
}

using System.Collections.Generic;
using Fusion;
using UnityEngine;

public class SidePortal : MonoBehaviour
{
    [SerializeField] private Vector2 destination;
    [SerializeField, Min(0f)] private float holdSeconds = 2f;
    // enteredAt에는 몸체가 트리거에 처음 머문 시간을 기록합니다. 이동한 몸체는 트리거에서 나갈 때까지 중복 이동을 막습니다.
    private readonly Dictionary<Rigidbody2D, float> enteredAt = new();
    private readonly HashSet<Rigidbody2D> teleported = new();

    private void OnTriggerStay2D(Collider2D other)
    {
        var body = other.attachedRigidbody;
        if (!IsPlayerBody(other, body) || teleported.Contains(body) ||
            !body.TryGetComponent(out NetworkObject player) || !player.IsValid || !player.HasStateAuthority)
            return;

        if (!enteredAt.TryGetValue(body, out float start))
        {
            enteredAt[body] = Time.fixedTime;
            return;
        }
        if (Time.fixedTime - start < holdSeconds)
            return;

        teleported.Add(body);
        body.linearVelocity = Vector2.zero;
        body.position = destination;
        if (body.TryGetComponent(out NetworkTransform networkTransform))
            networkTransform.Teleport(new Vector3(destination.x, destination.y, body.transform.position.z));
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        var body = other.attachedRigidbody;
        if (!IsPlayerBody(other, body))
            return;
        enteredAt.Remove(body);
        teleported.Remove(body);
    }

    private static bool IsPlayerBody(Collider2D collider, Rigidbody2D body) =>
        body != null && body.CompareTag("Player") && collider.gameObject == body.gameObject && !collider.isTrigger;

    private void OnDisable()
    {
        enteredAt.Clear();
        teleported.Clear();
    }
}
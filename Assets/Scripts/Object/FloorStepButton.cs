using System.Collections.Generic;
using Fusion;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public sealed class FloorStepButton : MonoBehaviour
{
    [SerializeField] private MovingFloor floor;
    private readonly HashSet<Rigidbody2D> pressing = new();

    // 플레이어의 몸만 검사합니다. 머리와 감지 트리거까지 세면 한 번 밟아도 여러 번 방향이 바뀝니다.
    private void OnTriggerStay2D(Collider2D other)
    {
        var body = other.attachedRigidbody;
        if (floor == null || floor.Object == null || !floor.Object.IsValid || !IsPlayerBody(other, body) ||
            !body.TryGetComponent(out NetworkObject player) || !player.IsValid || !player.HasStateAuthority ||
            !pressing.Add(body))
            return;

        floor.StartMove(true);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        var body = other.attachedRigidbody;
        if (IsPlayerBody(other, body))
            pressing.Remove(body);
    }

    private static bool IsPlayerBody(Collider2D collider, Rigidbody2D body) =>
        body != null && body.CompareTag("Player") && collider.gameObject == body.gameObject && !collider.isTrigger;

    private void OnDisable() => pressing.Clear();
}
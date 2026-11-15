using Fusion;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 특정 키 오브젝트를 가져야 열리는 문
/// </summary>
public class KeyDoor : NetworkBehaviour
{
    [SerializeField] private bool isBring;
    [SerializeField] private bool disactive;
    [SerializeField] private bool acceptTongueDelivery;
    [SerializeField] private bool acceptThrownKey;
    [SerializeField] private bool consumeAfterCrossing;
    [SerializeField] private Key[] openKeys;
    [SerializeField] private UnityEvent openAction;

    [Networked, OnChangedRender(nameof(ApplyOpened))] public NetworkBool IsOpened { get; private set; }
    [Networked] private NetworkObject DeliveringPlayer { get; set; }
    [Networked] private float DoorX { get; set; }
    [Networked] private float ApproachSide { get; set; }

    private Collider2D doorCollider;
    private bool appliedOpen;

    public override void Spawned()
    {
        doorCollider = GetComponent<Collider2D>();
        ApplyOpened();
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (Object == null || !Object.IsValid || !HasStateAuthority || IsOpened ||
            acceptTongueDelivery || acceptThrownKey || isBring)
            return;

        var body = collision.rigidbody;
        if (body == null || !body.CompareTag("Player") || openKeys == null || openKeys.Length == 0)
            return;

        foreach (var key in openKeys)
        {
            if (key == null || !key.isEarned)
                return;
        }

        Open();
    }

    private void CheckCarriedKeys()
    {
        NetworkObject player = openKeys[0] != null ? openKeys[0].GetCarryingPlayer() : null;
        if (player == null || !player.TryGetComponent(out Collider2D bodyCollider) || !bodyCollider.enabled)
            return;

        Bounds doorBounds = doorCollider.bounds;
        doorBounds.Expand(0.3f);
        if (!doorBounds.Intersects(bodyCollider.bounds))
            return;

        foreach (var key in openKeys)
        {
            if (key == null || key.GetCarryingPlayer() != player)
                return;
        }

        if (consumeAfterCrossing)
        {
            DeliveringPlayer = player;
            DoorX = doorCollider.bounds.center.x;
            ApproachSide = player.transform.position.x < DoorX ? -1f : 1f;
        }

        Open();
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority)
            return;

        if (IsOpened)
        {
            if (DeliveringPlayer != null &&
                (DeliveringPlayer.transform.position.x - DoorX) * ApproachSide < -0.25f)
            {
                ConsumeKeys();
                DeliveringPlayer = null;
            }
            return;
        }

        if (doorCollider == null || openKeys == null || openKeys.Length == 0)
            return;

        if (acceptThrownKey || acceptTongueDelivery)
            CheckReleasedKeys();
        else if (isBring)
            CheckCarriedKeys();
    }

    private void CheckReleasedKeys()
    {
        float deliveryDistance = acceptThrownKey ? 0.45f : 1.25f;
        foreach (var key in openKeys)
        {
            if (key == null || !key.TryGetComponent(out MovingObject item) || item.IsHeld)
                return;
            if (acceptThrownKey ? !item.ReleasedByThrower : !item.ReleasedByTongue)
                return;

            Vector2 position = key.transform.position;
            Vector2 nearest = doorCollider.ClosestPoint(position);
            if ((position - nearest).sqrMagnitude > deliveryDistance * deliveryDistance)
                return;
        }

        Open();
    }

    private void Open()
    {
        IsOpened = true;
        ApplyOpened();
        if (disactive && !consumeAfterCrossing)
            ConsumeKeys();
    }

    private void ConsumeKeys()
    {
        if (!disactive)
            return;

        foreach (var key in openKeys)
        {
            if (key != null && key.Object != null && key.Object.IsValid)
                key.RPC_Consume(this);
        }
    }

    private void ApplyOpened()
    {
        if (!IsOpened || appliedOpen)
            return;

        appliedOpen = true;
        if (HasStateAuthority)
            openAction?.Invoke();
    }
}

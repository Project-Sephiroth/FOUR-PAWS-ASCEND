using Fusion;
using UnityEngine;

public class Elevator : NetworkBehaviour
{
    [SerializeField] private GameObject floor;
    [SerializeField] private SpriteRenderer column;
    [SerializeField] private float originPos;
    [SerializeField] private float dest;
    [SerializeField] private float speed;
    [SerializeField] private bool keepMove;

    [Networked] private NetworkBool isUp { get; set; }
    [Networked] private NetworkBool isMoving { get; set; }
    [Networked] private float FloorY { get; set; }

    [SerializeField, Min(0f)] private float exitRise = 6f;
    [Networked] private NetworkBool ExitStarted { get; set; }
    private Collider2D deck;
    private float initialY;
    public bool ExitFinished => ExitStarted && !isMoving;
    private float lastRenderedY;
    private bool wasMoving;
    private Vector2 initialColumnSize;

    private void Awake()
    {
        if (floor == null)
        {
            var found = transform.Find("Floor");
            if (found != null)
                floor = found.gameObject;
        }
        if (floor != null)
        {
            initialY = floor.transform.position.y;
            deck = floor.GetComponentInChildren<Collider2D>();
        }
        if (column != null)
            initialColumnSize = column.size;
    }

    public override void Spawned()
    {
        if (HasStateAuthority)
            FloorY = initialY;

        ApplyPosition(false);
        lastRenderedY = FloorY;
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority || !isMoving || floor == null)
            return;

        float targetY = ExitStarted ? initialY + exitRise : isUp ? dest : originPos;
        FloorY = Mathf.MoveTowards(FloorY, targetY, Mathf.Max(0f, speed) * Runner.DeltaTime);
        ApplyPosition();

        if (Mathf.Approximately(FloorY, targetY))
        {
            if (keepMove && !ExitStarted)
                isUp = !isUp;
            else
                isMoving = false;
        }
    }

    public override void Render()
    {
        if (!HasStateAuthority)
            ApplyPosition();

        float delta = FloorY - lastRenderedY;
        if (Mathf.Abs(delta) > 0.0001f && !wasMoving)
            SoundManager.Play(delta > 0f ? SoundEffect.ElevatorUp : SoundEffect.ElevatorDown);

        wasMoving = Mathf.Abs(delta) > 0.0001f || (wasMoving && isMoving);
        lastRenderedY = FloorY;
    }

    public bool AllPlayersOnDeck(NetworkRunner runner)
    {
        int count = 0;
        foreach (var player in NetworkGameManager.GetPlayerObjects(runner))
        {
            if (!IsOnDeck(player))
                return false;
            count++;
        }
        return count > 0;
    }

    public void BeginExit()
    {
        if (Object != null && Object.IsValid && HasStateAuthority && !ExitStarted)
        {
            ExitStarted = true;
            isMoving = true;
        }
    }

    private bool IsOnDeck(NetworkObject player)
    {
        if (deck == null || player == null || !player.TryGetComponent(out Collider2D body))
            return false;
        Bounds top = deck.bounds;
        Bounds rider = body.bounds;
        return rider.max.x > top.min.x && rider.min.x < top.max.x &&
            Mathf.Abs(rider.min.y - top.max.y) <= 0.27f;
    }

    public void DoUp(bool up)
    {
        if (Object != null && Object.IsValid)
            RPC_SetDirection(up);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_SetDirection(bool up)
    {
        if (ExitStarted)
            return;
        isUp = up;
        isMoving = true;
    }

    public void Elevate(float amount)
    {
        if (HasStateAuthority)
        {
            FloorY += amount;
            ApplyPosition();
        }
    }

    private void ApplyPosition(bool carryRiders = true)
    {
        if (floor != null)
        {
            Vector3 position = floor.transform.position;
            float deltaY = FloorY - position.y;
            // 바닥이 움직이기 직전에 발이 닿은 플레이어를 찾습니다. 자기 캐릭터의 물리 몸체만 옮겨 각 클라이언트에서 한 번만 적용합니다.
            if (carryRiders && Mathf.Abs(deltaY) > 0.0001f && Runner != null)
                foreach (var player in NetworkGameManager.GetPlayerObjects(Runner))
                    if (player.HasStateAuthority && IsOnDeck(player) && player.TryGetComponent(out Rigidbody2D body) &&
                        body.simulated && body.linearVelocity.y < 0.15f)
                    {
                        Vector2 next = body.position + Vector2.up * deltaY;
                        player.transform.position += Vector3.up * deltaY;
                        body.position = next;
                    }
            position.y = FloorY;
            floor.transform.position = position;
        }

        if (column != null)
            column.size = initialColumnSize + Vector2.up * (FloorY - initialY);
    }
}

using Fusion;
using UnityEngine;

public sealed class RollingDrumSpawner : NetworkBehaviour
{
    private const int PoolSize = 3;

    [SerializeField] private NetworkObject drumPrefab;
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private BranchPuzzle encounter;
    [SerializeField, Min(0.1f)] private float respawnDelay = 3f;
    [SerializeField, Min(0.1f)] private float travelDistance = 26.2f;
    [SerializeField] private Vector2 speedRange = new(1.8f, 3.4f);

    [Networked, Capacity(PoolSize)] private NetworkArray<NetworkId> Pool => default;
    [Networked] private int NextSlot { get; set; }
    [Networked] private TickTimer LaunchDelay { get; set; }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority || !Runner.IsForward || drumPrefab == null || spawnPoint == null ||
            encounter == null || encounter.Object == null || !encounter.Object.IsValid)
            return;

        // 한 번 만든 드럼통을 슬롯에 보관합니다. 비어 있는 슬롯만 생성합니다.
        for (int slot = 0; slot < PoolSize; slot++)
        {
            if (FindDrum(Runner, slot) != null)
                continue;

            var item = Runner.Spawn(drumPrefab, spawnPoint.position, spawnPoint.rotation);
            if (item != null)
                Pool.Set(slot, item.Id);
        }

        if (!encounter.EnemyEnabled())
        {
            for (int slot = 0; slot < PoolSize; slot++)
                FindDrum(Runner, slot)?.Deactivate();
            LaunchDelay = default;
            return;
        }

        if (!LaunchDelay.ExpiredOrNotRunning(Runner))
            return;

        // 이전 출발 슬롯 다음부터 확인합니다. 굴러가거나 깜박이는 드럼통은 건너뜁니다.
        for (int offset = 0; offset < PoolSize; offset++)
        {
            int slot = (NextSlot + offset) % PoolSize;
            var drum = FindDrum(Runner, slot);
            if (drum == null || !drum.IsAvailable)
                continue;

            float minimum = Mathf.Max(0.1f, speedRange.x);
            drum.Launch(encounter.Object, Random.Range(minimum, Mathf.Max(minimum, speedRange.y)), travelDistance);
            NextSlot = (slot + 1) % PoolSize;
            LaunchDelay = TickTimer.CreateFromSeconds(Runner, respawnDelay);
            break;
        }
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (!hasState || !runner.IsRunning)
            return;

        for (int slot = 0; slot < PoolSize; slot++)
        {
            var drum = FindDrum(runner, slot);
            if (drum != null && drum.HasStateAuthority)
                runner.Despawn(drum.Object);
        }
    }

    private RollingDrum FindDrum(NetworkRunner runner, int slot) =>
        runner.TryFindObject(Pool.Get(slot), out var item) ? item.GetComponent<RollingDrum>() : null;
}

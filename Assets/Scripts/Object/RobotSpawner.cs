using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public sealed class RobotSpawner : MonoBehaviour
{
    public const float DefaultDelay = 5f;

    [SerializeField, Min(0.1f)] private float respawnDelay = DefaultDelay; //리스폰 딜레이
    [SerializeField] private bool useRandomDroneSchedule;

    [Header("스폰 설정")]
    [SerializeField, Range(1, 3)] private int maxDrones = 3;
    [SerializeField, Min(0.1f)] private float minSpawnDelay = 2f;
    [SerializeField, Min(0.1f)] private float maxSpawnDelay = 5f;

    private readonly bool[] waitingSlots = new bool[3];
    private readonly float[] firstSpawnTimes = new float[3];

    [SerializeField] private NetworkObject robotPrefab;
    [SerializeField] private BranchPuzzle encounter;

    [SerializeField] private Vector2 patrolOffsets = new(-2f, 4f);
    [SerializeField, Min(0.1f)] private float robotSpeed = 2f;

    private readonly NetworkObject[] pool = new NetworkObject[3];
    private float nextSpawnAt = -1f;
    private NetworkRunner poolRunner;

    private SpriteRenderer model;
    private Color readyColor; //깜박이는 연출을 위해 origin 컬러 캐싱

    public Vector3 SpawnPosition => transform.position;

    private void Awake()
    {
        model = GetComponent<SpriteRenderer>();
        readyColor = model.color;
    }

    public bool AllowsSlot(int slot) => !useRandomDroneSchedule || slot < Mathf.Clamp(maxDrones, 1, 3);

    public TickTimer StartFirstSpawnWait(NetworkRunner runner, int slot)
    {
        if (!useRandomDroneSchedule || !AllowsSlot(slot))
            return default;

        if (firstSpawnTimes[0] <= 0f)
        {
            float time = 0f;
            for (int i = 0; i < firstSpawnTimes.Length; i++)
            {
                time += PickSpawnDelay();
                firstSpawnTimes[i] = time;
            }
        }

        return TickTimer.CreateFromSeconds(runner, firstSpawnTimes[slot]);
    }

    public TickTimer StartRespawnWait(NetworkRunner runner)
    {
        float delay = useRandomDroneSchedule ? PickSpawnDelay() : respawnDelay;
        return TickTimer.CreateFromSeconds(runner, delay);
    }

    private float PickSpawnDelay()
    {
        float minimum = Mathf.Max(0.1f, minSpawnDelay);
        return Random.Range(minimum, Mathf.Max(minimum, maxSpawnDelay));
    }

    public void ShowWaiting(int slot, bool waiting)
    {
        if ((uint)slot < (uint)waitingSlots.Length)
            waitingSlots[slot] = waiting;
    }

    private void Update()
    {
        var network = NetworkGameManager.Instance;
        if (robotPrefab == null || network == null || network.Object == null || !network.Object.IsValid ||
            !network.HasStateAuthority || !network.Runner.IsSceneAuthority)
            return;

        poolRunner = network.Runner;
        if (nextSpawnAt < 0f)
            nextSpawnAt = Time.time + PickSpawnDelay();
        if (Time.time < nextSpawnAt)
            return;

        int limit = Mathf.Clamp(maxDrones, 1, pool.Length);
        for (int slot = 0; slot < limit; slot++)
        {
            if (pool[slot] != null && pool[slot].IsValid)
                continue;
            int index = slot;
            nextSpawnAt = Time.time + PickSpawnDelay();
            pool[slot] = poolRunner.Spawn(robotPrefab, SpawnPosition, Quaternion.identity,
                onBeforeSpawned: (_, item) => item.GetComponent<RobotBase>()
                    .ConfigurePool(this, index, encounter, patrolOffsets + Vector2.one * SpawnPosition.x, robotSpeed));
            break;
        }
    }

    private void OnDestroy()
    {
        if (poolRunner == null || !poolRunner.IsRunning)
            return;
        foreach (var item in pool)
            if (item != null && item.IsValid && item.HasStateAuthority)
                poolRunner.Despawn(item);
    }

    private void LateUpdate()
    {
        if (model == null)
            return;

        bool waiting = waitingSlots[0] || waitingSlots[1] || waitingSlots[2];
        float pulse = waiting ? 0.5f + 0.5f * Mathf.Sin(Time.time * 9f) : 0f;
        model.color = Color.Lerp(readyColor, new Color(0.62f, 1f, 0.82f), pulse);
    }
}

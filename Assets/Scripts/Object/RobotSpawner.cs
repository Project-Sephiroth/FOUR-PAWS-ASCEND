using Fusion;
using UnityEngine;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
[RequireComponent(typeof(SpriteRenderer))]
public sealed class RobotSpawner : MonoBehaviour
{
    public const float DefaultDelay = 5f;
    private const int PoolCapacity = 3;

    [SerializeField, Min(0.1f)] private float respawnDelay = DefaultDelay; //리스폰 딜레이
    [SerializeField] private bool useRandomDroneSchedule;
    [SerializeField] private bool spawnDuringHacks; //해킹 중 출연

    [Header("스폰 설정")]
    [FormerlySerializedAs("maxDrones")]
    [SerializeField, Range(1, PoolCapacity)] private int maxCount = PoolCapacity;
    [SerializeField, Min(0.1f)] private float minSpawnDelay = 2f;
    [SerializeField, Min(0.1f)] private float maxSpawnDelay = 5f;

    private readonly bool[] waitingSlots = new bool[PoolCapacity];
    private readonly float[] firstSpawnTimes = new float[PoolCapacity];

    [SerializeField] private NetworkObject robotPrefab;
    [SerializeField] private Elevator requiredElevator;
    [SerializeField] private bool overrideSpawnY;
    [SerializeField, Tooltip("World Y coordinate of the robot when it spawns or respawns.")]
    private float spawnY;
    [SerializeField] private BranchPuzzle encounter;

    [SerializeField] private Vector2 patrolOffsets = new(-2f, 4f);
    [SerializeField] private bool startPatrolLeft;
    internal bool StartPatrolLeft => startPatrolLeft;
    [SerializeField, Min(0.1f)] private float robotSpeed = 2f;

    private readonly NetworkObject[] pool = new NetworkObject[PoolCapacity];
    private float nextSpawnAt = -1f;
    private int hodDroneWave;
    private NetworkRunner poolRunner;
    private bool elevatorActivated;

    private SpriteRenderer model;
    private Color readyColor; //깜박이는 연출을 위해 origin 컬러 캐싱

    public Vector3 SpawnPosition
    {
        get
        {
            // 스포너 위치에서 시작하고 높이를 따로 지정한 경우에만 Y를 바꿉니다.
            Vector3 position = transform.position;
            if (overrideSpawnY)
                position.y = spawnY;
            return position;
        }
    }

    private void Awake()
    {
        model = GetComponent<SpriteRenderer>();
        readyColor = model.color;
    }

    // 예약된 드론과 필드 로봇 모두 같은 수량 제한으로 슬롯을 확인합니다.
    private int SpawnLimit => Mathf.Clamp(maxCount, 1, PoolCapacity);
    public bool AllowsSlot(int slot) => slot >= 0 && slot < SpawnLimit;

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

        if (requiredElevator != null && !elevatorActivated)
        {
            if (!requiredElevator.HasRisen)
                return;
            elevatorActivated = true;
        }

        poolRunner = network.Runner;
        if (spawnDuringHacks && encounter != null && encounter.Object != null &&
            encounter.Object.IsValid && encounter.UsesContactDrones)
        {
            int wave = encounter.HodDroneWave;
            if (wave != hodDroneWave)
            {
                DespawnPool();
                hodDroneWave = wave;
                nextSpawnAt = -1f;
            }
        }

        if (spawnDuringHacks && (encounter == null || encounter.Object == null ||
            !encounter.Object.IsValid || !encounter.HackSpawnsEnabled))
        {
            foreach (var item in pool)
                if (item != null && item.IsValid && item.HasStateAuthority &&
                    item.TryGetComponent(out RobotBase drone))
                    drone.DeactivateHodPoolSlot();

            nextSpawnAt = -1f;
            return;
        }

        if (nextSpawnAt < 0f)
            nextSpawnAt = spawnDuringHacks ? Time.time : Time.time + PickSpawnDelay();
        if (Time.time < nextSpawnAt)
            return;

        for (int slot = 0; slot < SpawnLimit; slot++)
        {
            if (pool[slot] != null && pool[slot].IsValid)
                continue;
            int index = slot;
            nextSpawnAt = Time.time + PickSpawnDelay();
            float centerX = SpawnPosition.x;
            Vector2 patrol = new(centerX + Mathf.Min(patrolOffsets.x, patrolOffsets.y),
                centerX + Mathf.Max(patrolOffsets.x, patrolOffsets.y));
            pool[slot] = poolRunner.Spawn(robotPrefab, SpawnPosition, Quaternion.identity,
                onBeforeSpawned: (_, item) => item.GetComponent<RobotBase>()
                    .ConfigurePool(this, index, encounter, patrol, robotSpeed));
            break;
        }
    }

    private void OnDestroy() => DespawnPool();

    private void DespawnPool()
    {
        if (poolRunner == null || !poolRunner.IsRunning)
            return;

        for (int slot = 0; slot < pool.Length; slot++)
        {
            var item = pool[slot];
            if (item == null || !item.IsValid || !item.HasStateAuthority)
                continue;

            item.GetComponent<RobotBase>().ReleaseForDespawn();
            poolRunner.Despawn(item);
            pool[slot] = null;
        }
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

using UnityEngine;

public class DroneRobot : RobotBase
{
    [SerializeField, Min(0f)] private float flightHeight = 0.65f; //시작 위치 기준 가장 높게 날 수 있는 양
    [SerializeField] private LayerMask flightObstacles; //장애물로 판단할 레이어
    [SerializeField] private AnimationClip deathAnimation; //없어지는 시간이랑 맞추려고 죽는 애니메이션 캐싱
    private BoxCollider2D flightHitbox; 
    private readonly RaycastHit2D[] flightHits = new RaycastHit2D[4]; //날라갈 거리 계산 시 장애물 캐싱

    protected override bool FlipModel => true;
    protected override float RemovalDuration => deathAnimation != null ? Mathf.Max(base.RemovalDuration, deathAnimation.length) : base.RemovalDuration;

    protected override void Awake()
    {
        base.Awake();
        flightHitbox = GetComponent<BoxCollider2D>();
    }

    protected override void UpdateMovement()
    {
        if (flightHitbox == null)
            return;

        Vector2 position = transform.position;

        float edge = GoingRight ? patrolX.y : patrolX.x; //현재 순찰 구간의 끝
        float span = Mathf.Max(0.01f, patrolX.y - patrolX.x); //순찰 구간 길이
        float progress = Mathf.Clamp01((position.x - patrolX.x) / span); //구간 중 현재 위치 
        float height = spawnPosition.y + Mathf.Sin(progress * Mathf.PI) * flightHeight; //구간 가운데에서 가장 높아지도록 사인 곡선으로 height를 구하기

        Vector2 step = Vector2.MoveTowards(position, new Vector2(edge, height), speed * Runner.DeltaTime) - position; //이번에 시도할 이동량

        //BoxCast 로 이동거리보다 0.025 더 멀리 검사합니다
        //블록이 감지 되면 순찰 방향을 반대방향으로 설정합니다
        if (step.sqrMagnitude > 0.000001f)
        {
            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(flightObstacles);
            Bounds bounds = flightHitbox.bounds;

            const float clearance = 0.025f; //벽과 몸체 사이에 남길 작은 간격
            float travel = step.magnitude; //충돌 검사 후 이번 틱에 실제로 갈 수 있는 거리
            int count = Physics2D.BoxCast(bounds.center, bounds.size, 0f, step.normalized, filter, flightHits, travel + clearance);
            
            for (int i = 0; i < count; i++)
                travel = Mathf.Min(travel, Mathf.Max(0f, flightHits[i].distance - clearance));

            FacingLeft = !GoingRight; //네트워크 불

            Vector2 next = position + step.normalized * travel;
            transform.position = new Vector3(next.x, next.y, transform.position.z);

            if (count > 0 || Mathf.Abs(next.x - edge) < 0.025f)
                GoingRight = !GoingRight;

            State = travel > 0f ? RobotState.Patrol : RobotState.Idle;
        }
        else
        {
            GoingRight = !GoingRight;
            State = RobotState.Idle;
        }
    }
}

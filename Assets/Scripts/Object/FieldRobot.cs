using UnityEngine;
using UnityEngine.SceneManagement;

public class FieldRobot : RobotBase
{
    protected override bool FlipModel => true;
    internal override bool RequiresCrushing =>
        gameObject.scene.name == nameof(NetworkGameManager.SceneName.Yesod) ||
        SceneManager.GetActiveScene().name == nameof(NetworkGameManager.SceneName.Yesod);

    protected override void UpdateMovement()
    {
        var target = FindTarget();
        if (UsesRangedAttack && UpdateRangedAttack(target))
            return;

        if (gameObject.scene.name == nameof(NetworkGameManager.SceneName.Netzach) ||
            SceneManager.GetActiveScene().name == nameof(NetworkGameManager.SceneName.Netzach))
        {
            FacingLeft = true;
            State = RobotState.Patrol;
            transform.position += Vector3.left * (Runner.DeltaTime * speed);
            return;
        }

        //추적할 플레이어가 있다면 플레이어를 따라가고 아닐 경우 순찰 구간 유지
        float destination = target != null ? target.transform.position.x : GoingRight ? patrolX.y : patrolX.x;
        destination = Mathf.Clamp(destination, patrolX.x, patrolX.y); //플레이어를 따라 순찰 구간을 넘지 않게

        Vector3 position = transform.position;

        position.x = Mathf.MoveTowards(position.x, destination, speed * Runner.DeltaTime);
        float step = position.x - transform.position.x;

        if (!Mathf.Approximately(step, 0f))
            FacingLeft = step < 0f;

        //현재 해당 상태를 기록
        State = Mathf.Approximately(step, 0f) ? RobotState.Idle : target != null ? RobotState.Chase : RobotState.Patrol;
        transform.position = position;

        //순찰 양 끝에 도달 시 방향 뒤집기
        if (target == null && Mathf.Approximately(position.x, destination))
            GoingRight = !GoingRight;
    }
}

using Fusion;
using UnityEngine;

/// <summary>
/// 스테이지 클리어 후 엘레베이터로 돌아와 마무리 합니다
/// </summary>
public class StageExit : NetworkBehaviour
{
    [SerializeField] private Vector2 gatheringSize = new(6f, 3f);
    [SerializeField] private Transform leftLeaf;
    [SerializeField] private Transform rightLeaf;
    [SerializeField] private float liftTravel = 2f;
    [SerializeField] private float liftDuration = 1.5f;

    [Networked] private int PlayersReady { get; set; }
    [Networked] private float LiftProgress { get; set; }

    private Vector3 leftStart;
    private Vector3 rightStart;
    private bool requestedTransition;
    private StagePuzzleDirector director;

    internal void SetDirector(StagePuzzleDirector stageDirector) => director = stageDirector;

    public override void Spawned()
    {
        if (leftLeaf != null)
            leftStart = leftLeaf.localPosition;

        if (rightLeaf != null)
            rightStart = rightLeaf.localPosition;
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority || requestedTransition)
            return;

        if (LiftProgress <= 0f)
        {
            PlayersReady = CountPlayersInArea();
            if (PlayersReady != GameManager.MaxPlayerCount)
                return;
        }

        LiftProgress = Mathf.Clamp01(LiftProgress + Runner.DeltaTime / Mathf.Max(0.1f, liftDuration));
        ApplyLift();

        if (LiftProgress >= 1f)
            requestedTransition = director != null && director.TryCompleteStage();
    }

    /// <summary>
    /// 모든 플레이어가 엘레베이터로 다시 돌아와야 함
    /// </summary>
    /// <returns></returns>
    private int CountPlayersInArea()
    {
        int count = 0;
        Vector2 center = transform.position;
        Vector2 halfSize = gatheringSize * 0.5f;

        foreach (var playerObject in NetworkGameManager.GetPlayerObjects(Runner))
        {
            Vector2 delta = (Vector2)playerObject.transform.position - center;
            if (Mathf.Abs(delta.x) <= halfSize.x && Mathf.Abs(delta.y) <= halfSize.y)
                count++;
        }
        return count;
    }

    public override void Render()
    {
        if (!HasStateAuthority)
            ApplyLift();
    }

    private void ApplyLift()
    {
        if (leftLeaf != null)
            leftLeaf.localPosition = leftStart + Vector3.left * (liftTravel * LiftProgress);
        if (rightLeaf != null)
            rightLeaf.localPosition = rightStart + Vector3.right * (liftTravel * LiftProgress);
    }

}

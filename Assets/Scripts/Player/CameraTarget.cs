using Fusion;

public class CameraTarget : NetworkBehaviour
{
    public override void Spawned()
    {
        if (LocalMultiplayerDebug.GetPlayers(Runner) != null)
            return;
        if (!HasStateAuthority)
            return;

        CameraFollow.Instance?.SetTarget(transform);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (LocalMultiplayerDebug.GetPlayers(Runner) != null)
            return;
        if (!HasStateAuthority)
            return;

        CameraFollow.Instance?.ClearTarget();
    }
}

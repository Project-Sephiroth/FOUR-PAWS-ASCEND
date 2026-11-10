using Fusion;

public class CameraTarget : NetworkBehaviour
{
    public override void Spawned()
    {
        if (!HasStateAuthority)
            return;

        CameraFollow.Instance?.SetTarget(transform);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (!HasStateAuthority)
            return;

        CameraFollow.Instance?.ClearTarget();
    }
}
using UnityEngine;
using static IMoveable;

public class Thrower : Interactor
{
    [SerializeField] private float throwDistance = 5f;
    [SerializeField] private float throwHeight = 1f;

    public void Throw(MovingObject movingObject)
    {
        if (!HasStateAuthority || movingObject == null)
            return;

        float direction = transform.localScale.x >= 0f ? 1f : -1f;

        Vector2 destination = (Vector2)transform.position + new Vector2(direction * throwDistance,throwHeight);

        movingObject.RPC_RequestMove(MoveType.Parabola, destination);
    }

}

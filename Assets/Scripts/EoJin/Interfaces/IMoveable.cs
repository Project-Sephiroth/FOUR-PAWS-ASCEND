using Fusion;
using UnityEngine;

public interface IMoveable 
{
    public enum MoveType { Linear, Parabola }

    /// <summary>
    /// 궤적을 설정합니다
    /// </summary>
    public void TryMove(MoveType pathType, Vector2 targetPosition);

    /// <summary>
    /// 설정된 궤적에 따라 움직입니다
    /// </summary>
    public void Move();
}

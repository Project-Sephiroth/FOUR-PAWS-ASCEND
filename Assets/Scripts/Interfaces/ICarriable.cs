using UnityEngine;

/// <summary>
/// 탑승할 수 있습니다
/// </summary>
public interface ICarriable
{
    /// <summary>
    /// carrier 에 탑승했을 때
    /// </summary>
    public void OnCarry(Carrier carrier);
    public void OnDrop();
}

using UnityEngine;

/// <summary>
/// 탑승합니다
/// </summary>
public interface IRideable
{
    /// <summary>
    /// Lifter 에 탑승했을 때
    /// </summary>
    public void Ride(Lifter lifter);
    public void Drop();
}

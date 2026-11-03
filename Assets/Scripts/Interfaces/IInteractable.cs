using UnityEngine;

public interface IInteractable
{
    /// <summary>
    /// Interactor 에 의해 상호작용 됩니다
    /// </summary>
    /// <param name="interactor"></param>
    public void OnInteract(Interactor interactor);
}

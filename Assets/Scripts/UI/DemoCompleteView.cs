using System;
using UnityEngine;

public sealed class DemoCompleteView : MonoBehaviour
{
    private bool leaving;

    private void OnEnable() => UIManager.EnsureLocalEventSystem();

    public void OpenCredits() => Choose(NetworkGameManager.SceneName.Credits);

    public void ReturnToTitle() => Choose(NetworkGameManager.SceneName.Title);

    private async void Choose(NetworkGameManager.SceneName destination)
    {
        if (leaving)
            return;

        leaving = true;
        try
        {
            await GameMenu.LoadAfterSessionAsync(destination);
        }
        catch (Exception error)
        {
            Debug.LogException(error, this);
        }
        finally
        {
            leaving = false;
            if (isActiveAndEnabled)
                UIManager.EnsureLocalEventSystem();
        }
    }
}

using System;
using UnityEngine;

public sealed class CreditsView : MonoBehaviour
{
    private bool returning;

    private void Start() => UIManager.EnsureLocalEventSystem();

    public async void ReturnToTitle()
    {
        if (returning || ScenePresentation.BlocksInput)
            return;

        returning = true;
        try
        {
            await GameMenu.LoadTitleAsync();
        }
        catch (Exception error)
        {
            Debug.LogException(error, this);
        }
        finally
        {
            returning = false;
        }
    }
}

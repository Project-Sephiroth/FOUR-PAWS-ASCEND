using UnityEngine;

// 부팅 때 필요한 UI 프리팹을 인스펙터에서 연결해 둡니다. 씬마다 같은 프리팹 경로를 다시 지정하지 않아도 됩니다.
public sealed class GamePresentationAssets : ScriptableObject
{
    public GameObject pauseMenu;
    public GameObject settingsMenu;
    public SceneTransitionView sceneTransition;
}

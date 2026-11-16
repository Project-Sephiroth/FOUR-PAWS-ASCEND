using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 검은 머리 모양 위에 같은 스프라이트를 채웁니다. 원본의 투명 영역이 마스크가 되어 바깥으로 색이 번지지 않습니다.
public sealed class PuzzleProgressHud : MonoBehaviour
{
    [SerializeField] private Canvas canvas;
    // 인스펙터에서 쥐, 곰, 토끼, 개구리 순서로 채움 이미지를 연결합니다.
    [SerializeField] private Image[] fills;
    private int sceneHandle = int.MinValue;
    private StagePuzzleDirector director;
    private int visibleHead = -1;
    private float shownProgress;

    // 씬이 바뀌면 해당 동물 하나를 표시합니다. 그 머리 전체가 씬의 0~100% 진행도를 나타냅니다.
    private void LateUpdate()
    {
        var scene = SceneManager.GetActiveScene();
        if (sceneHandle != scene.handle)
        {
            sceneHandle = scene.handle;
            director = null;
            shownProgress = 0f;
            visibleHead = scene.name switch
            {
                "Hod" => 0,
                "Malkuth" => 1,
                "Yesod" => 2,
                "Netzach" => 3,
                _ => -1
            };
            for (int i = 0; i < fills.Length; i++)
                if (fills[i] != null)
                {
                    fills[i].fillAmount = 0f;
                    fills[i].transform.parent.gameObject.SetActive(i == visibleHead);
                }
            if (HackingConsole.IsGameplayScene(scene))
                foreach (var item in scene.GetRootGameObjects())
                {
                    director = item.GetComponentInChildren<StagePuzzleDirector>();
                    if (director != null)
                        break;
                }
        }
        var manager = NetworkGameManager.Instance;
        canvas.enabled = visibleHead >= 0 && director != null && manager != null && manager.Object != null && manager.Object.IsValid;
        if (!canvas.enabled)
            return;

        shownProgress = Mathf.MoveTowards(shownProgress, director.ReadProgress(), Time.unscaledDeltaTime * 1.15f);
        if (fills[visibleHead] != null)
            fills[visibleHead].fillAmount = shownProgress;
    }
}
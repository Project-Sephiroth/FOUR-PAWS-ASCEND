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

    // 머리는 현재 조작하는 동물, 채움 정도는 현재 씬의 퍼즐 진행도를 따릅니다.
    private void LateUpdate()
    {
        var scene = SceneManager.GetActiveScene();
        if (sceneHandle != scene.handle)
        {
            sceneHandle = scene.handle;
            director = null;
            shownProgress = 0f;
            if (HackingConsole.IsGameplayScene(scene))
                foreach (var item in scene.GetRootGameObjects())
                {
                    director = item.GetComponentInChildren<StagePuzzleDirector>();
                    if (director != null)
                        break;
                }
        }
        int head = ScenePresentation.LocalCharacter() switch
        {
            MyEnum.CharacterType.Mouse => 0,
            MyEnum.CharacterType.Bear => 1,
            MyEnum.CharacterType.Rabbit => 2,
            MyEnum.CharacterType.Frog => 3,
            _ => -1
        };
        canvas.enabled = director != null && head >= 0 && head < fills.Length;
        if (!canvas.enabled)
            return;

        if (head != visibleHead)
        {
            visibleHead = head;
            for (int i = 0; i < fills.Length; i++)
                if (fills[i] != null)
                    fills[i].transform.parent.gameObject.SetActive(i == visibleHead);
        }

        shownProgress = Mathf.MoveTowards(shownProgress, director.ReadProgress(), Time.unscaledDeltaTime * 1.15f);
        if (fills[visibleHead] != null)
            fills[visibleHead].fillAmount = shownProgress;
    }
}
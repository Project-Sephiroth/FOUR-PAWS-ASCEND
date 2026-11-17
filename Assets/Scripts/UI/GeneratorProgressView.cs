using UnityEngine;
using UnityEngine.UI;

public sealed class GeneratorProgressView : MonoBehaviour
{
    [SerializeField] private Slider slider;

    private StagePuzzleDirector director;

    internal void Bind(StagePuzzleDirector source)
    {
        director = source;
        UIManager.SetCanvasOrder(gameObject, UIManager.Layer.Generator);
    }

    // 발전기가 공유하는 0~1 진행도를 읽어서 충전과 감소를 모두 표시합니다.
    private void LateUpdate()
    {
        if (director != null)
            slider.SetValueWithoutNotify(director.ReadGeneratorProgress());
    }
}

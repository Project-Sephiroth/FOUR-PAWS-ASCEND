using UnityEngine;

[DefaultExecutionOrder(20)]
public sealed class SpectatorViewHud : MonoBehaviour
{
    [SerializeField] private Canvas canvas;

    // 카메라가 이번 프레임에 따라가는 대상을 고른 뒤, 관전 중에만 테두리와 안내를 표시합니다.
    private void LateUpdate()
    {
        bool visible = CameraFollow.IsViewingOther;
        if (canvas.enabled != visible)
            canvas.enabled = visible;
    }
}

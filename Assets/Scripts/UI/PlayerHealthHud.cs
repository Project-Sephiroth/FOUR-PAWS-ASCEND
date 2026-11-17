using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class PlayerHealthHud : MonoBehaviour
{
    [SerializeField] private Canvas canvas;
    [SerializeField] private Image[] hearts;
    [SerializeField] private Color filledColor = new Color32(240, 50, 81, 255);
    [SerializeField] private Color emptyColor = Color.black;

    // 하트는 프리팹의 배열 순서로 검사합니다. 잃은 체력만큼 왼쪽 하트부터 색을 바꿉니다.
    private void LateUpdate()
    {
        var manager = NetworkGameManager.Instance;
        if (SceneManager.GetActiveScene().name == nameof(NetworkGameManager.SceneName.Lobby) ||
            manager == null || manager.Object == null || !manager.Object.IsValid ||
            !manager.TryGetGameplayPlayer(manager.Runner.LocalPlayer, out var player) ||
            player == null || !player.IsValid || !player.TryGetComponent(out PlayerHealth health))
        {
            canvas.enabled = false;
            return;
        }

        canvas.enabled = true;
        int lost = hearts.Length - Mathf.Clamp(health.Health, 0, hearts.Length);
        for (int i = 0; i < hearts.Length; i++)
            hearts[i].color = i < lost ? emptyColor : filledColor;
    }
}
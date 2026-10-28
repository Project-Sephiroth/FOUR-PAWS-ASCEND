using UnityEngine;
using UnityEngine.UI;
namespace FourPaws.PCS.StepPuzzle
{
    // 저장 타일을 재사용하며 색과 글자로 벽·시작·목표·방문을 함께 구분한다.
    public sealed class PCS_StepTileView : MonoBehaviour
    {
        [Tooltip("현재 보드의 좌표입니다. 데모가 보드 크기에 맞춰 배치합니다.")] public int x;
        public int y;
        public Image surface;
        public Text label;
        [SerializeField] private Color waiting = new Color(0.15f, 0.21f, 0.31f);
        [SerializeField] private Color visited = new Color(0.14f, 0.52f, 0.43f);
        [SerializeField] private Color failed = new Color(0.7f, 0.23f, 0.3f);
        public void Show(PCS_StepPuzzle model)
        {
            var p = new Vector2Int(x, y); bool wall = model.Current.IsWall(p); bool current = model.X == x && model.Y == y;
            surface.color = wall ? new Color(0.025f, 0.035f, 0.055f) : model.Failed && current ? failed : model.IsVisited(x, y) ? visited : waiting;
            string tag = wall ? "벽" : !model.Current.practice && p == model.Current.goal ? "목표" : p == model.Current.start ? "시작" : model.IsVisited(x, y) ? "방문" : "";
            label.text = $"{tag}\n({x},{y})";
        }
    }
}
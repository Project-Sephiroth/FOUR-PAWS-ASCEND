using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FourPaws.PCS.Hacking
{
    // 육각 핀을 메시로 그리고 자기 클릭 콜백만 전달한다. 중앙 회전 금지는 모델에서 재검사한다.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class PCS_HexTileView : MaskableGraphic, IPointerClickHandler, ICanvasRaycastFilter
    {
        [Tooltip("현재 선택한 단계의 타일 인덱스입니다. 고정/루트는 데이터로 판정합니다.")] public int tileIndex;
        private int mask;
        private int paired;private bool fixedTile;
        private Action<int> clicked;

        public void Bind(Action<int> callback) { clicked = callback; }
        public void Show(PCS_RotationPuzzle puzzle)
        {
            mask = puzzle.MaskAt(tileIndex);fixedTile=puzzle.IsFixed(tileIndex);raycastTarget=!fixedTile;
            paired = 0;
            for (int d = 0; d < 6; d++)
            {
                int neighbor = puzzle.NeighborAt(tileIndex, d);
                if (neighbor >= 0 && (puzzle.MaskAt(neighbor) & (1 << ((d + 3) % 6))) != 0) paired |= 1 << d;
            }
            SetVerticesDirty();
        }
        public bool IsRaycastLocationValid(Vector2 screenPoint,Camera eventCamera)
        {
            if(fixedTile||!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform,screenPoint,eventCamera,out Vector2 point))return false;
            point-=rectTransform.rect.center;float radius=Mathf.Min(rectTransform.rect.width,rectTransform.rect.height)*.48f;
            return Mathf.Abs(point.x)<=Mathf.Sqrt(3)*radius*.5f&&Mathf.Abs(point.x)/Mathf.Sqrt(3)+Mathf.Abs(point.y)<=radius;
        }
        public void OnPointerClick(PointerEventData eventData)
        {
            if (!fixedTile && eventData.button == PointerEventData.InputButton.Left) clicked?.Invoke(tileIndex);
        }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Vector2 center = rectTransform.rect.center;
            float radius = Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) * 0.48f;
            Color fill = fixedTile ? new Color(0.15f, 0.27f, 0.37f) : new Color(0.11f, 0.18f, 0.27f);
            vh.AddVert(center, fill, Vector2.zero);
            for (int i = 0; i < 6; i++) vh.AddVert(center + Direction(30f + 60f * i) * radius, fill, Vector2.zero);
            for (int i = 0; i < 6; i++)
            {
                vh.AddTriangle(0, i + 1, (i + 1) % 6 + 1);
                PCS_HackingView.Line(vh, center + Direction(30f + 60f * i) * radius,
                    center + Direction(90f + 60f * i) * radius, 2f, new Color(0.4f, 0.55f, 0.65f));
            }
            for (int d = 0; d < 6; d++)
            {
                if ((mask & (1 << d)) == 0) continue;
                Color tint = (paired & (1 << d)) != 0 ? new Color(0.28f, 0.94f, 0.74f) : new Color(1f, 0.72f, 0.3f);
                PCS_HackingView.Line(vh, center, center + Direction(60f * d) * radius * 0.86f, 7f, tint);
            }
        }
        private static Vector2 Direction(float angle)
        {
            float radians = angle * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(radians), -Mathf.Sin(radians));
        }
    }
}

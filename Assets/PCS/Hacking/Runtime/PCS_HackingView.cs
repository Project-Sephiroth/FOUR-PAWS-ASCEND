using UnityEngine;
using UnityEngine.UI;

namespace FourPaws.PCS.Hacking
{
    // 저장된 패널과 타일을 표시한다. Bind 때 이전 시도의 표시 캐시를 초기화한다.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class PCS_HackingView : MaskableGraphic
    {
        [Header("자기 패널의 한국어 표시")]
        public Text progress;
        public Text feedback;
        public Text alarm;
        public Text title;
        public GameObject rotationRoot;
        [Header("P1 선택적 Sprite 표시")]
        [Tooltip("없거나 비활성화되면 기존 메시 표시를 유지합니다.")] public PCS_TimingSpriteOverlay timingSpriteOverlay;
        [Tooltip("선택적 P1 배경 Image입니다. P1에서만 표시하며 비어 있으면 기존 표시를 유지합니다.")] public Image timingBackgroundDecoration;
        [Tooltip("최대 19개 표시 풀입니다. 선택한 단계 수만 활성화합니다.")] public PCS_HexTileView[] rotationTiles;
        [SerializeField] private Color waitingColor = new Color(0.36f, 0.44f, 0.56f);
        [SerializeField] private Color connectedColor = new Color(0.28f, 0.94f, 0.74f);
        [SerializeField] private Color pointerColor = new Color(1f, 0.82f, 0.37f);
        private PCS_TimingPuzzle puzzle;
        private int lastConnected = -1;
        private int lastMiss = -1;
        private int lastAlarm = -1;
        private bool rotationMode;

        public void ShowConfigurationError(string message){SetTimingBackgroundVisible(false);if(timingSpriteOverlay)timingSpriteOverlay.Hide();puzzle=null;rotationMode=false;if(rotationRoot)rotationRoot.SetActive(false);feedback.text=message;progress.text="설정 확인이 필요합니다.";SetVerticesDirty();}
        public void Bind(PCS_TimingPuzzle model)
        {
            puzzle = model;
            if(timingSpriteOverlay)timingSpriteOverlay.Bind(model,rectTransform);
            rotationMode = false;
            SetTimingBackgroundVisible(isActiveAndEnabled && model != null && model.Error.Length == 0);
            if (rotationRoot != null) rotationRoot.SetActive(false);
            if (title != null) title.text = "타이밍 해킹 / 모든 핀을 연결하세요";
            lastConnected = lastMiss = lastAlarm = -1;
            raycastTarget = false;
            SetVerticesDirty();
        }

        public void BindRotation(PCS_RotationPuzzle model, System.Action<int> callback)
        {
            if(timingSpriteOverlay)timingSpriteOverlay.Hide();
            rotationMode = true;
            SetTimingBackgroundVisible(false);
            rotationRoot.SetActive(true);
            title.text = "회전 연결 해킹 / "+model.TileCount+"개 타일을 연결하세요";
            lastAlarm = -1;
            Vector2 min=model.PositionAt(0),max=min;for(int i=1;i<model.TileCount;i++){min=Vector2.Min(min,model.PositionAt(i));max=Vector2.Max(max,model.PositionAt(i));}
            Vector2 span=max-min;float unit=Mathf.Min(400f/(span.x+1.8f),400f/(span.y+2f));
            for(int i=0;i<rotationTiles.Length;i++){var tile=rotationTiles[i];bool active=i<model.TileCount;tile.gameObject.SetActive(active);tile.Bind(active?callback:null);if(!active)continue;tile.tileIndex=i;tile.rectTransform.anchoredPosition=(model.PositionAt(i)-(min+max)*.5f)*unit;tile.rectTransform.sizeDelta=Vector2.one*unit*1.92f;var label=tile.GetComponentInChildren<Text>(true);if(label){label.text=i==model.RootIndex?"루트":model.IsFixed(i)?"고정":i.ToString();label.fontSize=model.TileCount>7?13:17;label.rectTransform.anchoredPosition=new Vector2(0,-unit*.58f);label.rectTransform.sizeDelta=new Vector2(unit*1.5f,20);}}
            ShowRotation(model, 0);
            SetVerticesDirty();
        }

        public void ShowRotation(PCS_RotationPuzzle model, int alarmCount)
        {
            var state = model.Status;
            progress.text = $"연결된 타일 {state.reachable} / {model.TileCount}\n바깥 핀 {state.outside} / 맞지 않는 핀 {state.unmatched}";
            alarm.text = $"모의 경보 {alarmCount}\n실제 적은 나타나지 않습니다";
            for(int i=0;i<model.TileCount;i++)rotationTiles[i].Show(model);
        }

        public void Show(int alarmCount)
        {
            if (puzzle == null || rotationMode) return;
            if(timingSpriteOverlay)
            {
                if(!timingSpriteOverlay.IsBoundTo(puzzle) && timingSpriteOverlay.isActiveAndEnabled)timingSpriteOverlay.Bind(puzzle,rectTransform);
                else timingSpriteOverlay.Show();
            }
            if (lastConnected != puzzle.ConnectedCount || lastMiss != puzzle.MissCount)
            {
                lastConnected = puzzle.ConnectedCount;
                lastMiss = puzzle.MissCount;
                progress.text = $"연결한 핀 {lastConnected} / {puzzle.ActiveTargetCount}\n오입력 {lastMiss} / {(puzzle.ActiveMissLimit==0?"무제한":puzzle.ActiveMissLimit.ToString())}";
            }
            if (lastAlarm != alarmCount)
            {
                lastAlarm = alarmCount;
                alarm.text = $"모의 경보 {alarmCount}\n실제 적은 나타나지 않습니다";
            }
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (rotationMode || puzzle == null || puzzle.Error.Length > 0) return;
            Vector2 center = rectTransform.rect.center;
            float radius = Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) * 0.35f;
            bool overlay=timingSpriteOverlay && timingSpriteOverlay.SpriteDisplayActive;
            if(!overlay || timingSpriteOverlay.DrawLegacyRing)
            for (int k = 0; k < 72; k++)
                Line(vh, center + Direction(k * 5f) * radius, center + Direction((k + 1) * 5f) * radius, 2f, waitingColor);
            for (int i = 0; i < puzzle.ActiveTargetCount; i++)
            {
                Vector2 direction = Direction(puzzle.TargetAngle(i));
                Color pinColor = puzzle.IsConnected(i) ? connectedColor : waitingColor;
                if(!overlay || timingSpriteOverlay.DrawLegacyPin(i)) Line(vh, center + direction * radius * 0.76f, center + direction * radius * 1.14f, 10f, pinColor);
                if (puzzle.IsConnected(i) && (!overlay || timingSpriteOverlay.DrawLegacyConnections)) Line(vh, center + direction * 16f, center + direction * radius * 0.76f, 3f, connectedColor);
            }
            if(!overlay || timingSpriteOverlay.DrawLegacyPointer)
            {
            Line(vh, center, center + Direction(puzzle.Angle) * radius * 0.93f, 5f, pointerColor);
            Line(vh, center + Vector2.left * 6f, center + Vector2.right * 6f, 12f, pointerColor);
            }
        }

        protected override void OnDisable()
        {
            SetTimingBackgroundVisible(false);
            if(timingSpriteOverlay)timingSpriteOverlay.Hide();
            base.OnDisable();
        }
        protected override void OnEnable()
        {
            base.OnEnable();
            SetTimingBackgroundVisible(!rotationMode && puzzle != null && puzzle.Error.Length == 0);
            if(!rotationMode && puzzle!=null && timingSpriteOverlay)timingSpriteOverlay.Bind(puzzle,rectTransform);
        }
        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            if(!rotationMode && timingSpriteOverlay)timingSpriteOverlay.Show();
        }

        // 일반 장식 Image도 기존 P1/P2 표시 전환을 따르게 한다. 원본 색·배치·활성 계층은 바꾸지 않는다.
        private void SetTimingBackgroundVisible(bool visible)
        {
            if(!Application.isPlaying)return; // Graphic의 Edit 생명주기에서는 저장된 장식 표시값을 건드리지 않는다.
            if(timingBackgroundDecoration && timingBackgroundDecoration.enabled != visible)timingBackgroundDecoration.enabled = visible;
        }

        private static Vector2 Direction(float angle)
        {
            float radians = angle * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(radians), Mathf.Cos(radians));
        }

        internal static void Line(VertexHelper vh, Vector2 a, Vector2 b, float width, Color tint)
        {
            Vector2 delta = b - a;
            Vector2 side = new Vector2(-delta.y, delta.x).normalized * width * 0.5f;
            int start = vh.currentVertCount;
            vh.AddVert(a - side, tint, Vector2.zero);
            vh.AddVert(a + side, tint, Vector2.zero);
            vh.AddVert(b + side, tint, Vector2.zero);
            vh.AddVert(b - side, tint, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }
    }
}

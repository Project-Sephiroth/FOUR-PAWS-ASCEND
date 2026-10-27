using UnityEngine;
using UnityEngine.UI;

namespace FourPaws.PCS.Hacking
{
    // 판정 모델은 읽기만 한다. 새 시도의 Bind에서 풀을 준비하고 Show에서는 저장된 표시만 갱신한다.
    [DisallowMultipleComponent, RequireComponent(typeof(RectTransform))]
    public sealed class PCS_TimingSpriteOverlay : MonoBehaviour
    {
        public enum DisplayMode { Legacy = 0, Hybrid = 1, Sprites = 2 }
        [Header("표시 방식 / 비어 있는 그림은 기존 메시 사용")]
        [Tooltip("Legacy: 기존 메시, Hybrid: 메시와 그림, Sprites: 준비된 그림만 메시 대신 표시합니다.")]
        public DisplayMode displayMode = DisplayMode.Legacy;
        public Sprite waitingSprite;
        [Tooltip("없으면 미연결 그림에 연결 색을 적용합니다.")] public Sprite connectedSprite;
        public Sprite pointerSprite;
        [Header("상태 색")]
        public Color waitingTint = Color.white;
        public Color connectedTint = new Color(.28f,.94f,.74f);
        public Color pointerTint = new Color(1f,.82f,.37f);
        [Header("원 반지름에 대한 배치 비율")]
        [Tooltip("핀 폭과 길이의 반지름 비율입니다.")] public Vector2 pinSizeRatio = new Vector2(.08f,.38f);
        [Min(0)] public float pinRadiusScale = .95f;
        [Min(0)] public float pointerWidthRatio = .035f;
        [Min(0)] public float pointerLengthRatio = .93f;
        [Tooltip("회전 중심에서 그림 밑변까지의 반지름 비율입니다. 중심 자체는 이동하지 않습니다.")]
        public float pointerOffsetRatio;
        [Tooltip("위쪽을 향한 그림의 기본값은 0도입니다.")] public float pinRotationOffset;
        public float pointerRotationOffset;
        public bool showLegacyRing = true;
        public bool showLegacyConnections = true;
        [Header("자기 표시 계층 / 장식은 클릭을 받지 않음")]
        public RectTransform pinsRoot;
        public Image[] pinImages = new Image[0];
        public RectTransform pointerPivot;
        public Image pointerImage;
        public int PoolCount => pinImages == null ? 0 : pinImages.Length;
        public int VisiblePinCount { get; private set; }
        private PCS_TimingPuzzle model;
        private RectTransform source;
        private bool prepared;
        public bool IsBoundTo(PCS_TimingPuzzle value) => model == value && source;
        public bool SpriteDisplayActive => isActiveAndEnabled && model != null && prepared && displayMode != DisplayMode.Legacy;
        public bool DrawLegacyRing => !SpriteDisplayActive || showLegacyRing;
        public bool DrawLegacyConnections => !SpriteDisplayActive || showLegacyConnections;
        public bool DrawLegacyPointer => !SpriteDisplayActive || displayMode != DisplayMode.Sprites || !Visible(pointerImage);
        public bool DrawLegacyPin(int index) => !SpriteDisplayActive || displayMode != DisplayMode.Sprites || index < 0 || index >= PoolCount || !Visible(pinImages[index]);
        private static bool Visible(Image value) => value && value.isActiveAndEnabled && value.sprite;

        // 초기 준비에서만 생성한다. 항상 모델 최대치 24개를 재사용하므로 핀 수 감소/재시도에 누적되지 않는다.
        public bool PreparePool()
        {
            if (!pinsRoot || !pointerPivot || !pointerImage) return false;
            if (pinImages == null) pinImages = new Image[0];
            if (pinImages.Length > 24) return false;
            if (pinImages.Length < 24) System.Array.Resize(ref pinImages,24);
            for (int i=0;i<24;i++)
            {
                if (!pinImages[i])
                {
                    var go = new GameObject("Pin_"+i.ToString("00"),typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));
                    go.transform.SetParent(pinsRoot,false);pinImages[i]=go.GetComponent<Image>();
                }
                if (pinImages[i].transform.parent != pinsRoot) return false;
                ConfigureImage(pinImages[i]);
            }
            if (pointerImage.transform.parent != pointerPivot) return false;
            ConfigureImage(pointerImage);return true;
        }
        private static void ConfigureImage(Image value)
        {
            value.raycastTarget=false;value.type=Image.Type.Simple;value.preserveAspect=false; // 폭·길이 비율을 실제 Rect에 그대로 적용한다.
            value.rectTransform.anchorMin=value.rectTransform.anchorMax=new Vector2(.5f,.5f);
        }
        public void Bind(PCS_TimingPuzzle value,RectTransform coordinateSource)
        {
            Hide();model=value;source=coordinateSource;
            if (isActiveAndEnabled && model!=null) prepared=PreparePool();
            Show();
        }
        public void Hide()
        {
            model=null;source=null;prepared=false;VisiblePinCount=0;
            if(pinImages!=null)foreach(var pin in pinImages)if(pin){pin.enabled=false;pin.sprite=null;pin.color=waitingTint;}
            if(pointerImage){pointerImage.enabled=false;pointerImage.sprite=null;}
            if(pointerPivot)pointerPivot.localRotation=Quaternion.identity;
        }
        public void Show()
        {
            bool visible=SpriteDisplayActive && source && model.Error.Length==0;
            VisiblePinCount=0;
            if(!visible){if(pinImages!=null)foreach(var pin in pinImages)if(pin)pin.enabled=false;if(pointerImage)pointerImage.enabled=false;return;}
            float radius=Mathf.Min(source.rect.width,source.rect.height)*.35f;
            Vector3 worldCenter=source.TransformPoint(source.rect.center);
            Vector3 rootCenter=pinsRoot.InverseTransformPoint(worldCenter);
            for(int i=0;i<PoolCount;i++)
            {
                var pin=pinImages[i];if(!pin)continue;
                bool used=i<model.ActiveTargetCount;bool connected=used&&model.IsConnected(i);
                Sprite sprite=connected&&connectedSprite?connectedSprite:waitingSprite;
                bool draw=used&&sprite&&radius>0&&pinSizeRatio.x>0&&pinSizeRatio.y>0;
                pin.raycastTarget=false;pin.sprite=sprite;pin.color=connected?connectedTint:waitingTint;pin.enabled=draw;
                if(!draw)continue;
                float angle=model.TargetAngle(i);float radians=angle*Mathf.Deg2Rad;
                Vector2 direction=new Vector2(Mathf.Sin(radians),Mathf.Cos(radians));
                pin.rectTransform.pivot=new Vector2(.5f,.5f);
                pin.rectTransform.localPosition=rootCenter+(Vector3)(direction*radius*pinRadiusScale);
                pin.rectTransform.sizeDelta=pinSizeRatio*radius;
                pin.rectTransform.localRotation=Quaternion.Euler(0,0,-angle+pinRotationOffset);
                VisiblePinCount++;
            }
            pointerPivot.localPosition=pointerPivot.parent.InverseTransformPoint(worldCenter);
            pointerPivot.localRotation=Quaternion.Euler(0,0,-model.Angle);
            pointerImage.raycastTarget=false;pointerImage.sprite=pointerSprite;pointerImage.color=pointerTint;
            pointerImage.enabled=pointerSprite&&radius>0&&pointerWidthRatio>0&&pointerLengthRatio>0;
            pointerImage.rectTransform.pivot=new Vector2(.5f,0);
            pointerImage.rectTransform.localPosition=new Vector3(0,radius*pointerOffsetRatio,0);
            pointerImage.rectTransform.sizeDelta=new Vector2(radius*pointerWidthRatio,radius*pointerLengthRatio);
            pointerImage.rectTransform.localRotation=Quaternion.Euler(0,0,pointerRotationOffset);
        }
        private void OnDisable(){Hide();}
    }
}

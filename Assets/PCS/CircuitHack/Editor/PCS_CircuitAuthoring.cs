using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering.Universal;
using FourPaws.PCS.Editor;

namespace FourPaws.PCS.CircuitHack.Editor
{
    // 명시 메뉴로만 실행한다. 소유 자산이 있으면 검사만 하며 통째로 재생성하지 않는다.
    public static class PCS_CircuitAuthoring
    {
        private const string Root="Assets/PCS/CircuitHack/";
        private const string OverlayPath=Root+"Prefabs/PCS_CircuitOverlay.prefab";
        private const string RigPath=Root+"Prefabs/PCS_CircuitDemoRig.prefab";
        private const string ScenePath=Root+"Scenes/PCS_CircuitHackDemo.unity";
        private static readonly string[] DataPaths={Root+"Data/PCS_CircuitStage01.asset",Root+"Data/PCS_CircuitStage02.asset",Root+"Data/PCS_CircuitStage03.asset"};
        private static readonly string[] Assets=DataPaths.Concat(new[]{OverlayPath,RigPath,ScenePath}).ToArray();
        private static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException("PCS 회로 검사: "+message);}
        [MenuItem("Tools/PCS/회로 미로/P4 생성 또는 저장 검사")]
        public static void CreateOrValidate()
        {
            PCS_DemoAuthoring.Preflight();foreach(string p in Assets)PCS_DemoAuthoring.CheckExactPath(p);
            if(Assets.Any(File.Exists))
            {
                Require(Assets.All(File.Exists),"부분 생성 자산이 있습니다. 보존 상태를 검토한 뒤 필요한 요소만 이어서 만드세요.");
                ValidateSaved();RunLogic();return;
            }
            foreach(string p in Assets)Require(!File.Exists(p+".meta"),"본체 없이 기존 메타가 있습니다: "+p);
            foreach(string folder in new[]{"Data","Prefabs","Scenes"}){string p=Root+folder;PCS_DemoAuthoring.CheckExactPath(p+".meta");if(!AssetDatabase.IsValidFolder(p))AssetDatabase.CreateFolder(Root.TrimEnd('/'),folder);}
            PCS_DemoAuthoring.RecordRevision("p4_folders",Root+"Data.meta",Root+"Prefabs.meta",Root+"Scenes.meta");
            for(int i=0;i<3;i++){var d=ScriptableObject.CreateInstance<CircuitMazeData>();Fill(d,i);Require(CircuitMazeData.Validate(d.Capture())==null,"초기 미로 경로");AssetDatabase.CreateAsset(d,DataPaths[i]);PCS_DemoAuthoring.RecordRevision("p4_data"+(i+1),DataPaths[i]);}
            // 생성 트랜잭션에서는 아래 명시 씬만 변경한다. 기존 씬은 사전 검사 뒤 닫는다.
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            GameObject overlay=null,rig=null;
            try
            {
                overlay=CreateOverlay();SaveNew(overlay,OverlayPath);UnityEngine.Object.DestroyImmediate(overlay);overlay=null;
                rig=CreateRig(scene);SaveNew(rig,RigPath);UnityEngine.Object.DestroyImmediate(rig);rig=null;
            }
            finally{if(overlay)UnityEngine.Object.DestroyImmediate(overlay);if(rig)UnityEngine.Object.DestroyImmediate(rig);}
            var camera=new GameObject("PCS_CircuitCamera",typeof(Camera),typeof(AudioListener),typeof(UniversalAdditionalCameraData));
            camera.transform.position=new Vector3(0,0,-10);camera.GetComponent<Camera>().clearFlags=CameraClearFlags.SolidColor;camera.GetComponent<Camera>().backgroundColor=new Color(.01f,.025f,.035f);
            PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(RigPath),scene);
            var backdrop=GameObject.CreatePrimitive(PrimitiveType.Cube);backdrop.name="PCS_CircuitBackdrop";backdrop.transform.localScale=new Vector3(12,7,.2f);backdrop.transform.position=new Vector3(0,0,2);
            PCS_DemoAuthoring.CheckExactPath(ScenePath);Require(EditorSceneManager.SaveScene(scene,ScenePath),"명시 씬 저장 실패");PCS_DemoAuthoring.RecordRevision("p4_scene",ScenePath);
            ValidateSaved();RunLogic();PCS_DemoAuthoring.EvidenceP4("P4_CREATED","SO 3개, Overlay/Rig 프리팹 2개, 독립 씬 1개 생성·저장·재열기·참조 검사 PASS");Debug.Log("PCS 회로 미로: 생성·저장·참조·논리 검사 통과. 실제 Play는 별도 확인하세요.");
        }
        private static void SaveNew(GameObject root,string path)
        {
            PCS_DemoAuthoring.CheckExactPath(path);Require(!File.Exists(path)&&!File.Exists(path+".meta"),"기존 자산을 덮어쓸 수 없습니다: "+path);
            PrefabUtility.SaveAsPrefabAsset(root,path,out bool ok);Require(ok,"프리팹 저장 실패: "+path);PCS_DemoAuthoring.RecordRevision("p4_"+Path.GetFileNameWithoutExtension(path),path);
        }
        private static RectTransform RectAt(string name,Transform parent,Vector2 position,Vector2 size)
        {
            var go=new GameObject(name,typeof(RectTransform));var r=(RectTransform)go.transform;if(parent)r.SetParent(parent,false);r.anchorMin=r.anchorMax=r.pivot=new Vector2(.5f,.5f);r.anchoredPosition=position;r.sizeDelta=size;return r;
        }
        private static Image Box(string name,Transform parent,Vector2 p,Vector2 size,Color color)
        {
            var r=RectAt(name,parent,p,size);r.gameObject.AddComponent<CanvasRenderer>();var image=r.gameObject.AddComponent<Image>();image.color=color;image.raycastTarget=false;return image;
        }
        private static Text Label(string name,Transform parent,Vector2 p,Vector2 size,string value,int fontSize)
        {
            var r=RectAt(name,parent,p,size);r.gameObject.AddComponent<CanvasRenderer>();var text=r.gameObject.AddComponent<Text>();text.font=AssetDatabase.LoadAssetAtPath<Font>("Assets/Imports/Galmuri7.ttf");Require(text.font,"한글 글꼴 누락");text.text=value;text.fontSize=fontSize;text.alignment=TextAnchor.MiddleCenter;text.color=new Color(.85f,.98f,.95f);text.raycastTarget=false;text.horizontalOverflow=HorizontalWrapMode.Wrap;text.verticalOverflow=VerticalWrapMode.Truncate;return text;
        }
        private static Button ButtonAt(string name,Transform parent,Vector2 p,Vector2 size,string value)
        {
            var image=Box(name,parent,p,size,new Color(.09f,.25f,.28f));image.raycastTarget=true;var b=image.gameObject.AddComponent<Button>();b.targetGraphic=image;b.navigation=new Navigation{mode=Navigation.Mode.None};Label("Label",b.transform,Vector2.zero,size-new Vector2(12,4),value,21);return b;
        }
        private static Canvas CanvasAt(string name)
        {
            var r=RectAt(name,null,Vector2.zero,new Vector2(1280,720));var canvas=r.gameObject.AddComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;var scale=r.gameObject.AddComponent<CanvasScaler>();scale.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scale.referenceResolution=new Vector2(1280,720);scale.matchWidthOrHeight=.5f;r.gameObject.AddComponent<GraphicRaycaster>();return canvas;
        }
        private static GameObject CreateOverlay()
        {
            var canvas=CanvasAt("PCS_CircuitOverlay");canvas.sortingOrder=10;var ui=canvas.gameObject.AddComponent<CircuitHackUI>();
            Box("Backdrop",canvas.transform,Vector2.zero,new Vector2(1280,720),new Color(.015f,.035f,.045f,.98f));
            ui.title=Label("Title",canvas.transform,new Vector2(0,320),new Vector2(1100,44),"회로 차단 / 단계 1 / 3",29);
            ui.counters=Label("Counters",canvas.transform,new Vector2(0,265),new Vector2(1100,64),"시도 0 · 실패 0 · 속도 8.00\n자동 전진 · 방향키 / WASD / D-pad로 방향 전환",20);
            ui.background=Box("Board",canvas.transform,new Vector2(0,-10),new Vector2(800,480),new Color(.015f,.10f,.10f));ui.board=ui.background.rectTransform;
            for(int i=0;i<9;i++){Box("TraceH"+i,ui.board,new Vector2(0,-210+i*52),new Vector2(780,1),new Color(.06f,.18f,.17f));}
            for(int i=0;i<15;i++){Box("TraceV"+i,ui.board,new Vector2(-365+i*52,0),new Vector2(1,460),new Color(.06f,.18f,.17f));}
            ui.wallViews=new Image[32];for(int i=0;i<32;i++){var w=Box("Wall"+i,ui.board,Vector2.zero,new Vector2(10,10),new Color(.18f,.29f,.32f));var outline=w.gameObject.AddComponent<Outline>();outline.effectColor=new Color(.43f,.60f,.58f);outline.effectDistance=Vector2.one*2;ui.wallViews[i]=w;w.gameObject.SetActive(false);}
            ui.start=Box("Start",ui.board,Vector2.zero,new Vector2(24,24),new Color(.1f,.65f,.65f)).rectTransform;
            ui.goal=Box("Goal",ui.board,Vector2.zero,new Vector2(64,64),new Color(.3f,.8f,.4f)).rectTransform;Label("GoalLabel",ui.goal,Vector2.zero,new Vector2(62,30),"출구",16);
            var trail=RectAt("Trail",ui.board,Vector2.zero,ui.board.sizeDelta);trail.gameObject.AddComponent<CanvasRenderer>();ui.trail=trail.gameObject.AddComponent<CircuitTrailRenderer>();ui.trail.raycastTarget=false;
            ui.head=Box("Head",ui.board,Vector2.zero,new Vector2(11.2f,11.2f),new Color(.4f,1,.8f)).rectTransform;
            ui.status=Label("Status",canvas.transform,new Vector2(0,-278),new Vector2(1180,42),"초록 출구로 신호를 보내세요. 기본 시간제한과 흔적 충돌은 없습니다.",20);
            ui.close=ButtonAt("Close",canvas.transform,new Vector2(-420,-330),new Vector2(250,42),"닫기 / Esc");ui.restart=ButtonAt("Restart",canvas.transform,new Vector2(0,-330),new Vector2(330,42),"현재 단계 다시 시작");ui.newHack=ButtonAt("NewHack",canvas.transform,new Vector2(420,-330),new Vector2(250,42),"새 해킹 시작");
            ui.Bind(AssetDatabase.LoadAssetAtPath<CircuitMazeData>(DataPaths[0]).Capture());return canvas.gameObject;
        }
        private static GameObject CreateRig(Scene preview)
        {
            var rig=new GameObject("PCS_CircuitDemoRig");SceneManager.MoveGameObjectToScene(rig,preview);
            var manager=rig.AddComponent<CircuitHackManager>();manager.input=rig.AddComponent<CircuitInputAdapter>();manager.failureAudio=rig.AddComponent<AudioSource>();manager.failureAudio.playOnAwake=false;manager.failureAudio.spatialBlend=0;manager.failureAudio.volume=.6f;
            manager.stages=DataPaths.Select(AssetDatabase.LoadAssetAtPath<CircuitMazeData>).ToArray();
            manager.overlay=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(OverlayPath),preview);manager.overlay.transform.SetParent(rig.transform,false);manager.view=manager.overlay.GetComponent<CircuitHackUI>();
            var launcher=CanvasAt("PCS_CircuitLauncher");launcher.transform.SetParent(rig.transform,false);Box("LauncherShade",launcher.transform,Vector2.zero,new Vector2(1280,720),new Color(.015f,.045f,.055f,.96f));Label("Title",launcher.transform,new Vector2(0,210),new Vector2(1100,70),"FOUR PAWS, ASCEND! / 회로 미로 실습",30);
            Label("Hint",launcher.transform,new Vector2(0,95),new Vector2(1000,125),"신호는 자동으로 전진합니다.\n방향키 / WASD / D-pad로 90도 방향만 바꾸세요.\n벽이나 경계에 닿으면 현재 단계가 자동으로 다시 시작됩니다.",23);
            var host=rig.AddComponent<CircuitDemoHost>();host.manager=manager;host.launch=ButtonAt("Launch",launcher.transform,new Vector2(0,-60),new Vector2(420,65),"회로 미로 시작(P4)");host.result=Label("Result",launcher.transform,new Vector2(0,-225),new Vector2(1150,70),"회로 결과: 대기 · 성공 전달 0회 · 취소 전달 0회",21);
            var events=new GameObject("PCS_CircuitEventSystem");events.SetActive(false);events.transform.SetParent(rig.transform,false);host.eventSystem=events.AddComponent<EventSystem>();host.eventSystem.sendNavigationEvents=false;host.uiModule=events.AddComponent<InputSystemUIInputModule>();host.uiModule.enabled=false;events.SetActive(true);
            manager.overlay.SetActive(false);return rig;
        }
        private static void Fill(CircuitMazeData data,int index)
        {
            if(index==0){data.walls=new[]{new Rect(30,0,6,42),new Rect(64,18,6,42)};data.goal=new Rect(90,6,8,8);data.referenceRoute=new[]{new Vector2(5,30),new Vector2(18,30),new Vector2(18,51),new Vector2(50,51),new Vector2(50,10),new Vector2(94,10)};}
            if(index==1){data.walls=new[]{new Rect(22,0,6,48),new Rect(45,12,6,48),new Rect(68,0,6,48)};data.goal=new Rect(90,50,8,8);data.referenceRoute=new[]{new Vector2(5,30),new Vector2(12,30),new Vector2(12,54),new Vector2(36,54),new Vector2(36,6),new Vector2(60,6),new Vector2(60,54),new Vector2(94,54)};}
            if(index==2){data.walls=new[]{new Rect(18,0,4,52),new Rect(34,8,4,52),new Rect(50,0,4,52),new Rect(66,8,4,52),new Rect(82,0,4,52)};data.goal=new Rect(90,52,8,7);data.referenceRoute=new[]{new Vector2(5,30),new Vector2(10,30),new Vector2(10,56),new Vector2(28,56),new Vector2(28,4),new Vector2(44,4),new Vector2(44,56),new Vector2(60,56),new Vector2(60,4),new Vector2(76,4),new Vector2(76,56),new Vector2(94,56)};}
        }
        [MenuItem("Tools/PCS/회로 미로/P4 저장 자산 검사")]
        public static void ValidateSaved()
        {
            var current=AssetDatabase.LoadAssetAtPath<GameObject>(RigPath);if(current&&current.GetComponent<CircuitHackManager>().layouts?.Length>0){PCS_DesignerAuthoring.ValidateCircuitAssets();Debug.Log("PCS 편집 배치 저장 참조 검사 통과. 경로 미검증은 별도 경고입니다.");return;}

            PCS_DemoAuthoring.Preflight();foreach(string p in Assets){PCS_DemoAuthoring.CheckExactPath(p);Require(File.Exists(p)&&File.Exists(p+".meta"),"저장 자산 누락: "+p);}
            foreach(string p in DataPaths){var d=AssetDatabase.LoadAssetAtPath<CircuitMazeData>(p);Require(d&&CircuitMazeData.Validate(d.Capture())==null,"저장 데이터와 실제 경로: "+p);}
            var overlay=PrefabUtility.LoadPrefabContents(OverlayPath);try{var ui=overlay.GetComponent<CircuitHackUI>();Require(ui&&ui.ReferencesValid,"Overlay 표시 참조");Require(overlay.GetComponentsInChildren<EventSystem>(true).Length==0,"Overlay에는 EventSystem이 없어야 합니다.");foreach(var graphic in overlay.GetComponentsInChildren<Graphic>(true))Require(graphic.GetComponent<CanvasRenderer>(),"CanvasRenderer 누락");foreach(var t in overlay.GetComponentsInChildren<Text>(true))Require(AssetDatabase.GetAssetPath(t.font)=="Assets/Imports/Galmuri7.ttf","한글 글꼴 참조");}finally{PrefabUtility.UnloadPrefabContents(overlay);}
            var rig=PrefabUtility.LoadPrefabContents(RigPath);try{CheckRig(rig);}finally{PrefabUtility.UnloadPrefabContents(rig);}
            var scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Single);Require(SceneManager.sceneCount==1&&!scene.isDirty,"P4 단독 저장 씬");
            var roots=scene.GetRootGameObjects();Require(roots.SelectMany(r=>r.GetComponentsInChildren<Camera>(true)).Count()==1,"카메라 1개");Require(roots.SelectMany(r=>r.GetComponentsInChildren<EventSystem>(true)).Count()==1,"EventSystem 1개");var host=roots.SelectMany(r=>r.GetComponentsInChildren<CircuitDemoHost>(true)).Single();CheckRig(host.gameObject);
            PCS_DemoAuthoring.EvidenceP4("P4_SAVED_REFERENCES","SO/Overlay/Rig/단독 씬 재열기, 필수 참조, 글꼴, CanvasRenderer, 입력 모듈 검사 PASS");Debug.Log("PCS 회로 미로: 저장 자산 참조 검사 통과");
        }
        private static void CheckRig(GameObject root)
        {
            Require(root.GetComponentsInChildren<MonoBehaviour>(true).All(c=>c!=null),"누락 스크립트");
            var host=root.GetComponent<CircuitDemoHost>();Require(host&&host.manager&&host.launch&&host.result&&host.eventSystem&&host.uiModule,"호스트 참조");var m=host.manager;
            Require(m.input&&m.view&&m.view.ReferencesValid&&m.overlay&&m.failureAudio&&m.ValidateSetup()==null,"Manager 필수 참조·설정");Require(!m.transform.IsChildOf(m.overlay.transform),"Manager가 Overlay에 포함됨");Require(!host.uiModule.enabled&&!host.eventSystem.sendNavigationEvents,"저장 UI 모듈은 자기 런타임 설정 전 비활성, 방향 탐색 없음");
            foreach(var t in new[]{m.transform,m.input.transform,m.view.transform,m.overlay.transform,m.failureAudio.transform,host.launch.transform,host.result.transform,host.eventSystem.transform,host.uiModule.transform})Require(t.IsChildOf(root.transform),"Rig 밖의 필수 참조");
            Require(m.view.wallViews.Distinct().Count()==32&&m.view.wallViews.All(w=>w.transform.IsChildOf(m.view.board)),"벽 풀 중복 또는 좌표계 이탈");
            for(int i=0;i<3;i++)Require(AssetDatabase.GetAssetPath(m.stages[i])==DataPaths[i],"단계 순서");
        }
        [MenuItem("Tools/PCS/회로 미로/P4 논리 검사 %#F6")]
        public static void LogicChecks(){PCS_DemoAuthoring.Preflight();RunLogic();}
        [MenuItem("Tools/PCS/회로 미로/P4 시작 화면 대비 보완")]
        public static void ImproveLauncher()
        {
            PCS_DemoAuthoring.Preflight();var rig=PrefabUtility.LoadPrefabContents(RigPath);
            try{var launcher=rig.transform.Find("PCS_CircuitLauncher");Require(launcher,"저장 시작 화면 누락");if(!launcher.Find("LauncherShade")){var shade=Box("LauncherShade",launcher,Vector2.zero,new Vector2(1280,720),new Color(.015f,.045f,.055f,.96f));shade.transform.SetAsFirstSibling();PCS_DemoAuthoring.CheckExactPath(RigPath);string guid=AssetDatabase.AssetPathToGUID(RigPath);PrefabUtility.SaveAsPrefabAsset(rig,RigPath,out bool ok);Require(ok&&guid==AssetDatabase.AssetPathToGUID(RigPath),"시작 화면 보존 저장");PCS_DemoAuthoring.RecordRevision("p4_launcher_contrast",RigPath);}}
            finally{PrefabUtility.UnloadPrefabContents(rig);}
            ValidateSaved();
        }
        [MenuItem("Tools/PCS/회로 미로/P4 Play 경로 주입 검사")]
        public static void PlayRouteCheck()
        {
            Require(EditorApplication.isPlaying&&SceneManager.sceneCount==1&&SceneManager.GetActiveScene().path==ScenePath,"P4 단독 Play에서만 실행하세요.");
            var manager=SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<CircuitHackManager>(true)).Single();
            manager.CloseHack();manager.StartHack();Require(manager.State==CircuitHackManager.HackState.Running,"Play 시작");
            int completed=0;Action<int> onComplete=_=>completed++;manager.OnHackCompleted+=onComplete;
            try{for(int i=0;i<3;i++){Walk(manager);if(i<2)manager.Advance(manager.stageDelay,Vector2Int.zero);}Require(manager.State==CircuitHackManager.HackState.Completed&&completed==1,"Play 3단계와 완료 단발");PCS_DemoAuthoring.EvidenceP4("P4_AUTOMATED_PLAY","실제 Play Manager에 방향과 활성 dt를 주입: 3단계 연속, 출구 충돌 코드, 완료 이벤트 1회 PASS. 물리 키 완주가 아니며 화면은 별도 캡처.");}
            finally{manager.OnHackCompleted-=onComplete;}
        }
        private static bool lifecycleCheckRunning;
        [MenuItem("Tools/PCS/회로 미로/P4 Play 종료 수명 검사 %#F5")]
        public static void PlayLifecycleCheck()
        {
            Require(EditorApplication.isPlaying&&SceneManager.sceneCount==1&&SceneManager.GetActiveScene().path==ScenePath,"P4 단독 Play에서만 실행하세요.");
            var host=SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<CircuitDemoHost>(true)).Single();var m=host.manager;
            Require(!lifecycleCheckRunning,"종료 수명 검사가 진행 중입니다.");
            Require(host.isActiveAndEnabled,"활성 자기 호스트가 필요합니다.");m.CloseHack();int cancels=0;
            Action<int> callback=_=>{cancels++;m.EndHost();m.StartHack();};m.OnHackCancelled+=callback;lifecycleCheckRunning=true;
            try
            {
                host.Begin();int run=m.RunId;host.enabled=false;Require(m.State==CircuitHackManager.HackState.Closed&&m.RunId==run&&cancels==1,"Host 비활성화 재진입 차단");host.enabled=true;
                host.Begin();run=m.RunId;host.gameObject.SetActive(false);Require(m.State==CircuitHackManager.HackState.Closed&&m.RunId==run&&cancels==2,"Rig 비활성화 재진입 차단");host.gameObject.SetActive(true);
                host.Begin();run=m.RunId;int frame=Time.frameCount;double deadline=EditorApplication.timeSinceStartup+8;m.overlay.SetActive(false);
                EditorApplication.CallbackFunction tick=null;
                tick=()=>
                {
                    if(EditorApplication.isPlaying&&m&&Time.frameCount<=frame+1&&EditorApplication.timeSinceStartup<deadline)return;
                    EditorApplication.update-=tick;
                    try{Require(EditorApplication.isPlaying&&m&&m.State==CircuitHackManager.HackState.Closed&&m.RunId==run&&cancels==3&&!m.overlay.activeSelf,"Overlay 비활성화 후 실제 Update 종료와 재진입 차단");m.OnHackCancelled-=callback;int oldAttempt=m.AttemptId;host.Begin();Require(m.State==CircuitHackManager.HackState.Running&&m.RunId==run+1&&m.AttemptId==oldAttempt+1,"재활성화 후 새 시도 시작");m.CloseHack();Require(m.State==CircuitHackManager.HackState.Closed,"재활성화 후 시작·닫기");PCS_DemoAuthoring.EvidenceP4("P4_PLAY_LIFECYCLE","실제 Play Host/전체 Rig/Overlay 비활성화·재활성화·중첩 종료 콜백 재진입 차단 PASS. Overlay는 실제 Update 뒤 판정. 객체 파괴·누수 계측·물리 입력 검사가 아닙니다.");Debug.Log("PCS 회로: 실제 Play 종료 수명 검사 통과");}
                    finally{if(m)m.OnHackCancelled-=callback;lifecycleCheckRunning=false;}
                };
                EditorApplication.update+=tick;
            }
            catch{m.OnHackCancelled-=callback;lifecycleCheckRunning=false;if(host){host.gameObject.SetActive(true);host.enabled=true;}throw;}
        }
        [MenuItem("Tools/PCS/회로 미로/P4 Play 입력 상태 기록")]
        public static void RecordPlayInput()
        {
            Require(EditorApplication.isPlaying&&SceneManager.sceneCount==1&&SceneManager.GetActiveScene().path==ScenePath,"P4 단독 Play에서만 실행하세요.");
            var m=SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<CircuitHackManager>(true)).Single();
            var keyboard=UnityEngine.InputSystem.Keyboard.current;
            string text="키보드 존재="+(keyboard!=null)+"; 활성="+(keyboard!=null&&keyboard.enabled)+"; 최근 장치 갱신="+(keyboard==null?"없음":keyboard.lastUpdateTime.ToString("0.000"))+"; 입력 갱신 모드="+UnityEngine.InputSystem.InputSystem.settings.updateMode+"; 입력 해제 완료="+m.input.ReleaseReady+"; 읽은 방향 프레임="+m.input.ObservedDirectionFrames+"; 창 포커스="+Application.isFocused+"; 퍼즐 상태="+m.State+"; 방향="+m.Signal.Direction;
            text+="; 실제 Update의 키보드 존재="+m.input.LastKeyboardPresent+"; 활성="+m.input.LastKeyboardEnabled+"; 최근 갱신="+m.input.LastKeyboardUpdate.ToString("0.000")+"; 키 누름 관측 프레임="+m.input.ObservedKeyPressFrames;
            PCS_DemoAuthoring.EvidenceP4("P4_INPUT_OBSERVATION",text);Debug.Log("PCS 회로 입력 관측: "+text);
        }
        private static void RunLogic()
        {
            var report=new StringBuilder();var data=ScriptableObject.CreateInstance<CircuitMazeData>();GameObject go=null;
            try
            {
                for(int i=0;i<3;i++){Fill(data,i);Require(CircuitMazeData.Validate(data.Capture())==null,"M14 단계 경로 "+i);report.AppendLine("M14 stage="+(i+1)+" 경로 PASS");}
                var signal=new CircuitSignalController();signal.Reset(Vector2.zero,Vector2Int.right,8);Require(!signal.Turn(Vector2Int.right)&&!signal.Turn(Vector2Int.left)&&signal.Turn(Vector2Int.up),"M01~03 방향 정책");
                foreach(int fps in new[]{30,60,120}){signal.Reset(Vector2.zero,Vector2Int.left,8);for(int i=0;i<10*fps;i++)signal.Commit(signal.Propose(1f/fps));Require(Mathf.Abs(signal.Position.x+80)<.001f,"M04 시간 기반 이동 "+fps);}
                data.walls=new[]{new Rect(20,0,.1f,60)};data.goal=new Rect(90,50,8,8);var snapshot=data.Capture();var hit=CircuitCollisionChecker.Check(snapshot,new Vector2(5,30),new Vector2(45,30));Require(hit.kind==CircuitCollisionChecker.Kind.Wall&&Mathf.Abs(hit.position.x-19.3f)<.0001f,"M05 얇은 벽 큰 구간");
                snapshot.walls=Array.Empty<Rect>();hit=CircuitCollisionChecker.Check(snapshot,new Vector2(5,30),new Vector2(105,30));Require(hit.kind==CircuitCollisionChecker.Kind.Wall&&Mathf.Abs(hit.position.x-99.3f)<.0001f,"M06 경계 첫 접촉");
                snapshot.walls=new[]{new Rect(20,20,5,5)};Require(CircuitCollisionChecker.Check(snapshot,new Vector2(5,19.3f),new Vector2(30,19.3f)).kind==CircuitCollisionChecker.Kind.Wall,"M07 머리 옆면");Require(CircuitCollisionChecker.Check(snapshot,new Vector2(5,19.2f),new Vector2(30,19.2f)).kind==CircuitCollisionChecker.Kind.None,"M07 여유 폭");
                snapshot.walls=Array.Empty<Rect>();snapshot.goal=new Rect(40,20,8,8);Require(CircuitCollisionChecker.Check(snapshot,new Vector2(5,24),new Vector2(40.6f,24)).kind==CircuitCollisionChecker.Kind.None,"M08 부분 진입");Require(CircuitCollisionChecker.Check(snapshot,new Vector2(5,24),new Vector2(42,24)).kind==CircuitCollisionChecker.Kind.Goal,"M08 전체 진입");
                snapshot.walls=new[]{new Rect(41.4f,20,1,8)};Require(CircuitCollisionChecker.Check(snapshot,new Vector2(5,24),new Vector2(60,24)).kind==CircuitCollisionChecker.Kind.Wall,"M09 동률 실패 우선");snapshot.walls=new[]{new Rect(50,20,1,8)};Require(CircuitCollisionChecker.Check(snapshot,new Vector2(5,24),new Vector2(60,24)).kind==CircuitCollisionChecker.Kind.Goal,"M09 출구 우선");
                Require(CircuitInputAdapter.Resolve(true,false,false,false)==Vector2Int.right&&CircuitInputAdapter.Resolve(true,true,false,false)==Vector2Int.zero,"P03 여러 방향 무시");
                Require(Mathf.Abs(CircuitHackManager.SpeedFor(8,3,true)-7.2f)<.0001f&&Mathf.Abs(CircuitHackManager.SpeedFor(8,15,true)-4.8f)<.0001f&&CircuitHackManager.SpeedFor(8,30,false)==8,"M16 감속 정책");
                go=new GameObject("PCS_CircuitLogicOnly");var manager=go.AddComponent<CircuitHackManager>();manager.stages=DataPaths.Select(AssetDatabase.LoadAssetAtPath<CircuitMazeData>).ToArray();int cancelled=0,completed=0;manager.OnHackCancelled+=_=>cancelled++;manager.OnHackCompleted+=_=>completed++;
                manager.StartHack();int run=manager.RunId,attempt=manager.AttemptId;for(int i=0;i<10;i++)manager.StartHack();Require(manager.RunId==run,"L01 중복 시작");manager.Advance(10,Vector2Int.zero);Require(manager.State==CircuitHackManager.HackState.FailFeedback&&manager.Failures==1,"M11 충돌");manager.Advance(.749f,Vector2Int.up);Require(manager.AttemptId==attempt,"M11 지연 전");manager.Advance(.0011f,Vector2Int.up);Require(manager.AttemptId==attempt+1&&manager.StageIndex==0&&manager.Signal.Direction==Vector2Int.right,"M11 자동 재시작 입력 폐기");
                manager.RestartStage();Require(manager.Failures==1,"M16 수동 재시작 누적 보존");for(int i=0;i<10;i++)manager.CloseHack();manager.Advance(100,Vector2Int.up);Require(cancelled==1&&manager.State==CircuitHackManager.HackState.Closed,"L02~03 중복 취소와 지연 무효");
                manager.StartHack();for(int i=0;i<3;i++){Walk(manager);if(i<2){int stage=manager.StageIndex;manager.Advance(.599f,Vector2Int.left);Require(manager.StageIndex==stage,"M12 지연 전");manager.Advance(.0011f,Vector2Int.left);Require(manager.StageIndex==stage+1&&manager.Failures==0,"M12 단계 전환");}}
                Require(completed==1&&manager.State==CircuitHackManager.HackState.Completed,"M13 완료 단발");manager.Advance(100,Vector2Int.up);manager.CloseHack();Require(completed==1&&cancelled==1,"L04 성공 후 닫기");
                manager.OnHackCancelled+=_=>manager.StartHack();manager.StartHack();int prior=manager.RunId;manager.CloseHack();Require(manager.RunId==prior+1&&manager.State==CircuitHackManager.HackState.Running,"L06 취소 콜백 재진입");
                ExtendedLogic(manager,data,report);
                BoundaryRegression(report);
                report.AppendLine("명시한 모델/Manager 논리 조건 PASS. 실제 키·포커스·패드·FPS·청취 검증과 구분.");
                PCS_DemoAuthoring.EvidenceP4("P4_LOGIC",report.ToString());Debug.Log("PCS 회로 미로: 명시 논리 검사 통과");
            }
            finally{if(go)UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(data);}
        }
        private static void Walk(CircuitHackManager manager)
        {
            var route=manager.CurrentData.referenceRoute;
            for(int i=1;i<route.Length;i++){var d=route[i]-manager.Signal.Position;manager.Advance(d.magnitude/manager.Signal.Speed,new Vector2Int(Math.Sign(d.x),Math.Sign(d.y)));}
        }
        private static void BoundaryRegression(StringBuilder report)
        {
            var go=new GameObject("PCS_CircuitBoundaryLogic");var m=go.AddComponent<CircuitHackManager>();m.stages=DataPaths.Select(AssetDatabase.LoadAssetAtPath<CircuitMazeData>).ToArray();
            bool nested;
            Action<int> callback=_=>{m.EndHost();m.StartHack();};m.OnHackCancelled+=callback;
            try{m.StartHack();int run=m.RunId;m.EndHost();nested=m.State==CircuitHackManager.HackState.Closed&&m.RunId==run;}
            finally{m.OnHackCancelled-=callback;UnityEngine.Object.DestroyImmediate(go);}
            var data=AssetDatabase.LoadAssetAtPath<CircuitMazeData>(DataPaths[0]).Capture();data.walls=new[]{new Rect(50,20,1,8)};data.goal=new Rect(40,20,8,8);
            var hit=CircuitCollisionChecker.Check(data,new Vector2(5,24),new Vector2(8000005,24));bool first=hit.kind==CircuitCollisionChecker.Kind.Goal;
            PCS_DemoAuthoring.EvidenceP4("P4_REGRESSION_BOUNDARY","중첩 종료 재진입 차단="+nested+"; 매우 긴 구간 출구 우선="+first);
            Require(nested&&first,"종료 중첩 또는 매우 긴 구간의 첫 사건 회귀");
            foreach(int mirror in new[]{1,-1})foreach(int fps in new[]{30,60,120})
            {
                data.walls=new[]{new Rect(mirror==1?20:79.9f,0,.1f,60)};data.goal=new Rect(90,50,8,8);
                var s=new CircuitSignalController();s.Reset(new Vector2(mirror==1?5:95,5),new Vector2Int(mirror,0),8);float hitTime=-1;
                for(int frame=0;frame<4*fps;frame++)
                {
                    if(frame==fps)s.Turn(Vector2Int.up);if(frame==2*fps)s.Turn(new Vector2Int(mirror,0));
                    hit=CircuitCollisionChecker.Check(data,s.Position,s.Propose(1f/fps));s.Commit(hit.position);
                    if(hit.kind!=CircuitCollisionChecker.Kind.None){Require(hit.kind==CircuitCollisionChecker.Kind.Wall,"M04 첫 충돌 종류");hitTime=(frame+hit.t)/fps;break;}
                }
                Require(Mathf.Abs(hitTime-2.7875f)<.001f&&Vector2.Distance(s.Position,new Vector2(mirror==1?19.3f:80.7f,13))<.001f,"M04 양/음 이동·동일 시각 회전·충돌 "+fps);
            }
            var rig=PrefabUtility.LoadPrefabContents(RigPath);var temp=ScriptableObject.CreateInstance<CircuitMazeData>();
            try
            {
                Fill(temp,0);temp.walls=Array.Empty<Rect>();temp.start=new Vector2(10,10);temp.goal=new Rect(90,50,8,8);temp.referenceRoute=new[]{temp.start,new Vector2(94,10),new Vector2(94,54)};
                var live=rig.GetComponent<CircuitHackManager>();live.stages=new[]{temp,temp,temp};live.StartHack();int run=live.RunId,attempt=live.AttemptId;live.Advance(1,Vector2Int.zero);
                var directions=new[]{Vector2Int.up,Vector2Int.left,Vector2Int.down,Vector2Int.right};
                for(int i=0;i<512;i++){Vector2 before=live.Signal.Position;var direction=directions[i%4];Require(live.Signal.Direction.x*direction.x+live.Signal.Direction.y*direction.y==0,"L07 유효 90도 입력");live.Advance(1,direction);Require(Vector2.Distance(live.Signal.Position,before+(Vector2)direction*8)<.001f,"L07 이동 거리");}
                Require(live.State==CircuitHackManager.HackState.Running&&live.RunId==run&&live.AttemptId==attempt&&live.Failures==0&&live.Signal.Speed==8&&live.view.trail.PointCount==256,"L07 Manager 512회 꺾임·속도·궤적 상한");live.EndHost();Require(live.view.trail.PointCount==0,"L07 닫기 궤적 정리");
            }
            finally{PrefabUtility.UnloadPrefabContents(rig);UnityEngine.Object.DestroyImmediate(temp);}
            report.AppendLine("추가 M04 양/음 이동·1초/2초 동일 시각 회전·얇은 벽 접촉 위치/시간 30·60·120 분할, M09 매우 긴 구간 첫 사건, L05 중첩 종료, L07 실제 Manager 꺾임512·속도8·점256/닫기0 PASS. 실제 렌더 FPS·객체 누수 계측과 구분.");
        }
        private static void ExtendedLogic(CircuitHackManager manager,CircuitMazeData data,StringBuilder report)
        {
            // 앞선 재진입 구독이 남은 Manager와 분리하여 수명 검사를 반복한다.
            var go=new GameObject("PCS_CircuitExtendedLogic");var m=go.AddComponent<CircuitHackManager>();m.stages=manager.stages;
            try
            {
                int cancels=0,completes=0;m.OnHackCancelled+=_=>cancels++;m.OnHackCompleted+=_=>completes++;
                m.assistSlowdown=true;m.StartHack();int attempt=m.AttemptId;
                for(int i=0;i<10;i++){m.Advance(100,Vector2Int.zero);Require(m.State==CircuitHackManager.HackState.FailFeedback,"L02 반복 실패");m.Advance(.75f,Vector2Int.zero);Require(m.AttemptId==attempt+i+1&&m.StageIndex==0&&m.Failures==i+1,"L02 반복 자동 재시작");}
                Require(Mathf.Abs(m.Signal.Speed-CircuitHackManager.SpeedFor(8,10,true))<.0001f,"M16 누적 감속");m.RestartStage();Require(m.Failures==10,"M16 수동 재시작 보존");
                m.Advance(100,Vector2Int.zero);m.CloseHack();m.Advance(100,Vector2Int.zero);Require(cancels==1&&m.State==CircuitHackManager.HackState.Closed,"L03 실패 대기 취소");
                m.StartHack();Require(m.Failures==0&&m.Signal.Speed==8,"M16 새 해킹 초기화");Walk(m);m.CloseHack();m.Advance(100,Vector2Int.zero);Require(cancels==2&&m.State==CircuitHackManager.HackState.Closed,"L03 단계 대기 취소");
                Action<int> reenter=_=>{m.CloseHack();m.StartHack();};m.OnHackCompleted+=reenter;m.StartHack();int run=m.RunId;
                for(int i=0;i<3;i++){Walk(m);if(i<2)m.Advance(.6f,Vector2Int.zero);}Require(m.RunId==run+1&&m.StageIndex==0&&m.State==CircuitHackManager.HackState.Running&&completes==1,"L06 완료 콜백 재진입");m.OnHackCompleted-=reenter;
                m.OnHackCancelled+=_=>m.StartHack();run=m.RunId;m.EndHost();Require(m.State==CircuitHackManager.HackState.Closed&&m.RunId==run,"L05 호스트 종료 중 재진입 차단");
                var trailGo=new GameObject("PCS_TrailLogic",typeof(RectTransform),typeof(CanvasRenderer));var trail=trailGo.AddComponent<CircuitTrailRenderer>();
                try{trail.ResetTrail(new Rect(0,0,100,60),new Vector2(10,10));for(int i=0;i<600;i++)trail.UpdateEnd(new Vector2(10+i%2,10+(i/2)%2),true);Require(trail.PointCount==256,"L07 궤적 상한");trail.ClearTrail();Require(trail.PointCount==0,"L07 흔적 초기화");}finally{UnityEngine.Object.DestroyImmediate(trailGo);}
                Fill(data,0);var valid=data.Capture();valid.walls=Array.Empty<Rect>();valid.goal=new Rect(90,50,8,8);
                foreach(var end in new[]{new Vector2(-5,30),new Vector2(105,30),new Vector2(50,-5),new Vector2(50,65)})Require(CircuitCollisionChecker.Check(valid,new Vector2(50,30),end).kind==CircuitCollisionChecker.Kind.Wall,"M06 네 경계");
                Require(CircuitCollisionChecker.Check(valid,new Vector2(.7f,30),new Vector2(.7f,30)).kind==CircuitCollisionChecker.Kind.Wall,"M06 접촉 시작 0 이동");
                Require(CircuitCollisionChecker.Check(valid,new Vector2(10,10),new Vector2(10,10)).kind==CircuitCollisionChecker.Kind.None,"M06 안전한 0 이동");
                valid.walls=new[]{new Rect(20,20,5,5)};Require(CircuitCollisionChecker.Check(valid,new Vector2(19.299995f,22),new Vector2(19.300001f,22)).kind==CircuitCollisionChecker.Kind.Wall,"M06 미세 구간 접촉");
                Require(CircuitCollisionChecker.Check(valid,new Vector2(5,5),new Vector2(19.3f,19.3f)).kind==CircuitCollisionChecker.Kind.Wall,"M06 모서리 접촉");
                valid.walls=Array.Empty<Rect>();var signal=new CircuitSignalController();signal.Reset(new Vector2(10,10),Vector2Int.right,8);
                foreach(var direction in new[]{Vector2Int.right,Vector2Int.up,Vector2Int.left,Vector2Int.down,Vector2Int.right}){signal.Turn(direction);var hit=CircuitCollisionChecker.Check(valid,signal.Position,signal.Propose(1));Require(hit.kind==CircuitCollisionChecker.Kind.None,"M10 자기 흔적 통과");signal.Commit(hit.position);}
                void Bad(Action<CircuitMazeData> mutate){Fill(data,0);data.bounds=new Rect(0,0,100,60);data.start=new Vector2(5,30);data.initialDirection=Vector2Int.right;data.baseSpeed=8;data.headWidth=data.visualHeadWidth=1.4f;mutate(data);Require(CircuitMazeData.Validate(data.Capture())!=null,"M15 잘못된 데이터 거부");}
                Bad(d=>d.baseSpeed=float.NaN);Bad(d=>d.headWidth=0);Bad(d=>d.visualHeadWidth=float.PositiveInfinity);Bad(d=>d.bounds=new Rect(0,0,-1,60));Bad(d=>d.start=new Vector2(.7f,30));Bad(d=>d.goal=new Rect(90,6,1,1));Bad(d=>d.walls=new[]{new Rect(90,6,1,1)});Bad(d=>d.walls=new[]{new Rect(-1,0,2,2)});Bad(d=>d.walls=new[]{new Rect(20,0,2,2),new Rect(20,0,2,2)});Bad(d=>d.referenceRoute[1]=d.start);Bad(d=>d.referenceRoute[1]=new Vector2(10,40));Bad(d=>d.initialDirection=Vector2Int.left);Bad(d=>d.referenceRoute[d.referenceRoute.Length-1]=new Vector2(60,10));
                m.stages=new CircuitMazeData[3];Require(m.ValidateSetup()!=null,"M15 null 단계");m.stages=manager.stages;m.failDelay=-1;Require(m.ValidateSetup()!=null,"M15 음수 대기");m.failDelay=float.NaN;Require(m.ValidateSetup()!=null,"M15 비유한 대기");m.failDelay=0;Require(m.ValidateSetup()==null,"M15 0 대기 허용");
                report.AppendLine("확장 M06 네 경계/0이동/미세선분/모서리, M10 자기 흔적, M15 잘못된 데이터 16종, M16 감속, L02 실패10회, L03 대기 취소, L05 호스트 종료, L06 완료 재진입, L07 점256/굴절600 PASS");
            }
            finally{UnityEngine.Object.DestroyImmediate(go);}
        }
    }
}

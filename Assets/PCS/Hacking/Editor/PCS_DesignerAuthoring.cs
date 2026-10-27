using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering.Universal;
using FourPaws.PCS.CircuitHack;
using FourPaws.PCS.Hacking;
using FourPaws.PCS.StepPuzzle;
using Object=UnityEngine.Object;
namespace FourPaws.PCS.Editor
{
    // 명시적인 이전/예제 생성만 디스크 소유권을 검사한다. 일반 Play와 배치 검사는 로컬 감사 파일과 독립적이다.
    public static class PCS_DesignerAuthoring
    {
        public const string Project="C:/FOUR-PAWS-ASCEND-NEWPCS";
        public const string Run="C:/FOUR-PAWS-PCS-BRIEF/FOUR-PAWS-PCS-IMPLEMENTATION-BRIEF/runs/20261015-201341-designer-c610c3db";
        const string CR="Assets/PCS/CircuitHack/";
        [Serializable] public sealed class FileRecord{public string path,sha256,guid;}
        [Serializable] public sealed class Records{public string reason,utc;public FileRecord[] files;}
        static void Require(bool value,string message){if(!value)throw new InvalidOperationException("PCS 확장 검사: "+message);}
        static string Hash(string path){using(var f=File.OpenRead(path))using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(f)).Replace("-","");}
        static void Safe(string path,string root)
        {
            string p=Path.GetFullPath(path).Replace('\\','/'),r=Path.GetFullPath(root).Replace('\\','/');Require(p==r||p.StartsWith(r+"/",StringComparison.OrdinalIgnoreCase),"경로 이탈");
            for(string part=p;!string.IsNullOrEmpty(part);part=Path.GetDirectoryName(part))if(File.Exists(part)||Directory.Exists(part))Require((File.GetAttributes(part)&FileAttributes.ReparsePoint)==0,"재분석 경로");
        }
        static Records Read(string path){Safe(path,Run);return JsonUtility.FromJson<Records>(File.ReadAllText(path));}
        static Records Scope()=>Read(Run+"/baseline/WRITE_MANIFEST.json");
        static Records Baseline()=>Read(Run+"/baseline/STATE.json");
        static Records Owned(){string p=Directory.GetFiles(Run+"/evidence","OWN_*.json").OrderBy(x=>x,StringComparer.Ordinal).LastOrDefault();return p==null?Baseline():Read(p);}
        public static void CheckPath(string path){Require(Scope().files.Any(f=>f.path==path),"확정한 쓰기 목록 밖: "+path);Safe(Project+"/"+path,Project);}
        public static void Guard(bool ownership=true)
        {
            Require(Path.GetFullPath(Application.dataPath+"/..").Replace('\\','/')==Project,"프로젝트 루트 불일치");
            Require(!EditorApplication.isPlayingOrWillChangePlaymode&&!EditorApplication.isCompiling,"Play/컴파일 중에는 저장 도구를 실행할 수 없습니다.");
            Require(PrefabStageUtility.GetCurrentPrefabStage()==null,"Prefab Stage 작업을 먼저 직접 마무리하세요.");
            for(int i=0;i<SceneManager.sceneCount;i++)Require(!SceneManager.GetSceneAt(i).isDirty,"미저장 씬은 자동 저장·폐기하지 않습니다.");
            Require(GitRead("branch --show-current")=="feature/PCS-NEW","브랜치 불일치");
            Require(GitRead("rev-parse HEAD")=="1b18d71c88a45e5fad6a85db5b3bfa6af3335b69","인수 HEAD 불일치");
            Require(GitRead("diff --cached --name-only").Length==0,"새 staged 변경");
            var allowed=new HashSet<string>(Scope().files.Select(f=>f.path));
            var known=new HashSet<string>(Baseline().files.Select(f=>f.path));
            foreach(string folder in new[]{"Assets","Packages","ProjectSettings"})CheckTree(Project+"/"+folder,known,allowed);
            foreach(var file in Baseline().files)
            {
                Safe(Project+"/"+file.path,Project);
                if(allowed.Contains(file.path)||IsIde(file.path))continue;
                Require(File.Exists(Project+"/"+file.path)&&Hash(Project+"/"+file.path)==file.sha256,"보호 변화: "+file.path);
            }
            Require(Hash(Project+"/ProjectSettings/QualitySettings.asset")=="79B05CB3497355DFDC96ED6DD7783E8733E99B62EB757D4FB09CAE7DD25DE4AC","품질 설정 고정 SHA 이탈");
            Require(Hash(Project+"/Assets/Imports/fusion-shared-basics-2.1.1/Assets/Photon/Fusion/Resources/PhotonAppSettings.asset")=="CD4443FB64CCE5EA804283BD677D1131ED07134263829DA4E09CAF2DEA8C045F","Photon 설정 고정 SHA 이탈");
            if(ownership)CheckOwnership();
        }
        static string GitRead(string arguments)
        {
            var info=new System.Diagnostics.ProcessStartInfo("git",arguments){WorkingDirectory=Project,UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};
            using(var process=System.Diagnostics.Process.Start(info)){string result=process.StandardOutput.ReadToEnd();process.WaitForExit();Require(process.ExitCode==0,"Git 읽기 검사 실패");return result.Trim();}
        }
        static void CheckTree(string folder,HashSet<string> known,HashSet<string> allowed)
        {
            Safe(folder,Project);
            foreach(string path in Directory.GetFiles(folder)){Safe(path,Project);string relative=path.Replace('\\','/').Substring(Project.Length+1);Require(known.Contains(relative)||allowed.Contains(relative),"새 보호 경로: "+relative);}
            foreach(string dir in Directory.GetDirectories(folder))CheckTree(dir,known,allowed);
        }
        static bool IsIde(string p)=>p=="Assembly-CSharp.csproj"||p=="Assembly-CSharp-Editor.csproj"||p=="FOUR-PAWS-ASCEND-NEWPCS.slnx";
        public static void CheckOwnership()
        {
            foreach(var f in Owned().files.Where(f=>f.path=="Assets/PCS.meta"||f.path.StartsWith("Assets/PCS/",StringComparison.Ordinal)))
                Require(File.Exists(Project+"/"+f.path)&&Hash(Project+"/"+f.path)==f.sha256,"생성/이전 전에 별도 편집 차이를 검토해야 합니다: "+f.path);
            foreach(var f in Baseline().files.Where(f=>f.guid!=null&&f.guid.Length>0))Require(AssetDatabase.AssetPathToGUID(f.path.Substring(0,f.path.Length-5))==f.guid,"기존 GUID 변화: "+f.path);
        }
        static void Backup(string path)
        {
            CheckPath(path);if(!File.Exists(Project+"/"+path))return;
            string target=Run+"/backup/editor_"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ")+"/"+path;Safe(target,Run);Directory.CreateDirectory(Path.GetDirectoryName(target));File.Copy(Project+"/"+path,target,false);
        }
        public static void Record(string reason,params string[] changed)
        {
            var allowed=new HashSet<string>(changed.SelectMany(p=>p.EndsWith(".meta",StringComparison.Ordinal)?new[]{p}:new[]{p,p+".meta"}));foreach(var p in allowed)CheckPath(p);
            var previous=Owned();
            foreach(var f in previous.files.Where(f=>f.path.StartsWith("Assets/PCS",StringComparison.Ordinal)))Require(File.Exists(Project+"/"+f.path)&&(Hash(Project+"/"+f.path)==f.sha256||allowed.Contains(f.path)),"명시 저장 밖 변화: "+f.path);
            var files=previous.files.ToDictionary(f=>f.path,f=>f);
            foreach(var p in Scope().files.Select(f=>f.path).Where(p=>File.Exists(Project+"/"+p)))
            {if(!files.ContainsKey(p))Require(allowed.Contains(p),"기록되지 않은 신규 경로: "+p);files[p]=new FileRecord{path=p,sha256=Hash(Project+"/"+p),guid=p.EndsWith(".meta",StringComparison.Ordinal)?AssetDatabase.AssetPathToGUID(p.Substring(0,p.Length-5)):null};}
            foreach(var f in Baseline().files.Where(f=>f.guid!=null&&f.guid.Length>0))Require(files[f.path].guid==f.guid,"기존 GUID 변경");
            string output=Run+"/evidence/OWN_"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ")+".json";Safe(output,Run);Require(!File.Exists(output),"소유 기록 충돌");File.WriteAllText(output,JsonUtility.ToJson(new Records{reason=reason,utc=DateTime.UtcNow.ToString("o"),files=files.Values.ToArray()},true));
        }
        public static void Evidence(string name,string detail)
        {
            string p=Run+"/evidence/"+name+"_"+DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ")+".txt";Safe(p,Run);Require(!File.Exists(p),"증거 충돌");File.WriteAllText(p,DateTime.UtcNow.ToString("o")+"\nUnity "+Application.unityVersion+"\n"+detail);
        }
        public static void IntakeCheck(){Guard();Evidence("EDITOR_INTAKE","현재 루트·보호·소유·GUID·Edit 상태 검사 통과. 실제 화면/입력 검사는 아님.");}
        static void SavePrefab(GameObject obj,string path){Backup(path);string guid=AssetDatabase.AssetPathToGUID(path);PrefabUtility.SaveAsPrefabAsset(obj,path,out bool ok);Require(ok,"프리팹 저장: "+path);Require(guid.Length==0||guid==AssetDatabase.AssetPathToGUID(path),"프리팹 GUID 변화");Record("prefab_save",path);}
        static void SaveScene(Scene scene,string path){Backup(path);Require(EditorSceneManager.SaveScene(scene,path),"씬 저장");Record("scene_save",path);}
        static RectTransform RectAt(string name,Transform parent,Vector2 position,Vector2 size)
        {var g=new GameObject(name,typeof(RectTransform));var r=(RectTransform)g.transform;if(parent)r.SetParent(parent,false);r.anchorMin=r.anchorMax=r.pivot=Vector2.one*.5f;r.anchoredPosition=position;r.sizeDelta=size;return r;}
        static Image Box(string name,Transform parent,Vector2 position,Vector2 size,Color color){var r=RectAt(name,parent,position,size);var im=r.gameObject.AddComponent<Image>();im.color=color;im.raycastTarget=false;return im;}
        static Text Label(string name,Transform parent,Vector2 position,Vector2 size,string text,int fontSize=20){var r=RectAt(name,parent,position,size);var t=r.gameObject.AddComponent<Text>();t.font=AssetDatabase.LoadAssetAtPath<Font>("Assets/Imports/Galmuri7.ttf");t.fontSize=fontSize;t.text=text;t.alignment=TextAnchor.MiddleCenter;t.color=Color.white;t.raycastTarget=false;return t;}
        static Button ButtonAt(string name,Transform parent,Vector2 position,Vector2 size,string text){var image=Box(name,parent,position,size,new Color(.09f,.25f,.28f));image.raycastTarget=true;var b=image.gameObject.AddComponent<Button>();b.targetGraphic=image;b.navigation=new Navigation{mode=Navigation.Mode.None};Label("Label",b.transform,Vector2.zero,size-new Vector2(8,4),text,18);return b;}

        static string StagePath(int index)=>CR+"Prefabs/PCS_CircuitStage"+(index+1).ToString("00")+".prefab";
        static void LayoutMap(int index,out Vector2[] route,out Rect[] walls,out Rect goal)
        {
            if(index==0){route=new[]{new Vector2(4f,10f),new Vector2(12f,10f),new Vector2(12f,17f),new Vector2(33f,17f),new Vector2(33f,5f),new Vector2(58f,5f),new Vector2(58f,17f),new Vector2(82f,17f),new Vector2(82f,31f),new Vector2(58f,31f),new Vector2(58f,37f),new Vector2(26f,37f),new Vector2(26f,55f),new Vector2(42f,55f),new Vector2(42f,47f),new Vector2(68f,47f),new Vector2(68f,55f),new Vector2(80f,55f),new Vector2(80f,47f),new Vector2(96f,47f)};walls=new[]{new Rect(0f,0f,100f,2.5f),new Rect(0f,2.5f,30.5f,5f),new Rect(60.5f,2.5f,39.5f,12f),new Rect(0f,7.5f,1.5f,5f),new Rect(14.5f,7.5f,16f,7f),new Rect(35.5f,7.5f,20f,12f),new Rect(0f,12.5f,9.5f,7f),new Rect(84.5f,14.5f,15.5f,19f),new Rect(0f,19.5f,79.5f,9f),new Rect(0f,28.5f,55.5f,6f),new Rect(60.5f,33.5f,39.5f,6f),new Rect(0f,34.5f,23.5f,23f),new Rect(28.5f,39.5f,71.5f,5f),new Rect(28.5f,44.5f,11f,8f),new Rect(70.5f,44.5f,7f,8f),new Rect(99f,44.5f,1f,5f),new Rect(44.5f,49.5f,21f,8f),new Rect(82.5f,49.5f,17.5f,8f),new Rect(0f,57.5f,100f,2.5f)};goal=new Rect(93f,44.5f,6f,5f);return;}
            if(index==1){route=new[]{new Vector2(4f,48f),new Vector2(12f,48f),new Vector2(12f,41f),new Vector2(4f,41f),new Vector2(4f,25f),new Vector2(13f,25f),new Vector2(13f,12f),new Vector2(26f,12f),new Vector2(26f,5f),new Vector2(38f,5f),new Vector2(38f,13f),new Vector2(49f,13f),new Vector2(49f,19f),new Vector2(63f,19f),new Vector2(63f,13f),new Vector2(74f,13f),new Vector2(74f,35f),new Vector2(60f,35f),new Vector2(60f,43f),new Vector2(68f,43f),new Vector2(68f,55f),new Vector2(82f,55f),new Vector2(82f,46f),new Vector2(90f,46f),new Vector2(90f,38f),new Vector2(96f,38f),new Vector2(96f,24f),new Vector2(90f,24f),new Vector2(90f,5f)};walls=new[]{new Rect(0f,0f,100f,1f),new Rect(0f,1f,87.5f,1.5f),new Rect(92.5f,1f,7.5f,20.5f),new Rect(0f,2.5f,23.5f,7f),new Rect(40.5f,2.5f,47f,8f),new Rect(28.5f,7.5f,7f,7f),new Rect(0f,9.5f,10.5f,13f),new Rect(51.5f,10.5f,9f,6f),new Rect(76.5f,10.5f,11f,16f),new Rect(15.5f,14.5f,20f,1f),new Rect(15.5f,15.5f,31f,6f),new Rect(65.5f,15.5f,6f,6f),new Rect(15.5f,21.5f,56f,6f),new Rect(98.5f,21.5f,1.5f,19f),new Rect(0f,22.5f,1.5f,21f),new Rect(76.5f,26.5f,17f,9f),new Rect(6.5f,27.5f,65f,5f),new Rect(6.5f,32.5f,51f,6f),new Rect(76.5f,35.5f,11f,2f),new Rect(62.5f,37.5f,25f,3f),new Rect(14.5f,38.5f,43f,7f),new Rect(70.5f,40.5f,17f,3f),new Rect(92.5f,40.5f,7.5f,8f),new Rect(0f,43.5f,9.5f,2f),new Rect(70.5f,43.5f,9f,9f),new Rect(0f,45.5f,1.5f,5f),new Rect(14.5f,45.5f,51f,5f),new Rect(84.5f,48.5f,15.5f,9f),new Rect(0f,50.5f,65.5f,7f),new Rect(0f,57.5f,100f,2.5f)};goal=new Rect(87.5f,1f,5f,7f);return;}
            if(index==2){route=new[]{new Vector2(93f,56f),new Vector2(93f,49f),new Vector2(87f,49f),new Vector2(87f,37f),new Vector2(96f,37f),new Vector2(96f,25f),new Vector2(87f,25f),new Vector2(87f,17f),new Vector2(72f,17f),new Vector2(72f,40f),new Vector2(78f,40f),new Vector2(78f,55f),new Vector2(66f,55f),new Vector2(66f,46f),new Vector2(50f,46f),new Vector2(50f,53f),new Vector2(36f,53f),new Vector2(36f,31f),new Vector2(43f,31f),new Vector2(43f,19f),new Vector2(34f,19f),new Vector2(34f,11f),new Vector2(52f,11f),new Vector2(52f,5f),new Vector2(19f,5f),new Vector2(19f,22f),new Vector2(11f,22f),new Vector2(11f,36f),new Vector2(17f,36f),new Vector2(17f,52f),new Vector2(4f,52f)};walls=new[]{new Rect(0f,0f,100f,2.5f),new Rect(0f,2.5f,16.5f,17f),new Rect(54.5f,2.5f,45.5f,11f),new Rect(21.5f,7.5f,28f,1f),new Rect(21.5f,8.5f,10f,13f),new Rect(36.5f,13.5f,63.5f,1f),new Rect(36.5f,14.5f,33f,2f),new Rect(89.5f,14.5f,10.5f,8f),new Rect(45.5f,16.5f,24f,17f),new Rect(0f,19.5f,8.5f,19f),new Rect(74.5f,19.5f,10f,8f),new Rect(21.5f,21.5f,19f,3f),new Rect(98.5f,22.5f,1.5f,17f),new Rect(13.5f,24.5f,27f,4f),new Rect(74.5f,27.5f,19f,7f),new Rect(13.5f,28.5f,20f,5f),new Rect(19.5f,33.5f,14f,21f),new Rect(38.5f,33.5f,31f,9f),new Rect(74.5f,34.5f,10f,3f),new Rect(80.5f,37.5f,4f,14f),new Rect(0f,38.5f,14.5f,11f),new Rect(89.5f,39.5f,10.5f,7f),new Rect(38.5f,42.5f,37f,1f),new Rect(38.5f,43.5f,9f,7f),new Rect(68.5f,43.5f,7f,9f),new Rect(95.5f,46.5f,4.5f,12f),new Rect(52.5f,48.5f,11f,7f),new Rect(0f,49.5f,1f,5f),new Rect(80.5f,51.5f,10f,6f),new Rect(0f,54.5f,33.5f,1f),new Rect(0f,55.5f,63.5f,2f),new Rect(0f,57.5f,90.5f,1f),new Rect(0f,58.5f,100f,1.5f)};goal=new Rect(1f,49.5f,6f,5f);return;}
            throw new ArgumentOutOfRangeException(nameof(index));
        }
        static void Place(RectTransform rt,Rect r){rt.anchorMin=rt.anchorMax=rt.pivot=Vector2.one*.5f;rt.anchoredPosition=(r.center-new Vector2(50,30))*8;rt.sizeDelta=r.size*8;}
        [MenuItem("Tools/PCS/설계 확장/P4 편집 배치 최초 연결")]
        public static void BuildCircuitLayouts()
        {
            Guard();string wallPath=CR+"Prefabs/PCS_CircuitWall.prefab";
            if(!File.Exists(wallPath))
            {var wall=Box("PCS_CircuitWall",null,Vector2.zero,new Vector2(40,40),new Color(.18f,.29f,.32f));wall.gameObject.AddComponent<CircuitWall>();SavePrefab(wall.gameObject,wallPath);Object.DestroyImmediate(wall.gameObject);}
            for(int i=0;i<3;i++)if(!File.Exists(StagePath(i)))
            {
                LayoutMap(i,out var route,out var walls,out var goal);
                var root=RectAt("Stage"+(i+1).ToString("00"),null,Vector2.zero,new Vector2(800,480));var layout=root.gameObject.AddComponent<CircuitStageLayout>();layout.stageId="circuit-"+(i+1).ToString("00");layout.displayNumber=i+1;
                layout.board=Box("Board",root,Vector2.zero,new Vector2(800,480),new Color(.015f,.10f,.10f)).rectTransform;
                layout.wallsRoot=RectAt("Walls",layout.board,Vector2.zero,new Vector2(800,480));
                for(int j=0;j<walls.Length;j++){var w=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(wallPath));w.name="Wall"+j.ToString("00");w.transform.SetParent(layout.wallsRoot,false);Place((RectTransform)w.transform,walls[j]);PrefabUtility.RecordPrefabInstancePropertyModifications(w.transform);}
                layout.decorationRoot=RectAt("Decoration",layout.board,Vector2.zero,new Vector2(800,480));
                foreach(Rect w in walls.Where(w=>w.width>12&&w.height>6)){var d=Box("PCB_Block",layout.decorationRoot,Vector2.zero,Vector2.one,new Color(.12f,.21f,.23f));Place(d.rectTransform,new Rect(w.x+2,w.y+2,w.width-4,w.height-4));}
                layout.start=Box("Start",layout.board,Vector2.zero,Vector2.one,new Color(.1f,.65f,.65f)).rectTransform;Place(layout.start,new Rect(route[0]-Vector2.one*.7f,Vector2.one*1.4f));
                layout.goal=Box("Goal",layout.board,Vector2.zero,Vector2.one,new Color(.3f,.8f,.4f)).rectTransform;Place(layout.goal,goal);
                layout.initialDirection=i==2?Vector2Int.down:Vector2Int.right;layout.referenceRoute=route;
                Require(layout.TryCapture(out var captured,out string error),error);Require(captured.routeError==null,captured.routeError);
                SavePrefab(root.gameObject,StagePath(i));Object.DestroyImmediate(root.gameObject);
            }
            string rigPath=CR+"Prefabs/PCS_CircuitDemoRig.prefab";var rig=PrefabUtility.LoadPrefabContents(rigPath);
            try
            {
                var manager=rig.GetComponent<CircuitHackManager>();
                if(manager.layouts==null||manager.layouts.Length==0){AddLayouts(rig,new[]{0,1,2});var host=rig.GetComponent<CircuitDemoHost>();var launcher=host.launch.transform.parent;
                    host.launch.GetComponentInChildren<Text>().text="선택한 3단계 순차 실행";
                    host.singleLaunches=new Button[3];for(int i=0;i<3;i++)host.singleLaunches[i]=ButtonAt("Single"+(i+1),launcher,new Vector2((i-1)*300,-145),new Vector2(270,50),(i+1)+"단계만 실행");
                    host.reloadLayout=ButtonAt("ReloadLayout",manager.view.transform,new Vector2(135,-330),new Vector2(250,42),"편집 배치 다시 읽기");ArrangeCircuitButtons(host);
                    SavePrefab(rig,rigPath);
                }
            }finally{PrefabUtility.UnloadPrefabContents(rig);}
            string single=CR+"Scenes/PCS_CircuitStage02Demo.unity";
            if(!File.Exists(single))
            {
                var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
                var root=new GameObject("PCS_CircuitStage02Rig");var m=root.AddComponent<CircuitHackManager>();m.input=root.AddComponent<CircuitInputAdapter>();m.failureAudio=root.AddComponent<AudioSource>();m.failureAudio.playOnAwake=false;
                m.overlay=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(CR+"Prefabs/PCS_CircuitOverlay.prefab"),scene);m.overlay.transform.SetParent(root.transform,false);m.view=m.overlay.GetComponent<CircuitHackUI>();
                var canvas=CanvasRoot("PCS_CircuitLauncher",root.transform);Box("Shade",canvas.transform,Vector2.zero,new Vector2(1280,720),new Color(.015f,.045f,.055f,1));Label("Title",canvas.transform,new Vector2(0,130),new Vector2(1100,80),"회로 차단 2단계 단독 호출 실습",28);
                var host=root.AddComponent<CircuitDemoHost>();host.manager=m;host.defaultSingle=true;host.launch=ButtonAt("Launch",canvas.transform,Vector2.zero,new Vector2(500,60),"2단계만 시작");host.result=Label("Result",canvas.transform,new Vector2(0,-170),new Vector2(1150,70),"회로 결과: 대기",20);
                var events=new GameObject("PCS_EventSystem");events.SetActive(false);events.transform.SetParent(root.transform,false);host.eventSystem=events.AddComponent<EventSystem>();host.eventSystem.sendNavigationEvents=false;host.uiModule=events.AddComponent<InputSystemUIInputModule>();host.uiModule.enabled=false;events.SetActive(true);
                AddLayouts(root,new[]{1});m.overlay.SetActive(false);CreateCamera("PCS_CircuitCamera");SaveScene(scene,single);
            }
            ValidateCircuitAssets();CircuitDesignerChecks();Evidence("P4_DESIGNER_SAVE","벽/단계 프리팹 4개, 기존 Rig 보존 연결, 2단계 단독 씬 저장·재열기 검사 통과. 실제 입력·화면 검사는 별도.");
        }
        static Canvas CanvasRoot(string name,Transform parent){var r=RectAt(name,parent,Vector2.zero,new Vector2(1280,720));var c=r.gameObject.AddComponent<Canvas>();c.renderMode=RenderMode.ScreenSpaceOverlay;var scaler=r.gameObject.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1280,720);scaler.matchWidthOrHeight=.5f;r.gameObject.AddComponent<GraphicRaycaster>();return c;}
        static void CreateCamera(string name){var camera=new GameObject(name,typeof(Camera),typeof(AudioListener),typeof(UniversalAdditionalCameraData));camera.transform.position=new Vector3(0,0,-10);camera.GetComponent<Camera>().clearFlags=CameraClearFlags.SolidColor;camera.GetComponent<Camera>().backgroundColor=new Color(.01f,.025f,.035f);}
        static void AddLayouts(GameObject rig,int[] indices)
        {
            var canvas=CanvasRoot("PCS_EditableLayouts",rig.transform);canvas.sortingOrder=-10;var m=rig.GetComponent<CircuitHackManager>();m.layouts=new CircuitStageLayout[indices.Length];
            for(int i=0;i<indices.Length;i++){var instance=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(StagePath(indices[i])),rig.scene);instance.transform.SetParent(canvas.transform,false);m.layouts[i]=instance.GetComponent<CircuitStageLayout>();instance.SetActive(i==0);PrefabUtility.RecordPrefabInstancePropertyModifications(instance);}
            // 이전 SO는 자산으로 보존하며 새 Rig 실행에서는 혼합하거나 자동 대체하지 않는다.
            m.stages=Array.Empty<CircuitMazeData>();
        }
        [MenuItem("Tools/PCS/설계 확장/선택한 P4 배치 읽기 검사")]
        public static void ValidateSelectedCircuit()
        {
            var layout=Selection.activeGameObject?Selection.activeGameObject.GetComponentInParent<CircuitStageLayout>(true):null;
            if(!layout){Debug.LogError("Stage 루트 또는 벽을 선택하세요.");return;}
            if(!layout.TryCapture(out var data,out string error)){Debug.LogError(error,layout);return;}
            Debug.Log(data.routeError==null?"배치 유효 / 저장된 검수 경로 통과":"배치 유효 / 경로 미검증: "+data.routeError+" — 직접 테스트할 수 있습니다.",layout);
        }
        public static void ValidateCircuitAssets(bool requireDefaultRoutes=false)
        {
            foreach(int i in new[]{0,1,2}){var root=PrefabUtility.LoadPrefabContents(StagePath(i));try{Require(root.GetComponent<CircuitStageLayout>().TryCapture(out var d,out string e),e);if(requireDefaultRoutes)Require(d.routeError==null,d.routeError);else if(d.routeError!=null)Debug.LogWarning("배치는 유효하지만 경로 미검증: "+d.routeError,root);}finally{PrefabUtility.UnloadPrefabContents(root);}}
            foreach(string path in new[]{CR+"Scenes/PCS_CircuitHackDemo.unity",CR+"Scenes/PCS_CircuitStage02Demo.unity"})
            {
                SafeSceneOpen(path);var scene=SceneManager.GetActiveScene();Require(!scene.isDirty&&SceneManager.sceneCount==1,"단독 저장 씬 재열기");var host=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<CircuitDemoHost>(true)).Single();Require(host.manager.ValidateSetup()==null,host.manager.ValidateSetup());Require(host.manager.view.ReferencesValid,"저장 UI 참조");Require(host.manager.stages.Length==0,"이전 SO 혼합");if(path.Contains("Stage02"))Require(host.manager.layouts.Length==1&&host.manager.layouts[0].displayNumber==2,"2단계만 참조");
                Require(scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<MonoBehaviour>(true)).All(c=>c),"누락 스크립트");
            }
        }
        static void WalkCircuit(CircuitHackManager manager)
        {
            int run=manager.RunId;var route=manager.CurrentData.referenceRoute;
            for(int i=1;i<route.Length&&manager.RunId==run&&manager.State==CircuitHackManager.HackState.Running;i++){Vector2 delta=route[i]-manager.Signal.Position;Vector2Int direction=Mathf.Abs(delta.x)>.01f?new Vector2Int((int)Mathf.Sign(delta.x),0):new Vector2Int(0,(int)Mathf.Sign(delta.y));manager.Advance(delta.magnitude/manager.Signal.Speed,direction);}
            Require(manager.RunId!=run||manager.State==CircuitHackManager.HackState.Completed||manager.State==CircuitHackManager.HackState.StageFeedback,"연속 구간 실제 경로 완주");
        }
        public static void CircuitDesignerChecks()
        {
            var roots=new List<GameObject>();var managerRoot=new GameObject("PCS_ExplicitLogicCheck");var manager=managerRoot.AddComponent<CircuitHackManager>();int complete=0,cancel=0;manager.OnHackCompleted+=_=>complete++;manager.OnHackCancelled+=_=>cancel++;
            try
            {
                for(int i=0;i<3;i++)roots.Add(PrefabUtility.LoadPrefabContents(StagePath(i)));var layouts=roots.Select(r=>r.GetComponent<CircuitStageLayout>()).ToArray();
                foreach(var l in layouts){Require(manager.StartSingleStage(l),"단독 시작");WalkCircuit(manager);int count=complete;manager.Advance(100,Vector2Int.right);Require(complete==count,"완료 중복 방지");manager.CloseHack();}
                Require(complete==3&&cancel==0,"단독 완료/완료 뒤 닫기");Require(manager.StartSequence(layouts),"순차 시작");for(int i=0;i<3;i++){WalkCircuit(manager);if(i<2){Require(complete==3,"중간 완료 전달 금지");manager.Advance(.6f,Vector2Int.zero);}}Require(complete==4,"순차 최종 1회");manager.CloseHack();
                var first=layouts[0];var wall=Box("ValidationWall",first.wallsRoot,Vector2.zero,Vector2.one,Color.gray);wall.gameObject.AddComponent<CircuitWall>();Place(wall.rectTransform,new Rect(7.5f,7.5f,1,5));Require(first.TryCapture(out var data,out string error),error);Require(data.routeError!=null,"새 벽이면 경로 미검증");Require(CircuitCollisionChecker.Check(data,data.start,new Vector2(12,10)).kind==CircuitCollisionChecker.Kind.Wall,"편집 벽 실제 충돌");
                Require(manager.StartSingleStage(first),"경로 미검증도 시작");manager.Advance(1,Vector2Int.right);Require(manager.State==CircuitHackManager.HackState.FailFeedback,"배치 충돌 실패");wall.gameObject.SetActive(false);manager.Advance(.75f,Vector2Int.zero);manager.Advance(1,Vector2Int.right);Require(manager.State==CircuitHackManager.HackState.FailFeedback,"재시도는 기존 스냅샷 유지");manager.CloseHack();Require(first.TryCapture(out data,out error),error);Require(data.routeError==null,"벽 비활성 통과");
                wall.gameObject.SetActive(true);wall.transform.localRotation=Quaternion.Euler(0,0,20);Require(!first.TryCapture(out _,out error)&&error.Contains("ValidationWall"),"벽 회전 거절");wall.transform.localRotation=Quaternion.identity;wall.gameObject.SetActive(false);first.transform.localScale=new Vector3(-1,1,1);Require(!first.TryCapture(out _,out _),"루트 음수 배율 거절");first.transform.localScale=Vector3.one;
                var originalBoard=first.board;first.board=layouts[1].board;Require(!first.TryCapture(out _,out _),"다른 단계 Board 참조 거절");first.board=originalBoard;
                Object.DestroyImmediate(wall.gameObject);Require(manager.StartSingleStage(layouts[1]),"2단계 재시작");int oldRun=manager.RunId;Action<int> reenter=_=>manager.StartSingleStage(layouts[0]);manager.OnHackCompleted+=reenter;WalkCircuit(manager);manager.OnHackCompleted-=reenter;Require(manager.RunId==oldRun+1&&manager.State==CircuitHackManager.HackState.Running,"완료 후 새 세션 재진입");manager.CloseHack();
                Evidence("P4_DESIGNER_LOGIC","3개 실제 연속 경로, 단독/순차 완료 단발, 편집 벽 충돌·비활성 통과, 재시도 스냅샷, 경로 미검증 실행, 회전/음수/외부 Board 거절, 완료 재진입 PASS. Editor 논리 검사이며 물리 입력 아님.");
            }finally{Object.DestroyImmediate(managerRoot);foreach(var root in roots)PrefabUtility.UnloadPrefabContents(root);}
        }


        const string HR="Assets/PCS/Hacking/";
        static string TimingPath(int index)=>HR+"Data/PCS_TimingStage"+(index+1).ToString("00")+".asset";
        static string RotationPath(int index)=>HR+"Data/PCS_RotationStage"+(index+1).ToString("00")+".asset";
        static Vector2Int[] HexCells(int count)
        {
            var list=new List<Vector2Int>{new Vector2Int(0,0),new Vector2Int(1,0),new Vector2Int(0,1),new Vector2Int(-1,1),new Vector2Int(-1,0),new Vector2Int(0,-1),new Vector2Int(1,-1)};
            if(count==13)list.AddRange(new[]{new Vector2Int(2,0),new Vector2Int(1,1),new Vector2Int(0,2),new Vector2Int(-1,2),new Vector2Int(-2,1),new Vector2Int(2,-1)});
            if(count==19)list.AddRange(new[]{new Vector2Int(2,0),new Vector2Int(1,1),new Vector2Int(0,2),new Vector2Int(-1,2),new Vector2Int(-2,2),new Vector2Int(-2,1),new Vector2Int(-2,0),new Vector2Int(-1,-1),new Vector2Int(0,-2),new Vector2Int(1,-2),new Vector2Int(2,-2),new Vector2Int(2,-1)});return list.ToArray();
        }
        [MenuItem("Tools/PCS/설계 확장/P1 P2 단계 최초 연결")]
        public static void BuildHackingStages()
        {
            Guard();if(!AssetDatabase.IsValidFolder(HR+"Data")){CheckPath(HR+"Data.meta");AssetDatabase.CreateFolder(HR.TrimEnd('/'),"Data");Record("hacking_data_folder",HR+"Data.meta");}
            for(int i=0;i<3;i++)
            {
                if(!File.Exists(TimingPath(i))){CheckPath(TimingPath(i));var d=ScriptableObject.CreateInstance<PCS_TimingStageData>();d.stageId="timing-"+(i+1).ToString("00");d.displayNumber=i+1;d.targetCount=new[]{6,8,10}[i];d.degreesPerSecond=new[]{90f,120f,150f}[i];d.toleranceDegrees=new[]{12f,9f,6f}[i];d.missLimit=new[]{6,4,3}[i];AssetDatabase.CreateAsset(d,TimingPath(i));Record("timing_stage",TimingPath(i));}
                if(!File.Exists(RotationPath(i)))
                {
                    CheckPath(RotationPath(i));var d=ScriptableObject.CreateInstance<PCS_RotationStageData>();d.stageId="rotation-"+(i+1).ToString("00");d.displayNumber=i+1;d.cells=HexCells(new[]{7,13,19}[i]);d.rootIndex=0;d.fixedTiles=i==0?new[]{0}:i==1?new[]{0,9}:new[]{0,13};d.minimumClicks=new[]{1,4,5}[i];
                    if(i==0)d.graphs=PCS_RotationPuzzle.Legacy().graphs;
                    if(i==1)d.graphs=new[]{new PCS_RotationStageData.Graph{variantId="loop-a",solvedMasks=new[]{56,53,37,18,7,2,7,8,8,8,49,32,12},fallbackTurns=new[]{0,4,5,4,5,4,5,4,5,0,5,4,5}},new PCS_RotationStageData.Graph{variantId="loop-b",solvedMasks=new[]{41,15,40,11,37,4,5,24,20,32,16,33,10},fallbackTurns=new[]{0,4,5,4,5,4,5,4,5,0,5,4,5}}};
                    if(i==2)d.graphs=new[]{new PCS_RotationStageData.Graph{variantId="mesh-a",solvedMasks=new[]{7,56,23,44,28,56,34,20,44,56,33,32,33,33,7,2,4,4,6},fallbackTurns=new[]{0,4,5,4,5,4,5,4,5,4,5,4,5,0,5,4,5,4,5}},new PCS_RotationStageData.Graph{variantId="mesh-b",solvedMasks=new[]{54,19,23,44,40,22,22,24,24,16,32,32,17,35,36,6,3,10,18},fallbackTurns=new[]{0,4,5,4,5,4,5,4,5,4,5,4,5,0,5,4,5,4,5}}};
                    Require(d.Capture().Validate()==null,d.Capture().Validate());AssetDatabase.CreateAsset(d,RotationPath(i));Record("rotation_stage",RotationPath(i));
                }
            }
            string panelPath=HR+"Prefabs/PCS_HackingPanel.prefab";var panel=PrefabUtility.LoadPrefabContents(panelPath);
            try
            {
                var view=panel.GetComponentInChildren<PCS_HackingView>(true);bool changed=false;
                if(view.rotationTiles.Length<19){var list=view.rotationTiles.ToList();while(list.Count<19){var tile=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(HR+"Prefabs/PCS_HexTile.prefab"),panel.scene);tile.name="Tile"+list.Count;tile.transform.SetParent(view.rotationRoot.transform,false);var t=tile.GetComponent<PCS_HexTileView>();t.tileIndex=list.Count;t.gameObject.SetActive(false);list.Add(t);}view.rotationTiles=list.ToArray();changed=true;}
                if(!panel.transform.Find("NextStage")){ButtonAt("NextStage",panel.transform,new Vector2(125,-165),new Vector2(215,42),"다음 단계");ButtonAt("NewConfiguration",panel.transform,new Vector2(355,-165),new Vector2(215,42),"새 섞기 / 새 구성");changed=true;}
                changed|=EnsureTileLabels(view);if(changed)SavePrefab(panel,panelPath);
            }finally{PrefabUtility.UnloadPrefabContents(panel);}
            string rigPath=HR+"Prefabs/PCS_HackingDemoRig.prefab";var rig=PrefabUtility.LoadPrefabContents(rigPath);
            try
            {
                var c=rig.GetComponent<PCS_HackingDemoController>();if(!c.session)
                {
                    AttachSession(c);c.timingStages=Enumerable.Range(0,3).Select(i=>AssetDatabase.LoadAssetAtPath<PCS_TimingStageData>(TimingPath(i))).ToArray();c.rotationStages=Enumerable.Range(0,3).Select(i=>AssetDatabase.LoadAssetAtPath<PCS_RotationStageData>(RotationPath(i))).ToArray();
                    var parent=c.beginTiming.transform.parent;c.beginTiming.GetComponentInChildren<Text>().text="타이밍 3단계 순차";c.beginRotation.GetComponentInChildren<Text>().text="회전 연결 3단계 순차";
                    c.timingSingleButtons=new Button[3];c.rotationSingleButtons=new Button[3];for(int i=0;i<3;i++){c.timingSingleButtons[i]=ButtonAt("TimingSingle"+(i+1),parent,new Vector2(-450+i*160,175),new Vector2(150,42),"타이밍 "+(i+1)+"단계");c.rotationSingleButtons[i]=ButtonAt("RotationSingle"+(i+1),parent,new Vector2(130+i*160,175),new Vector2(150,42),"회전 "+(i+1)+"단계");}
                    var note=parent.Find("DirectStartNote");if(note){note.GetComponent<Text>().text="단계 하나만 또는 순차 실행을 선택하세요. 패널을 닫은 뒤 다른 퍼즐을 시작합니다.";((RectTransform)note).anchoredPosition=new Vector2(0,-160);}
                    c.panel.transform.SetAsLastSibling();SavePrefab(rig,rigPath);
                }
            }finally{PrefabUtility.UnloadPrefabContents(rig);}
            string sessionPath=HR+"Prefabs/PCS_HackingSession.prefab";
            if(!File.Exists(sessionPath))
            {
                var root=new GameObject("PCS_HackingSession");var core=root.AddComponent<PCS_HackingSession>();var presenter=root.AddComponent<PCS_HackingSessionPresenter>();presenter.session=core;presenter.useDemoAlarm=false;
                var canvas=CanvasRoot("PCS_SessionUI",root.transform);var ui=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(panelPath));ui.transform.SetParent(canvas.transform,false);BindPresenter(presenter,ui);ui.SetActive(false);SavePrefab(root,sessionPath);Object.DestroyImmediate(root);
            }
            string single=HR+"Scenes/PCS_HackingStage02Demo.unity";
            if(!File.Exists(single))
            {
                var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);var root=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(sessionPath),scene);var c=root.AddComponent<PCS_HackingDemoController>();c.session=root.GetComponent<PCS_HackingSession>();c.presenter=root.GetComponent<PCS_HackingSessionPresenter>();c.panel=c.presenter.panel;c.view=c.presenter.view;c.restart=c.presenter.restart;c.close=c.presenter.close;c.cancel=c.presenter.cancel;
                c.timingStages=new[]{AssetDatabase.LoadAssetAtPath<PCS_TimingStageData>(TimingPath(1))};c.rotationStages=new[]{AssetDatabase.LoadAssetAtPath<PCS_RotationStageData>(RotationPath(1))};
                var canvas=root.transform.Find("PCS_SessionUI");Label("Title",canvas,new Vector2(0,315),new Vector2(1100,55),"P1 / P2 · 2단계 단독 호출 실습",28);
                c.beginTiming=ButtonAt("BeginTiming",canvas,new Vector2(-270,240),new Vector2(450,50),"타이밍 2단계만 시작");c.beginRotation=ButtonAt("BeginRotation",canvas,new Vector2(270,240),new Vector2(450,50),"회전 연결 2단계만 시작");c.result=Label("TimingResult",canvas,new Vector2(0,-315),new Vector2(1150,42),"타이밍 결과: 대기",19);c.rotationResult=Label("RotationResult",canvas,new Vector2(0,-315),new Vector2(1150,42),"회전 연결 결과: 대기",19);c.panel.transform.SetAsLastSibling();
                CreateCamera("PCS_Camera");var events=new GameObject("PCS_EventSystem",typeof(EventSystem),typeof(StandaloneInputModule));events.GetComponent<EventSystem>().sendNavigationEvents=false;SaveScene(scene,single);
            }
            ValidateHackingAssets();HackingDesignerChecks();Evidence("P1_P2_DESIGNER_SAVE","6개 독립 단계 데이터, 19타일 보존 확장, 최소 Session 프리팹, 2단계 단독 씬 저장·재열기 PASS.");
        }
        static void AttachSession(PCS_HackingDemoController c){c.session=c.gameObject.AddComponent<PCS_HackingSession>();c.presenter=c.gameObject.AddComponent<PCS_HackingSessionPresenter>();c.presenter.session=c.session;BindPresenter(c.presenter,c.panel);c.presenter.firstAlarmDelay=c.firstAlarmDelay;c.presenter.alarmInterval=c.alarmInterval;}
        static void BindPresenter(PCS_HackingSessionPresenter presenter,GameObject panel){presenter.panel=panel;presenter.view=panel.GetComponentInChildren<PCS_HackingView>(true);presenter.restart=panel.transform.Find("Restart").GetComponent<Button>();presenter.close=panel.transform.Find("Close").GetComponent<Button>();presenter.cancel=panel.transform.Find("Cancel").GetComponent<Button>();presenter.next=panel.transform.Find("NextStage").GetComponent<Button>();presenter.newConfiguration=panel.transform.Find("NewConfiguration").GetComponent<Button>();}
        public static void ValidateHackingAssets()
        {
            foreach(string path in new[]{HR+"Scenes/PCS_HackingDemo.unity",HR+"Scenes/PCS_HackingStage02Demo.unity"})
            {
                SafeSceneOpen(path);var scene=SceneManager.GetActiveScene();Require(!scene.isDirty,"저장 씬 재열기");var c=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<PCS_HackingDemoController>(true)).Single();Require(c.session&&c.presenter&&c.view&&c.panel&&c.presenter.next&&c.presenter.newConfiguration,"세션 표시 연결");Require(c.view.rotationTiles.Length==19&&c.view.rotationTiles.Distinct().Count()==19,"19개 표시 풀");Require(c.view.rotationTiles.All(t=>t.GetComponentInChildren<Text>(true)),"모든 타일 번호/고정 표시 연결");Require(scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<MonoBehaviour>(true)).All(m=>m),"누락 스크립트");if(path.Contains("Stage02"))Require(c.timingStages.Length==1&&c.rotationStages.Length==1&&c.timingStages[0].displayNumber==2&&c.rotationStages[0].displayNumber==2,"P1/P2 2단계만 참조");
                foreach(var text in c.GetComponentsInChildren<Text>(true))Require(AssetDatabase.GetAssetPath(text.font)=="Assets/Imports/Galmuri7.ttf","한글 폰트 참조");
            }
        }
        static void SolveTiming(PCS_HackingSession session){int count=session.timing.ActiveTargetCount;long attempt=session.AttemptId;for(int i=0;i<count;i++){float delta=Mathf.Repeat(session.timing.TargetAngle(i)-session.timing.Angle,360);session.Advance(delta/session.timing.ActiveDegreesPerSecond);session.Press(attempt);}}
        static void SolveRotation(PCS_HackingSession session){long attempt=session.AttemptId;for(int i=0;i<session.rotation.TileCount;i++){int turns=(6-session.rotation.InitialRotation(i))%6;for(int j=0;j<turns;j++)session.Rotate(i,attempt);}}
        public static void HackingDesignerChecks()
        {
            var timing=new PCS_TimingPuzzle();foreach(int count in new[]{2,6,8,10,24}){Require(timing.TryConfigure(count,90,Mathf.Min(6,90f/count),2,out _),"유효 설정");Require(timing.Begin(),"설정 시작");for(int i=0;i<count;i++){timing.Advance(Mathf.Repeat(timing.TargetAngle(i)-timing.Angle,360)/timing.ActiveDegreesPerSecond);timing.Connect();}Require(timing.Succeeded,"동적 핀 완주");}
            Require(!timing.TryConfigure(24,90,7.5f,2,out _)&&!timing.TryConfigure(6,float.NaN,12,2,out _)&&!timing.TryConfigure(6,90,12,-1,out _),"잘못된 원자 설정 거절");
            timing.TryConfigure(6,90,12,2,out _);timing.Begin();timing.Connect();Require(timing.Connect()==PCS_TimingPuzzle.Hit.AlreadyConnected&&timing.MissCount==0,"연결 중복 무벌점");timing.Advance(30/90f);Require(timing.Connect()==PCS_TimingPuzzle.Hit.Miss&&timing.Connect()==PCS_TimingPuzzle.Hit.Failed&&!timing.Running,"N번째 실제 오입력 실패");float stop=timing.Angle;timing.Advance(5);Require(timing.Angle==stop&&timing.Connect()==PCS_TimingPuzzle.Hit.Ignored,"실패 후 정지");
            timing.Begin();timing.TryConfigure(10,150,6,3,out _);Require(timing.ActiveTargetCount==6&&timing.ActiveDegreesPerSecond==90,"다음 시도 적용");timing.Begin();Require(timing.ActiveTargetCount==10&&timing.ActiveDegreesPerSecond==150,"재시작 적용");
            Require(PCS_RotationPuzzle.Evaluate(new[]{21,24,8,33,32,6,2}).Solved,"P2 기존 대안 해답");int draws=0;var rotation=new PCS_RotationPuzzle();rotation.Begin(1,()=>{draws++;return 0;});Require(draws==96&&rotation.UsedFallback&&rotation.Candidates==16,"P2 기존 유한 섞기");
            var report=new System.Text.StringBuilder();
            for(int stage=0;stage<3;stage++){var data=AssetDatabase.LoadAssetAtPath<PCS_RotationStageData>(RotationPath(stage)).Capture();for(int graph=0;graph<data.graphs.Length;graph++){Require(rotation.Begin(data,20261015,graph,()=>0)&&rotation.UsedFallback,rotation.Error??"단계별 fallback 검사");Require(rotation.Begin(data,20261015,graph),rotation.Error);var initial=Enumerable.Range(0,rotation.TileCount).Select(rotation.MaskAt).ToArray();Require(PCS_RotationPuzzle.ShortSolution(initial,data,data.minimumClicks-1,50000,out int expanded)==PCS_RotationPuzzle.SearchResult.Absent,"짧은 클릭 해답 없음");for(int i=0;i<rotation.TileCount;i++)if(!rotation.IsFixed(i)){int before=rotation.MaskAt(i);for(int j=0;j<6;j++)rotation.Rotate(i);Require(rotation.MaskAt(i)==before,"6회 회전 복귀");}rotation.Restart();for(int i=0;i<rotation.TileCount;i++){int turns=(6-rotation.InitialRotation(i))%6;for(int j=0;j<turns;j++)rotation.Rotate(i);}Require(rotation.Succeeded,"모델 실제 연결 완주");report.AppendLine($"P2 단계{stage+1} 그래프{graph}: {data.cells.Length}타일 / 최소 {data.minimumClicks}클릭 하한 검사 {expanded}상태 / 완주 PASS");}}
            var root=new GameObject("PCS_SessionLogicCheck");var session=root.AddComponent<PCS_HackingSession>();int completed=0,cancelled=0;session.OnHackCompleted+=_=>completed++;session.OnHackCancelled+=_=>cancelled++;
            try
            {
                var p1=AssetDatabase.LoadAssetAtPath<PCS_TimingStageData>(TimingPath(1));var p2=AssetDatabase.LoadAssetAtPath<PCS_RotationStageData>(RotationPath(1));
                Require(session.StartSingleStage(p1),session.Error);long previous=session.AttemptId;SolveTiming(session);Require(session.State==PCS_HackingSession.SessionState.Completed&&completed==1&&!session.NextStage(),"P1 2단계 단독 완료");session.CloseHack();
                Require(session.StartSingleStage(p2),session.Error);Require(session.Press(previous)==PCS_TimingPuzzle.Hit.Ignored,"이전 콜백 무효");SolveRotation(session);Require(completed==2&&session.State==PCS_HackingSession.SessionState.Completed,"P2 2단계 단독 완료");session.CloseHack();Require(cancelled==0,"완료 뒤 닫기 취소 아님");
                var stages=Enumerable.Range(0,3).Select(i=>AssetDatabase.LoadAssetAtPath<PCS_TimingStageData>(TimingPath(i))).ToArray();Require(session.StartSequence(stages),session.Error);for(int i=0;i<3;i++){SolveTiming(session);if(i<2){Require(completed==2&&session.State==PCS_HackingSession.SessionState.StageSucceeded,"중간 완료 미전달");Require(session.NextStage(),"다음 단계");}}Require(completed==3,"순차 최종 단발");session.RestartSession();Require(session.Deliveries==0,"새 세션 재시작");session.CloseHack();session.CloseHack();Require(cancelled==1,"취소 단발");
                Require(session.StartSingleStage(p1),session.Error);long run=session.SessionId;Action<long> again=_=>session.StartSingleStage(p2);session.OnHackCompleted+=again;SolveTiming(session);session.OnHackCompleted-=again;Require(session.SessionId==run+1&&session.IsRotation&&session.State==PCS_HackingSession.SessionState.Running,"완료 콜백 재진입");
                session.CloseHack();Require(session.StartSingleStage(p1),session.Error);int beforeFinal=completed;Action<long,long,string> duringStage=(id,attempt,stage)=>{session.CloseHack();Require(!session.StartSingleStage(p2),"최종 단계 알림 재진입 거부");};session.OnStageCompleted+=duringStage;SolveTiming(session);session.OnStageCompleted-=duringStage;Require(completed==beforeFinal+1,"최종 알림 중 완료 유실 없음");
                Require(session.TryConfigureTiming(6,90,12,0,false,out _)&&session.RestartSession()&&session.timing.ActiveTargetCount==6&&session.timing.ActiveMissLimit==0,"완료 뒤 다음 시도 설정 유지");session.CloseHack();
                Require(rotation.Begin(PCS_RotationPuzzle.Legacy(),77),"모델 revision 시작");long revision=rotation.Revision;rotation.Rotate(1);Require(rotation.Revision>revision,"외부 회전 표시 revision");var isolated=PCS_RotationPuzzle.Legacy();rotation.Begin(isolated,2);isolated.cells[1]=new Vector2Int(8,8);Require(rotation.PositionAt(1)==PCS_RotationPuzzle.Position(1),"시작 데이터 깊은 복사");Require(!rotation.Begin(null,2)&&!rotation.Restart(),"실패 뒤 이전 복원 배치 미사용");
                report.AppendLine("P1 핀2/6/8/10/24, 원자 설정, 스냅샷, N번째 실패, 중복 무벌점; P1/P2 독립2, 순차·다음버튼, 종결 단발, 오래된 시도·재진입 PASS. Editor 논리이며 실제 키/클릭 아님.");Evidence("HACKING_DESIGNER_LOGIC",report.ToString());
            }finally{Object.DestroyImmediate(root);}
        }


        [Serializable] sealed class MazeEvidence{public string difficulty;public PCS_StepPuzzle.Board[] boards;}
        [MenuItem("Tools/PCS/설계 확장/검수/P3 생성 품질 200판 검사")]
        public static void P3DesignerChecks()
        {
            PCS_DemoAuthoring.MazeRuleChecks();var summary=new System.Text.StringBuilder();
            foreach(var difficulty in new[]{PCS_StepPuzzle.Difficulty.Normal,PCS_StepPuzzle.Difficulty.Hard})
            {
                var keys=new HashSet<string>();var geometries=new HashSet<string>();var budgets=new HashSet<string>();var boards=new List<PCS_StepPuzzle.Board>();int fallbacks=0,unknown=0,minSolutions=int.MaxValue,minChoices=int.MaxValue,minDelayed=int.MaxValue,minSensitive=int.MaxValue;double totalMs=0,maxMs=0;
                var csv=new System.Text.StringBuilder("seed,candidate,fallback,solverExpanded,solutions,winningChoices,delayedLosingChoices,budgetSensitiveChoices,rejectedUnknown,elapsedMs\n");
                for(int seed=0;seed<100;seed++)
                {
                    var watch=System.Diagnostics.Stopwatch.StartNew();var board=PCS_StepPuzzle.Generate(difficulty,seed);watch.Stop();double ms=watch.Elapsed.TotalMilliseconds;totalMs+=ms;maxMs=Math.Max(maxMs,ms);
                    Require(PCS_StepPuzzle.VerifyProof(board,out string e),e);Require(PCS_StepPuzzle.VerifyRoute(board,board.alternateProof,out e),e);Require(PCS_StepPuzzle.Quality(board,difficulty),"복수 해법·계획 분기 품질");var analysis=PCS_StepPuzzle.Analyze(board);
                    Require(analysis.complete,"선택 보드는 평가 완료여야 합니다.");Require(PCS_StepPuzzle.Canonical(board)==PCS_StepPuzzle.Canonical(PCS_StepPuzzle.Generate(difficulty,seed)),"동일 seed 재현");
                    keys.Add(PCS_StepPuzzle.Canonical(board));geometries.Add(PCS_StepPuzzle.GeometryCanonical(board));budgets.Add(string.Join(",",board.budgets));boards.Add(board);if(board.fallback>=0)fallbacks++;unknown+=board.rejectedUnknown;
                    minSolutions=Math.Min(minSolutions,analysis.solutions);minChoices=Math.Min(minChoices,analysis.winningChoices);minDelayed=Math.Min(minDelayed,analysis.delayedLosingChoices);minSensitive=Math.Min(minSensitive,analysis.budgetSensitiveChoices);
                    csv.AppendLine($"{seed},{board.candidate},{board.fallback},{analysis.expanded},{analysis.solutions},{analysis.winningChoices},{analysis.delayedLosingChoices},{analysis.budgetSensitiveChoices},{board.rejectedUnknown},{ms:0.###}");
                }
                for(int i=0;i<3;i++){var board=PCS_StepPuzzle.Fallback(difficulty,i);Require(PCS_StepPuzzle.Quality(board,difficulty),"대체판 복수해법·분기 검증 "+difficulty+" "+i);}
                var exhausted=PCS_StepPuzzle.Generate(difficulty,123,null,7,0);Require(exhausted.fallback>=0&&exhausted.candidate==7&&exhausted.expanded==0,"유한 상한 대체판");
                var unknownFixture=PCS_StepPuzzle.Analyze(boards[0],1);Require(!unknownFixture.complete&&unknownFixture.Status=="UnknownBudget","상한 도달 미확인 구분");
                Require(geometries.Count>=80,"구조 다양성 80/100 미달");
                summary.AppendLine($"{difficulty}: boards=100 normalizedKeys={keys.Count} geometry={geometries.Count} budgets={budgets.Count} fallback={fallbacks}% rejectedUnknown={unknown} minSolutions={minSolutions} minWinningChoices={minChoices} minDelayedLoss={minDelayed} minBudgetSensitive={minSensitive} generationMeanMs={totalMs/100:0.###} generationMaxMs={maxMs:0.###}");
                Evidence("P3_"+difficulty+"_SAMPLES",csv.ToString());Evidence("P3_"+difficulty+"_BOARDS",JsonUtility.ToJson(new MazeEvidence{difficulty=difficulty.ToString(),boards=boards.ToArray()},true));
            }
            Evidence("P3_DESIGNER_SUMMARY",summary+"\nC# Editor 모델 검사. 실제 사람의 재미 평가·키 입력·FPS 검사가 아닙니다.");
        }
        [MenuItem("Tools/PCS/설계 확장/검수/기본 예제 전체 회귀")]
        public static void CoreRegression()
        {
            Guard();ValidateCircuitAssets(true);CircuitDesignerChecks();ValidateHackingAssets();HackingDesignerChecks();PCS_DemoAuthoring.P1LogicChecks();P3DesignerChecks();
        }


        static void SafeSceneOpen(string path)
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode,"먼저 PCS Play를 종료하세요.");Require(PrefabStageUtility.GetCurrentPrefabStage()==null,"Prefab Stage 작업을 먼저 직접 마무리하세요.");for(int i=0;i<SceneManager.sceneCount;i++)Require(!SceneManager.GetSceneAt(i).isDirty,"미저장 씬을 자동 저장·폐기하지 않습니다.");EditorSceneManager.OpenScene(path,OpenSceneMode.Single);
        }
        [MenuItem("Tools/PCS/설계 확장/화면/P1 P2 데모 열기 %#F10")]
        public static void OpenHacking(){SafeSceneOpen(HR+"Scenes/PCS_HackingDemo.unity");}
        [MenuItem("Tools/PCS/설계 확장/화면/P3 데모 열기 %#F11")]
        public static void OpenStep(){SafeSceneOpen("Assets/PCS/StepPuzzle/Scenes/PCS_StepPuzzleDemo.unity");}
        [MenuItem("Tools/PCS/설계 확장/화면/P4 데모 열기 %#F8")]
        public static void OpenCircuit(){SafeSceneOpen(CR+"Scenes/PCS_CircuitHackDemo.unity");}
        public static void PrepareGui(){Guard();OpenHacking();Evidence("GUI_PREPARED","PCS 해킹 씬 하나만 열림. Play 아님. 저장하지 않음.");}
        static GameObject[] PlayRoots()
        {
            string path=SceneManager.GetActiveScene().path;Require(EditorApplication.isPlaying&&SceneManager.sceneCount==1&&(path==HR+"Scenes/PCS_HackingDemo.unity"||path==HR+"Scenes/PCS_HackingStage02Demo.unity"||path==CR+"Scenes/PCS_CircuitHackDemo.unity"||path==CR+"Scenes/PCS_CircuitStage02Demo.unity"||path=="Assets/PCS/StepPuzzle/Scenes/PCS_StepPuzzleDemo.unity"),"허용된 PCS 단독 Play에서 실행하세요.");return SceneManager.GetActiveScene().GetRootGameObjects();
        }
        [MenuItem("Tools/PCS/설계 확장/검수/현재 Play 상태 기록 %#F7")]
        public static void CapturePlayState()
        {
            var roots=PlayRoots();var report=new System.Text.StringBuilder();report.AppendLine("scene="+SceneManager.GetActiveScene().path+" frame="+Time.frameCount+" focused="+Application.isFocused+" size="+Screen.width+"x"+Screen.height);
            var h=roots.SelectMany(r=>r.GetComponentsInChildren<PCS_HackingSession>(true)).FirstOrDefault();if(h)report.AppendLine($"Hacking session={h.SessionId} attempt={h.AttemptId} state={h.State} stage={h.StageId} rotation={h.IsRotation} deliveries={h.Deliveries} angle={h.timing.Angle} pins={h.timing.ConnectedCount}/{h.timing.ActiveTargetCount} misses={h.timing.MissCount} tiles={h.rotation.TileCount} pairs={h.rotation.Status.pairs} reachable={h.rotation.Status.reachable} masks="+string.Join(",",Enumerable.Range(0,h.rotation.TileCount).Select(h.rotation.MaskAt)));
            var c=roots.SelectMany(r=>r.GetComponentsInChildren<CircuitHackManager>(true)).FirstOrDefault();if(c)report.AppendLine($"Circuit run={c.RunId} attempt={c.AttemptId} state={c.State} stage={c.StageId} failures={c.Failures} position={c.Signal.Position} direction={c.Signal.Direction} keyFrames={c.input.ObservedKeyPressFrames} directionFrames={c.input.ObservedDirectionFrames} releaseReady={c.input.ReleaseReady} keyboardPresent={c.input.LastKeyboardPresent} keyboardEnabled={c.input.LastKeyboardEnabled} backend={c.input.KeyboardBackend}");
            var p=roots.SelectMany(r=>r.GetComponentsInChildren<PCS_StepDemoController>(true)).FirstOrDefault();if(p)report.AppendLine($"Step attempt={p.AttemptId} running={p.puzzle.Running} success={p.puzzle.Succeeded} failed={p.puzzle.Failed} position={p.puzzle.X},{p.puzzle.Y} count={p.puzzle.Count} deliveries={p.Deliveries}\n"+JsonUtility.ToJson(p.CurrentBoard));
            Evidence("ACTUAL_PLAY_STATE",report.ToString());Debug.Log("PCS 현재 Play 상태를 외부 증거 폴더에 기록했습니다. 입력 성공은 이전/이후와 화면을 함께 비교하세요.");
        }
        [MenuItem("Tools/PCS/설계 확장/검수/현재 Play 모델 주입 %#F9")]
        public static void InjectPlayCheck()
        {
            var roots=PlayRoots();var h=roots.SelectMany(r=>r.GetComponentsInChildren<PCS_HackingSession>(true)).FirstOrDefault();
            if(h){h.CloseHack();var host=h.GetComponent<PCS_HackingDemoController>();var p1=host.timingStages.First(d=>d.displayNumber==2);var p2=host.rotationStages.First(d=>d.displayNumber==2);Require(h.StartSingleStage(p1),h.Error);SolveTiming(h);Require(h.State==PCS_HackingSession.SessionState.Completed,"실제 Play P1 모델 주입");h.CloseHack();Require(h.StartSingleStage(p2),h.Error);SolveRotation(h);Require(h.State==PCS_HackingSession.SessionState.Completed,"실제 Play P2 모델 주입");if(host.presenter)host.presenter.Refresh();Evidence("HACKING_PLAY_INJECTED","실제 Play 안에서 public API로 P1/P2 2단계 단독 완주. 물리 키보드·마우스 완주 아님.");}
            var c=roots.SelectMany(r=>r.GetComponentsInChildren<CircuitHackManager>(true)).FirstOrDefault();if(c){c.CloseHack();Require(c.StartSequence(c.layouts),c.Error);for(int i=0;i<c.StageCount;i++){WalkCircuit(c);if(i<c.StageCount-1)c.Advance(c.stageDelay,Vector2Int.zero);}Require(c.State==CircuitHackManager.HackState.Completed,"실제 Play P4 경로 주입");Evidence("CIRCUIT_PLAY_INJECTED","실제 Play의 연속 구간 충돌로 선택 단계 전체 완주. 실제 키보드 완주 아님.");}
            var p=roots.SelectMany(r=>r.GetComponentsInChildren<PCS_StepDemoController>(true)).FirstOrDefault();if(p){p.Begin();var path=p.CurrentBoard.alternateProof.Length>0?p.CurrentBoard.alternateProof:p.CurrentBoard.proof;for(int i=1;i<path.Length;i++){var delta=path[i]-path[i-1];p.puzzle.Move(delta.x,delta.y);}Require(p.puzzle.Succeeded&&p.DeliverResult(p.AttemptId),"실제 Play P3 대안 경로");p.RefreshView();Evidence("STEP_PLAY_INJECTED","실제 Play의 Move API로 현재 보드 대안 경로 완주. 실제 키보드 완주 아님.");}CapturePlayState();
        }

        // 이미 저장된 자기 UI의 표시만 보완한다. 기존 씬 인스턴스의 배치는 건드리지 않는다.
        public static void RefineSmallScreenLayout()
        {
            Guard();string path=HR+"Prefabs/PCS_HackingPanel.prefab";var root=PrefabUtility.LoadPrefabContents(path);
            try{var image=root.GetComponent<Image>();var color=image.color;color.a=1;image.color=color;SavePrefab(root,path);}finally{PrefabUtility.UnloadPrefabContents(root);}
            path=HR+"Prefabs/PCS_HackingDemoRig.prefab";root=PrefabUtility.LoadPrefabContents(path);
            try{var c=root.GetComponent<PCS_HackingDemoController>();for(int i=0;i<3;i++){((RectTransform)c.timingSingleButtons[i].transform).anchoredPosition=new Vector2(-450+i*160,175);((RectTransform)c.rotationSingleButtons[i].transform).anchoredPosition=new Vector2(130+i*160,175);}var note=c.beginTiming.transform.parent.Find("DirectStartNote");((RectTransform)note).anchoredPosition=new Vector2(0,-160);SavePrefab(root,path);}finally{PrefabUtility.UnloadPrefabContents(root);}
            ValidateHackingAssets();HackingDesignerChecks();Evidence("SMALL_SCREEN_REFINEMENT","1024 화면의 시작 버튼 범위·접근 안내 겹침·패널 투명도 수정. 저장 재열기 참조와 모델 검사 통과. 실제 화면은 별도 확인.");
        }

        static bool EnsureTileLabels(PCS_HackingView view)
        {
            bool changed=false;foreach(var tile in view.rotationTiles)if(!tile.GetComponentInChildren<Text>(true)){Label("TileLabel",tile.transform,new Vector2(0,-24),new Vector2(65,20),tile.tileIndex.ToString(),13);changed=true;}return changed;
        }
        static void ArrangeCircuitButtons(CircuitDemoHost host)
        {
            var ui=host.manager.view;var buttons=new[]{ui.close,ui.restart,host.reloadLayout,ui.newHack};
            for(int i=0;i<buttons.Length;i++){var b=buttons[i];Require(b,"P4 하단 버튼 연결");var rect=(RectTransform)b.transform;rect.anchoredPosition=new Vector2(-405+i*270,-330);rect.sizeDelta=new Vector2(250,42);var label=b.GetComponentInChildren<Text>();label.rectTransform.sizeDelta=new Vector2(242,38);PrefabUtility.RecordPrefabInstancePropertyModifications(rect);PrefabUtility.RecordPrefabInstancePropertyModifications(label.rectTransform);}
        }
        // 보드와 버튼의 영역을 분리하고 확장 타일의 저장 라벨 누락을 보완한다.
        public static void FinalVisualRefinement()
        {
            Guard();string path=HR+"Prefabs/PCS_HackingPanel.prefab";var root=PrefabUtility.LoadPrefabContents(path);
            try{EnsureTileLabels(root.GetComponentInChildren<PCS_HackingView>(true));SavePrefab(root,path);}finally{PrefabUtility.UnloadPrefabContents(root);}
            path=CR+"Prefabs/PCS_CircuitDemoRig.prefab";root=PrefabUtility.LoadPrefabContents(path);
            try{ArrangeCircuitButtons(root.GetComponent<CircuitDemoHost>());SavePrefab(root,path);}finally{PrefabUtility.UnloadPrefabContents(root);}
            HackingDesignerChecks();CircuitDesignerChecks();ValidateHackingAssets();ValidateCircuitAssets();
            Evidence("FINAL_VISUAL_REFINEMENT","P2 19타일의 저장 라벨과 P4 보드 밖 하단 4버튼 배치. 모든 그래프 fallback 강제 검사·단독 세션·저장 재열기 통과. 실제 화면 별도 확인.");
        }

        public static void FinalRuntimeRefinement()
        {
            Guard();string path=CR+"Prefabs/PCS_CircuitDemoRig.prefab";var root=PrefabUtility.LoadPrefabContents(path);
            try{ArrangeCircuitButtons(root.GetComponent<CircuitDemoHost>());SavePrefab(root,path);}finally{PrefabUtility.UnloadPrefabContents(root);}
            HackingDesignerChecks();CircuitDesignerChecks();ValidateHackingAssets();ValidateCircuitAssets();
            Evidence("RUNTIME_REFINEMENT","외부 P2 Rotate 표시 revision, 완료 후 P1 다음 시도 설정, 안전한 읽기 씬 열기, Both 모드 키보드 경로 통일. 키 입력은 후속 실제 검사로 확인.");
        }

        static CircuitStageLayout SceneStage(int number)=>SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<CircuitStageLayout>(true)).Single(l=>l.displayNumber==number);
        static void AssertEditWall(CircuitStageLayout layout,bool blocked)
        {
            Require(layout.TryCapture(out var d,out string e),e);Require((d.routeError!=null)==blocked,"검수 경로 상태 구분");
            Require((CircuitCollisionChecker.Check(d,d.start,new Vector2(12,10)).kind==CircuitCollisionChecker.Kind.Wall)==blocked,"저장 편집 벽 충돌 일치");
            var root=new GameObject("PCS_SavedLayoutCheck");var m=root.AddComponent<CircuitHackManager>();try{Require(m.StartSingleStage(layout),m.Error);m.Advance(1,Vector2Int.right);Require((m.State==CircuitHackManager.HackState.FailFeedback)==blocked,"저장 배치 실제 모델 이동");}finally{Object.DestroyImmediate(root);}
        }
        // 감사된 자기 예제에서만 실행한다. 자신이 추가한 검수 벽만 제거하며 기존 배치는 보존한다.
        [MenuItem("Tools/PCS/설계 확장/검수/저장 편집 흐름 검사")]
        public static void SavedEditingChecks()
        {
            Guard();string scenePath=CR+"Scenes/PCS_CircuitHackDemo.unity";SafeSceneOpen(scenePath);
            var first=SceneStage(1);Require(!first.wallsRoot.Find("PCS_VerificationWall"),"검수 벽 이름 충돌");
            var otherHashes=new[]{Hash(StagePath(1)),Hash(StagePath(2))};first.TryCapture(out var original,out _);SceneStage(2).TryCapture(out var other2,out _);SceneStage(3).TryCapture(out var other3,out _);string shape2=JsonUtility.ToJson(other2),shape3=JsonUtility.ToJson(other3);
            var wall=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(CR+"Prefabs/PCS_CircuitWall.prefab"),first.wallsRoot);wall.name="PCS_VerificationWall";var rect=(RectTransform)wall.transform;
            Undo.IncrementCurrentGroup();Vector2 beforeSize=rect.sizeDelta;Undo.RecordObject(rect,"PCS 검수 벽 이동과 크기");Place(rect,new Rect(7.5f,7.5f,1,5));Undo.FlushUndoRecordObjects();Undo.PerformUndo();Require(rect.sizeDelta==beforeSize,"벽 크기 Undo");Undo.PerformRedo();Require(rect.sizeDelta==new Vector2(8,40),"벽 크기 Redo");wall.name="PCS_VerificationWall";PrefabUtility.RecordPrefabInstancePropertyModifications(wall);PrefabUtility.RecordPrefabInstancePropertyModifications(rect);
            AssertEditWall(first,true);EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());SaveScene(SceneManager.GetActiveScene(),scenePath);SafeSceneOpen(scenePath);AssertEditWall(SceneStage(1),true);
            SceneStage(2).TryCapture(out var after2,out _);SceneStage(3).TryCapture(out var after3,out _);Require(shape2==JsonUtility.ToJson(after2)&&shape3==JsonUtility.ToJson(after3),"1단계 편집이 다른 단계에 새지 않음");
            first=SceneStage(1);wall=first.wallsRoot.Find("PCS_VerificationWall").gameObject;wall.SetActive(false);PrefabUtility.RecordPrefabInstancePropertyModifications(wall);EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());SaveScene(SceneManager.GetActiveScene(),scenePath);SafeSceneOpen(scenePath);AssertEditWall(SceneStage(1),false);
            first=SceneStage(1);Object.DestroyImmediate(first.wallsRoot.Find("PCS_VerificationWall").gameObject);EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());SaveScene(SceneManager.GetActiveScene(),scenePath);SafeSceneOpen(scenePath);AssertEditWall(SceneStage(1),false);
            string path=StagePath(0);var assetRoot=PrefabUtility.LoadPrefabContents(path);
            try{first=assetRoot.GetComponent<CircuitStageLayout>();var w=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(CR+"Prefabs/PCS_CircuitWall.prefab"),first.wallsRoot);w.name="PCS_VerificationWall";PrefabUtility.RecordPrefabInstancePropertyModifications(w);Place((RectTransform)w.transform,new Rect(7.5f,7.5f,1,5));PrefabUtility.RecordPrefabInstancePropertyModifications(w.transform);SavePrefab(assetRoot,path);}finally{PrefabUtility.UnloadPrefabContents(assetRoot);}
            assetRoot=PrefabUtility.LoadPrefabContents(path);try{AssertEditWall(assetRoot.GetComponent<CircuitStageLayout>(),true);}finally{PrefabUtility.UnloadPrefabContents(assetRoot);}SafeSceneOpen(scenePath);AssertEditWall(SceneStage(1),true);
            assetRoot=PrefabUtility.LoadPrefabContents(path);try{first=assetRoot.GetComponent<CircuitStageLayout>();first.wallsRoot.Find("PCS_VerificationWall").gameObject.SetActive(false);PrefabUtility.RecordPrefabInstancePropertyModifications(first.wallsRoot.Find("PCS_VerificationWall").gameObject);SavePrefab(assetRoot,path);}finally{PrefabUtility.UnloadPrefabContents(assetRoot);}
            assetRoot=PrefabUtility.LoadPrefabContents(path);try{first=assetRoot.GetComponent<CircuitStageLayout>();AssertEditWall(first,false);Object.DestroyImmediate(first.wallsRoot.Find("PCS_VerificationWall").gameObject);SavePrefab(assetRoot,path);}finally{PrefabUtility.UnloadPrefabContents(assetRoot);}
            SafeSceneOpen(scenePath);first=SceneStage(1);AssertEditWall(first,false);first.TryCapture(out var restored,out _);Require(JsonUtility.ToJson(original)==JsonUtility.ToJson(restored),"검수 종료 뒤 기본 기하 보존");
            var scale=first.transform.localScale;first.transform.localScale=new Vector3(.75f,2,1);first.TryCapture(out var scaled,out _);first.transform.localScale=scale;Require(JsonUtility.ToJson(restored)==JsonUtility.ToJson(scaled),"부모 양수 배율과 논리 기하 독립");
            Require(otherHashes[0]==Hash(StagePath(1))&&otherHashes[1]==Hash(StagePath(2)),"다른 단계 원본 바이트 보존");
            Evidence("P4_SAVED_EDITING","공식 Prefab/Scene API로 벽 인스턴스 복제·이동·크기 Undo/Redo, 씬 override 저장·재열기·충돌·비활성 통과·자기 벽 제거, 원본 프리팹 저장·재열기·씬 반영·비활성·자기 벽 제거 PASS. 기본 기하·다른 단계 바이트 보존. Editor 모델 이동 검사이며 UI Ctrl+D 조작/키보드 완주와 구분.");
        }

        [MenuItem("Tools/PCS/설계 확장/선택한 P1 P2 단계 읽기 검사")]
        public static void ValidateSelectedStageData()
        {
            if(Selection.activeObject is PCS_TimingStageData timing){string error=timing.Capture().Validate();if(error==null)Debug.Log("타이밍 단계 설정 유효",timing);else Debug.LogError(error,timing);return;}
            if(Selection.activeObject is PCS_RotationStageData rotation){var data=rotation.Capture();string error=data.Validate();if(error!=null){Debug.LogError(error,rotation);return;}for(int i=0;i<data.graphs.Length;i++){var model=new PCS_RotationPuzzle();if(!model.Begin(data,2026,i)||!model.Begin(data,2026,i,()=>0)){Debug.LogError(model.Error,rotation);return;}}Debug.Log("회전 단계 구조와 유한 시작 배치 검사 통과",rotation);return;}
            Debug.LogError("P1 또는 P2 단계 데이터 자산을 선택하세요.");
        }
        // 아래 단계별 명시 메서드는 현재 작업 단위에서만 추가된다.
    }
}

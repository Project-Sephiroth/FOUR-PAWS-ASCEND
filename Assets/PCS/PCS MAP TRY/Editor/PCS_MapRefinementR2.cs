using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace PCS.MapPlanning.R2.EditorTools
{
    // Explicit Editor commands only. No runtime hooks, automatic rebuilds or shared asset saves.
    public static class PCS_MapRefinementR2
    {
        const string Project = @"C:\FOUR-PAWS-ASCEND-NEWPCS";
        const string Run = @"C:\FOUR-PAWS-PCS-BRIEF\CPLAN_REFINEMENT_20261030\CIMPL_R2\run-001";
        const string Folder = "Assets/PCS/PCS MAP TRY/Scenes/";
        const string Helper = "Assets/PCS/PCS MAP TRY/Editor/PCS_MapRefinementR2.cs";
        static readonly string[] Originals = { "PCS_MAP00_Malkuth_Blockout", "PCS_MAP01_Yesod_Blockout", "PCS_MAP02_Hod_Blockout", "PCS_MAP03_Netzach_Blockout" };
        static readonly string[] Names = { "PCS_MAP00_Malkuth_AscendingGathering", "PCS_MAP00_Malkuth_ONE MORE VERSION", "PCS_MAP01_Yesod_SourceA_Edit", "PCS_MAP01_Yesod_SourceB_Edit", "PCS_MAP02_Hod_Blockout", "PCS_MAP03_Netzach_Blockout" };
        static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();
        static Material material;
        [Serializable] public class FileRec { public string path, sha256; public long bytes; }
        [Serializable] public class Baseline { public FileRec[] files; public string scope, capturedAt; }
        [Serializable] public class Phase { public string name, status, startedAt, finishedAt, detail; }
        [Serializable] public class State
        {
            public int schemaVersion; public string packageVersion, runId, projectRoot, approvalEvidence, branch, head, baselineGitStatus;
            public FileRec[] packageHashes, originalScenes; public List<FileRec> ownedFiles; public List<Phase> phases;
            public string lastCompletedPhase, pendingSavePath, pendingSavePreviousHash, updatedAt; public string[] sourceDeviations, pendingIssues;
        }
        [Serializable] public class ObjectRec
        {
            public string id, transformId, path, name, parentId, sprite, material;
            public string[] componentIds, componentTypes; public float[] worldMatrix, position, scale, rendererBounds, colliderBounds, tint, spriteSize, colliderSize;
            public bool activeSelf, activeHierarchy, rendererEnabled, colliderEnabled; public int layer, sortingOrder; public string drawMode;
        }
        [Serializable] public class IdentityPair { public ObjectRec source, target; }
        [Serializable] public class IdentityScene { public string scene, sourceScene, sourceGuid, targetGuid, stage; public List<IdentityPair> pairs = new List<IdentityPair>(); }
        [Serializable] public class Identity { public List<IdentityScene> scenes = new List<IdentityScene>(); }
        [Serializable] public class Change { public string scene, stage, kind, reason; public ObjectRec before, after; }
        [Serializable] public class Changes { public List<Change> moves = new List<Change>(); }
        [Serializable] public class SceneReport
        {
            public string path, guid, sha256, checkedAt; public int gameObjects, activeRenderers, activeColliders, effectors, missingScripts, prefabInstances;
            public bool reopened, passed; public ObjectRec[] objects; public string[] checks;
        }
        [Serializable] public class Capture { public string file,scene,mode,capturedAt,capturedAtKST,caption,sha256,sourceEncoding,conversion; public long bytes; public int width,height; }
        [Serializable] public class QA { public string runId = "run-001", status = "IN_PROGRESS"; public List<SceneReport> scenes = new List<SceneReport>(); public List<Capture> captures = new List<Capture>(); }
        [Serializable] public class Inventory { public string runId = "run-001"; public List<FileRec> files; }
        static string PathFor(int i) { return Folder + Names[i] + ".unity"; }
        static string Now() { return DateTimeOffset.Now.ToString("o"); }
        static void Need(bool value, string message) { if (!value) throw new InvalidOperationException("R2 GUARD: " + message); }
        static string Hash(string path) { using (var s = File.OpenRead(path)) using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(s)).Replace("-", "").ToLowerInvariant(); }
        static T Read<T>(string name) { return JsonUtility.FromJson<T>(File.ReadAllText(Run + "/" + name)); }
        static void Write(string name, object value) { File.WriteAllText(Run + "/" + name, JsonUtility.ToJson(value, true), new UTF8Encoding(false)); }
        static void WriteState(State state)
        {
            state.updatedAt = Now(); Write("STATE.json.tmp", state); File.Replace(Run + "/STATE.json.tmp", Run + "/STATE.json", null);
        }
        static void InventoryUpdate()
        {
            var state = Read<State>("STATE.json");
            foreach (var r in state.ownedFiles) if (File.Exists(r.path)) { r.sha256 = Hash(r.path); r.bytes = new FileInfo(r.path).Length; }
            WriteState(state); Write("CREATED_ASSETS.json", new Inventory { files = state.ownedFiles });
        }
        static void Protection()
        {
            var baseline = Read<Baseline>("PROTECTED_BASELINE.json");
            foreach (var f in baseline.files) Need(File.Exists(f.path) && Hash(f.path) == f.sha256, "Protected mismatch: " + f.path);
            var known = new HashSet<string>(baseline.files.Select(f => Path.GetFullPath(f.path)), StringComparer.OrdinalIgnoreCase);
            var allowed = new HashSet<string>(Read<State>("STATE.json").ownedFiles.Select(f => Path.GetFullPath(f.path)), StringComparer.OrdinalIgnoreCase);
            foreach (var d in new[] { "Assets", "Packages", "ProjectSettings" }) foreach (var f in Directory.GetFiles(d, "*", SearchOption.AllDirectories))
                Need(known.Contains(Path.GetFullPath(f)) || allowed.Contains(Path.GetFullPath(f)), "Unexpected project file: " + f);
        }
        static void Guard()
        {
            Need(string.Equals(Path.GetFullPath(Application.dataPath + "/.."), Project, StringComparison.OrdinalIgnoreCase), "Wrong project");
            Need(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling, "Stable Edit mode required");
            var state = Read<State>("STATE.json"); Need(state.packageVersion == "CPLAN-R2-v1.0", "Run identity");
            for (int i = 0; i < SceneManager.sceneCount; i++) Need(!SceneManager.GetSceneAt(i).isDirty, "Unsaved scene: " + SceneManager.GetSceneAt(i).path);
            foreach (var f in state.packageHashes) Need(Hash(f.path) == f.sha256, "Package changed");
            foreach (var f in state.ownedFiles)
            {
                if (!string.IsNullOrEmpty(f.sha256)) Need(File.Exists(f.path) && Hash(f.path) == f.sha256, "Owned output changed: " + f.path);
                else Need(!File.Exists(f.path) || f.path.EndsWith(".meta"), "Unregistered existing file: " + f.path);
            }
        }
        static void Step(int number, Action action, bool visualPending = false)
        {
            Guard(); Protection(); var s = Read<State>("STATE.json"); var p = s.phases.Single(x => x.name == "R2-" + number.ToString("00"));
            if (p.status == "PASS") { Debug.Log("R2 already complete, verified and skipped: " + p.name); return; }
            Need(s.phases[number - 1].status == "PASS", "Previous checkpoint"); p.status = "IN_PROGRESS"; p.startedAt = Now(); WriteState(s);
            References(); InventoryUpdate(); action(); Protection(); InventoryUpdate(); s = Read<State>("STATE.json"); p = s.phases[number];
            p.status = visualPending ? "IN_PROGRESS" : "PASS"; p.finishedAt = visualPending ? "" : Now(); p.detail = visualPending ? "Saved/reopened data pass; actual UI14 captures pending" : "SaveScene true; reopened checks and protected hashes passed";
            if (!visualPending) s.lastCompletedPhase = p.name; WriteState(s);
            string line = Now() + " " + p.name + " " + p.detail; File.AppendAllText(Run + "/UNITY_LOG_HISTORY.md", "\n- " + line + "\n"); File.AppendAllText(Run + "/UnityEditor.log", line + "\n");
            File.WriteAllText(Run + "/CHECKPOINT.md", "# R2 체크포인트\n\n" + line + "\n", new UTF8Encoding(false)); Debug.Log(line);
        }
        static void References()
        {
            Sprites.Clear();
            Load("Assets/Resources/img/BG/stage1-1.png", "e2c77b115d7fe39458f6a1c13b272c86", "stage1-1_0");
            foreach (var n in new[] { "Tiles_0", "Tiles_3", "Tiles_4", "Tiles_5" }) Load("Assets/Resources/img/Tiles/Tiles.png", "9cac4b34d8bf0f742bfbbf52ab59ea89", n);
            foreach (var n in new[] { "objects_4", "objects_7" }) Load("Assets/Resources/img/object/objects.png", "31841eeb4c020f24cbdf27597590d5f9", n);
            Load("Assets/Resources/img/object/Switch-Sheet.png", "a36f828a8a278c449bcdd1bc4b81d68a", "Switch-Sheet_0");
            material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath("a97c105638bdf8b4a8650670310a4cd3")); Need(material != null, "Material missing");
        }
        static void Load(string path, string guid, string name) { Need(AssetDatabase.AssetPathToGUID(path) == guid, "Sprite GUID"); var sp = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().SingleOrDefault(x => x.name == name); Need(sp != null, name); Sprites.Add(name, sp); }
        // Finding an existing node never resets its pose.
        static Transform Node(Transform parent, string path)
        {
            var cursor = parent;
            foreach (var part in path.Split('/')) { var child = cursor.Find(part); if (child == null) { child = new GameObject(part).transform; child.SetParent(cursor, false); } cursor = child; }
            return cursor;
        }
        static Transform At(Transform parent, string path, float x, float y, float z = 0) { var n = Node(parent, path); n.localPosition = new Vector3(x, y, z); n.localRotation = Quaternion.identity; n.localScale = Vector3.one; return n; }
        static string FullPath(Transform t) { return t.parent == null ? t.name : FullPath(t.parent) + "/" + t.name; }
        static string Id(Object o) { return GlobalObjectId.GetGlobalObjectIdSlow(o).targetObjectId.ToString(); }
        static string AssetId(Object o) { if (o == null) return ""; AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o, out string guid, out long id); return guid + ":" + id; }
        static float[] V(Vector3 v) { return new[] { v.x, v.y, v.z }; }
        static float[] B(Bounds b) { return new[] { b.center.x, b.center.y, b.center.z, b.size.x, b.size.y, b.size.z }; }
        static ObjectRec Record(Transform t)
        {
            var sr = t.GetComponent<SpriteRenderer>(); var bc = t.GetComponent<BoxCollider2D>(); var m = t.localToWorldMatrix; var comps = t.GetComponents<Component>();
            return new ObjectRec { id = Id(t.gameObject), transformId = Id(t), name = t.name, path = FullPath(t), parentId = t.parent == null ? "" : Id(t.parent.gameObject), worldMatrix = Enumerable.Range(0,16).Select(i => m[i]).ToArray(), position = V(t.position), scale = V(t.lossyScale), activeSelf = t.gameObject.activeSelf, activeHierarchy = t.gameObject.activeInHierarchy, layer = t.gameObject.layer,
                componentIds = comps.Select(Id).ToArray(), componentTypes = comps.Select(c => c.GetType().FullName).ToArray(), sprite = sr == null ? "" : AssetId(sr.sprite), material = sr == null ? "" : AssetId(sr.sharedMaterial), rendererEnabled = sr != null && sr.enabled, colliderEnabled = bc != null && bc.enabled,
                rendererBounds = sr == null ? new float[0] : B(sr.bounds), colliderBounds = bc == null ? new float[0] : B(bc.bounds), tint = sr == null ? new float[0] : new[] {sr.color.r,sr.color.g,sr.color.b,sr.color.a}, spriteSize = sr == null ? new float[0] : new[] { sr.size.x,sr.size.y }, colliderSize = bc == null ? new float[0] : new[] { bc.size.x,bc.size.y,bc.offset.x,bc.offset.y }, sortingOrder = sr == null ? 0 : sr.sortingOrder, drawMode = sr == null ? "" : sr.drawMode.ToString() };
        }
        static Transform Root(Scene scene) { var roots = scene.GetRootGameObjects(); Need(roots.Length == 1, "Expected one scene root"); return roots[0].transform; }
        static Scene Open(int i) { return EditorSceneManager.OpenScene(PathFor(i), OpenSceneMode.Single); }
        static void Save(Scene scene, int i)
        {
            string path = PathFor(i); var s = Read<State>("STATE.json"); var f = s.ownedFiles.Single(x => x.path == path);
            Need(scene.path == path || string.IsNullOrEmpty(scene.path), "Wrong save target"); Need(!File.Exists(path) || Hash(path) == f.sha256, "Save ownership conflict");
            s.pendingSavePath = path; s.pendingSavePreviousHash = File.Exists(path) ? Hash(path) : "NEW"; WriteState(s);
            Need(EditorSceneManager.SaveScene(scene, path, false), "SaveScene false"); InventoryUpdate(); s = Read<State>("STATE.json"); s.pendingSavePath = ""; s.pendingSavePreviousHash = ""; WriteState(s);
        }
        static SpriteRenderer Picture(Transform p, string name, string sprite, float x, float y, float width, float height, Color tint, int order = -1, bool tiled = false, float z = 0)
        {
            var t = At(p, name, x, y, z); var sr = t.GetComponent<SpriteRenderer>(); if (sr == null) sr = t.gameObject.AddComponent<SpriteRenderer>(); sr.sprite = Sprites[sprite]; sr.sharedMaterial = material; sr.color = tint; sr.sortingLayerID = 0; sr.sortingOrder = order;
            sr.drawMode = tiled ? SpriteDrawMode.Tiled : SpriteDrawMode.Simple;
            if (tiled) { sr.size = new Vector2(width, sr.sprite.bounds.size.y); t.localScale = new Vector3(1,height/sr.sprite.bounds.size.y,1); }
            else t.localScale = new Vector3(width/sr.sprite.bounds.size.x,height/sr.sprite.bounds.size.y,1);
            Vector3 center = Vector3.Scale(sr.sprite.bounds.center, t.localScale); if (tiled) center.x = 0; t.localPosition -= new Vector3(center.x,center.y,0); return sr;
        }
        static BoxCollider2D Box(Transform p, string name, float x, float y, float width, float height)
        {
            var t = At(p,name,x,y); t.gameObject.layer = 6; var bc=t.GetComponent<BoxCollider2D>(); if(bc==null) bc=t.gameObject.AddComponent<BoxCollider2D>(); bc.size=new Vector2(width,height); bc.offset=Vector2.zero; bc.isTrigger=false; return bc;
        }
        static Transform Platform(Transform p,string name,float left,float right,float top,bool collision=true)
        {
            var t=At(p,name,(left+right)/2,top); Picture(t,"Visual","Tiles_0",0,-.275f,right-left,.55f,Color.white,-1,true); if(collision) Box(t,"Collider_Layout",0,-.2f,right-left,.4f); Node(t,"FloorTop"); return t;
        }
        static Transform Block(Transform p,string name,float x,float y,float w,float h,Color tint,bool collision=false,string sprite="Tiles_3",int order=-1)
        {
            var t=At(p,name,x,y); Picture(t,"Visual",sprite,0,0,w,h,tint,order); if(collision) Box(t,"Collider_Layout",0,0,w,h); return t;
        }
        static void BayPose(Transform bay,float x,float top)
        {
            bay.position=new Vector3(x,top,0); var col=bay.Find("SupportColumn_Visual").GetComponent<SpriteRenderer>();
            col.transform.localPosition=new Vector3(0,-2,0)-Vector3.Scale(col.sprite.bounds.center,col.transform.localScale);
        }
        static void Background(Transform p,string name,float x,float y,float width)
        {
            var sp=Sprites["stage1-1_0"]; float scale=width/sp.bounds.size.x; var sr=Picture(p,name,"stage1-1_0",x,y,width,sp.bounds.size.y*scale,Color.white,-2,false,10); sr.gameObject.SetActive(true);
        }
        static void DisableOldBackgrounds(Transform root)
        {
            foreach(var sr in root.GetComponentsInChildren<SpriteRenderer>(true)) if(sr.sprite.name=="stage1-1_0") sr.gameObject.SetActive(false);
        }
        static void CorrectPivots(Transform root)
        {
            foreach(var sr in root.GetComponentsInChildren<SpriteRenderer>(true)) if(sr.sprite.name=="Tiles_5" && (sr.name=="SupportColumn_Visual" || sr.name=="Column_VISUAL_ONLY"))
            {
                var before=Record(sr.transform); float y=sr.name=="SupportColumn_Visual" ? -2 : 1.6f; sr.transform.localPosition=new Vector3(0,y,0)-Vector3.Scale(sr.sprite.bounds.center,sr.transform.localScale);
                LogChange(root.gameObject.scene.path,"PIVOT","Sprite.bounds.center correction; original Import untouched",before,Record(sr.transform));
            }
        }
        static void LogChange(string scene,string kind,string reason,ObjectRec before,ObjectRec after)
        {
            var c=Read<Changes>("HIERARCHY_CHANGELOG.json"); c.moves.Add(new Change{scene=scene,stage=Read<State>("STATE.json").phases.Last(x=>x.status=="IN_PROGRESS").name,kind=kind,reason=reason,before=before,after=after}); Write("HIERARCHY_CHANGELOG.json",c);
        }
        static void BackupHelper(string phase) { string to=Run+"/backups/"+phase+"_Helper.before.cs"; if(!File.Exists(to)) File.Copy(Helper,to,false); }
        static void NewView(Transform root,float x,float y)
        {
            var ct=At(root,"VIEW/MainCamera_Blockout",x,y,-10); var cam=ct.gameObject.AddComponent<Camera>(); cam.orthographic=true; cam.orthographicSize=5; cam.clearFlags=CameraClearFlags.SolidColor; cam.backgroundColor=new Color(.035f,.055f,.075f); cam.nearClipPlane=.1f; cam.farClipPlane=100;
            var cd=ct.gameObject.AddComponent<UniversalAdditionalCameraData>(); cd.renderPostProcessing=false; cd.renderShadows=false; cd.SetRenderer(-1);
            var lt=Node(root,"VIEW/GlobalLight2D"); var light=lt.gameObject.AddComponent<Light2D>(); light.enabled=false; light.lightType=Light2D.LightType.Global; light.intensity=1; light.color=Color.white; light.enabled=true;
        }
        [MenuItem("Tools/C-IMPL-R2/01 Create four selective copies",false,201)]
        public static void CreateFour() { Step(1,()=> {
            for(int i=0;i<4;i++)
            {
                if(File.Exists(PathFor(i))) { Validate(Open(i),i,false); continue; }
                int source=i<2?0:1; var src=EditorSceneManager.OpenScene(Folder+Originals[source]+".unity",OpenSceneMode.Single); var sourceRoot=Root(src);
                var dest=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive); SceneManager.SetActiveScene(dest); var root=new GameObject(Names[i]).transform;
                var identity=new IdentityScene{scene=PathFor(i),sourceScene=src.path,sourceGuid=AssetDatabase.AssetPathToGUID(src.path),stage="R2-01"}; var pairs=new List<KeyValuePair<ObjectRec,Transform>>();
                Action<Transform,Transform> clone=(branch,parent)=> {
                    var copy=Object.Instantiate(branch.gameObject,parent,false).transform; copy.name=branch.name;
                    foreach(var original in branch.GetComponentsInChildren<Transform>(true)) { string relative=original==branch?"":FullPath(original).Substring(FullPath(branch).Length+1); var target=relative==""?copy:copy.Find(relative); pairs.Add(new KeyValuePair<ObjectRec,Transform>(Record(original),target)); }
                };
                foreach(var name in new[]{"ENVIRONMENT","TERRAIN","ELEVATOR_ENTRY_VISUAL","ELEVATOR_EXIT_VISUAL","PLAYER_SPAWN_MARKERS","PROPOSED_PLACEHOLDERS","TEAM_INTEGRATION_REQUIRED"}) clone(sourceRoot.Find(name),root);
                var puzzle=Node(root,"PUZZLE_AREAS"); foreach(var name in i<2?new[]{"RABBIT","RAT","FROG","BEAR"}:new[]{i==2?"PZ02":"PZ01"}) clone(sourceRoot.Find("PUZZLE_AREAS/"+name),puzzle);
                Need(!src.isDirty,"Original marked dirty during selective copy"); Need(EditorSceneManager.CloseScene(src,true),"Close clean source failed"); NewView(root,-6,3); Save(dest,i);
                identity.targetGuid=AssetDatabase.AssetPathToGUID(PathFor(i)); foreach(var pair in pairs) identity.pairs.Add(new IdentityPair{source=pair.Key,target=Record(pair.Value)});
                var ids=Read<Identity>("OBJECT_IDENTITY_MAP.json"); ids.scenes.Add(identity); Write("OBJECT_IDENTITY_MAP.json",ids); Validate(Open(i),i,false);
            }
        }); }
        static void WallPort(Transform room,string name,float bottom,float top)
        {
            var wall=room.Find("TERRAIN/"+name); float x=name=="LeftWall"?-.25f:18.25f;
            wall.localPosition=new Vector3(x,(top+8.5f)/2,0); Picture(wall,"Visual","Tiles_3",0,0,.5f,8.5f-top,Color.white); var box=wall.Find("Collider_Layout").GetComponent<BoxCollider2D>(); box.size=new Vector2(.5f,8.5f-top);
            if(bottom>0) Block(room.Find("TERRAIN"),name+"Lower_R2",x,bottom/2,.5f,bottom,Color.white,true);
        }
        static void RoomPorts(Transform room,float port)
        {
            WallPort(room,"LeftWall",0,2.8f); WallPort(room,"RightWall",port,port+(port==0?2.8f:3)); Platform(Node(room,"R2_PORTS_PROPOSED"),"ExitLip_PROPOSED",18,18.5f,port);
        }
        static void MoveRoom(Transform room,Vector3 position)
        {
            var before=Record(room); room.position=position; LogChange(room.gameObject.scene.path,"PLANNED_AREA_DELTA","Approved PROPOSED room/selected puzzle relocation; internal local relationships retained",before,Record(room));
        }
        [MenuItem("Tools/C-IMPL-R2/02 Malkuth ascending and ONE MORE VERSION",false,202)]
        public static void Malkuth() { Step(2,()=> {
            BackupHelper("R22"); string[] rooms={"RABBIT","RAT","FROG","BEAR"};
            for(int i=0;i<2;i++)
            {
                var sc=Open(i); var root=Root(sc); DisableOldBackgrounds(root); var env=Node(root,"ENVIRONMENT/R2_BACKDROPS"); var paths=Node(root,"TERRAIN/R2_GATHERING_PROPOSED"); float baseY=i==0?0:14;
                for(int k=0;k<4;k++)
                {
                    float x=(i==0?44:28)*k, port=k==1?0:3.2f; var room=root.Find("PUZZLE_AREAS/"+rooms[k]); MoveRoom(room,new Vector3(x,baseY,0)); RoomPorts(room,port); Background(env,rooms[k]+"_Backdrop",x+16,baseY+5,i==0?44:28);
                    Node(room,"TEAM_MARKERS/DISTRIBUTED_START_TEAM_CONTRACT");
                    if(i==0)
                    {
                        int count=Mathf.CeilToInt((14-port)/.6f); for(int j=0;j<count;j++) Platform(Node(paths,rooms[k]+"_FixedAscent_PROPOSED"),"Step_"+(j+1).ToString("00"),x+18.5f+j*.9f,x+18.5f+(j+1)*.9f,port+(14-port)*(j+1)/count);
                    }
                    else
                    {
                        Platform(Node(paths,rooms[k]+"_Drop_PROPOSED"),"ExitLip",x+18.5f,x+19,baseY+port); At(paths,rooms[k]+"_Drop_PROPOSED/DropCenter_START",x+20.5f,baseY+port); At(paths,rooms[k]+"_Drop_PROPOSED/Landing_CENTER",x+20.5f,.05f);
                        Picture(Node(paths,rooms[k]+"_Drop_PROPOSED"),"ShaftRail_Left_VISUAL_ONLY","Tiles_5",x+19,(baseY+port)/2,.22f,baseY+port,new Color(.48f,.62f,.7f),-1);
                        Picture(Node(paths,rooms[k]+"_Drop_PROPOSED"),"ShaftRail_Right_VISUAL_ONLY","Tiles_5",x+22,(baseY+port)/2,.22f,baseY+port,new Color(.48f,.62f,.7f),-1);
                    }
                }
                var entry=root.Find("ELEVATOR_ENTRY_VISUAL"); var exit=root.Find("ELEVATOR_EXIT_VISUAL"); BayPose(entry,-6,baseY); BayPose(exit,i==0?190:118,i==0?14:0); Platform(paths,"EntryApproach",-3,0,baseY);
                if(i==0)
                {
                    var common=Platform(paths,"UpperCommon_OneWay_PROPOSED",34.7f,178,14); var bc=common.Find("Collider_Layout").GetComponent<BoxCollider2D>(); bc.usedByEffector=true; var eff=bc.GetComponent<PlatformEffector2D>(); if(eff==null) eff=bc.gameObject.AddComponent<PlatformEffector2D>(); eff.useOneWay=true; eff.surfaceArc=180; eff.useColliderMask=false;
                    Platform(paths,"GatheringWait_PROPOSED",174,184,14); Platform(paths,"ExitApproach",184,187,14); Background(env,"UpperGathering",180,17,36);
                    for(int k=0;k<4;k++) Background(env,"AscentUpperBackdrop_"+k,22+44*k,13,44);
                }
                else
                {
                    Platform(paths,"LowerCommonGathering_PROPOSED",-3,112,0); Platform(paths,"ExitApproach",112,115,0); Node(paths,"WAIT_X100_TO112_PROPOSED");
                    var bear=root.Find("PUZZLE_AREAS/BEAR"); var pocket=Node(bear,"R2_BEAR_GAP_ISOLATION_PROPOSED"); Platform(pocket,"PocketCatch_NoRecoveryFunction",3.5f,7.5f,-3); Block(pocket,"PocketLeft",3.25f,-1.5f,.5f,3,Color.white,true); Block(pocket,"PocketRight",7.75f,-1.5f,.5f,3,Color.white,true);
                    for(int k=0;k<4;k++) Background(env,"LowerHallBackdrop_"+k,10.5f+28*k,3,28); Background(env,"ExitBackdrop",118,3,28);
                }
                root.Find("VIEW/MainCamera_Blockout").position=new Vector3(-6,baseY+3,-10); CorrectPivots(root); Save(sc,i); Validate(Open(i),i,true);
            }
        }); }
        [MenuItem("Tools/C-IMPL-R2/03 Yesod SourceA and SourceB",false,203)]
        public static void Yesod() { Step(3,()=> {
            BackupHelper("R23"); for(int i=2;i<4;i++) { var sc=Open(i); var root=Root(sc); var area=root.Find("PUZZLE_AREAS/"+(i==2?"PZ02":"PZ01")); MoveRoom(area,Vector3.zero); Node(area,i==2?"SOURCE_A_FROM_PZ02_NOT_FINAL_NUMBER":"SOURCE_B_FROM_PZ01_NOT_FINAL_NUMBER");
                BayPose(root.Find("ELEVATOR_ENTRY_VISUAL"),-6,0); BayPose(root.Find("ELEVATOR_EXIT_VISUAL"),32,6.4f); var terrain=Node(root,"TERRAIN/R2_APPROACHES"); Platform(terrain,"EntryApproach",-3,0,0); Platform(terrain,"UpperExitApproach_PROPOSED",26,29,6.4f);
                DisableOldBackgrounds(root); var env=Node(root,"ENVIRONMENT/R2_BACKDROPS"); Background(env,"Puzzle",13,4.3f,40); Background(env,"Entry",-6,3,26); Background(env,"Exit",32,8.5f,26); CorrectPivots(root); Save(sc,i); Validate(Open(i),i,true);
            }
        }); }
        static void RoleFrame(Transform parent,string name,float x,float y,float width,float height,Color tint)
        {
            var frame=At(parent,name,x,y); Picture(frame,"LeftRail_VISUAL_ONLY","Tiles_4",-width/2,height/2,.22f,height,tint,1); Picture(frame,"RightRail_VISUAL_ONLY","Tiles_4",width/2,height/2,.22f,height,tint,1); Picture(frame,"Header_VISUAL_ONLY","Tiles_0",0,height,width,.25f,tint,1,true);
        }
        [MenuItem("Tools/C-IMPL-R2/04 Refine existing Hod and Netzach",false,204)]
        public static void Existing() { Step(4,()=> {
            BackupHelper("R24"); for(int i=4;i<6;i++) { var sc=Open(i); var root=Root(sc); RecordExistingIdentity(sc,i); var before=root.GetComponentsInChildren<Transform>(true).Select(Record).ToArray(); CorrectPivots(root);
                if(i==4)
                {
                    DisableOldBackgrounds(root); var env=Node(root,"ENVIRONMENT/R2_BACKDROPS"); Background(env,"EntryAndExit",-8,3,28); Background(env,"RoleHall",14,3,28);
                    var puzzles=root.Find("PUZZLE_AREAS"); RoleFrame(puzzles.Find("BearDefenseZone"),"R2_BearRole_FRAME_ONLY",7,0,4,2.7f,new Color(.9f,.65f,.3f)); RoleFrame(puzzles.Find("FrogTraversalZone"),"R2_FrogRole_FRAME_ONLY",14,0,6,5,new Color(.4f,.8f,.5f)); RoleFrame(puzzles.Find("RabbitUpperZone"),"R2_RabbitRole_FRAME_ONLY",21,4.5f,6,2.5f,new Color(.7f,.7f,1)); RoleFrame(puzzles.Find("ThreatOrigin_Slot"),"R2_ThreatAlternative_FRAME_ONLY",0,0,2,2,new Color(1,.4f,.3f));
                    Picture(Node(root,"ENVIRONMENT/R2_DECORATION"),"ZoneBoundaryRail","Tiles_5",2,3,.25f,6,Color.white,1); Picture(Node(puzzles,"RatHackZone_Left/R2_ROLE_VISUAL"),"TerminalMarker","Switch-Sheet_0",1.5f,2,.5f,.5f,Color.white,1);
                }
                else
                {
                    Platform(Node(root,"TERRAIN/R2_APPROACHES"),"EntryBridge_3p5",-5,-1.5f,0); var env=Node(root,"ENVIRONMENT/R2_SEAM_FRAME"); Picture(env,"EntrySeamRail_VISUAL_ONLY","Tiles_5",-1.5f,4,.18f,8,new Color(.55f,.65f,.75f),-1);
                }
                Save(sc,i); var reopened=Open(i); Validate(reopened,i,true); VerifyPreserved(before,Root(reopened),i); UpdateIdentity(i);
            }
        }); }
        static void RecordExistingIdentity(Scene scene,int i)
        {
            var ids=Read<Identity>("OBJECT_IDENTITY_MAP.json"); if(ids.scenes.Any(x=>x.scene==PathFor(i))) return;
            var entry=new IdentityScene{scene=PathFor(i),sourceScene=PathFor(i),sourceGuid=AssetDatabase.AssetPathToGUID(PathFor(i)),targetGuid=AssetDatabase.AssetPathToGUID(PathFor(i)),stage="R2-04_existing_same_identity"};
            foreach(var t in Root(scene).GetComponentsInChildren<Transform>(true)) { var r=Record(t); entry.pairs.Add(new IdentityPair{source=r,target=r}); } ids.scenes.Add(entry); Write("OBJECT_IDENTITY_MAP.json",ids);
        }
        static bool Close(float[] a,float[] b) { return a.Length==b.Length && a.Zip(b,(x,y)=>Mathf.Abs(x-y)<=.0001f).All(x=>x); }
        static void VerifyPreserved(ObjectRec[] before,Transform root,int map)
        {
            var after=root.GetComponentsInChildren<Transform>(true).Select(Record).ToDictionary(x=>x.id);
            foreach(var b in before) { Need(after.ContainsKey(b.id),"Existing object ID lost: "+b.path); var a=after[b.id]; Need(a.name==b.name && a.transformId==b.transformId && a.componentIds.SequenceEqual(b.componentIds),"Existing component identity changed"); Need(a.sprite==b.sprite && a.material==b.material,"Asset reference changed");
                bool locked=map==5 && (b.path.Contains("/BlueFixedTerrain/") || b.path.Contains("/GreenGroup/") || b.path.Contains("/RedGroup/") || b.path.Contains("/LowerControls/") || b.path.Contains("/HackingConsoleSlots/") || b.path.Contains("/PurpleGate_Visual"));
                bool collider=b.componentTypes.Contains(typeof(BoxCollider2D).FullName); bool marker=b.componentTypes.Length==1 && !b.path.Contains("BACKGROUND");
                if(locked || collider || marker) Need(Close(a.worldMatrix,b.worldMatrix),"Locked world pose changed: "+b.path); if(locked) Need(Close(a.tint,b.tint) && Close(a.spriteSize,b.spriteSize),"Locked tint/size changed"); if(collider) Need(Close(a.colliderSize,b.colliderSize),"Existing collider dimensions changed");
                if(!Close(a.worldMatrix,b.worldMatrix) || a.activeSelf!=b.activeSelf) LogChange(root.gameObject.scene.path,"APPROVED_DISPLAY_DELTA","Existing scene restricted visual/pivot/background refinement",b,a);
            }
        }
        static void UpdateIdentity(int i)
        {
            var ids=Read<Identity>("OBJECT_IDENTITY_MAP.json"); var entry=ids.scenes.Single(x=>x.scene==PathFor(i)); var root=Root(SceneManager.GetActiveScene()); var objects=root.GetComponentsInChildren<Transform>(true).ToDictionary(t=>Id(t.gameObject));
            foreach(var pair in entry.pairs) { Need(objects.ContainsKey(pair.target.id),"Identity target missing"); pair.target=Record(objects[pair.target.id]); } Write("OBJECT_IDENTITY_MAP.json",ids);
        }
        static void Reparent(Transform item,Transform parent,List<Change> output)
        {
            if(item==null || item.parent==parent) return; var nodes=item.GetComponentsInChildren<Transform>(true); var before=nodes.Select(Record).ToArray(); item.SetParent(parent,true);
            for(int j=0;j<nodes.Length;j++) { var a=Record(nodes[j]); var b=before[j]; Need(Close(a.worldMatrix,b.worldMatrix) && Close(a.rendererBounds,b.rendererBounds) && Close(a.colliderBounds,b.colliderBounds),"Reparent pose/bounds drift: "+b.path); Need(a.activeSelf==b.activeSelf && a.activeHierarchy==b.activeHierarchy && a.rendererEnabled==b.rendererEnabled && a.colliderEnabled==b.colliderEnabled && a.sprite==b.sprite && a.material==b.material,"Reparent active/reference drift"); output.Add(new Change{scene=item.gameObject.scene.path,stage="R2-05",kind="PURE_REPARENT",reason="SetParent(true), world/bounds/active/component/reference preserved within1e-4",before=b,after=a}); }
        }
        static void Organize(Transform root)
        {
            var changes=new List<Change>(); var map=Node(root,"00_MAP_EDIT"); var view=Node(root,"01_SCENE_VIEW"); var dev=Node(root,"90_DEV_HANDOFF_NOT_FOR_MAP_EDIT");
            Reparent(root.Find("VIEW"),view,changes); Reparent(root.Find("ENVIRONMENT"),map,changes); Reparent(root.Find("TERRAIN"),map,changes); Node(map,"PLATFORMS"); Node(map,"DECORATION");
            var elevators=Node(map,"ELEVATORS_VISUAL"); Reparent(root.Find("ELEVATOR_ENTRY_VISUAL"),elevators,changes); var exit=root.Find("ELEVATOR_EXIT_VISUAL"); bool pending=exit!=null && exit.Find("VisualReference_INACTIVE")!=null;
            Reparent(exit,pending?Node(dev,"PROPOSED_REFERENCE_MARKERS"):elevators,changes); Reparent(root.Find("PUZZLE_AREAS"),Node(map,"PUZZLE_VISUALS"),changes);
            Reparent(root.Find("PLAYER_SPAWN_MARKERS"),dev,changes); Reparent(root.Find("TEAM_INTEGRATION_REQUIRED"),dev,changes); Reparent(root.Find("PROPOSED_PLACEHOLDERS"),Node(dev,"PROPOSED_REFERENCE_MARKERS"),changes); Node(dev,"PENDING_CONNECTIONS"); Node(dev,"PUZZLE_LOGIC_ANCHORS");
            var areas=map.Find("PUZZLE_VISUALS/PUZZLE_AREAS");
            foreach(var area in areas.Cast<Transform>().ToArray())
            {
                var stone=area.Find("TEAM_MARKERS/ThrowStone_101"); if(stone!=null) Reparent(stone,Node(area,"DEVICE_VISUALS"),changes);
                var team=area.Find("TEAM_MARKERS"); if(team!=null) Reparent(team,Node(area,"90_DEV_LOCAL_ANCHORS"),changes);
                var draft=area.Find("C01_DRAFT"); if(draft!=null) { Need(!draft.gameObject.activeSelf,"Draft active"); Reparent(draft,Node(dev,"ARCHIVED_C01_DRAFT/"+area.name),changes); }
                var state=area.Find("STATE_REFERENCE_MARKERS"); if(state!=null) Reparent(state,Node(area,"90_DEV_LOCAL_ANCHORS"),changes);
                var battery=area.Find("BatteryTarget"); if(battery!=null) Reparent(battery,Node(area,"90_DEV_LOCAL_ANCHORS"),changes);
                // Preserve the empty groups from the interrupted first R2-05 attempt outside counted visual groups.
                if(new[]{"LowerControls","HackingConsoleSlots","PurpleGate_Visual","RightReturnRoute_Candidate"}.Contains(area.name))
                    foreach(var group in new[]{"DEVICE_VISUALS","90_DEV_LOCAL_ANCHORS"}) { var extra=area.Find(group); if(extra!=null) { Need(extra.childCount==0 && extra.GetComponents<Component>().Length==1,"Unexpected content in owned empty group"); Reparent(extra,Node(dev,"R2_EMPTY_GROUPS_PRESERVED/"+area.name),changes); } }
            }
            Reparent(areas.Find("RightReturnRoute_Candidate"),Node(dev,"PENDING_CONNECTIONS"),changes);
            foreach(var t in map.GetComponentsInChildren<Transform>(true).ToArray())
            {
                if(t.parent==null || FullPath(t).Contains("/90_DEV_LOCAL_ANCHORS/") || t.name.StartsWith("90_DEV") || t.childCount!=0 || t.GetComponents<Component>().Length!=1) continue;
                bool marker=t.name=="FloorTop" || t.name=="Anchor_FloorTop" || t.name=="Arrival_Marker" || t.name=="Departure_Marker" || t.name.Contains("_SLOT") || t.name.Contains("_PENDING") || t.name.Contains("_REFERENCE") || t.name.Contains("_ROLE") || t.name.Contains("_TEAM_REVIEW") || t.name.StartsWith("Route_") || t.name.StartsWith("DropCenter_") || t.name.StartsWith("Landing_") || t.name.StartsWith("ALTERNATIVE_") || t.name.StartsWith("SOURCE_");
                if(marker) Reparent(t,Node(t.parent,"90_DEV_LOCAL_ANCHORS"),changes);
            }
            var log=Read<Changes>("HIERARCHY_CHANGELOG.json"); log.moves.AddRange(changes); Write("HIERARCHY_CHANGELOG.json",log);
        }
        [MenuItem("Tools/C-IMPL-R2/05 Organize six Hierarchies",false,205)]
        public static void Hierarchies() { Step(5,()=> { BackupHelper("R25"); for(int i=0;i<6;i++) { var sc=Open(i); Organize(Root(sc)); Save(sc,i); Validate(Open(i),i,true); UpdateIdentity(i); } }); }
        static Transform FindAny(Transform root,string name) { return root.GetComponentsInChildren<Transform>(true).SingleOrDefault(t=>t.name==name && !FullPath(t).Contains("/ARCHIVED_C01_DRAFT/") && !FullPath(t).Contains("/R2_EMPTY_GROUPS_PRESERVED/")); }
        static void Gap(Transform root,Transform area,float left,float right,float floor)
        {
            foreach(var bc in root.GetComponentsInChildren<BoxCollider2D>(false)) { var min=area.InverseTransformPoint(bc.bounds.min); var max=area.InverseTransformPoint(bc.bounds.max); bool crosses=min.y<floor+.02f && max.y>floor-.02f; Need(!crosses || max.x<=left+.001f || min.x>=right-.001f,"Designed puzzle gap filled: "+FullPath(bc.transform)); }
        }
        static void Validate(Scene scene,int map,bool detailed)
        {
            var root=Root(scene); var nodes=root.GetComponentsInChildren<Transform>(true); var permitted=new HashSet<Type>{typeof(Transform),typeof(SpriteRenderer),typeof(Camera),typeof(UniversalAdditionalCameraData),typeof(Light2D),typeof(BoxCollider2D),typeof(PlatformEffector2D)};
            foreach(var t in nodes) { Need(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)==0 && !PrefabUtility.IsPartOfPrefabInstance(t),"Missing script/prefab"); foreach(var c in t.GetComponents<Component>()) Need(c!=null && permitted.Contains(c.GetType()),"Forbidden component"); }
            foreach(var sr in root.GetComponentsInChildren<SpriteRenderer>(true)) Need(sr.sprite!=null && Sprites.Values.Contains(sr.sprite) && sr.sharedMaterial==material && sr.sortingLayerID==0 && sr.sortingOrder>=-2 && sr.sortingOrder<=2,"Sprite/material/sorting");
            foreach(var bc in root.GetComponentsInChildren<BoxCollider2D>(true)) Need(!bc.isTrigger && bc.gameObject.layer==6 && Close(V(bc.transform.lossyScale),new[]{1f,1f,1f}),"Collider layer/scale/trigger");
            foreach(var eff in root.GetComponentsInChildren<PlatformEffector2D>(true)) Need(map==0 && eff.name=="Collider_Layout" && FullPath(eff.transform).Contains("UpperCommon_OneWay_PROPOSED") && eff.useOneWay && eff.surfaceArc==180,"Effector outside approved upper corridor");
            foreach(var comp in nodes.SelectMany(t=>t.GetComponents<Component>())) { var so=new SerializedObject(comp); var prop=so.GetIterator(); while(prop.Next(true)) if(prop.propertyType==SerializedPropertyType.ObjectReference && prop.objectReferenceValue!=null) { var obj=prop.objectReferenceValue; var go=obj as GameObject; if(obj is Component component) go=component.gameObject; if(go!=null && go.scene.IsValid()) Need(go.scene==scene,"Cross-scene object reference"); } }
            if(detailed)
            {
                if(map<2) { var rat=FindAny(root,"RAT"); for(int j=1;j<=3;j++) Need(FindAny(rat,"HackTerminal_"+j.ToString("00"))!=null,"Rat terminals"); var bear=FindAny(root,"BEAR"); Need(FindAny(bear,"RightFloorActuator_VISUAL_ONLY")!=null,"Bear actuator"); Gap(root,bear,4,7,0); }
                if(map==1) { for(int k=0;k<4;k++) foreach(var bc in root.GetComponentsInChildren<BoxCollider2D>(false)) { var b=bc.bounds; float l=28*k+19,r=28*k+22,top=k==1?14:17.2f; Need(!(b.max.x>l+.001f && b.min.x<r-.001f && b.max.y>.5f && b.min.y<top-.01f),"Drop shaft blocked by "+FullPath(bc.transform)); } }
                if(map==2) { var area=FindAny(root,"PZ02"); Need(FindAny(root,"PZ01")==null && FindAny(root,"SeparatedPads").childCount==4,"SourceA selection/pads"); Gap(root,area,4.8f,12.2f,0); }
                if(map==3) { Need(FindAny(root,"PZ02")==null && FindAny(root,"SwitchSlots").childCount==3 && FindAny(root,"PartitionSlots").childCount==3,"SourceB selection"); Need(FindAny(root,"ThrowStone_101").GetComponent<SpriteRenderer>().sprite.name=="objects_4","Stone101"); }
                if(map==4) Need(FindAny(root,"Route_START_RIGHT").position.x==27 && FindAny(root,"Route_END_LEFT").position.x==4,"Hod direction");
                if(map==5) { foreach(var pair in new[]{new KeyValuePair<string,int>("BlueFixedTerrain",10),new KeyValuePair<string,int>("GreenGroup",6),new KeyValuePair<string,int>("RedGroup",3),new KeyValuePair<string,int>("LowerControls",4),new KeyValuePair<string,int>("HackingConsoleSlots",2)}) Need(FindAny(root,pair.Key).childCount==pair.Value,"Netzach count "+pair.Key); var pending=FindAny(root,"VisualReference_INACTIVE"); Need(!pending.gameObject.activeSelf && pending.GetComponentsInChildren<Collider2D>(true).Length==0,"Pending exit changed"); }
            }
            var qa=Read<QA>("QA_RESULTS.json"); qa.scenes.RemoveAll(s=>s.path==scene.path); qa.scenes.Add(new SceneReport{path=scene.path,guid=AssetDatabase.AssetPathToGUID(scene.path),sha256=Hash(scene.path),checkedAt=Now(),gameObjects=nodes.Length,activeRenderers=root.GetComponentsInChildren<SpriteRenderer>(false).Count(r=>r.enabled),activeColliders=root.GetComponentsInChildren<BoxCollider2D>(false).Count(c=>c.enabled),effectors=root.GetComponentsInChildren<PlatformEffector2D>(true).Length,reopened=true,passed=true,objects=nodes.Select(Record).ToArray(),checks=new[]{"Component/reference/layer/sorting/scale checks","Selective puzzle/required counts/designed gaps","Edit data only; Play not run"}}); Write("QA_RESULTS.json",qa);
        }
        [MenuItem("Tools/C-IMPL-R2/06 Data QA and pre-QA backups",false,206)]
        public static void DataQA() { Step(6,()=> { BackupHelper("R26"); string[] b={"MAP00A","MAP00B","YESODA","YESODB","MAP02","MAP03"}; for(int i=0;i<6;i++) { string to=Run+"/backups/QA_"+b[i]+".unity"; if(!File.Exists(to)) File.Copy(PathFor(i),to,false); Validate(Open(i),i,true); UpdateIdentity(i); } },true); }
        [MenuItem("Tools/C-IMPL-R2/07 C06 background continuity polish",false,207)]
        public static void PolishBackgrounds()
        {
            Guard(); Protection(); Need(Read<State>("STATE.json").phases[6].status=="IN_PROGRESS","C06 pre-QA backups required"); References();
            foreach(var n in new[]{"MAP00A","MAP00B","YESODA","YESODB","MAP02","MAP03"}) Need(File.Exists(Run+"/backups/QA_"+n+".unity"),"Missing QA backup");
            for(int i=0;i<6;i++)
            {
                var sc=Open(i); var root=Root(sc); DisableOldBackgrounds(root); var env=Node(FindAny(root,"ENVIRONMENT"),"R2_FINAL_BACKGROUND");
                if(i==0)
                {
                    float h=44*Sprites["stage1-1_0"].bounds.size.y/Sprites["stage1-1_0"].bounds.size.x;
                    for(int k=0;k<5;k++) { Background(env,"Band_"+k,6+44*k,10,44); if(k>0) Picture(env,"SeamRail_"+k,"Tiles_5",-16+44*k,10,.18f,h,new Color(.5f,.6f,.7f),-1,false,2); }
                }
                else if(i==1)
                {
                    float h=28*Sprites["stage1-1_0"].bounds.size.y/Sprites["stage1-1_0"].bounds.size.x, join=18.5f-h/2;
                    for(int k=0;k<6;k++) { float x=-20+28*k; Background(env,"UpperBand_"+k,x,18.5f,28); Background(env,"LowerBand_"+k,x,join-h/2,28); if(k>0) Picture(env,"BandSeam_"+k,"Tiles_5",x-14,join,.15f,2*h,new Color(.5f,.6f,.7f),-1,false,2); }
                    Picture(env,"UpperLowerArchitecturalSeam","Tiles_0",50,join,168,.18f,new Color(.5f,.6f,.7f),-1,true,2);
                }
                else if(i<4) Background(env,"PuzzleAndBays",13,4.5f,58);
                else if(i==4) Background(env,"RoleHallAndBays",6.5f,4.3f,56);
                else Background(env,"PuzzleAndEntry",3,3,40);
                Save(sc,i); Validate(Open(i),i,true); UpdateIdentity(i);
            }
            Protection(); InventoryUpdate(); var s=Read<State>("STATE.json"); s.sourceDeviations=s.sourceDeviations.Concat(new[]{"C06 actual screen observation found black entry background in Malkuth and mismatched backdrop rectangles in Yesod. Previous generated backgrounds preserved inactive; uniform-aspect aligned bands plus non-collision architectural seams in Malkuth, one proportional background for each compact map. Geometry/neutral poses unchanged."}).Distinct().ToArray(); WriteState(s); Debug.Log("R2 C06 BACKGROUND_POLISH saved/reopened; actual UI capture review pending");
        }
        [MenuItem("Tools/C-IMPL-R2/08 C06 Netzach visual bridge joints",false,208)]
        public static void BridgeJoints()
        {
            Guard(); Need(Read<State>("STATE.json").phases[6].status=="IN_PROGRESS" && File.Exists(Run+"/backups/QA_MAP03.unity"),"C06 backup required"); References(); var sc=Open(5); var root=Root(sc); var bridge=FindAny(root,"EntryBridge_3p5");
            Block(bridge,"LeftArtJoint_NO_COLLIDER",-1.75f,-.275f,.8f,.55f,Color.white);
            Block(bridge,"RightArtJoint_NO_COLLIDER",1.75f,-.275f,.8f,.55f,new Color(.30f,.65f,1));
            Save(sc,5); Validate(Open(5),5,true); UpdateIdentity(5); Protection(); var s=Read<State>("STATE.json"); s.sourceDeviations=s.sourceDeviations.Concat(new[]{"Netzach actual UI showed alpha-margin seams despite continuous collision at bridge endpoints. Two non-collision visual joints added to new bridge only; old BlueFixedTerrain pose/size/tint and bridge3.5x0.4 Collider unchanged."}).Distinct().ToArray(); WriteState(s); Debug.Log("R2 C06 NETZACH_ART_JOINTS saved/reopened; existing neutral pose unchanged");
        }
        static void Inspect(int i,string target,Bounds bounds)
        {
            Guard(); var sc=Open(i); var root=Root(sc); var t=string.IsNullOrEmpty(target)?root:(target.Contains("/")?root.Find(target):FindAny(root,target)); Need(t!=null,"Inspect target"); Selection.activeGameObject=t.gameObject; var v=SceneView.lastActiveSceneView; if(v==null) v=EditorWindow.GetWindow<SceneView>(); v.in2DMode=true; v.sceneLighting=true; v.Frame(bounds,true); v.Repaint(); Debug.Log("R2 INSPECT Edit "+Names[i]+" / "+target);
        }
        [MenuItem("Tools/C-IMPL-R2/Inspect/01 MAP00A overview")] public static void I01(){Inspect(0,"00_MAP_EDIT",new Bounds(new Vector3(92,6,0),new Vector3(210,40,1)));}
        [MenuItem("Tools/C-IMPL-R2/Inspect/02 MAP00B overview")] public static void I02(){Inspect(1,"00_MAP_EDIT",new Bounds(new Vector3(55,11,0),new Vector3(140,34,1)));}
        [MenuItem("Tools/C-IMPL-R2/Inspect/03 YesodA overview")] public static void I03(){Inspect(2,"00_MAP_EDIT",new Bounds(new Vector3(13,4,0),new Vector3(53,22,1)));}
        [MenuItem("Tools/C-IMPL-R2/Inspect/04 YesodB overview")] public static void I04(){Inspect(3,"00_MAP_EDIT",new Bounds(new Vector3(13,4,0),new Vector3(53,22,1)));}
        [MenuItem("Tools/C-IMPL-R2/Inspect/05 Hod overview")] public static void I05(){Inspect(4,"00_MAP_EDIT",new Bounds(new Vector3(6.5f,3,0),new Vector3(50,20,1)));}
        [MenuItem("Tools/C-IMPL-R2/Inspect/06 Netzach overview")] public static void I06(){Inspect(5,"00_MAP_EDIT",new Bounds(new Vector3(4,3,0),new Vector3(36,18,1)));}
        [MenuItem("Tools/C-IMPL-R2/Inspect/07 MAP00A ascent detail")] public static void I07(){Inspect(0,"00_MAP_EDIT/TERRAIN/R2_GATHERING_PROPOSED/UpperCommon_OneWay_PROPOSED/Collider_Layout",new Bounds(new Vector3(34,8,0),new Vector3(48,24,1)));}
        [MenuItem("Tools/C-IMPL-R2/Inspect/08 MAP00B drop detail")] public static void I08(){Inspect(1,"LowerCommonGathering_PROPOSED",new Bounds(new Vector3(55,11,0),new Vector3(135,32,1)));}
        [MenuItem("Tools/C-IMPL-R2/Inspect/09 YesodA pads detail")] public static void I09(){Inspect(2,"00_MAP_EDIT/PUZZLE_VISUALS/PUZZLE_AREAS/PZ02/SeparatedPads/Pad_203_2_SINKING_UNIMPLEMENTED/Visual",new Bounds(new Vector3(9,1,0),new Vector3(20,10,1)));}
        [MenuItem("Tools/C-IMPL-R2/Inspect/10 YesodB stone detail")] public static void I10(){Inspect(3,"ThrowStone_101",new Bounds(new Vector3(4,2,0),new Vector3(13,8,1)));}
        [MenuItem("Tools/C-IMPL-R2/Inspect/11 Hod entry roles")] public static void I11(){Inspect(4,"PUZZLE_AREAS",new Bounds(new Vector3(7,3,0),new Vector3(48,18,1)));}
        [MenuItem("Tools/C-IMPL-R2/Inspect/12 Netzach entry bridge")] public static void I12(){Inspect(5,"EntryBridge_3p5",new Bounds(new Vector3(-3,1,0),new Vector3(21,12,1)));}
        [MenuItem("Tools/C-IMPL-R2/Inspect/13 Hierarchy R2")] public static void I13(){Inspect(3,"PZ01",new Bounds(new Vector3(13,4,0),new Vector3(44,16,1)));}
        [MenuItem("Tools/C-IMPL-R2/Inspect/14 Collider pair")] public static void I14(){Inspect(5,"00_MAP_EDIT/TERRAIN/R2_APPROACHES/EntryBridge_3p5/Collider_Layout",new Bounds(new Vector3(-3,0,0),new Vector3(12,8,1)));}
    }
}

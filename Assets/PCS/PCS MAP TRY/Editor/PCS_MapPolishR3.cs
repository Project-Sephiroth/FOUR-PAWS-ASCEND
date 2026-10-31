using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace PCS.MapPlanning.R3.EditorTools
{
    // Explicit commands only. Existing builders, runtime behaviours and shared assets are never saved.
    public static class PCS_MapPolishR3
    {
        public const string Run = "C:/FOUR-PAWS-PCS-BRIEF/CPLAN_POLISH_R3_20261031/CIMPL_R3/run-001";
        const string Project = "C:/FOUR-PAWS-ASCEND-NEWPCS";
        const string Base = "Assets/PCS/PCS MAP TRY/";
        const string Art = Base + "Art/R3/";
        const string Helper = Base + "Editor/PCS_MapPolishR3.cs";
        static readonly string[] Names = { "PCS_MAP00_Malkuth_ONE MORE VERSION", "PCS_MAP01_Yesod_SourceA_Edit", "PCS_MAP01_Yesod_SourceB_Edit", "PCS_MAP02_Hod_Blockout", "PCS_MAP03_Netzach_Blockout" };
        static readonly string[] Rooms = { "RABBIT", "RAT", "FROG", "BEAR" };
        static Material mat;
        static SceneView reviewView;
        static R3ArtReview artView;
        static int reviewIndex=2;
        [MenuItem("Tools/C-IMPL-R3/Next Review")] public static void NextReview(){reviewIndex=(reviewIndex+1)%18;Inspect(reviewIndex);}
        [MenuItem("Tools/C-IMPL-R3/Restore All ON")] public static void RestoreOn(){reviewIndex=0;Inspect(0);}
        [Serializable] public class FileRec { public string path, sha256; public bool editable; }
        [Serializable] public class Baseline { public FileRec[] records; }
        [Serializable] public class State { public string run; public string[] allowedProjectFiles; }
        [Serializable] public class ObjRec
        {
            public string id, transformId, path, name, parent, sprite, material, drawMode;
            public string[] componentIds, componentTypes;
            public float[] matrix, position, scale, rendererBounds, colliderBounds, tint, size, box;
            public bool active, activeHierarchy, rendererEnabled, colliderEnabled;
            public int layer, order;
        }
        [Serializable] public class Pair { public string scene; public ObjRec[] before, after; public string[] added, changes; }
        [Serializable] public class Identity { public List<Pair> scenes = new List<Pair>(); }
        [Serializable] public class SceneQA { public string path, guid, sha256; public int objects, renderers, colliders, missingScripts, missingSprites; public bool reopened; public ObjRec[] rows; }
        [Serializable] public class ToggleQA { public string mode; public bool colliderAndMarkersEqual; public int activeOptional; }
        [Serializable] public class QA
        {
            public List<string> completed = new List<string>(); public List<SceneQA> scenes = new List<SceneQA>();
            public List<ToggleQA> toggles = new List<ToggleQA>(); public List<string> artChecks = new List<string>();
            public string visualStatus = "VISUAL_QA_PENDING", play = "NOT_TESTED", build = "NOT_TESTED", network = "NOT_TESTED";
        }
        static T Read<T>(string n) { return JsonUtility.FromJson<T>(File.ReadAllText(Run + "/" + n)); }
        static void Write(string n, object o) { File.WriteAllText(Run + "/" + n, JsonUtility.ToJson(o, true), new UTF8Encoding(false)); }
        static string Hash(string p) { using (var f = File.OpenRead(p)) using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(f)).Replace("-", "").ToLowerInvariant(); }
        static void Need(bool b, string why) { if (!b) throw new InvalidOperationException("R3 GUARD: " + why); }
        static string PathFor(int i) { return Base + "Scenes/" + Names[i] + ".unity"; }
        static QA GetQA() { return File.Exists(Run + "/QA_RESULTS.json") ? Read<QA>("QA_RESULTS.json") : new QA(); }
        static void Guard()
        {
            Need(Path.GetFullPath(Application.dataPath + "/..").Replace('\\','/') == Project, "Wrong project");
            Need(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling, "Stable Edit mode required");
            for (int i=0;i<SceneManager.sceneCount;i++) Need(!SceneManager.GetSceneAt(i).isDirty, "Unsaved user scene: " + SceneManager.GetSceneAt(i).path);
            var s=Read<State>("STATE.json"); Need(s.run=="CIMPL_R3/run-001", "Run identity");
            if(File.Exists(Run+"/CREATED_ASSETS.json"))foreach(var f in Read<Baseline>("CREATED_ASSETS.json").records.Where(f=>!f.editable))Need(File.Exists(f.path)&&Hash(f.path)==f.sha256,"Owned asset changed after handoff: "+f.path);
            var baseline=Read<Baseline>("PROTECTED_BASELINE.json");
            foreach(var f in baseline.records.Where(x=>!x.editable)) Need(File.Exists(f.path) && Hash(f.path)==f.sha256, "Protected file changed: "+f.path);
            var q=GetQA();
            foreach(var f in baseline.records.Where(x=>x.editable))
            {
                var current=q.scenes.SingleOrDefault(x=>Path.GetFullPath(x.path)==Path.GetFullPath(f.path));
                Need(Hash(f.path)==(current==null?f.sha256:current.sha256),"User/unknown edit: "+f.path);
            }
            var known=new HashSet<string>(baseline.records.Select(x=>Path.GetFullPath(x.path)),StringComparer.OrdinalIgnoreCase);
            var allowed=new HashSet<string>(s.allowedProjectFiles.Select(Path.GetFullPath),StringComparer.OrdinalIgnoreCase);
            foreach(var d in new[]{"Assets","Packages","ProjectSettings"}) foreach(var p in Directory.GetFiles(d,"*",SearchOption.AllDirectories))
                Need(known.Contains(Path.GetFullPath(p)) || allowed.Contains(Path.GetFullPath(p)),"Unexpected project file: "+p);
            mat=AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath("a97c105638bdf8b4a8650670310a4cd3")); Need(mat!=null,"Existing Sprite material missing");
        }
        static string Id(Object o) { return o==null?"MISSING":GlobalObjectId.GetGlobalObjectIdSlow(o).ToString(); }
        static string AId(Object o) { if(o==null)return ""; AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o,out string g,out long id);return g+":"+id; }
        static string Full(Transform t) { return t.parent==null?t.name:Full(t.parent)+"/"+t.name; }
        static float[] V(Vector3 v) { return new[]{v.x,v.y,v.z}; }
        static float[] B(Bounds b) { return new[]{b.center.x,b.center.y,b.center.z,b.size.x,b.size.y,b.size.z}; }
        static ObjRec Record(Transform t)
        {
            var s=t.GetComponent<SpriteRenderer>();var c=t.GetComponent<BoxCollider2D>();var m=t.localToWorldMatrix;var cs=t.GetComponents<Component>();
            return new ObjRec{id=Id(t.gameObject),transformId=Id(t),path=Full(t),name=t.name,parent=t.parent==null?"":Id(t.parent.gameObject),matrix=Enumerable.Range(0,16).Select(i=>m[i]).ToArray(),position=V(t.position),scale=V(t.lossyScale),active=t.gameObject.activeSelf,activeHierarchy=t.gameObject.activeInHierarchy,layer=t.gameObject.layer,componentIds=cs.Select(Id).ToArray(),componentTypes=cs.Select(x=>x==null?"MISSING":x.GetType().FullName).ToArray(),sprite=s==null?"":AId(s.sprite),material=s==null?"":AId(s.sharedMaterial),drawMode=s==null?"":s.drawMode.ToString(),rendererEnabled=s!=null&&s.enabled,colliderEnabled=c!=null&&c.enabled,rendererBounds=s==null?new float[0]:B(s.bounds),colliderBounds=c==null?new float[0]:B(c.bounds),tint=s==null?new float[0]:new[]{s.color.r,s.color.g,s.color.b,s.color.a},size=s==null?new float[0]:new[]{s.size.x,s.size.y},box=c==null?new float[0]:new[]{c.size.x,c.size.y,c.offset.x,c.offset.y},order=s==null?0:s.sortingOrder};
        }
        static ObjRec[] Snapshot(Scene s) { return s.GetRootGameObjects().SelectMany(x=>x.GetComponentsInChildren<Transform>(true)).Select(Record).ToArray(); }
        static Transform Root(Scene s) { Need(s.GetRootGameObjects().Length==1,"Single scene root required");return s.GetRootGameObjects()[0].transform; }
        static Transform Find(Transform r,string suffix) { var matches=r.GetComponentsInChildren<Transform>(true).Where(x=>Full(x).EndsWith("/"+suffix,StringComparison.Ordinal)).ToArray(); Need(matches.Length==1,"Unique path required: "+suffix+" count="+matches.Length);return matches[0]; }
        static Transform Node(Transform p,string path)
        {
            foreach(var part in path.Split('/')) { var t=p.Find(part);if(t==null){t=new GameObject(part).transform;t.SetParent(p,false);}p=t; }return p;
        }
        static Transform Fresh(Transform p,string name)
        {
            Need(p.Find(name)==null,"Existing node must not be overwritten: "+Full(p)+"/"+name);var t=new GameObject(name).transform;t.SetParent(p,false);return t;
        }
        static Sprite Sprite(string path,string name=null) { var all=AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>();var s=name==null?all.SingleOrDefault():all.SingleOrDefault(x=>x.name==name);Need(s!=null,"Sprite missing "+path+" "+name);return s; }
        static Sprite Module(string name) { return Sprite(Art+"SpriteSheets/MetalModules_R3.png",name); }
        static SpriteRenderer ArtRect(Transform p,string name,Sprite sprite,Bounds bounds,Color tint,int order,bool sliced=false)
        {
            var t=Fresh(p,name);t.position=bounds.center;t.rotation=Quaternion.identity;
            var lossy=p.lossyScale;t.localScale=new Vector3(1/lossy.x,1/lossy.y,1/lossy.z);
            var sr=t.gameObject.AddComponent<SpriteRenderer>();sr.sprite=sprite;sr.sharedMaterial=mat;sr.color=tint;sr.sortingOrder=order;
            sr.drawMode=sliced?SpriteDrawMode.Sliced:SpriteDrawMode.Tiled;sr.size=new Vector2(bounds.size.x,bounds.size.y);
            // Correct non-centred pivots without altering any shared Sprite importer.
            var pivot=sprite.pivot;var rect=sprite.rect;var offset=new Vector3((.5f-pivot.x/rect.width)*sr.size.x,(.5f-pivot.y/rect.height)*sr.size.y,0);
            t.position-=offset;return sr;
        }
        static Bounds Rect(float left,float bottom,float right,float top,float z=0) { return new Bounds(new Vector3((left+right)/2,(bottom+top)/2,z),new Vector3(right-left,top-bottom,0)); }
        static Transform StaticRect(Transform p,string name,float left,float bottom,float right,float top)
        {
            var t=Fresh(p,name);t.position=new Vector3((left+right)/2,(bottom+top)/2,0);
            var c=Fresh(t,"STATIC_COLLIDER").gameObject.AddComponent<BoxCollider2D>();c.gameObject.layer=6;c.size=new Vector2(right-left,top-bottom);
            ArtRect(t,"ART_VISUAL",Module(right-left>top-bottom?"Ceiling":"Wall"),Rect(left,bottom,right,top),Color.white,-1);
            Fresh(t,"DEV_MARKER");return t;
        }
        static void Import(string path,bool multiple=false,Vector2? pivot=null)
        {
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);var ti=(TextureImporter)AssetImporter.GetAtPath(path);Need(ti!=null,"New texture importer");
            ti.textureType=TextureImporterType.Sprite;ti.spriteImportMode=multiple?SpriteImportMode.Multiple:SpriteImportMode.Single;ti.spritePixelsPerUnit=32;ti.filterMode=FilterMode.Point;ti.mipmapEnabled=false;ti.textureCompression=TextureImporterCompression.Uncompressed;ti.alphaIsTransparency=true;ti.sRGBTexture=true;ti.npotScale=TextureImporterNPOTScale.None;
            var settings=new TextureImporterSettings();ti.ReadTextureSettings(settings);settings.spriteMeshType=SpriteMeshType.FullRect;settings.spriteAlignment=(int)SpriteAlignment.Custom;settings.spritePivot=pivot??new Vector2(.5f,.5f);ti.SetTextureSettings(settings);ti.SaveAndReimport();
            if(multiple)
            {
                var fac=new SpriteDataProviderFactories();fac.Init();var dp=fac.GetSpriteEditorDataProviderFromObject(ti);dp.InitSpriteEditorDataProvider();
                string[] names={"Wall","Ceiling","Corner","Column","Cap","Plate","DoorFrame"};var rects=new[]{new Rect(0,0,16,32),new Rect(32,0,32,16),new Rect(64,0,16,16),new Rect(96,0,8,32),new Rect(112,0,16,16),new Rect(128,0,32,32),new Rect(160,0,64,96)};
                var existing=dp.GetSpriteRects();var rs=names.Select((n,i)=>new SpriteRect{name=n,rect=rects[i],alignment=SpriteAlignment.Center,pivot=new Vector2(.5f,.5f),border=n=="DoorFrame"?new Vector4(8,8,8,8):Vector4.zero,spriteID=existing.SingleOrDefault(x=>x.name==n)?.spriteID??GUID.Generate()}).ToArray();
                dp.SetSpriteRects(rs);dp.GetDataProvider<ISpriteNameFileIdDataProvider>().SetNameFileIdPairs(rs.Select(x=>new SpriteNameFileIdPair(x.name,x.spriteID)));dp.Apply();ti.SaveAndReimport();
            }
        }
        static void Stage(string name,Action action)
        {
            Guard();var q=GetQA();if(q.completed.Contains(name)){Debug.Log("R3 verified skip "+name);return;}
            action();q=GetQA();q.completed.Add(name);Write("QA_RESULTS.json",q);
            File.Copy(Helper,Run+"/backups/PCS_MapPolishR3_"+name+".cs",false);
            string line=DateTimeOffset.Now.ToString("o")+" "+name+" PASS (data; visual QA separate)";
            File.AppendAllText(Run+"/UNITY_LOG_HISTORY.md","\n- "+line+"\n");File.AppendAllText(Run+"/UnityEditor.log",line+"\n");File.WriteAllText(Run+"/CHECKPOINT.md","# R3 체크포인트\n\n"+line+"\n",new UTF8Encoding(false));Debug.Log(line);
        }
        static void SaveQA(Scene s,int i,ObjRec[] before=null)
        {
            Need(s.path==PathFor(i),"Exact existing save path");Need(EditorSceneManager.SaveScene(s,s.path,false),"SaveScene false");var hash=Hash(s.path);s=EditorSceneManager.OpenScene(s.path,OpenSceneMode.Single);Need(Hash(s.path)==hash,"Reopen altered disk");
            var rows=Snapshot(s);var q=GetQA();q.scenes.RemoveAll(x=>x.path==PathFor(i));q.scenes.Add(new SceneQA{path=PathFor(i),guid=AssetDatabase.AssetPathToGUID(PathFor(i)),sha256=hash,objects=rows.Length,renderers=rows.Count(x=>x.rendererEnabled&&x.activeHierarchy),colliders=rows.Count(x=>x.colliderEnabled&&x.activeHierarchy),missingScripts=rows.Count(x=>x.componentTypes.Contains("MISSING")),missingSprites=rows.Count(x=>x.componentTypes.Contains(typeof(SpriteRenderer).FullName)&&x.sprite==""),reopened=true,rows=rows});Write("QA_RESULTS.json",q);
            Need(q.scenes.Last().missingScripts==0&&q.scenes.Last().missingSprites==0,"Missing component/reference");
            if(before!=null)
            {
                var identities=File.Exists(Run+"/OBJECT_IDENTITY_MAP.json")?Read<Identity>("OBJECT_IDENTITY_MAP.json"):new Identity();var after=rows.ToDictionary(x=>x.id);var changes=new List<string>();
                foreach(var b in before)
                {
                    Need(after.ContainsKey(b.id),"Original object deleted "+b.path);var a=after[b.id];Need(a.name==b.name&&a.transformId==b.transformId&&b.componentIds.All(a.componentIds.Contains),"Original identity/component changed "+b.path);
                    Need(a.sprite==b.sprite&&a.material==b.material&&a.tint.SequenceEqual(b.tint),"Original asset or tint changed "+b.path);
                    if(JsonUtility.ToJson(a)!=JsonUtility.ToJson(b))changes.Add(b.id+" "+b.path);
                }
                identities.scenes.Add(new Pair{scene=PathFor(i),before=before,after=rows,added=rows.Where(x=>!before.Any(b=>b.id==x.id)).Select(x=>x.id+" "+x.path).ToArray(),changes=changes.ToArray()});Write("OBJECT_IDENTITY_MAP.json",identities);Write("HIERARCHY_CHANGELOG.json",identities);
            }
        }
        static void Prefab(string name,Sprite sp,Vector2 size,bool sliced=false,bool unused=false)
        {
            string path=Art+(unused?"UNUSED_CANDIDATES/":"Prefabs/")+name+".prefab";Need(!File.Exists(path),"Existing prefab collision "+path);
            var go=new GameObject(name);try{ArtRect(go.transform,"ART_VISUAL",sp,new Bounds(Vector3.zero,new Vector3(size.x,size.y,0)),Color.white,-1,sliced);Fresh(go.transform,"STATIC_COLLIDER");Fresh(go.transform,"DEV_MARKER").gameObject.SetActive(false);Need(go.GetComponentsInChildren<Collider2D>(true).Length==0,"Visual prefab collider");Need(PrefabUtility.SaveAsPrefabAsset(go,path)!=null,"Prefab save failed");}finally{Object.DestroyImmediate(go);}
        }
        public static void Continuous()
        {
            Stage("R3-01",()=>{Import(Art+"SpriteSheets/MetalModules_R3.png",true);Import(Art+"Textures/EntryElevator_R3.png",false,new Vector2(.5f,0));Import(Art+"UNUSED_CANDIDATES/Drum_R3_UNUSED.png");Import(Art+"UNUSED_CANDIDATES/ThreatSign_R3_UNUSED.png");var q=GetQA();q.artChecks.Add("4 PNG imported: PPU32/Point/MipmapOff/Uncompressed/FullRect; seven atlas rects; DoorFrame border8; ShaftPanel NOT_CREATED");Write("QA_RESULTS.json",q);});
            Stage("R3-02",()=>{Prefab("WallRun_R3",Module("Wall"),new Vector2(.5f,4));Prefab("CeilingRun_R3",Module("Ceiling"),new Vector2(8,.5f));Prefab("Column_R3",Module("Column"),new Vector2(.25f,6));Prefab("DoorFrame_R3",Module("DoorFrame"),new Vector2(2,3),true);Prefab("EntryElevatorOptional_R3",Sprite(Art+"Textures/EntryElevator_R3.png"),new Vector2(4,4));});
            Stage("R3-03",()=>{var s=EditorSceneManager.OpenScene(PathFor(0));var before=Snapshot(s);var r=Root(s);Polish(r);Malkuth(r);SaveQA(s,0,before);});
            Stage("R3-04",()=>{for(int i=1;i<5;i++){Guard();var s=EditorSceneManager.OpenScene(PathFor(i));var before=Snapshot(s);Polish(Root(s));SaveQA(s,i,before);}});
            Stage("R3-05",()=>{Prefab("Drum_R3_UNUSED",Sprite(Art+"UNUSED_CANDIDATES/Drum_R3_UNUSED.png"),new Vector2(1.5f,2),false,true);Prefab("ThreatSign_R3_UNUSED",Sprite(Art+"UNUSED_CANDIDATES/ThreatSign_R3_UNUSED.png"),new Vector2(2,1),false,true);});
            Stage("R3-06",()=>{for(int i=0;i<5;i++){var s=EditorSceneManager.OpenScene(PathFor(i));var r=Root(s);foreach(var n in new[]{"00_MAP_EDIT","01_SCENE_VIEW","90_DEV_HANDOFF_NOT_FOR_MAP_EDIT"})Need(r.Find(n)!=null,"Hierarchy "+n);}});
            Stage("R3-07",()=>{for(int i=0;i<5;i++){var s=EditorSceneManager.OpenScene(PathFor(i));SaveQA(s,i);} ToggleChecks();});
            Inspect(0);Debug.Log("R3-01..07 saved/reopened data QA complete. Actual UI captures/manual pending.");
        }
        static void Polish(Transform r)
        {
            foreach(var sr in r.GetComponentsInChildren<SpriteRenderer>(true).Where(x=>x.enabled&&x.sprite!=null&&x.drawMode==SpriteDrawMode.Simple).ToArray())
            {
                var path=Full(sr.transform);if(path.Contains("R3_")||path.Contains("/ART_VISUAL"))continue;
                var scale=sr.transform.lossyScale;float min=Mathf.Min(Mathf.Abs(scale.x),Mathf.Abs(scale.y));float max=Mathf.Max(Mathf.Abs(scale.x),Mathf.Abs(scale.y));
                // Backgrounds, coloured puzzle units and intentionally small switches stay in their original poses.
                if(min<=0 || (max/min<2.99f && max<=2.5f) || sr.sprite.name.StartsWith("stage"))continue;
                if(!sr.sprite.name.StartsWith("Tiles_"))continue;
                var b=sr.bounds;string module=b.size.x>b.size.y?"Ceiling":"Wall";if(b.size.x<.3f)module="Column";
                ArtRect(sr.transform,"R3_ART_VISUAL",Module(module),b,sr.color,sr.sortingOrder);sr.enabled=false;
            }
        }
        static void UpdateOldBox(Transform r,string suffix,Bounds b)
        {
            var t=Find(r,suffix);var c=t.GetComponent<BoxCollider2D>();Need(c!=null,"Expected box "+suffix);
            c.offset=t.InverseTransformPoint(b.center);c.size=new Vector2(b.size.x/Mathf.Abs(t.lossyScale.x),b.size.y/Mathf.Abs(t.lossyScale.y));
        }
        static void Malkuth(Transform r)
        {
            var optional=Node(r,"00_MAP_EDIT/ELEVATORS_VISUAL/ROOM_ENTRY_ELEVATORS_OPTIONAL");var terrain=Node(r,"00_MAP_EDIT/TERRAIN/R3_ENTRY_ACCESS_STATIC");
            for(int i=0;i<4;i++)
            {
                float x=i*28;string room=Rooms[i];var p=Fresh(terrain,room);var fixedRoot=Fresh(p,"ENTRY_BYPASS_BLOCKING_TERRAIN");
                StaticRect(fixedRoot,"UpperCorridor_Floor",x+3.5f,25.5f,x+22.5f,26);StaticRect(fixedRoot,"Dock",x+22.5f,25.5f,x+26.5f,26);
                StaticRect(fixedRoot,"UpperCorridor_Ceiling",x+.5f,29.5f,x+27,30);StaticRect(fixedRoot,"RightBoundary",x+26.5f,26,x+27,29.5f);
                StaticRect(fixedRoot,"Chute_RightWall",x+3.5f,17,x+4,25.5f);
                string roomPath="PUZZLE_VISUALS/PUZZLE_AREAS/"+room+"/TERRAIN/";
                UpdateOldBox(r,roomPath+"LeftWall/Collider_Layout",Rect(x-.5f,13.6f,x,30));
                var left=Find(r,roomPath+"LeftWall/Visual");foreach(var sr in left.GetComponentsInChildren<SpriteRenderer>(true))sr.enabled=false;
                ArtRect(left,"R3_FULL_HEIGHT_ART_VISUAL",Module("Wall"),Rect(x-.5f,13.6f,x,30),Color.white,-1);
                UpdateOldBox(r,roomPath+"Ceiling/Collider_Layout",Rect(x,22.5f,x+.5f,23));
                var ceiling=Find(r,roomPath+"Ceiling/Visual");foreach(var sr in ceiling.GetComponentsInChildren<SpriteRenderer>(true))sr.enabled=false;
                ArtRect(ceiling,"R3_LEFT_ART_VISUAL",Module("Ceiling"),Rect(x,22.5f,x+.5f,23),Color.white,-1);
                StaticRect(fixedRoot,"OriginalCeiling_RightSegment",x+3.5f,22.5f,x+18,23);
                float port=i==1?14:17.2f;
                StaticRect(fixedRoot,"CompletionDrop_LeftBoundary",x+18.5f,2.8f,x+19,port-.4f);StaticRect(fixedRoot,"CompletionDrop_RightBoundary",x+22,2.8f,x+22.5f,25.5f);
                var roomDisplay=Fresh(optional,room);var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(Art+"Prefabs/EntryElevatorOptional_R3.prefab");var inst=(GameObject)PrefabUtility.InstantiatePrefab(prefab,roomDisplay);inst.name="ENTRY_ELEVATOR_OPTIONAL_VISUAL";inst.transform.position=new Vector3(x+24.5f,28,0);
                var anchors=Node(p,"90_DEV_LOCAL_ANCHORS");var marker=Fresh(anchors,"R3_ENTRY_REFERENCE_NOT_SPAWN");marker.position=new Vector3(x+24.5f,26,0);
                var bg=Sprite("Assets/Resources/img/BG/stage1-1.png","stage1-1_0");
                // Use the original aspect ratio uniformly and crop its Tiled area to the new upper strip.
                var band=Fresh(Node(r,"00_MAP_EDIT/ENVIRONMENT/R3_UPPER_BACKGROUND"),room);band.position=new Vector3(x+13.25f,28,-1);band.localScale=Vector3.one*.535885f;
                var bs=band.gameObject.AddComponent<SpriteRenderer>();bs.sprite=bg;bs.sharedMaterial=mat;bs.sortingOrder=-10;bs.drawMode=SpriteDrawMode.Tiled;bs.size=new Vector2(28/.535885f,4.2f/.535885f);
            }
            var dev=Node(r,"90_DEV_HANDOFF_NOT_FOR_MAP_EDIT/ARCHIVED_R2_ENTRY_REFERENCE");
            foreach(var t in r.GetComponentsInChildren<Transform>(true).Where(t=>t.name=="ELEVATOR_ENTRY_VISUAL"||t.name=="EntryApproach").ToArray())
            {
                var markers=t.GetComponentsInChildren<Transform>(true).Where(z=>z!=t&&z.name=="90_DEV_LOCAL_ANCHORS").ToArray();
                foreach(var marker in markers)marker.SetParent(Node(r,"90_DEV_HANDOFF_NOT_FOR_MAP_EDIT/ARCHIVED_R2_ENTRY_MARKERS"),true);
                t.SetParent(dev,true);t.gameObject.SetActive(false);
            }
            var cam=r.GetComponentsInChildren<Camera>(true).Single();cam.transform.position=new Vector3(24.5f,28,-10);cam.orthographicSize=6;
        }
        static string Critical(Transform r)
        {
            return string.Join("\n",r.GetComponentsInChildren<Transform>(true).Where(t=>t.GetComponent<Collider2D>()!=null || Full(t).Contains("90_DEV") || t.name.Contains("Marker") || t.name.Contains("Anchor")).Select(t=>JsonUtility.ToJson(Record(t))));
        }
        static Transform[] Optionals(Transform r) { return Find(r,"ROOM_ENTRY_ELEVATORS_OPTIONAL").GetComponentsInChildren<Transform>(true).Where(t=>t.name=="ENTRY_ELEVATOR_OPTIONAL_VISUAL").ToArray(); }
        static void ToggleChecks()
        {
            var s=EditorSceneManager.OpenScene(PathFor(0));var r=Root(s);var opts=Optionals(r);Need(opts.Length==4&&opts.All(x=>x.GetComponentsInChildren<Collider2D>(true).Length==0),"Four optional visual-only entries");string baseline=Critical(r);var q=GetQA();
            for(int mode=-1;mode<4;mode++){foreach(var t in opts)t.gameObject.SetActive(true);if(mode==-1)foreach(var t in opts)t.gameObject.SetActive(false);else opts[mode].gameObject.SetActive(false);bool same=Critical(r)==baseline;Need(same,"Toggle changed terrain/markers");q.toggles.Add(new ToggleQA{mode=mode==-1?"ALL_OFF":opts[mode].parent.name+"_OFF",colliderAndMarkersEqual=same,activeOptional=mode==-1?0:3});}
            foreach(var t in opts)t.gameObject.SetActive(true);Write("QA_RESULTS.json",q);SaveQA(s,0);
        }
        public static readonly string[] CaptureNames={"Malkuth_ON_overview","Malkuth_OFF_overview","Rabbit_ON","Rabbit_OFF","Rat_ON","Rat_OFF","Frog_ON","Frog_OFF","Bear_ON","Bear_OFF","YesodA_overview","YesodB_overview","Hod_overview","Netzach_overview","Art_modules","Hierarchy_R3","Collider_pair","ART_TEXTURE_QA"};
        public static void Inspect(int n)
        {
            Guard();if(artView!=null)artView.Close();int i=n<10?0:n<14?n-9:n==15?0:n==16?4:0;
            var s=EditorSceneManager.OpenScene(PathFor(i));var r=Root(s);if(reviewView==null){foreach(var old in Resources.FindObjectsOfTypeAll<SceneView>().Where(v=>!v.docked && Mathf.Abs(v.position.x-10)<2 && Mathf.Abs(v.position.width-1800)<2).ToArray())old.Close();reviewView=ScriptableObject.CreateInstance<SceneView>();reviewView.titleContent=new GUIContent("R3 Scene Review");reviewView.ShowUtility();reviewView.position=new Rect(10,80,1800,900);}
            var sv=reviewView;sv.in2DMode=true;sv.orthographic=true;sv.drawGizmos=false;
            Bounds b;Transform selected=r.Find("00_MAP_EDIT");
            if(n<10)
            {
                var opts=Optionals(r);foreach(var t in opts)t.gameObject.SetActive(true);
                if(n==1)foreach(var t in opts)t.gameObject.SetActive(false);
                if(n>=2&&n%2==1)opts.Single(t=>t.parent.name==Rooms[(n-2)/2]).gameObject.SetActive(false);
                b=n<2?new Bounds(new Vector3(56,15,0),new Vector3(137,36,1)):new Bounds(new Vector3(((n-2)/2)*28+13,22,0),new Vector3(31,20,1));
                selected=n<2?Find(r,"ROOM_ENTRY_ELEVATORS_OPTIONAL"):opts.Single(t=>t.parent.name==Rooms[(n-2)/2]);SaveQA(s,0);
                r=Root(SceneManager.GetActiveScene());selected=n<2?Find(r,"ROOM_ENTRY_ELEVATORS_OPTIONAL"):Optionals(r).Single(t=>t.parent.name==Rooms[(n-2)/2]);
            }
            else if(n==10||n==11)b=new Bounds(new Vector3(13,4,0),new Vector3(48,18,1));
            else if(n==12)b=new Bounds(new Vector3(7,3,0),new Vector3(47,18,1));
            else if(n==13)b=new Bounds(new Vector3(4,3,0),new Vector3(34,18,1));
            else if(n==16){selected=Find(r,"EntryBridge_3p5/Collider_Layout");b=new Bounds(new Vector3(-3,0,0),new Vector3(12,8,1));}
            else {b=new Bounds(new Vector3(13,26,0),new Vector3(28,13,1));if(n==14||n==17){reviewView.Close();reviewView=null;artView=ScriptableObject.CreateInstance<R3ArtReview>();artView.titleContent=new GUIContent("R3 Imported Art QA");artView.ShowUtility();artView.position=new Rect(10,80,1800,930);return;}}
            if(n==15||n==16){reviewView.Close();reviewView=null;sv=Resources.FindObjectsOfTypeAll<SceneView>().First(v=>v.docked);sv.in2DMode=true;sv.orthographic=true;sv.drawGizmos=n==16;}
            Selection.activeGameObject=selected.gameObject;sv.Frame(b,false);sv.Repaint();
            Debug.Log("R3 CAPTURE "+CaptureNames[n]+" Edit mode "+DateTimeOffset.Now.ToString("o")+" "+Full(selected));
        }
        [MenuItem("Tools/C-IMPL-R3/Tile and Slice QA")]
        public static void TileQA()
        {
            Guard();string p=Art+"Prefabs/DoorFrame_R3.prefab";string original=Hash(p);var go=PrefabUtility.LoadPrefabContents(p);
            try
            {
                var sr=go.GetComponentInChildren<SpriteRenderer>(true);Need(sr.sprite.border==new Vector4(8,8,8,8),"New border8");
                foreach(var mode in new[]{SpriteDrawMode.Sliced,SpriteDrawMode.Tiled})foreach(var size in new[]{new Vector2(2,3),new Vector2(3,4)})
                {sr.drawMode=mode;sr.size=size;Need(Mathf.Abs(sr.bounds.size.x-size.x)<.0001f && Mathf.Abs(sr.bounds.size.y-size.y)<.0001f,"Sliced/Tiled bounds");}
            }
            finally{PrefabUtility.UnloadPrefabContents(go);}
            Need(Hash(p)==original,"Prefab preview changed file");var q=GetQA();q.artChecks.Add("Native DoorFrame preview: Sliced and Tiled bounds correct at 2x3 and 3x4 WU; border8; prefab file unchanged. Visual repeat QA shown separately.");Write("QA_RESULTS.json",q);
            if(reviewView!=null){reviewView.Close();reviewView=null;}if(artView!=null)artView.Close();artView=ScriptableObject.CreateInstance<R3ArtReview>();artView.repeats=true;artView.titleContent=new GUIContent("R3 Tile 2x2 4x4 QA");artView.ShowUtility();artView.position=new Rect(10,80,1800,930);
        }
        [MenuItem("Tools/C-IMPL-R3/Open Control Panel",false,100)] public static void OpenPanel(){var w=EditorWindow.GetWindow<R3ControlPanel>("C-IMPL-R3");w.position=new Rect(1900,80,640,650);}
    }
    public class R3ArtReview : EditorWindow
    {
        const string A="Assets/PCS/PCS MAP TRY/Art/R3/";
        public bool repeats;
        void Repeat(string label,Rect pixels,int nx,int ny,int scale,float x,float y)
        {
            GUI.Label(new Rect(x,y-28,650,24),label,EditorStyles.boldLabel);var tex=AssetDatabase.LoadAssetAtPath<Texture2D>(A+"SpriteSheets/MetalModules_R3.png");
            EditorGUI.DrawRect(new Rect(x,y,pixels.width*scale*nx,pixels.height*scale*ny),new Color(.38f,.2f,.32f));
            Rect uv=new Rect(pixels.x/256,pixels.y/128,pixels.width/256,pixels.height/128);
            for(int j=0;j<ny;j++)for(int i=0;i<nx;i++)GUI.DrawTextureWithTexCoords(new Rect(x+i*pixels.width*scale,y+j*pixels.height*scale,pixels.width*scale,pixels.height*scale),tex,uv,true);
        }
        void Draw(string label,string path,float x,float y,int scale)
        {
            var t=AssetDatabase.LoadAssetAtPath<Texture2D>(path);if(t==null)return;
            GUI.Label(new Rect(x,y-24,1000,22),label,EditorStyles.boldLabel);
            Rect r=new Rect(x,y,t.width*scale,t.height*scale);
            EditorGUI.DrawRect(r,new Color(.16f,.18f,.2f));
            for(int cy=0;cy<r.height;cy+=16)for(int cx=0;cx<r.width;cx+=16)if((cx/16+cy/16)%2==0)EditorGUI.DrawRect(new Rect(x+cx,y+cy,Mathf.Min(16,r.width-cx),Mathf.Min(16,r.height-cy)),new Color(.26f,.28f,.3f));
            GUI.DrawTexture(r,t,ScaleMode.StretchToFill,true);
        }
        void OnGUI()
        {
            if(repeats)
            {
                GUI.Label(new Rect(20,10,1700,30),"R3 / 2x2 + 4x4 imported tile repeats / 400% / native Sliced + Tiled bounds checked / Edit mode",EditorStyles.boldLabel);
                Repeat("Wall 2x2",new Rect(0,0,16,32),2,2,4,20,80);
                Repeat("Wall 4x4",new Rect(0,0,16,32),4,4,4,220,80);
                Repeat("Ceiling 2x2",new Rect(32,0,32,16),2,2,4,600,80);
                Repeat("Ceiling 4x4",new Rect(32,0,32,16),4,4,4,600,300);
                Repeat("Column 2x2",new Rect(96,0,8,32),2,2,4,1250,80);
                Repeat("Column 4x4",new Rect(96,0,8,32),4,4,4,1450,80);
                GUI.Label(new Rect(20,700,1750,120),"Tile joints are intentional modular steel edges. No non-uniform Transform scaling.\nDoorFrame native preview: border8; Sliced/Tiled sizes 2x3 and 3x4 produce matching bounds.\nNo Prefab asset was changed by the native preview check. ShaftPanel rejected; existing BG retained.",EditorStyles.boldLabel);
                return;
            }
            GUI.Label(new Rect(20,10,1600,26),"R3 / IMPORTED TEXTURES / Point + PPU32 / 100%, 200%, 400% / Edit mode",EditorStyles.boldLabel);
            Draw("MetalModules 100%",A+"SpriteSheets/MetalModules_R3.png",20,70,1);
            Draw("MetalModules 200%",A+"SpriteSheets/MetalModules_R3.png",20,240,2);
            Draw("Existing Tiles / reference 100% / READ ONLY","Assets/Resources/img/Tiles/Tiles.png",20,550,1);
            Draw("MetalModules 400% / alpha and pixel-edge inspection",A+"SpriteSheets/MetalModules_R3.png",600,70,4);
            Draw("Entry 100% / scene visual",A+"Textures/EntryElevator_R3.png",600,650,1);
            Draw("Drum UNUSED",A+"UNUSED_CANDIDATES/Drum_R3_UNUSED.png",830,650,1);
            Draw("Sign UNUSED",A+"UNUSED_CANDIDATES/ThreatSign_R3_UNUSED.png",1050,650,1);
            GUI.Label(new Rect(600,820,1100,70),"ShaftPanel: NOT_CREATED (repeat quality rejected). Existing Sprite backgrounds retained.\nUNUSED images/prefabs are not placed in any Scene.\nWall/Ceiling/Column/DoorFrame/Entry visual prefabs: root scale 1; Collider 0.");
        }
    }
    public class R3ControlPanel : EditorWindow
    {
        void OnGUI()
        {
            GUILayout.Label("C-IMPL-R3 / 승인 범위 전용",EditorStyles.boldLabel);
            GUILayout.Label("미저장 사용자 씬은 실행을 차단합니다. Play 검증은 실행하지 않습니다.");
            if(GUILayout.Button("01–07 승인된 작업 연속 실행",GUILayout.Height(32)))PCS_MapPolishR3.Continuous();
            GUILayout.Label("실제 화면 검수 / 아래 버튼은 승인된 씬만 열고 외형 상태를 저장합니다.");
            for(int i=0;i<PCS_MapPolishR3.CaptureNames.Length;i++){if(GUILayout.Button(i.ToString("00")+"  "+PCS_MapPolishR3.CaptureNames[i],GUILayout.Height(24)))PCS_MapPolishR3.Inspect(i);}
        }
    }
}

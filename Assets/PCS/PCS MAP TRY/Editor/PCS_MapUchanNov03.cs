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
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace PCS.MapPlanning.Nov03.EditorTools
{
    // Manual tools only. Never save shared assets, run old builders, or enter Play mode.
    public static class PCS_MapUchanNov03
    {
        public const string Project = "C:/FOUR-PAWS-ASCEND-NEWPCS";
        public const string Run = "C:/FOUR-PAWS-PCS-BRIEF/CPLAN_UCHAN_NOV03_20261103/CIMPL_NOV03/run-001";
        public const string Base = "Assets/PCS/PCS MAP TRY/";
        public const string ClipPath = Base + "Animation/Uchan_NOV03_44Frame_VisualPreview.anim";
        public const string PanelPath = Base + "Art/N02Remake/Uchan_ControlPanel_NOV03.png";
        public static readonly string[] Names = { "PCS_MAP00_Malkuth_ONE MORE VERSION", "PCS_MAP01_Yesod_SourceA_Edit", "PCS_MAP02_Hod_Blockout", "PCS_MAP03_Netzach_Blockout" };
        public static string ScenePath(int i) { return Base + "Scenes/" + Names[i] + ".unity"; }
        [Serializable] public class FileRow { public string path, sha256; public long bytes; }
        [Serializable] public class Protected { public FileRow[] files; }
        [Serializable] public class Backup { public string source, backup, sha256, backup_sha256; public long bytes; public bool readback_verified; }
        [Serializable] public class Backups { public Backup[] items; }
        [Serializable] public class Row
        {
            public string id, transformId, path, parent, prefab, sprite, spriteName, material, drawMode;
            public long localId; public string[] componentIds, componentTypes, references;
            public float[] position, localPosition, scale, localScale, rotation, matrix, bounds, size, color;
            public int order, layer; public bool active, activeHierarchy, rendererEnabled;
        }
        [Serializable] public class SceneRecord
        {
            public string path, guid, beforeSha, savedSha; public Row[] before, after;
            public List<string> changes = new List<string>(); public int missingScripts, missingSprites;
            public bool reopened, nativeClean;
        }
        [Serializable] public class Identity { public List<SceneRecord> scenes = new List<SceneRecord>(); }
        [Serializable] public class PhysicsRow
        {
            public string scene, id, path, type, prefab, role, verdict, reason, serialized, attachedRigidbody, effector;
            public bool active, enabled, trigger, usedByEffector; public int layer; public float[] bounds, position, scale;
        }
        [Serializable] public class PhysicsQA { public List<PhysicsRow> before = new List<PhysicsRow>(), after = new List<PhysicsRow>(); public List<string> changes = new List<string>(); public string play = "NOT_TESTED"; }
        [Serializable] public class ElevatorRow
        {
            public string scene, id, path, source, floor, column, status, reason, behaviour;
            public string[] networkIds, references; public float[] floorBounds, floorPosition, columnSize;
            public bool floorSameInstance, columnSameInstance, nestedNetworkReference, behaviourReference, oneWay;
        }
        [Serializable] public class ElevatorQA { public string elevatorSha, floorSha; public List<ElevatorRow> before = new List<ElevatorRow>(), after = new List<ElevatorRow>(); public string runtime = "NOT_TESTED", network = "NOT_TESTED"; }
        [Serializable] public class Modes { public bool playing, animationMode, prefabMode, compiling; public string[] openScenes, dirtyScenes, dirtySourceAssets; }
        [Serializable] public class UserChanges { public string policy = "Latest saved user layout is authoritative. Removed objects are ALREADY_USER_REMOVED / SKIP; never reconstructed."; public Modes modes; public string[] notes; }
        public static T Read<T>(string name) { return JsonUtility.FromJson<T>(File.ReadAllText(Run + "/" + name)); }
        public static void Write(string name, object value) { File.WriteAllText(Run + "/" + name, JsonUtility.ToJson(value, true), new UTF8Encoding(false)); }
        public static void Log(string message) { File.AppendAllText(Run + "/logs/native_editor.log", DateTimeOffset.Now.ToString("o") + " " + message + "\n", new UTF8Encoding(false)); Debug.Log("NOV03: " + message); }
        public static void Need(bool condition, string reason) { if (!condition) throw new InvalidOperationException("NOV03 HOLD: " + reason); }
        public static string Hash(string path) { using (var f = File.OpenRead(path)) using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(f)).Replace("-", "").ToLowerInvariant(); }
        public static string Id(Object o) { return o == null ? "NULL" : GlobalObjectId.GetGlobalObjectIdSlow(o).ToString(); }
        public static long LocalId(Object o) { return o == null ? 0 : unchecked((long)GlobalObjectId.GetGlobalObjectIdSlow(o).targetObjectId); }
        public static string Full(Transform t) { return t.parent == null ? t.name : Full(t.parent) + "/" + t.name; }
        public static float[] V(Vector3 v) { return new[] { v.x, v.y, v.z }; }
        public static float[] B(Bounds b) { return new[] { b.center.x, b.center.y, b.center.z, b.size.x, b.size.y, b.size.z }; }
        public static string AssetId(Object o) { if (o == null) return ""; AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o, out string guid, out long id); return guid + ":" + id; }
        public static Transform[] All(Scene scene) { return scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray(); }
        public static Transform ById(Scene scene, long id) { var a = All(scene).Where(t => LocalId(t.gameObject) == id).ToArray(); Need(a.Length == 1, "Unique current GO " + id + " found " + a.Length); return a[0]; }
        public static string[] References(IEnumerable<Component> components)
        {
            var result = new List<string>();
            foreach (var c in components.Where(c => c != null && !(c is Transform)))
            {
                var it = new SerializedObject(c).GetIterator();
                while (it.Next(true)) if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue != null) result.Add(Id(c) + "/" + it.propertyPath + "=" + Id(it.objectReferenceValue));
            }
            return result.OrderBy(s => s).ToArray();
        }
        public static Row Record(Transform t)
        {
            var sr = t.GetComponent<SpriteRenderer>(); var m = t.localToWorldMatrix; var cs = t.GetComponents<Component>();
            return new Row { id = Id(t.gameObject), transformId = Id(t), localId = LocalId(t.gameObject), path = Full(t), parent = t.parent == null ? "" : Id(t.parent.gameObject), prefab = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject),
                componentIds = cs.Select(Id).ToArray(), componentTypes = cs.Select(c => c == null ? "MISSING" : c.GetType().FullName).ToArray(), references = References(cs), position = V(t.position), localPosition = V(t.localPosition), scale = V(t.lossyScale), localScale = V(t.localScale), rotation = V(t.eulerAngles), matrix = Enumerable.Range(0, 16).Select(i => m[i]).ToArray(),
                active = t.gameObject.activeSelf, activeHierarchy = t.gameObject.activeInHierarchy, layer = t.gameObject.layer, sprite = sr == null ? "" : AssetId(sr.sprite), spriteName = sr == null || sr.sprite == null ? "" : sr.sprite.name, material = sr == null ? "" : AssetId(sr.sharedMaterial), drawMode = sr == null ? "" : sr.drawMode.ToString(), bounds = sr == null ? new float[0] : B(sr.bounds), size = sr == null ? new float[0] : new[] { sr.size.x, sr.size.y }, color = sr == null ? new float[0] : new[] { sr.color.r, sr.color.g, sr.color.b, sr.color.a }, order = sr == null ? 0 : sr.sortingOrder, rendererEnabled = sr != null && sr.enabled };
        }
        public static Modes ModeState()
        {
            var scenes = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).ToArray();
            // Imported shader/font caches may be dirty without any serialized source edit. Never save them.
            var dirty = Resources.FindObjectsOfTypeAll<Object>().Where(o => o != null && EditorUtility.IsPersistent(o) && EditorUtility.IsDirty(o) && !(o is Shader) && !(o is Font) && o.GetType().FullName != "TMPro.TMP_FontAsset")
                .Select(AssetDatabase.GetAssetPath).Where(p => !string.IsNullOrEmpty(p) && (p.StartsWith("Assets/") || p.StartsWith("ProjectSettings/")))
                .Where(p => !(AssetImporter.GetAtPath(p) is TrueTypeFontImporter)).Distinct().ToArray();
            return new Modes { playing = EditorApplication.isPlayingOrWillChangePlaymode, animationMode = AnimationMode.InAnimationMode(), prefabMode = PrefabStageUtility.GetCurrentPrefabStage() != null, compiling = EditorApplication.isCompiling,
                openScenes = scenes.Select(s => s.path).ToArray(), dirtyScenes = scenes.Where(s => s.isDirty).Select(s => s.path).ToArray(), dirtySourceAssets = dirty };
        }
        public static void Stable()
        {
            Need(Path.GetFullPath(Application.dataPath + "/..").Replace('\\', '/') == Project, "Wrong project"); var mode = ModeState();
            Need(!mode.playing && !mode.animationMode && !mode.prefabMode && !mode.compiling, "Edit/Prefab/Animation mode collision");
            Need(mode.dirtyScenes.Length == 0, "Unsaved user Scene: " + string.Join(",", mode.dirtyScenes));
            // No SaveAssets is ever used; report source dirty state separately and guard byte hashes.
            Need(mode.dirtySourceAssets.Length == 0, "Dirty source asset: " + string.Join(",", mode.dirtySourceAssets));
        }
        public static void ProtectedGuard()
        {
            foreach (var f in Read<Protected>("CURRENT_PROTECTED_BASELINE.json").files) Need(File.Exists(f.path) && Hash(f.path) == f.sha256, "Protected file changed " + f.path);
            var backups = JsonUtility.FromJson<Backups>("{\"items\":" + File.ReadAllText(Run + "/BACKUP_INDEX.json") + "}");
            Need(backups.items.Length == 8, "8 backups required"); foreach (var b in backups.items) Need(b.readback_verified && File.Exists(b.backup) && Hash(b.backup) == b.sha256 && new FileInfo(b.backup).Length == b.bytes, "Backup readback failed " + b.backup);
        }
        public static List<PhysicsRow> PhysicsRows(Scene scene)
        {
            var rows = new List<PhysicsRow>();
            foreach (var t in All(scene)) foreach (var c in t.GetComponents<Component>().Where(c => c is Collider2D || c is Rigidbody2D))
            {
                var col = c as Collider2D; var rb = c as Rigidbody2D; var ef = t.GetComponent<PlatformEffector2D>(); string prefab = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject);
                rows.Add(new PhysicsRow { scene = scene.path, id = Id(c), path = Full(t), type = c.GetType().FullName, prefab = prefab, role = prefab.StartsWith("Assets/Prefabs/") ? "NETWORK_PREFAB" : col != null && col.isTrigger ? "TRIGGER" : rb != null ? "DYNAMIC_PHYSICS" : ef != null ? "ONE_WAY_PLATFORM" : "STATIC_SOLID", verdict = prefab.StartsWith("Assets/Prefabs/") || col != null && col.isTrigger || rb != null ? "NEEDS_DEVELOPER" : "UNKNOWN", reason = "Native inventory; alignment and gameplay are separate checks", serialized = EditorJsonUtility.ToJson(c), position = V(t.position), scale = V(t.lossyScale), active = t.gameObject.activeInHierarchy, enabled = col == null || col.enabled, trigger = col != null && col.isTrigger, layer = t.gameObject.layer, bounds = col == null ? new float[0] : B(col.bounds), attachedRigidbody = col == null ? Id(rb) : Id(col.attachedRigidbody), effector = Id(ef), usedByEffector = col != null && col.usedByEffector });
            }
            return rows;
        }
        public static List<ElevatorRow> Elevators(Scene scene)
        {
            var rows = new List<ElevatorRow>();
            foreach (var mb in All(scene).SelectMany(t => t.GetComponents<MonoBehaviour>()).Where(mb => mb != null && mb.GetType().Name == "Elevator"))
            {
                var so = new SerializedObject(mb); var f = so.FindProperty("floor").objectReferenceValue as GameObject; var column = so.FindProperty("column").objectReferenceValue as SpriteRenderer;
                var nos = mb.GetComponentsInChildren<Component>(true).Where(c => c != null && c.GetType().Name == "NetworkObject").ToArray(); var rootNO = nos.FirstOrDefault(c => c.gameObject == mb.gameObject); var floorNO = f == null ? null : nos.FirstOrDefault(c => c.gameObject == f);
                var ns = rootNO == null ? null : new SerializedObject(rootNO); var nested = ns == null ? null : ns.FindProperty("NestedObjects"); var behaviours = ns == null ? null : ns.FindProperty("NetworkedBehaviours");
                var box = f == null ? null : f.GetComponentInChildren<BoxCollider2D>(true); var ef = box == null ? null : box.GetComponent<PlatformEffector2D>();
                var r = new ElevatorRow { scene = scene.path, id = Id(mb.gameObject), path = Full(mb.transform), source = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(mb.gameObject), floor = Id(f), column = Id(column), behaviour = Id(mb), networkIds = nos.Select(Id).ToArray(), references = References(mb.GetComponentsInChildren<Component>(true)), floorSameInstance = f != null && f.transform.IsChildOf(mb.transform), columnSameInstance = column != null && column.transform.IsChildOf(mb.transform), nestedNetworkReference = nested != null && nested.arraySize == 1 && nested.GetArrayElementAtIndex(0).objectReferenceValue == floorNO && floorNO != null, behaviourReference = behaviours != null && behaviours.arraySize == 1 && behaviours.GetArrayElementAtIndex(0).objectReferenceValue == mb, oneWay = box != null && box.usedByEffector && ef != null && ef.useOneWay, floorBounds = box == null ? new float[0] : B(box.bounds), floorPosition = f == null ? new float[0] : V(f.transform.position), columnSize = column == null ? new float[0] : new[] { column.size.x, column.size.y } };
                r.status = r.source == "Assets/Prefabs/Elevator.prefab" && r.floorSameInstance && r.columnSameInstance && r.nestedNetworkReference && r.behaviourReference && r.oneWay && nos.Length == 2 ? "NATIVE_REFERENCE_PASS" : "ELEVATOR_HOLD";
                r.reason = "Current Scene instance native inspection; no Runtime/Network claim"; rows.Add(r);
            }
            return rows;
        }
        public static void Audit()
        {
            Stable(); ProtectedGuard(); Need(!File.Exists(Run + "/CURRENT_IDENTITY_MAP.json"), "Baseline already captured; preserve it");
            var modes = ModeState(); Write("CURRENT_USER_CHANGES.json", new UserChanges { modes = modes, notes = new[] { "Baseline current user names, removals, transforms and references. No historical restore.", "NOV03 latest PNG + 44F only; deleted DEV is SKIP." } });
            var identity = new Identity(); var physics = new PhysicsQA(); var elevators = new ElevatorQA { elevatorSha = Hash("Assets/Prefabs/Elevator.prefab"), floorSha = Hash("Assets/Prefabs/Floor.prefab") }; string original = SceneManager.GetActiveScene().path;
            try
            {
                for (int i = 0; i < 4; i++)
                {
                    Stable(); var s = EditorSceneManager.OpenScene(ScenePath(i), OpenSceneMode.Single); var row = new SceneRecord { path = s.path, guid = AssetDatabase.AssetPathToGUID(s.path), beforeSha = Hash(s.path), savedSha = Hash(s.path), before = All(s).Select(Record).ToArray(), nativeClean = !s.isDirty };
                    row.missingScripts = All(s).Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)); row.missingSprites = All(s).Select(t => t.GetComponent<SpriteRenderer>()).Count(sr => sr != null && sr.enabled && sr.sprite == null);
                    Need(!s.isDirty, "Read-only audit dirtied " + s.path); identity.scenes.Add(row); physics.before.AddRange(PhysicsRows(s)); elevators.before.AddRange(Elevators(s)); Log("I00 native read " + Names[i] + " GO=" + row.before.Length + " missing script/sprite=" + row.missingScripts + "/" + row.missingSprites);
                }
            }
            finally { if (!string.IsNullOrEmpty(original) && !SceneManager.GetActiveScene().isDirty) EditorSceneManager.OpenScene(original, OpenSceneMode.Single); }
            Write("CURRENT_IDENTITY_MAP.json", identity); Write("COLLIDER_QA.json", physics); Write("NATIVE_ELEVATOR_QA.json", elevators); ProtectedGuard(); Stable(); Log("I00 native baseline complete; Elevator slots=" + elevators.before.Count + ", passed=" + elevators.before.Count(r => r.status == "NATIVE_REFERENCE_PASS"));
        }
        public static Sprite Sprite(string path, string name = null)
        {
            var a = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().Where(s => name == null || s.name == name).ToArray(); Need(a.Length == 1, "Exact Sprite " + path + "/" + name); return a[0];
        }
        public static Material Lit { get { return AssetDatabase.LoadAssetAtPath<Material>("Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Lit-Default.mat"); } }
        public static void ImportPanels()
        {
            Stable(); ProtectedGuard(); Need(File.Exists(PanelPath), "Approved source-derived atlas missing");
            AssetDatabase.ImportAsset(PanelPath, ImportAssetOptions.ForceSynchronousImport);
            var sprites = AssetDatabase.LoadAllAssetsAtPath(PanelPath).OfType<Sprite>().ToArray();
            if (sprites.Any(s => s.name == "제어판_빨강") && sprites.Any(s => s.name == "제어판_초록")) { Log("I01 atlas already imported; kept GUID and subasset IDs"); return; }
            var ti = (TextureImporter)AssetImporter.GetAtPath(PanelPath); ti.textureType = TextureImporterType.Sprite; ti.spriteImportMode = SpriteImportMode.Multiple; ti.spritePixelsPerUnit = 100; ti.filterMode = FilterMode.Point; ti.mipmapEnabled = false; ti.textureCompression = TextureImporterCompression.Uncompressed; ti.alphaIsTransparency = true; ti.npotScale = TextureImporterNPOTScale.None;
            var settings = new TextureImporterSettings(); ti.ReadTextureSettings(settings); settings.spriteMeshType = SpriteMeshType.FullRect; ti.SetTextureSettings(settings); ti.SaveAndReimport();
            var factory = new SpriteDataProviderFactories(); factory.Init(); var provider = factory.GetSpriteEditorDataProviderFromObject(ti); provider.InitSpriteEditorDataProvider();
            var rects = new[] { new SpriteRect { name = "제어판_빨강", rect = new Rect(0, 0, 96, 96), alignment = SpriteAlignment.Center, pivot = Vector2.one * .5f, spriteID = GUID.Generate() }, new SpriteRect { name = "제어판_초록", rect = new Rect(96, 0, 96, 96), alignment = SpriteAlignment.Center, pivot = Vector2.one * .5f, spriteID = GUID.Generate() } };
            provider.SetSpriteRects(rects); provider.GetDataProvider<ISpriteNameFileIdDataProvider>().SetNameFileIdPairs(rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID))); provider.Apply(); ti.SaveAndReimport();
            Need(Sprite(PanelPath, "제어판_빨강") != null && Sprite(PanelPath, "제어판_초록") != null, "Native atlas slices"); ProtectedGuard(); Log("I01 new atlas native import PASS; original assets untouched");
        }
        public static Scene OpenApproved(int i)
        {
            Stable(); ProtectedGuard(); var row = Read<Identity>("CURRENT_IDENTITY_MAP.json").scenes[i]; Need(Hash(row.path) == row.savedSha, "Concurrent disk edit " + row.path); return EditorSceneManager.OpenScene(row.path, OpenSceneMode.Single);
        }
        public static Scene OpenForPreview(int i)
        {
            // Routine Preview accepts the owner's later saved Scene. Builder writes still require OpenApproved/hash matching.
            Stable(); ProtectedGuard(); Need(File.Exists(ScenePath(i)), "Exact current approved Scene path"); return EditorSceneManager.OpenScene(ScenePath(i), OpenSceneMode.Single);
        }
        public static Transform New(Transform parent, string name)
        {
            var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, parent.gameObject.scene); go.transform.SetParent(parent, false); Undo.RegisterCreatedObjectUndo(go, "NOV03 " + name); return go.transform;
        }
        static void RenameMove(Transform t, Transform parent, string name, Vector3 position)
        {
            Undo.RecordObject(t, "NOV03 layout"); Undo.RecordObject(t.gameObject, "NOV03 name"); t.SetParent(parent, true); t.name = name; t.position = position; t.rotation = Quaternion.identity; t.localScale = Vector3.one;
        }
        static SpriteRenderer Fit(SpriteRenderer sr, Sprite sprite, Vector2 center, Vector2 size, int order = 0, bool sliced = true)
        {
            sr.enabled = true; sr.sprite = sprite; sr.sharedMaterial = Lit; sr.color = Color.white; sr.sortingOrder = order; sr.flipX = sr.flipY = false; sr.transform.rotation = Quaternion.identity; sr.transform.localScale = Vector3.one;
            sr.drawMode = sliced && sprite.border.sqrMagnitude > 0 ? SpriteDrawMode.Sliced : SpriteDrawMode.Simple;
            if (sr.drawMode == SpriteDrawMode.Sliced) { sr.size = size; sr.transform.localScale = Vector3.one; }
            else sr.transform.localScale = new Vector3(size.x / sprite.bounds.size.x, size.y / sprite.bounds.size.y, 1);
            sr.transform.position = new Vector3(center.x - sprite.bounds.center.x * sr.transform.lossyScale.x, center.y - sprite.bounds.center.y * sr.transform.lossyScale.y, 0);
            return sr;
        }
        static void SetBox(BoxCollider2D box, Vector2 center, Vector2 size)
        {
            Need(!PrefabUtility.IsPartOfPrefabInstance(box) && box.attachedRigidbody == null && !box.isTrigger, "Only independent Scene-local static boxes");
            box.transform.SetPositionAndRotation(new Vector3(center.x, center.y, 0), Quaternion.identity); box.transform.localScale = Vector3.one; box.offset = Vector2.zero; box.size = size; box.enabled = true;
        }
        static Transform Platform(Scene scene, Transform group, long rootId, long artId, long boxId, string name, Vector2 center, Vector2 size, Sprite sprite, bool stateHidden = false)
        {
            var t = rootId == 0 ? New(group, name) : ById(scene, rootId); RenameMove(t, group, name, center);
            foreach (var sr in t.GetComponentsInChildren<SpriteRenderer>(true)) { Undo.RecordObject(sr, "NOV03 replace old artwork"); sr.enabled = false; }
            var art = artId == 0 ? New(t, "그림").gameObject.AddComponent<SpriteRenderer>() : ById(scene, artId).GetComponent<SpriteRenderer>(); art.transform.SetParent(t, true); art.name = "그림"; Fit(art, sprite, center, size);
            if (stateHidden) art.color = new Color(1, 1, 1, 0);
            var box = boxId == 0 ? New(t, "충돌(정적)").gameObject.AddComponent<BoxCollider2D>() : ById(scene, boxId).GetComponent<BoxCollider2D>(); box.transform.SetParent(t, true); box.name = "충돌(정적)";
            SetBox(box, new Vector2(center.x, center.y + size.y / 2 - .08f), new Vector2(size.x, .16f)); return t;
        }
        static void Device(SpriteRenderer sr, Transform parent, string name, Sprite sprite, Vector2 center, Vector2 visibleSize, bool terminal = false)
        {
            foreach (var child in sr.GetComponentsInChildren<SpriteRenderer>(true)) child.enabled = false;
            sr.transform.SetParent(parent, true); sr.name = name; sr.enabled = true; sr.sprite = sprite; sr.sharedMaterial = Lit; sr.drawMode = SpriteDrawMode.Simple; sr.color = Color.white; sr.sortingOrder = 5; sr.transform.rotation = Quaternion.identity;
            Vector2 alphaSize = terminal ? new Vector2(31f / 16, 29f / 16) : (Vector2)sprite.bounds.size;
            Vector2 alphaCenter = terminal ? new Vector2(-.03125f, -.09375f) : (Vector2)sprite.bounds.center;
            float scale = Mathf.Min(visibleSize.x / alphaSize.x, visibleSize.y / alphaSize.y); sr.transform.localScale = Vector3.one * scale; sr.transform.position = new Vector3(center.x - alphaCenter.x * scale, center.y - alphaCenter.y * scale, 0);
        }
        static void PositionCurve(AnimationClip clip, string path, Func<int, Vector3> value)
        {
            foreach (int axis in new[] { 0, 1, 2 })
            {
                var c = new AnimationCurve(Enumerable.Range(0, 45).Select(f => new Keyframe(f * .1f, value(Math.Min(f, 43))[axis])).ToArray());
                for (int k = 0; k < c.length; k++) { AnimationUtility.SetKeyLeftTangentMode(c, k, AnimationUtility.TangentMode.Constant); AnimationUtility.SetKeyRightTangentMode(c, k, AnimationUtility.TangentMode.Constant); }
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalPosition." + "xyz"[axis]), c);
            }
        }
        static void AlphaCurve(AnimationClip clip, string path, Func<int, float> value)
        {
            var c = new AnimationCurve(Enumerable.Range(0, 45).Select(f => new Keyframe(f * .1f, value(Math.Min(f, 43)))).ToArray()); for (int k = 0; k < c.length; k++) { AnimationUtility.SetKeyLeftTangentMode(c, k, AnimationUtility.TangentMode.Constant); AnimationUtility.SetKeyRightTangentMode(c, k, AnimationUtility.TangentMode.Constant); }
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(SpriteRenderer), "m_Color.a"), c);
        }
        public static void BuildYesod()
        {
            Stable(); ProtectedGuard(); Need(!File.Exists(ClipPath), "44F Clip already exists; use saved checkpoint, not rebuild"); Need(Sprite(PanelPath, "제어판_빨강") != null, "Atlas import first");
            var scene = OpenApproved(1); var identity = Read<Identity>("CURRENT_IDENTITY_MAP.json"); var rec = identity.scenes[1]; var puzzle = ById(scene, 123506200); var map = ById(scene, 773642046);
            Undo.IncrementCurrentGroup(); int undo = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("NOV03 최신 우찬 리메이크");
            RenameMove(puzzle, map, "우찬 퍼즐", Vector3.zero);
            // Reuse existing role objects and IDs. Only their old renderers are replaced; PZ02 is never disabled.
            var tile = Sprite("Assets/Resources/img/Tiles/Tiles.png", "Tiles_0"); var crate = Sprite("Assets/Resources/img/Tiles/Tiles.png", "Tiles_3");
            Platform(scene, puzzle, 1042884431, 196808777, 1227903249, "시작 바닥", new Vector2(1.089844f, -.251953f), new Vector2(2.179688f, .503906f), tile);
            long[] roots = { 1022222070, 1448294894, 1762620364, 764243136 }, artIds = { 1037788678, 577037166, 564143942, 1159848262 }, boxIds = { 1172572440, 201153774, 1194164979, 1079707056 };
            for (int i = 0; i < 4; i++) Platform(scene, puzzle, roots[i], artIds[i], boxIds[i], "하부 발판 " + (i + 1), new Vector2(3.322266f + 1.7578125f * i, -.462891f), new Vector2(.902344f, 1.019531f), crate, i == 1 || i == 3);
            Platform(scene, puzzle, 661335837, 1519010915, 722850040, "하부 중앙 고정 발판", new Vector2(12.580078f, -.509766f), new Vector2(5.941406f, 1.113281f), tile);
            Platform(scene, puzzle, 1090652447, 499771376, 1752485048, "왼쪽 단말 발판", new Vector2(1.201172f, 5.208984f), new Vector2(2.402344f, .550781f), tile);
            Platform(scene, puzzle, 1541813825, 1228009426, 279599237, "중간 오른쪽 발판", new Vector2(13.869141f, 5.208984f), new Vector2(3.410156f, .550781f), tile);
            Platform(scene, puzzle, 572397042, 1919532333, 1097483642, "상부 출구 발판", new Vector2(19.96875f, 8.103516f), new Vector2(5.0625f, .925781f), tile);
            Platform(scene, puzzle, 354346679, 1288282207, 1465982561, "상부 발판 1", new Vector2(2.554688f, 10.552734f), new Vector2(1.265625f, .410156f), tile);
            Platform(scene, puzzle, 862809184, 35345975, 1744807465, "상부 발판 2(낮음)", new Vector2(5.425781f, 8.443359f), new Vector2(1.265625f, .410156f), tile);
            Platform(scene, puzzle, 1651842242, 65061807, 770252060, "상부 발판 3", new Vector2(8.695312f, 10.552734f), new Vector2(1.265625f, .410156f), tile);
            Platform(scene, puzzle, 457431452, 1438938163, 1955936553, "상부 발판 4", new Vector2(13.933594f, 10.552734f), new Vector2(1.265625f, .410156f), tile);
            var shuttle = Platform(scene, puzzle, 470106931, 734742136, 1857079563, "중央 이동 발판", new Vector2(10.265625f, 5.208984f), new Vector2(3.515625f, .550781f), tile);
            shuttle.name = "중앙 이동 발판"; var moving = ById(scene, 1449436008); moving.SetParent(shuttle, false); moving.name = "움직이는 그림"; moving.localPosition = Vector3.zero; var shuttleArt = ById(scene, 734742136); shuttleArt.SetParent(moving, true);
            var lift = ById(scene, 822529744); RenameMove(lift, puzzle, "오른쪽 승강 발판", new Vector3(20.425781f, -.539062f, 0)); foreach (var sr in lift.GetComponentsInChildren<SpriteRenderer>(true)) sr.enabled = false;
            var liftMoving = ById(scene, 1673848809); liftMoving.SetParent(lift, false); liftMoving.name = "움직이는 그림"; liftMoving.localPosition = Vector3.zero; liftMoving.localScale = Vector3.one; var liftArt = ById(scene, 986552299).GetComponent<SpriteRenderer>(); liftArt.transform.SetParent(liftMoving, false); liftArt.name = "그림"; Fit(liftArt, tile, lift.position, new Vector2(4.148438f, 1.078125f));
            var liftBox = New(lift, "충돌(중립 고정)").gameObject.AddComponent<BoxCollider2D>(); SetBox(liftBox, new Vector2(20.425781f, -.08f), new Vector2(4.148438f, .16f));
            var column = ById(scene, 999457431).GetComponent<SpriteRenderer>(); column.transform.SetParent(lift, true); column.name = "승강 기둥(그림)"; Fit(column, Sprite("Assets/Resources/img/Tiles/Tiles.png", "Tiles_5"), new Vector2(20.425781f, -2.5f), new Vector2(.8f, 4), -1, false);
            Device(ById(scene, 1440255727).GetComponent<SpriteRenderer>(), puzzle, "왼쪽 해킹 단말", Sprite("Assets/Resources/img/object/objects.png", "objects_7"), new Vector2(1.634766f, 5.888672f), new Vector2(1.042969f, .808594f), true);
            Device(ById(scene, 1931613436).GetComponent<SpriteRenderer>(), liftMoving, "승강장 해킹 단말", Sprite("Assets/Resources/img/object/objects.png", "objects_7"), new Vector2(19.195312f, .433594f), new Vector2(.890625f, .867188f), true);
            var red = Sprite(PanelPath, "제어판_빨강"); var green = Sprite(PanelPath, "제어판_초록");
            Device(ById(scene, 225464774).GetComponent<SpriteRenderer>(), puzzle, "왼쪽 제어판", red, new Vector2(1.523438f, 3.486328f), new Vector2(1.125f, 1.125f));
            Device(ById(scene, 1114779810).GetComponent<SpriteRenderer>(), puzzle, "오른쪽 제어판", red, new Vector2(4.289062f, 3.486328f), new Vector2(1.125f, 1.125f));
            Device(ById(scene, 77913048).GetComponent<SpriteRenderer>(), puzzle, "가운데 버튼", Sprite("Assets/Resources/img/object/objects.png", "objects_2"), new Vector2(9.814453f, .246094f), new Vector2(.527344f, .398438f));
            Device(ById(scene, 1667287586).GetComponent<SpriteRenderer>(), puzzle, "상부 배터리", Sprite("Assets/Resources/img/object/objects.png", "objects_6"), new Vector2(21.005859f, 8.935547f), new Vector2(.410156f, .738281f)); ById(scene, 1667287586).GetComponent<SpriteRenderer>().color = new Color(1, 1, 1, 0);
            var trap = New(puzzle, "가시장치").gameObject.AddComponent<SpriteRenderer>(); Device(trap, puzzle, "가시장치", Sprite("Assets/Resources/img/Tiles/Trap.png", "Trap_0"), new Vector2(16.875f, 6.433594f), new Vector2(2.8125f, 3.421875f));
            var upperLaser = ById(scene, 2033513923).GetComponent<SpriteRenderer>(); upperLaser.transform.SetParent(puzzle, true); upperLaser.name = "상부 레이저"; Fit(upperLaser, Sprite("Assets/Resources/img/Tiles/Laser.png", "Laser_0"), new Vector2(11.25f, 8.818359f), new Vector2(22.5f, .117188f), 8, false);
            var lowerLaser = New(puzzle, "하부 레이저").gameObject.AddComponent<SpriteRenderer>(); Fit(lowerLaser, upperLaser.sprite, new Vector2(11.25f, 4.494141f), new Vector2(22.5f, .117188f), 8, false);
            // External entry and exit are different from the internal visual lift. Preserve nested components and references.
            var slots = Elevators(scene); Need(slots.All(e => e.status == "NATIVE_REFERENCE_PASS"), "Current Yesod native Elevator gate"); var exit = All(scene).Single(t => t.name == "YESOD_EXIT_PROPOSED_ORIGINAL_ELEVATOR_N02");
            Undo.RecordObject(exit, "NOV03 exit root alignment"); exit.position += new Vector3(25.5f - slots[1].floorBounds[0], 8.566406f - (slots[1].floorBounds[1] + slots[1].floorBounds[4] / 2), 0); PrefabUtility.RecordPrefabInstancePropertyModifications(exit);
            foreach (long id in new long[] { 751397659, 974642955, 826932509 }) { var b = ById(scene, id).GetComponent<BoxCollider2D>(); Undo.RecordObject(b, "NOV03 outdated duplicate deck"); b.enabled = false; rec.changes.Add("Disabled Scene-local outdated/duplicate Deck box " + Id(b) + "; functional Floor/effector unchanged"); }
            var bg = ById(scene, 554069282).GetComponent<SpriteRenderer>(); bg.transform.position = new Vector3(11.25f, 4.488281f, 10); bg.transform.localScale = Vector3.one * (22.5f / bg.sprite.bounds.size.x); bg.sortingOrder = -20;
            var extension = ById(scene, 1023450303).GetComponent<SpriteRenderer>(); extension.enabled = false;
            // Reuse two existing inactive bay backgrounds for off-map access; no new background assets.
            foreach (var a in new[] { new { id = 331808904L, x = -8.75f, y = 4.488281f }, new { id = 282558787L, x = 31.25f, y = 4.488281f } })
            { var sr = ById(scene, a.id).GetComponent<SpriteRenderer>(); sr.transform.parent.gameObject.SetActive(true); sr.gameObject.SetActive(true); sr.enabled = true; sr.transform.position = new Vector3(a.x, a.y, 10); sr.transform.localScale = bg.transform.localScale; sr.sortingOrder = -21; }
            BuildClip(puzzle, moving, liftMoving, trap, upperLaser, lowerLaser, red, green);
            rec.changes.Add("Latest 11/03 PNG + 44F geometry and visual state remake; 13 existing platform/device role roots reused. Existing IDs retained, no PZ02 subtree deactivation, no runtime logic.");
            rec.changes.Add("Simplified editable role roots under 00_MAP_EDIT/우찬 퍼즐. Existing unused art Renderers disabled; ambiguous DEV/old Preview dependencies retained as HOLD, no deletion.");
            rec.changes.Add("Exit original Elevator root only aligned to Floor top8.566406 x25.5. Source Prefab, nested Floor components/NetworkObject properties preserved.");
            Undo.CollapseUndoOperations(undo); Save(scene, identity, 1); Log("I02 SourceA saved/reopened; Preview/runtime separate");
        }
        static void BuildClip(Transform puzzle, Transform moving, Transform lift, SpriteRenderer trap, SpriteRenderer upper, SpriteRenderer lower, Sprite red, Sprite green)
        {
            Need(!File.Exists(ClipPath), "Do not overwrite a finished clip"); var clip = new AnimationClip { name = "우찬 NOV03 원본44상태 시각 Preview", frameRate = 10, wrapMode = WrapMode.ClampForever };
            string P(Transform t) { return AnimationUtility.CalculateTransformPath(t, puzzle); }
            Vector3 m = moving.localPosition, l = lift.localPosition, tr = trap.transform.localPosition;
            PositionCurve(clip, P(moving), f => m + Vector3.left * (f < 26 ? 0 : f == 26 ? 250 * 3f / 256 : 508 * 3f / 256));
            PositionCurve(clip, P(lift), f => l + Vector3.up * (f < 13 ? 0 : 446 * 3f / 256));
            PositionCurve(clip, P(trap.transform), f => tr + Vector3.down * ((f < 7 ? 0 : f == 7 ? 154 : f == 8 ? 448 : 688) * 3f / 256));
            AlphaCurve(clip, P(upper.transform), f => f < 30 ? 1 : 0); AlphaCurve(clip, P(lower.transform), f => f < 11 ? 1 : 0);
            AlphaCurve(clip, "하부 발판 2/그림", f => f < 4 ? 0 : 1); AlphaCurve(clip, "하부 발판 4/그림", f => f < 4 ? 0 : 1); AlphaCurve(clip, "상부 배터리", f => f >= 41 ? 1 : 0);
            foreach (var a in new[] { new { name = "왼쪽 제어판", frame = 41 }, new { name = "오른쪽 제어판", frame = 12 } })
                AnimationUtility.SetObjectReferenceCurve(clip, EditorCurveBinding.PPtrCurve(a.name, typeof(SpriteRenderer), "m_Sprite"), new[] { new ObjectReferenceKeyframe { time = 0, value = red }, new ObjectReferenceKeyframe { time = a.frame * .1f, value = green }, new ObjectReferenceKeyframe { time = 4.4f, value = green } });
            var setting = AnimationUtility.GetAnimationClipSettings(clip); setting.loopTime = false; AnimationUtility.SetAnimationClipSettings(clip, setting); AssetDatabase.CreateAsset(clip, ClipPath); AssetDatabase.SaveAssetIfDirty(clip);
            Need(AnimationUtility.GetCurveBindings(clip).All(b => b.type == typeof(Transform) || b.type == typeof(SpriteRenderer)), "Visual-only clip binding");
        }
        public static void FinishYesodArt()
        {
            var s = OpenApproved(1); var identity = Read<Identity>("CURRENT_IDENTITY_MAP.json"); var r = identity.scenes[1]; Need(!r.changes.Contains("I02_ALPHA_AND_SLICE_FINISH"), "Already finished; inspect rather than repeat");
            var art = ById(s, 35345975).GetComponent<SpriteRenderer>(); Undo.RecordObject(art.transform, "NOV03 slice scale"); art.transform.localScale = Vector3.one;
            var puzzle = All(s).Single(t => t.name == "우찬 퍼즐"); var trap = All(s).Single(t => t.name == "가시장치");
            // Trap has alpha=1 padding around the bezel. The visible alpha>16 shape is 244x292 px.
            float scale = Mathf.Min(2.8125f / 2.44f, 3.421875f / 2.92f); trap.localScale = Vector3.one * scale; trap.position = new Vector3(16.875f - .04f * scale, 6.433594f - .095f * scale, 0);
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath); Vector3 initial = trap.localPosition;
            PositionCurve(clip, AnimationUtility.CalculateTransformPath(trap, puzzle), f => initial + Vector3.down * ((f < 7 ? 0 : f == 7 ? 154 : f == 8 ? 448 : 688) * 3f / 256));
            var col = All(s).Single(t => t.name == "승강 기둥(그림)"); Vector3 cscale = col.localScale; string path = AnimationUtility.CalculateTransformPath(col, puzzle);
            foreach (int axis in new[] { 0, 1, 2 })
            {
                int a = axis; var curve = new AnimationCurve(Enumerable.Range(0, 45).Select(f => new Keyframe(f * .1f, a == 1 ? cscale.y * (f < 13 ? 1 : (4 + 446 * 3f / 256) / 4) : cscale[a])).ToArray());
                for (int k = 0; k < curve.length; k++) { AnimationUtility.SetKeyLeftTangentMode(curve, k, AnimationUtility.TangentMode.Constant); AnimationUtility.SetKeyRightTangentMode(curve, k, AnimationUtility.TangentMode.Constant); }
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalScale." + "xyz"[axis]), curve);
            }
            AssetDatabase.SaveAssetIfDirty(clip); r.changes.Add("I02_ALPHA_AND_SLICE_FINISH"); r.changes.Add("Small upper platform2 sliced Transform normalized; Trap alpha>16 bezel scaled uniformly; internal lift column visual extends at F13 with no physics binding."); Save(s, identity, 1);
        }
        public static void Save(Scene scene, Identity identity, int i)
        {
            ProtectedGuard(); Need(scene.path == ScenePath(i), "Exact approved Scene"); var r = identity.scenes[i]; Need(Hash(scene.path) == r.savedSha, "Concurrent Scene disk change");
            EditorSceneManager.MarkSceneDirty(scene); Need(EditorSceneManager.SaveScene(scene, scene.path), "Save failed"); r.savedSha = Hash(scene.path); scene = EditorSceneManager.OpenScene(scene.path, OpenSceneMode.Single); r.after = All(scene).Select(Record).ToArray(); r.reopened = true; r.nativeClean = !scene.isDirty;
            r.missingScripts = All(scene).Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)); r.missingSprites = All(scene).Select(t => t.GetComponent<SpriteRenderer>()).Count(sr => sr != null && sr.enabled && sr.sprite == null); Need(r.missingScripts == 0 && r.missingSprites == 0 && r.nativeClean && AssetDatabase.AssetPathToGUID(scene.path) == r.guid, "Native saved regression");
            Write("CURRENT_IDENTITY_MAP.json", identity); var p = Read<PhysicsQA>("COLLIDER_QA.json"); p.after.RemoveAll(x => x.scene == scene.path); p.after.AddRange(PhysicsRows(scene)); Write("COLLIDER_QA.json", p); var e = Read<ElevatorQA>("NATIVE_ELEVATOR_QA.json"); e.after.RemoveAll(x => x.scene == scene.path); e.after.AddRange(Elevators(scene)); Write("NATIVE_ELEVATOR_QA.json", e);
            File.WriteAllText(Run + "/CHECKPOINT.md", "# NOV03 체크포인트\n\n" + Names[i] + " SAVED_VERIFIED / native reopened.\nSHA256 " + r.savedSha + "\nNo full rebuild. Continue remaining QA/Preview/manual from CURRENT_IDENTITY_MAP.json.\n", new UTF8Encoding(false)); ProtectedGuard(); Stable();
        }
        public static Camera MapCamera(Scene scene) { return All(scene).Select(t => t.GetComponent<Camera>()).First(c => c != null); }
        public static Texture2D Render(Camera camera, Vector2 center, float halfHeight, float aspect = 16f / 9, int height = 1080)
        {
            string json = EditorJsonUtility.ToJson(camera); Vector3 position = camera.transform.position; Quaternion rotation = camera.transform.rotation; var target = camera.targetTexture; float oldAspect = camera.aspect;
            var rt = new RenderTexture(Mathf.RoundToInt(height * aspect), height, 24); var active = RenderTexture.active;
            try
            {
                camera.transform.SetPositionAndRotation(new Vector3(center.x, center.y, position.z), Quaternion.identity); camera.orthographic = true; camera.orthographicSize = halfHeight; camera.aspect = aspect;
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt }); RenderTexture.active = rt;
                var image = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); image.Apply(); return image;
            }
            finally { RenderTexture.active = active; camera.targetTexture = target; camera.transform.SetPositionAndRotation(position, rotation); EditorJsonUtility.FromJsonOverwrite(json, camera); camera.aspect = oldAspect; rt.Release(); Object.DestroyImmediate(rt); }
        }
        public static void Image(string filename, Texture2D image)
        {
            string[] allowed = { "MAP00_Game.png", "MAP01_Game.png", "MAP02_Game.png", "MAP03_Game.png", "MAP01_Preview_F00.png", "MAP01_Preview_F11.png", "MAP01_Preview_F13.png", "MAP01_Preview_F26.png", "MAP01_Preview_F30.png", "MAP01_Preview_F41.png" };
            Need(allowed.Contains(filename), "Exact image output"); File.WriteAllBytes(Run + "/visual/" + filename, image.EncodeToPNG());
        }
        public static Vector2 Center(int i) { return i == 0 ? new Vector2(64, 16) : i == 1 ? new Vector2(11.25f, 4.488281f) : i == 2 ? new Vector2(12, 3) : new Vector2(3, 4); }
        public static float HalfHeight(int i) { return i == 0 ? 20 : i == 1 ? 6.328125f : i == 2 ? 9 : 15; }
        public static void TrimNetzachArt()
        {
            var s = OpenApproved(3); var identity = Read<Identity>("CURRENT_IDENTITY_MAP.json"); var r = identity.scenes[3]; Need(!r.changes.Contains("I04_RIGHT_ART_TRIM"), "Already trimmed; inspect rather than repeat"); string physics = PhysicsSignature(s);
            var boundary = ById(s, 1315056296).GetComponent<SpriteRenderer>(); float right = boundary.bounds.max.x;
            Need(right > 21 && right < 23, "Current user outer frame boundary");
            foreach (long id in new long[] { 1687242051, 814113966 })
            {
                var sr = ById(s, id).GetComponent<SpriteRenderer>(); Need(sr.drawMode == SpriteDrawMode.Tiled && Mathf.Abs(sr.transform.eulerAngles.z) < .001f && sr.GetComponents<Component>().All(c => c is Transform || c is SpriteRenderer), "Horizontal pure art leaf only");
                float left = sr.bounds.min.x, oldWidth = sr.bounds.size.x; Need(sr.bounds.max.x > right + 1, "Confirmed right visual overhang"); Undo.RecordObject(sr, "NOV03 right art trim"); Undo.RecordObject(sr.transform, "NOV03 right art trim");
                sr.size = new Vector2(sr.size.x * (right - left) / oldWidth, sr.size.y); sr.transform.position += Vector3.right * ((left + right) / 2 - sr.bounds.center.x);
                Need(Mathf.Abs(sr.bounds.min.x - left) < .001f && Mathf.Abs(sr.bounds.max.x - right) < .001f, "Left edge preserved; right edge matches current outer wall");
                r.changes.Add("Trimmed horizontal art GO " + id + " only: minX=" + left + " unchanged; maxX=" + right + ". No parent/Collider/Floor/gate/block changes.");
            }
            Need(PhysicsSignature(s) == physics, "Netzach physics must remain byte-equivalent natively"); r.changes.Add("I04_RIGHT_ART_TRIM"); Save(s, identity, 3);
        }
        public static void Review(int i)
        {
            var scene = OpenApproved(i); var before = Pose(scene); var physics = PhysicsSignature(scene); string sha = Hash(scene.path); bool dirty = scene.isDirty; var camera = MapCamera(scene);
            float aspect = i == 0 ? 4.2f : i == 2 ? 3.333333f : 16f / 9; var image = Render(camera, Center(i), HalfHeight(i), aspect); Image("MAP0" + i + "_Game.png", image); Object.DestroyImmediate(image);
            Need(Pose(scene) == before && PhysicsSignature(scene) == physics && scene.isDirty == dirty && Hash(scene.path) == sha, "Render changed source state");
            var sv = SceneView.lastActiveSceneView; if (sv != null) { sv.in2DMode = true; sv.Frame(new Bounds(Center(i), new Vector3(HalfHeight(i) * 2 * aspect, HalfHeight(i) * 2, 1)), true); }
            Selection.activeObject = i == 1 ? All(scene).Single(t => t.name == "우찬 퍼즐").gameObject : scene.GetRootGameObjects()[0];
            var identity = Read<Identity>("CURRENT_IDENTITY_MAP.json"); var r = identity.scenes[i]; r.after = All(scene).Select(Record).ToArray(); r.reopened = true; r.nativeClean = !scene.isDirty; Write("CURRENT_IDENTITY_MAP.json", identity);
            var p = Read<PhysicsQA>("COLLIDER_QA.json"); p.after.RemoveAll(x => x.scene == scene.path); p.after.AddRange(PhysicsRows(scene)); Write("COLLIDER_QA.json", p); var e = Read<ElevatorQA>("NATIVE_ELEVATOR_QA.json"); e.after.RemoveAll(x => x.scene == scene.path); e.after.AddRange(Elevators(scene)); Write("NATIVE_ELEVATOR_QA.json", e);
            Stable(); ProtectedGuard(); Log("I06 Native reopen/render " + Names[i] + "; temporary overview camera restored; saved camera NOT reframed");
        }
        public static string Pose(Scene scene)
        {
            return string.Join("\n", All(scene).Select(t => { var sr = t.GetComponent<SpriteRenderer>(); return Id(t) + "|" + t.localPosition.ToString("F6") + t.localRotation.ToString("F6") + t.localScale.ToString("F6") + "|" + t.gameObject.activeSelf + "|" + (sr == null ? "" : AssetId(sr.sprite) + sr.enabled + sr.color.ToString("F6") + sr.size.ToString("F6") + sr.sortingOrder); }).OrderBy(s => s));
        }
        public static string PhysicsSignature(Scene scene) { return string.Join("\n", PhysicsRows(scene).Select(r => r.id + r.serialized + string.Join(",", r.position) + string.Join(",", r.scale)).OrderBy(s => s)); }
        public static void Inspect(string kind)
        {
            Stable(); var scene = SceneManager.GetActiveScene(); Transform target = null;
            if (kind == "Trap") target = All(scene).Single(t => t.name == "가시장치");
            if (kind == "Laser") target = All(scene).Single(t => t.name == "하부 레이저");
            if (kind == "Panel") target = All(scene).Single(t => t.name == "오른쪽 제어판");
            if (kind == "Elevator") target = All(scene).First(t => t.GetComponents<MonoBehaviour>().Any(c => c != null && c.GetType().Name == "Elevator"));
            Need(target != null, "Inspection target in current Scene"); Selection.activeObject = target.gameObject; if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.Frame(new Bounds(target.position, new Vector3(10, 7, 1)), true);
        }
        public static void SelectColliders()
        {
            Stable(); var scene = SceneManager.GetActiveScene(); Selection.objects = All(scene).SelectMany(t => t.GetComponents<Collider2D>()).Select(c => (Object)c.gameObject).Distinct().ToArray();
            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.drawGizmos = true; SceneView.RepaintAll();
        }
        [MenuItem("Tools/NOV03 Uchan/Open Control Panel")]
        public static void Open() { N03ControlPanel.Open(); }
    }
    public class N03ControlPanel : EditorWindow
    {
        string status = "수동 실행 전용 / PLAY 및 Runtime 구현 없음"; Vector2 scroll;
        public static void Open() { var w = GetWindow<N03ControlPanel>(false, "NOV03 Uchan"); w.minSize = new Vector2(470, 300); var r = EditorGUIUtility.GetMainWindowPosition(); w.position = new Rect(r.x + 640, r.y + 205, 470, 610); w.Show(); }
        void Run(Action action) { try { action(); status = "완료: " + DateTime.Now.ToString("HH:mm:ss"); } catch (Exception e) { status = e.ToString(); PCS_MapUchanNov03.Log("HOLD " + e); } Repaint(); }
        void OnGUI()
        {
            EditorGUILayout.LabelField("NOV03 승인 범위 전용", EditorStyles.boldLabel); EditorGUILayout.HelpBox("현재 사용자 저장본 보호. Native 검사부터 실행하세요. 전체 Builder/Play 자동 실행 없음.", MessageType.Info);
            if (GUILayout.Button("I00 현재 Native 상태·참조 기록 (한 번)", GUILayout.Height(42))) Run(PCS_MapUchanNov03.Audit);
            if (GUILayout.Button("I01 원본 제어판 Atlas 임포트", GUILayout.Height(32))) Run(PCS_MapUchanNov03.ImportPanels);
            if (GUILayout.Button("I02 최신 우찬 시각 리메이크 (한 번)", GUILayout.Height(38))) Run(PCS_MapUchanNov03.BuildYesod);
            if (GUILayout.Button("I02 확인된 알파·Slice 마감 (한 번)", GUILayout.Height(28))) Run(PCS_MapUchanNov03.FinishYesodArt);
            if (GUILayout.Button("I04 네짜흐 수평 아트 오른쪽 끝만 보정 (한 번)", GUILayout.Height(28))) Run(PCS_MapUchanNov03.TrimNetzachArt);
            EditorGUILayout.BeginHorizontal(); for (int i = 0; i < 4; i++) { int n = i; if (GUILayout.Button("검수 MAP0" + i, GUILayout.Height(30))) Run(() => PCS_MapUchanNov03.Review(n)); } EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("44F / 문 / 압착기 / Elevator Preview", GUILayout.Height(30))) N03PreviewWindow.Open();
            if (GUILayout.Button("현재 씬 Native Collider 표시")) Run(PCS_MapUchanNov03.SelectColliders);
            EditorGUILayout.BeginHorizontal(); foreach (string k in new[] { "Trap", "Laser", "Panel", "Elevator" }) { string key = k; if (GUILayout.Button(k + " 선택")) Run(() => PCS_MapUchanNov03.Inspect(key)); } EditorGUILayout.EndHorizontal();
            scroll = EditorGUILayout.BeginScrollView(scroll); EditorGUILayout.SelectableLabel(status, EditorStyles.wordWrappedLabel, GUILayout.MinHeight(120)); EditorGUILayout.EndScrollView();
        }
    }
    public class N03PreviewWindow : EditorWindow
    {
        [Serializable] public class Result { public string kind, scene, sha, stopCause, status, target; public bool poseRestored, colliderRestored, dirtyRestored, shaRestored, sampledStateAssertions; public float[] samples; }
        [Serializable] public class Results { public List<Result> records = new List<Result>(); public string runtime = "NOT_TESTED", network = "NOT_TESTED"; }
        Transform target, proxyFloor; SpriteRenderer proxyColumn; PreviewRenderUtility utility; Texture2D picture; Vector3 floorStart; Vector2 columnStart; string scenePath, hash, pose, physics; bool ownsAnimation, dirty; int kind, slot, doorClip; float time; readonly List<float> samples = new List<float>(); string status = "Preview는 외형만 재생합니다. Collider는 정적 중립 상태입니다.";
        readonly Dictionary<SpriteRenderer, Sprite> initialSprites = new Dictionary<SpriteRenderer, Sprite>();
        public static void Open()
        {
            var w = GetWindow<N03PreviewWindow>(false, "NOV03 Preview"); w.minSize = new Vector2(680, 500);
            var main = EditorGUIUtility.GetMainWindowPosition();
            float width = Mathf.Clamp(main.width - 60, 680, 900), height = Mathf.Clamp(main.height - 90, 500, 760);
            w.position = new Rect(main.center.x - width / 2, main.center.y - height / 2, width, height); w.Show();
        }
        void OnEnable() { Selection.selectionChanged += SelectionChanged; AssemblyReloadEvents.beforeAssemblyReload += ReloadStop; EditorApplication.playModeStateChanged += PlayChange; }
        void OnDisable() { Selection.selectionChanged -= SelectionChanged; AssemblyReloadEvents.beforeAssemblyReload -= ReloadStop; EditorApplication.playModeStateChanged -= PlayChange; Stop("WINDOW_CLOSED"); }
        void SelectionChanged() { Stop("SELECTION_CHANGED"); }
        void ReloadStop() { Stop("ASSEMBLY_RELOAD"); }
        void PlayChange(PlayModeStateChange state) { Stop("PLAY_MODE_CHANGE"); }
        void Do(Action action) { try { action(); } catch (Exception e) { try { Stop("ERROR"); } finally { status = e.ToString(); PCS_MapUchanNov03.Log("PREVIEW HOLD " + e); } } Repaint(); }
        AnimationClip Clip()
        {
            string path = kind == 0 ? PCS_MapUchanNov03.ClipPath : kind == 1 ? PCS_MapUchanNov03.Base + "Animation/R4/" + new[] { "Door_Open_R4.anim", "Door_Close_R4.anim", "Door_ClosedIdle_R4.anim" }[doorClip] : PCS_MapUchanNov03.Base + "Animation/N02Remake/Hod_CrusherCycle_N02.anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path); PCS_MapUchanNov03.Need(clip != null, "Existing approved clip " + path); return clip;
        }
        void Begin(int mode)
        {
            Stop("MODE_SWITCH"); PCS_MapUchanNov03.Stable(); PCS_MapUchanNov03.ProtectedGuard(); kind = mode; doorClip = 0; samples.Clear(); time = 0;
            if (mode == 0) { var s = PCS_MapUchanNov03.OpenForPreview(1); target = PCS_MapUchanNov03.All(s).Single(t => t.name == "우찬 퍼즐"); }
            else if (mode == 2) { var s = PCS_MapUchanNov03.OpenForPreview(2); target = PCS_MapUchanNov03.All(s).Single(t => t.name == "PCS_HodCrusher_N02_EDITOR_PREVIEW_ONLY_NO_DAMAGE"); }
            else
            {
                var e = PCS_MapUchanNov03.Read<PCS_MapUchanNov03.ElevatorQA>("NATIVE_ELEVATOR_QA.json").before[slot]; PCS_MapUchanNov03.Need(e.status == "NATIVE_REFERENCE_PASS", "Elevator slot gate");
                var s = EditorSceneManager.OpenScene(e.scene, OpenSceneMode.Single); target = PCS_MapUchanNov03.All(s).Single(t => PCS_MapUchanNov03.Id(t.gameObject) == e.id); if (mode == 1) target = target.Find("Floor/DoorRoot");
                PCS_MapUchanNov03.Need(PCS_MapUchanNov03.Elevators(s).Single(r => r.id == e.id).status == "NATIVE_REFERENCE_PASS", "Current native slot recheck");
            }
            PCS_MapUchanNov03.Need(target != null, "Preview target"); scenePath = target.gameObject.scene.path; hash = PCS_MapUchanNov03.Hash(scenePath); pose = PCS_MapUchanNov03.Pose(target.gameObject.scene); physics = PCS_MapUchanNov03.PhysicsSignature(target.gameObject.scene); dirty = target.gameObject.scene.isDirty;
            initialSprites.Clear(); foreach (var sr in target.GetComponentsInChildren<SpriteRenderer>(true)) initialSprites.Add(sr, sr.sprite);
            if (kind == 3)
            {
                utility = new PreviewRenderUtility(); var proxy = Clone(target, null); utility.AddSingleGO(proxy); proxyFloor = proxy.transform.Find("Floor"); proxyColumn = proxy.transform.Find("Column").GetComponent<SpriteRenderer>(); floorStart = proxyFloor.localPosition; columnStart = proxyColumn.size;
                var lamp = new GameObject("Preview Light") { hideFlags = HideFlags.HideAndDontSave }; lamp.transform.position = target.position + Vector3.up; var light = lamp.AddComponent<Light2D>(); light.lightType = Light2D.LightType.Point; light.pointLightInnerRadius = 100; light.pointLightOuterRadius = 101; light.pointLightInnerAngle = light.pointLightOuterAngle = 360; utility.AddSingleGO(lamp);
                var camera = utility.camera; camera.CopyFrom(PCS_MapUchanNov03.MapCamera(target.gameObject.scene)); camera.scene = proxy.scene; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.08f, .12f, .16f);
                PCS_MapUchanNov03.Need(proxy.GetComponentsInChildren<Component>(true).All(c => c is Transform || c is SpriteRenderer), "Proxy must not contain Collider/Network/Runtime component");
            }
            else { AnimationMode.StartAnimationMode(); ownsAnimation = true; }
            Sample(0); status = "EDIT/PREVIEW ONLY. 실제 동작/네트워크/승객/피해는 NOT_TESTED.";
        }
        GameObject Clone(Transform source, Transform parent)
        {
            var go = new GameObject(source.name) { hideFlags = HideFlags.HideAndDontSave }; var t = go.transform; t.SetParent(parent, false); if (parent == null) { t.SetPositionAndRotation(source.position, source.rotation); t.localScale = source.lossyScale; } else { t.localPosition = source.localPosition; t.localRotation = source.localRotation; t.localScale = source.localScale; }
            var a = source.GetComponent<SpriteRenderer>(); if (a != null) { var b = go.AddComponent<SpriteRenderer>(); b.sprite = a.sprite; b.sharedMaterial = a.sharedMaterial; b.color = a.color; b.sortingLayerID = a.sortingLayerID; b.sortingOrder = a.sortingOrder; b.drawMode = a.drawMode; b.size = a.size; b.flipX = a.flipX; b.flipY = a.flipY; b.enabled = a.enabled; }
            foreach (Transform child in source) Clone(child, t); go.SetActive(source.gameObject.activeSelf); return go;
        }
        void Sample(float value)
        {
            PCS_MapUchanNov03.Need(target != null, "Start Preview first"); if (float.IsNaN(value)) throw new ArgumentException("Controlled sampling failure");
            // All 44F keys are exact stepped samples. Snap decimal slider/button values to the same float key times.
            if (kind == 0) value = Mathf.Clamp(Mathf.RoundToInt(value * 10), 0, 43) * .1f; time = value; samples.Add(value);
            if (kind == 3) { float offset = 3 * Mathf.Sin(Mathf.PI * value / 2); proxyFloor.localPosition = floorStart + Vector3.up * offset; proxyColumn.size = columnStart + Vector2.up * offset; }
            else
            {
                AnimationMode.BeginSampling();
                try
                {
                    var clip = Clip(); AnimationMode.SampleAnimationClip(target.gameObject, clip, value);
                    // Evaluate the clip's PPtr keys explicitly as part of this Editor sampler. No Animator or runtime component is added.
                    foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                    {
                        PCS_MapUchanNov03.Need(binding.type == typeof(SpriteRenderer) && binding.propertyName == "m_Sprite", "Only visual Sprite PPtr curves");
                        var sr = target.Find(binding.path).GetComponent<SpriteRenderer>(); var keys = AnimationUtility.GetObjectReferenceCurve(clip, binding); var key = keys.Last(k => k.time <= value + .00001f);
                        AnimationMode.AddPropertyModification(binding, new PropertyModification { target = sr, propertyPath = "m_Sprite", objectReference = initialSprites[sr] }, false); sr.sprite = key.value as Sprite;
                        PCS_MapUchanNov03.Need(sr.sprite == key.value, "Sprite key sample failed " + binding.path);
                    }
                }
                finally { AnimationMode.EndSampling(); }
                if (kind == 0)
                {
                    int frame = Mathf.RoundToInt(value * 10); var red = PCS_MapUchanNov03.Sprite(PCS_MapUchanNov03.PanelPath, "제어판_빨강"); var green = PCS_MapUchanNov03.Sprite(PCS_MapUchanNov03.PanelPath, "제어판_초록");
                    PCS_MapUchanNov03.Need(target.Find("오른쪽 제어판").GetComponent<SpriteRenderer>().sprite == (frame >= 12 ? green : red) && target.Find("왼쪽 제어판").GetComponent<SpriteRenderer>().sprite == (frame >= 41 ? green : red), "Observed red/green frame boundary");
                    PCS_MapUchanNov03.Need(Mathf.Abs(target.Find("오른쪽 승강 발판/움직이는 그림").localPosition.y - (frame >= 13 ? 446 * 3f / 256 : 0)) < .0001f, "Lift frame boundary");
                    PCS_MapUchanNov03.Need(Mathf.Abs(target.Find("중앙 이동 발판/움직이는 그림").localPosition.x + (frame < 26 ? 0 : frame == 26 ? 250 * 3f / 256 : 508 * 3f / 256)) < .0001f, "Shuttle frame boundary");
                }
            }
            PCS_MapUchanNov03.Need(PCS_MapUchanNov03.PhysicsSignature(target.gameObject.scene) == physics, "Preview changed actual Collider/Rigidbody");
            if (picture != null) DestroyImmediate(picture);
            var center = kind == 0 ? PCS_MapUchanNov03.Center(1) : (Vector2)target.position + (kind == 3 ? new Vector2(0, 3) : kind == 1 ? new Vector2(0, 1.5f) : new Vector2(0, -1.2f));
            picture = PCS_MapUchanNov03.Render(kind == 3 ? utility.camera : PCS_MapUchanNov03.MapCamera(target.gameObject.scene), center, kind == 0 ? 6.328125f : kind == 1 ? 3 : 6, 16f / 9, 720); SceneView.RepaintAll(); Repaint();
        }
        void Stop(string cause)
        {
            if (target == null && !ownsAnimation && utility == null) return;
            var scene = target == null ? default : target.gameObject.scene; string path = target == null ? "" : PCS_MapUchanNov03.Full(target);
            try { if (ownsAnimation) AnimationMode.StopAnimationMode(); }
            finally { ownsAnimation = false; if (utility != null) { utility.Cleanup(); utility = null; proxyFloor = null; proxyColumn = null; } }
            if (target != null)
            {
                var r = new Result { kind = new[] { "UCHAN_44F_VISUAL_ONLY", "DOOR_VISUAL_ONLY", "CRUSHER_NO_DAMAGE", "ELEVATOR_RENDER_PROXY_ONLY" }[kind], scene = scenePath, sha = hash, target = path, stopCause = cause, samples = samples.ToArray(), poseRestored = PCS_MapUchanNov03.Pose(scene) == pose, colliderRestored = PCS_MapUchanNov03.PhysicsSignature(scene) == physics, dirtyRestored = scene.isDirty == dirty, shaRestored = PCS_MapUchanNov03.Hash(scenePath) == hash, sampledStateAssertions = kind == 0 && cause != "ERROR" };
                r.status = r.poseRestored && r.colliderRestored && r.dirtyRestored && r.shaRestored ? "PREVIEW_RESTORED_NATIVE_PASS" : "PREVIEW_RESTORE_HOLD"; var q = File.Exists(PCS_MapUchanNov03.Run + "/PREVIEW_QA.json") ? PCS_MapUchanNov03.Read<Results>("PREVIEW_QA.json") : new Results(); q.records.Add(r); PCS_MapUchanNov03.Write("PREVIEW_QA.json", q); target = null; status = r.status + " / " + cause; PCS_MapUchanNov03.Need(r.status == "PREVIEW_RESTORED_NATIVE_PASS", "Preview failed to restore; never save"); PCS_MapUchanNov03.ProtectedGuard();
            }
            if (picture != null) { DestroyImmediate(picture); picture = null; } Repaint();
        }
        void FullQA()
        {
            Begin(0); foreach (int f in new[] { 0, 11, 13, 26, 30, 41 }) { Sample(f * .1f); PCS_MapUchanNov03.Image("MAP01_Preview_F" + f.ToString("00") + ".png", picture); } for (int f = 0; f < 44; f++) Sample(f * .1f); Stop("ALL_44_SAMPLES");
            Begin(0); Sample(2.6f); Stop("SELECTION_CHANGE_EQUIVALENT_CHECK");
            Begin(0); try { Sample(float.NaN); } catch (ArgumentException) { Stop("CONTROLLED_SAMPLING_EXCEPTION"); }
            Begin(1); Sample(.6f); doorClip = 1; Sample(.6f); doorClip = 2; Sample(0); Stop("OPEN_CLOSE_IDLE");
            Begin(2); Sample(.6f); Sample(1.6f); Stop("CRUSHER_CYCLE");
            Begin(3); Sample(1); Sample(2); Stop("ELEVATOR_PROXY_CYCLE");
            status = "44F/문/압착기/Proxy 샘플 및 Stop 원복 완료. 실제 Play는 실행하지 않음.";
        }
        void AllElevatorsQA()
        {
            int count = PCS_MapUchanNov03.Read<PCS_MapUchanNov03.ElevatorQA>("NATIVE_ELEVATOR_QA.json").before.Count;
            for (slot = 0; slot < count; slot++)
            { Begin(1); Sample(.6f); doorClip = 1; Sample(.6f); doorClip = 2; Sample(0); Stop("SLOT_" + slot + "_OPEN_CLOSE_IDLE"); Begin(3); Sample(1); Sample(2); Stop("SLOT_" + slot + "_ELEVATOR_PROXY_CYCLE"); }
            slot = 0; status = count + "개 슬롯 문/시각 Proxy 원복 검증 완료. 실제 운행/네트워크 NOT_TESTED.";
        }
        void OnGUI()
        {
            EditorGUILayout.HelpBox("정적 Collider + 움직이는 그림. 실제 승객/피해/PCS/Fusion 기능은 개발자 담당입니다. 미저장 Scene이면 시작하지 않습니다.", MessageType.Info);
            var rows = PCS_MapUchanNov03.Read<PCS_MapUchanNov03.ElevatorQA>("NATIVE_ELEVATOR_QA.json").before; slot = EditorGUILayout.Popup("문 / Elevator 슬롯", Mathf.Clamp(slot, 0, rows.Count - 1), rows.Select(r => Path.GetFileNameWithoutExtension(r.scene) + " | " + r.path.Split('/').Last()).ToArray());
            EditorGUILayout.BeginHorizontal(); if (GUILayout.Button("우찬 44F")) Do(() => Begin(0)); if (GUILayout.Button("문 Open/Close")) Do(() => Begin(1)); if (GUILayout.Button("호드 압착기")) Do(() => Begin(2)); if (GUILayout.Button("Elevator 그림 Proxy")) Do(() => Begin(3)); EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("I06 전체 샘플·원복 QA (Play 없음)")) Do(FullQA);
            if (GUILayout.Button("I06 원본 Elevator 10곳 문 / Proxy 원복 검사")) Do(AllElevatorsQA);
            if (target != null)
            {
                EditorGUILayout.BeginHorizontal(); if (GUILayout.Button("기본 / 닫힘")) Do(() => { doorClip = 2; Sample(0); }); if (GUILayout.Button("열림 / 하강 / 상부")) Do(() => { doorClip = 0; Sample(kind == 0 ? 1.3f : kind == 1 ? .6f : kind == 2 ? .6f : 1); }); if (GUILayout.Button("닫힘 / 복귀")) Do(() => { doorClip = 1; Sample(kind == 0 ? 4.3f : kind == 1 ? .6f : kind == 2 ? 1.6f : 2); }); if (GUILayout.Button("STOP 원복")) Do(() => Stop("USER_STOP")); EditorGUILayout.EndHorizontal();
                float next = EditorGUILayout.Slider("시각 샘플 시간", time, 0, kind == 0 ? 4.3f : kind == 1 ? .6f : kind == 2 ? 1.6f : 2); if (Mathf.Abs(next - time) > .0001f) Do(() => Sample(next));
                if (kind == 0) { EditorGUILayout.BeginHorizontal(); foreach (int f in new[] { 0, 11, 13, 26, 30, 41 }) { int frame = f; if (GUILayout.Button("F" + f.ToString("00"))) Do(() => Sample(frame * .1f)); } EditorGUILayout.EndHorizontal(); }
                if (GUILayout.Button("QA: 선택 변경 원복")) Selection.activeObject = target.gameObject;
            }
            EditorGUILayout.LabelField(status, EditorStyles.wordWrappedLabel);
            if (picture != null)
            {
                // Keep the lowest platforms visible when the floating window is short.
                float width = position.width - 20;
                float height = Mathf.Min(width * picture.height / picture.width, Mathf.Max(100, position.height - 320));
                var rect = GUILayoutUtility.GetRect(width, height); GUI.DrawTexture(rect, picture, ScaleMode.ScaleToFit);
            }
        }
    }
}

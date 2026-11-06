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
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace PCS.MapPlanning.R4.EditorTools
{
    public static class PCS_MapElevatorR4
    {
        public const string Project = "C:/FOUR-PAWS-ASCEND-NEWPCS";
        public const string Run = "C:/FOUR-PAWS-PCS-BRIEF/CPLAN_ELEVATOR_R4_20261031/CIMPL_R4/run-001";
        public const string Base = "Assets/PCS/PCS MAP TRY/";
        public const string Helper = Base + "Editor/PCS_MapElevatorR4.cs";
        public const string DoorPrefab = Base + "Art/R4/Prefabs/PCS_ElevatorDoor_R4.prefab";
        public const string VisualPrefab = Base + "Art/R4/Prefabs/PCS_ElevatorVisual_R4.prefab";
        public const string Anim = Base + "Animation/R4/";
        public const string Atlas = Base + "Art/R4/Doors/DoorPanels_R4.png";
        static Material material;
        public static readonly string[] Names = { "PCS_MAP00_Malkuth_ONE MORE VERSION", "PCS_MAP01_Yesod_SourceA_Edit", "PCS_MAP01_Yesod_SourceB_Edit", "PCS_MAP02_Hod_Blockout", "PCS_MAP03_Netzach_Blockout" };
        [Serializable] public class FileRec { public string path, sha256, guid, operation; public long bytes; public bool editable; }
        [Serializable] public class Baseline { public FileRec[] records; }
        [Serializable] public class State { public string run; public string[] allowedProjectFiles; }
        [Serializable] public class Row
        {
            public string id, transformId, path, name, parent, sprite, material, drawMode;
            public string[] componentIds, componentTypes, references;
            public float[] matrix, position, scale, rendererBounds, colliderBounds, tint, size, box;
            public bool active, activeHierarchy, rendererEnabled, colliderEnabled, marker;
            public int layer, order;
        }
        [Serializable] public class Pair { public string scene; public Row[] before, after; public string[] changes; }
        [Serializable] public class Identity { public List<Pair> scenes = new List<Pair>(); }
        [Serializable] public class SceneQA { public string path, guid, sha256; public int objects, activeObjects, renderers, activeRenderers, colliders, missingScripts, missingSprites; public bool reopened; public Row[] rows; }
        [Serializable] public class QA
        {
            public List<string> completed = new List<string>();
            public List<SceneQA> scenes = new List<SceneQA>();
            public List<string> clipChecks = new List<string>(), toggleChecks = new List<string>();
            public string visualStatus = "PENDING", play = "NOT_TESTED", build = "NOT_TESTED", network = "NOT_TESTED";
        }
        public static T Read<T>(string name) { return JsonUtility.FromJson<T>(File.ReadAllText(Run + "/" + name)); }
        public static void Write(string name, object value) { File.WriteAllText(Run + "/" + name, JsonUtility.ToJson(value, true), new UTF8Encoding(false)); }
        public static string Hash(string path) { using (var f = File.OpenRead(path)) using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(f)).Replace("-", "").ToLowerInvariant(); }
        public static void Need(bool ok, string reason) { if (!ok) throw new InvalidOperationException("R4 GUARD: " + reason); }
        public static string PathFor(int i) { return Base + "Scenes/" + Names[i] + ".unity"; }
        public static QA GetQA() { return Read<QA>("QA_RESULTS.json"); }
        public static void PreviewGuard(bool requireMap = true)
        {
            Need(Path.GetFullPath(Application.dataPath + "/..").Replace('\\', '/') == Project, "Wrong project");
            Need(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !AnimationMode.InAnimationMode(), "Stable Edit mode required");
            for (int i = 0; i < SceneManager.sceneCount; i++) Need(!SceneManager.GetSceneAt(i).isDirty, "Save your edits manually before read-only preview: " + SceneManager.GetSceneAt(i).path);
            if (requireMap) Need(Enumerable.Range(0, 5).Any(i => SceneManager.GetActiveScene().path == PathFor(i)), "Approved PCS map scene required");
        }
        public static void Guard()
        {
            Need(Path.GetFullPath(Application.dataPath + "/..").Replace('\\', '/') == Project, "Wrong project");
            Need(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling, "Stable Edit mode required");
            Need(!AnimationMode.InAnimationMode(), "End preview before modifying assets or scenes");
            for (int i = 0; i < SceneManager.sceneCount; i++) Need(!SceneManager.GetSceneAt(i).isDirty, "Unsaved user scene: " + SceneManager.GetSceneAt(i).path);
            var state = Read<State>("STATE.json"); Need(state.run == "CIMPL_R4/run-001", "Run identity");
            var baseline = Read<Baseline>("PROTECTED_BASELINE.json"); var qa = GetQA();
            if (File.Exists(Run + "/CREATED_ASSETS.json")) foreach (var f in Read<Baseline>("CREATED_ASSETS.json").records.Where(x => !x.editable)) Need(File.Exists(f.path) && Hash(f.path) == f.sha256, "Owned asset changed after recorded stage: " + f.path);
            foreach (var f in baseline.records)
            {
                var saved = f.editable ? qa.scenes.SingleOrDefault(x => Path.GetFullPath(x.path) == Path.GetFullPath(f.path)) : null;
                Need(File.Exists(f.path) && Hash(f.path) == (saved == null ? f.sha256 : saved.sha256), "File changed outside recorded stage: " + f.path);
            }
            var known = new HashSet<string>(baseline.records.Select(x => Path.GetFullPath(x.path)), StringComparer.OrdinalIgnoreCase);
            var allowed = new HashSet<string>(state.allowedProjectFiles.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
            foreach (var d in new[] { "Assets", "Packages", "ProjectSettings" }) foreach (var f in Directory.GetFiles(d, "*", SearchOption.AllDirectories)) Need(known.Contains(Path.GetFullPath(f)) || allowed.Contains(Path.GetFullPath(f)), "Unexpected source file: " + f);
        }
        static string Id(Object o) { return o == null ? "MISSING" : GlobalObjectId.GetGlobalObjectIdSlow(o).ToString(); }
        static string AssetId(Object o) { if (o == null) return ""; AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o, out string g, out long id); return g + ":" + id; }
        public static string Full(Transform t) { return t.parent == null ? t.name : Full(t.parent) + "/" + t.name; }
        static float[] V(Vector3 v) { return new[] { v.x, v.y, v.z }; }
        static float[] B(Bounds b) { return new[] { b.center.x, b.center.y, b.center.z, b.size.x, b.size.y, b.size.z }; }
        static string[] References(Component[] components)
        {
            var refs = new List<string>();
            foreach (var c in components.Where(x => x != null && !(x is Transform)))
            {
                var so = new SerializedObject(c); var it = so.GetIterator();
                while (it.Next(true)) if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue != null) refs.Add(Id(c) + "/" + it.propertyPath + "=" + Id(it.objectReferenceValue));
            }
            return refs.OrderBy(x => x).ToArray();
        }
        public static Row Record(Transform t)
        {
            var s = t.GetComponent<SpriteRenderer>(); var c = t.GetComponent<BoxCollider2D>(); var m = t.localToWorldMatrix; var cs = t.GetComponents<Component>();
            var path = Full(t);
            return new Row { id = Id(t.gameObject), transformId = Id(t), path = path, name = t.name, parent = t.parent == null ? "" : Id(t.parent.gameObject), matrix = Enumerable.Range(0, 16).Select(i => m[i]).ToArray(), position = V(t.position), scale = V(t.lossyScale), active = t.gameObject.activeSelf, activeHierarchy = t.gameObject.activeInHierarchy, layer = t.gameObject.layer, componentIds = cs.Select(Id).ToArray(), componentTypes = cs.Select(x => x == null ? "MISSING" : x.GetType().FullName).ToArray(), references = References(cs), sprite = s == null ? "" : AssetId(s.sprite), material = s == null ? "" : AssetId(s.sharedMaterial), drawMode = s == null ? "" : s.drawMode.ToString(), rendererEnabled = s != null && s.enabled, colliderEnabled = c != null && c.enabled, rendererBounds = s == null ? new float[0] : B(s.bounds), colliderBounds = c == null ? new float[0] : B(c.bounds), tint = s == null ? new float[0] : new[] { s.color.r, s.color.g, s.color.b, s.color.a }, size = s == null ? new float[0] : new[] { s.size.x, s.size.y }, box = c == null ? new float[0] : new[] { c.size.x, c.size.y, c.offset.x, c.offset.y }, order = s == null ? 0 : s.sortingOrder, marker = path.Contains("90_DEV") || path.Contains("Anchor") || path.Contains("MARKER") || path.Contains("Marker") };
        }
        public static Row[] Snapshot(Scene s) { return s.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Transform>(true)).Select(Record).ToArray(); }
        public static Transform Root(Scene s) { Need(s.GetRootGameObjects().Length == 1, "One map root expected"); return s.GetRootGameObjects()[0].transform; }
        public static void Log(string message) { File.AppendAllText(Run + "/UnityEditor.log", DateTimeOffset.Now.ToString("o") + " " + message + "\n"); Debug.Log("R4 " + message); }
        static Transform Fresh(Transform parent, string name)
        {
            Need(parent == null || parent.Find(name) == null, "Existing object must not be overwritten: " + name);
            var t = new GameObject(name).transform; if (parent != null) t.SetParent(parent, false); return t;
        }
        static Transform Node(Transform parent, string name) { return parent.Find(name) ?? Fresh(parent, name); }
        public static Transform Find(Transform root, string suffix)
        {
            var matches = root.GetComponentsInChildren<Transform>(true).Where(x => Full(x).EndsWith("/" + suffix, StringComparison.Ordinal)).ToArray();
            Need(matches.Length == 1, "Unique object required: " + suffix + " count=" + matches.Length); return matches[0];
        }
        static Sprite Sprite(string path, string name)
        {
            var s = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().SingleOrDefault(x => x.name == name); Need(s != null, "Missing Sprite: " + name); return s;
        }
        public static Sprite Module(string name) { return Sprite(Base + "Art/R3/SpriteSheets/MetalModules_R3.png", name); }
        static Sprite Tile(string name) { return Sprite("Assets/Resources/img/Tiles/Tiles.png", name); }
        static Material Mat()
        {
            if (material == null) material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath("a97c105638bdf8b4a8650670310a4cd3"));
            Need(material != null, "Shared Sprite material missing"); return material;
        }
        static SpriteRenderer Art(Transform p, string name, Sprite sprite, Vector2 center, Vector2 size, Color color, int order)
        {
            var t = Fresh(p, name); var sr = t.gameObject.AddComponent<SpriteRenderer>(); sr.sprite = sprite; sr.sharedMaterial = Mat(); sr.color = color; sr.sortingOrder = order; sr.drawMode = SpriteDrawMode.Tiled; sr.size = size;
            t.localPosition = new Vector3(center.x - (.5f - sprite.pivot.x / sprite.rect.width) * size.x, center.y - (.5f - sprite.pivot.y / sprite.rect.height) * size.y, 0);
            return sr;
        }
        static void Folder(string path)
        {
            Need(!AssetDatabase.IsValidFolder(path) && !Directory.Exists(path), "New folder exists: " + path);
            Need(!string.IsNullOrEmpty(AssetDatabase.CreateFolder(Path.GetDirectoryName(path).Replace('\\', '/'), Path.GetFileName(path))), "CreateFolder failed: " + path);
        }
        static void Stage(string name, Action work)
        {
            Guard(); var q = GetQA(); if (q.completed.Contains(name)) { Log(name + " already complete; skipped"); return; }
            var copy = Run + "/backups/PCS_MapElevatorR4_before_" + name + ".cs"; if (!File.Exists(copy)) File.Copy(Helper, copy, false);
            work(); q = GetQA(); q.completed.Add(name); Write("QA_RESULTS.json", q); RecordCreated();
            File.WriteAllText(Run + "/CHECKPOINT.md", "# R4 체크포인트\n\n" + name + " data PASS; actual visual QA is recorded separately.\n", new UTF8Encoding(false)); Log(name + " data PASS");
        }
        static void RecordCreated()
        {
            var state = Read<State>("STATE.json");
            Write("CREATED_ASSETS.json", new Baseline { records = state.allowedProjectFiles.Where(File.Exists).Select(p => new FileRec { path = p, sha256 = Hash(p), guid = AssetDatabase.AssetPathToGUID(p.Replace('\\', '/').Replace(Project + "/", "")), bytes = new FileInfo(p).Length, editable = p.EndsWith(".unity", StringComparison.OrdinalIgnoreCase), operation = p.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) ? "EDIT_EXISTING" : "CREATE_NEW" }).ToArray() });
        }
        static AnimationCurve Curve(float[] times, float[] values)
        {
            var curve = new AnimationCurve(times.Select((t, i) => new Keyframe(t, values[i])).ToArray());
            for (int i = 0; i < curve.length; i++) { AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear); AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear); }
            curve.preWrapMode = WrapMode.ClampForever; curve.postWrapMode = WrapMode.ClampForever; return curve;
        }
        static void Clip(string name, float[] times, float[] left, float[] right)
        {
            string path = Anim + name + ".anim"; Need(!File.Exists(path), "Clip exists: " + path);
            var clip = new AnimationClip { name = name, frameRate = 30, wrapMode = WrapMode.ClampForever, legacy = false };
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("LeftLeaf", typeof(Transform), "m_LocalPosition.x"), Curve(times, left));
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("RightLeaf", typeof(Transform), "m_LocalPosition.x"), Curve(times, right));
            var settings = AnimationUtility.GetAnimationClipSettings(clip); settings.loopTime = false; AnimationUtility.SetAnimationClipSettings(clip, settings);
            AssetDatabase.CreateAsset(clip, path); AssetDatabase.SaveAssetIfDirty(clip);
        }
        static void Leaf(Transform door, string name, float x, Color tint)
        {
            // Unity bakes Transform position curves as vectors. Keep the two animated targets at y/z=0;
            // the static child offset preserves the 1x3 artwork without adding Y/Z clip bindings.
            var t = Fresh(door, name); t.localPosition = new Vector3(x, 0, 0);
            Art(t, "ART_VISUAL", Module("Plate"), new Vector2(0, 1.5f), new Vector2(1, 3), tint, 3);
        }
        [MenuItem("Tools/C-IMPL-R4/01 Create visual assets")]
        public static void CreateAssets()
        {
            Stage("R4-01", () =>
            {
                Need(GetQA().completed.Contains("R4-00"), "Record current identity first");
                foreach (var f in new[] { Base + "Art/R4", Base + "Art/R4/Doors", Base + "Art/R4/Prefabs", Base + "Animation", Base + "Animation/R4" }) Folder(f);
                var preview = EditorSceneManager.NewPreviewScene();
                try
                {
                    var door = Fresh(null, "PCS_ElevatorDoor_R4"); SceneManager.MoveGameObjectToScene(door.gameObject, preview);
                    // The header and opaque pocket covers remain fixed while the independent leaves slide behind them.
                    var frame = Art(door, "Frame_Static", Module("Ceiling"), new Vector2(0, 3.25f), new Vector2(4, .5f), Color.white, 5);
                    Art(frame.transform, "LeftPocketCover", Module("Plate"), new Vector2(-1.5f, -1.75f), new Vector2(1, 3), new Color(.78f, .86f, .91f), 5);
                    Art(frame.transform, "RightPocketCover", Module("Plate"), new Vector2(1.5f, -1.75f), new Vector2(1, 3), new Color(.78f, .86f, .91f), 5);
                    Art(door, "Backing", Module("Plate"), new Vector2(0, 1.5f), new Vector2(2, 3), new Color(.10f, .15f, .20f), 1);
                    Leaf(door, "LeftLeaf", -.5f, new Color(.55f, .67f, .75f));
                    Leaf(door, "RightLeaf", .5f, new Color(.50f, .62f, .70f));
                    Need(!File.Exists(DoorPrefab), "Door prefab exists"); Need(PrefabUtility.SaveAsPrefabAsset(door.gameObject, DoorPrefab) != null, "Door prefab save failed");
                }
                finally { EditorSceneManager.ClosePreviewScene(preview); }
                preview = EditorSceneManager.NewPreviewScene();
                try
                {
                    var root = Fresh(null, "PCS_ElevatorVisual_R4"); SceneManager.MoveGameObjectToScene(root.gameObject, preview);
                    Art(root, "Deck", Tile("Tiles_0"), new Vector2(0, -.671875f), new Vector2(6, 1.34375f), Color.white, 4);
                    Art(root, "Column", Tile("Tiles_5"), new Vector2(0, -2.65f), new Vector2(1.7f, 4.3f), Color.white, 0);
                    var door = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(DoorPrefab), preview); door.name = "DoorRoot"; door.transform.SetParent(root, false);
                    Need(!File.Exists(VisualPrefab), "Visual prefab exists"); Need(PrefabUtility.SaveAsPrefabAsset(root.gameObject, VisualPrefab) != null, "Visual prefab save failed");
                }
                finally { EditorSceneManager.ClosePreviewScene(preview); }
                Clip("Door_ClosedIdle_R4", new[] { 0f, 1f }, new[] { -.5f, -.5f }, new[] { .5f, .5f });
                Clip("Door_Open_R4", new[] { 0f, .3f, .6f }, new[] { -.5f, -1f, -1.5f }, new[] { .5f, 1f, 1.5f });
                Clip("Door_Close_R4", new[] { 0f, .3f, .6f }, new[] { -1.5f, -1f, -.5f }, new[] { 1.5f, 1f, .5f });
                Log("New PNG NOT_NEEDED candidate: existing R3 metal modules reused; visual quality remains pending actual screen review");
            });
        }
        public static void Sample(GameObject door, string clip, float time)
        {
            AnimationMode.BeginSampling();
            try { AnimationMode.SampleAnimationClip(door, AssetDatabase.LoadAssetAtPath<AnimationClip>(Anim + clip + ".anim"), time); }
            finally { AnimationMode.EndSampling(); }
        }
        public static void RepairLeafTargets()
        {
            Guard(); Need(!GetQA().completed.Contains("R4-02") && GetQA().scenes.Count == 0, "Repair only before scene application");
            var go = PrefabUtility.LoadPrefabContents(DoorPrefab);
            try
            {
                foreach (var name in new[] { "LeftLeaf", "RightLeaf" })
                {
                    var leaf = go.transform.Find(name); var sr = leaf.GetComponent<SpriteRenderer>(); Need(sr != null && leaf.Find("ART_VISUAL") == null, "Expected initial R4 leaf only");
                    Art(leaf, "ART_VISUAL", sr.sprite, new Vector2(0, 1.5f), sr.size, sr.color, sr.sortingOrder);
                    Object.DestroyImmediate(sr); leaf.localPosition = new Vector3(leaf.localPosition.x, 0, 0);
                }
                Need(PrefabUtility.SaveAsPrefabAsset(go, DoorPrefab) != null, "Repair save failed");
            }
            finally { PrefabUtility.UnloadPrefabContents(go); }
            RecordCreated(); Log("Sparse position repair: leaf targets y/z0, artwork children y1.5; exact two X bindings retained. New door9/assembly12 GO; no existing scene touched.");
        }
        static void NoFunction(GameObject go)
        {
            Need(go.GetComponentsInChildren<Collider2D>(true).Length == 0 && go.GetComponentsInChildren<Animator>(true).Length == 0 && go.GetComponentsInChildren<MonoBehaviour>(true).Length == 0, "Visual asset contains gameplay components");
        }
        [MenuItem("Tools/C-IMPL-R4/02 Validate clips")]
        public static void ValidateClips()
        {
            Stage("R4-02", () =>
            {
                var q = GetQA();
                foreach (var path in new[] { DoorPrefab, VisualPrefab })
                {
                    var go = PrefabUtility.LoadPrefabContents(path);
                    try { NoFunction(go); q.clipChecks.Add(path + " objects=" + go.GetComponentsInChildren<Transform>(true).Length + " Collider/Animator/Runtime=0"); }
                    finally { PrefabUtility.UnloadPrefabContents(go); }
                }
                var door = PrefabUtility.LoadPrefabContents(DoorPrefab); var left = door.transform.Find("LeftLeaf"); var right = door.transform.Find("RightLeaf"); var lp = left.localPosition; var rp = right.localPosition;
                try
                {
                    Need(Mathf.Abs(lp.x + .5f) < .0001f && Mathf.Abs(rp.x - .5f) < .0001f, "Closed saved pose");
                    foreach (var n in new[] { "Door_ClosedIdle_R4", "Door_Open_R4", "Door_Close_R4" })
                    {
                        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Anim + n + ".anim"); var bindings = AnimationUtility.GetCurveBindings(clip);
                        Need(clip.frameRate == 30 && !AnimationUtility.GetAnimationClipSettings(clip).loopTime && AnimationUtility.GetAnimationEvents(clip).Length == 0 && bindings.Length == 2, "Clip settings");
                        Need(bindings.All(b => b.type == typeof(Transform) && b.propertyName == "m_LocalPosition.x") && bindings.Select(b => b.path).OrderBy(x => x).SequenceEqual(new[] { "LeftLeaf", "RightLeaf" }), "Exact binding paths");
                        AnimationMode.StartAnimationMode();
                        try
                        {
                            foreach (float time in n.Contains("Idle") ? new[] { 0f, .3f, .6f, 1f } : new[] { 0f, .3f, .6f })
                            {
                                Sample(door, n, time); float expected = n.Contains("Idle") ? -.5f : n.Contains("Open") ? -.5f - time / .6f : -1.5f + time / .6f;
                                Need(Mathf.Abs(left.localPosition.x - expected) < .0001f && Mathf.Abs(right.localPosition.x + expected) < .0001f && Mathf.Abs(left.localPosition.y) < .0001f && Mathf.Abs(right.localPosition.y) < .0001f && Mathf.Abs(left.Find("ART_VISUAL").localPosition.y - 1.5f) < .0001f, "Sample pose mismatch " + n + " time=" + time + " expected=" + expected + " left=" + left.localPosition.ToString("F6") + " right=" + right.localPosition.ToString("F6"));
                            }
                        }
                        finally { AnimationMode.StopAnimationMode(); }
                        Need((left.localPosition - lp).sqrMagnitude < 1e-8f && (right.localPosition - rp).sqrMagnitude < 1e-8f, "Preview failed to restore Closed");
                        q.clipChecks.Add(n + " binding2/fps30/loopOff/events0/sample/Closed restore PASS");
                    }
                    AnimationMode.StartAnimationMode();
                    try { Sample(door, "Door_Open_R4", .6f); Sample(door, "Door_Close_R4", .6f); Need((left.localPosition - lp).sqrMagnitude < 1e-8f && (right.localPosition - rp).sqrMagnitude < 1e-8f, "Roundtrip error"); }
                    finally { AnimationMode.StopAnimationMode(); }
                    q.clipChecks.Add("Open -> Close -> Stop: original Closed restored within 1e-4; prefab source unchanged"); Write("QA_RESULTS.json", q);
                }
                finally { if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode(); PrefabUtility.UnloadPrefabContents(door); }
            });
        }
        static bool Equal(float[] a, float[] b) { return a.Length == b.Length && a.Zip(b, (x, y) => Mathf.Abs(x - y)).All(x => x <= .0001f); }
        static void Preserve(Row[] before, Row[] after)
        {
            var ids = new HashSet<string>(before.Select(x => x.id)); var map = after.Where(x => ids.Contains(x.id)).ToDictionary(x => x.id);
            foreach (var old in before)
            {
                Need(map.ContainsKey(old.id), "Original object disappeared: " + old.path); var current = map[old.id];
                Need(old.componentIds.SequenceEqual(current.componentIds) && old.references.SequenceEqual(current.references), "Original component/reference changed: " + old.path);
                Need(Equal(old.matrix, current.matrix) && old.active == current.active && old.activeHierarchy == current.activeHierarchy, "Original pose/active changed: " + old.path);
                if (old.box.Length > 0) Need(Equal(old.box, current.box) && Equal(old.colliderBounds, current.colliderBounds) && old.colliderEnabled == current.colliderEnabled, "Collider changed: " + old.path);
                if (old.marker) Need(old.rendererEnabled == current.rendererEnabled, "Marker renderer changed: " + old.path);
            }
        }
        static void RetainOldArt(Transform root)
        {
            foreach (var sr in root.GetComponentsInChildren<SpriteRenderer>(true)) if (!Record(sr.transform).marker) { sr.enabled = false; if (PrefabUtility.IsPartOfPrefabInstance(sr)) PrefabUtility.RecordPrefabInstancePropertyModifications(sr); }
        }
        static GameObject Assembly(Transform parent, Vector3 bottom, float deckWidth)
        {
            Need(parent.Find("R4_VISUAL") == null, "R4 visual exists");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(VisualPrefab), parent.gameObject.scene); go.name = "R4_VISUAL"; go.transform.SetParent(parent, true); go.transform.position = bottom; go.transform.rotation = Quaternion.identity;
            var ls = parent.lossyScale; go.transform.localScale = new Vector3(1 / ls.x, 1 / ls.y, 1 / ls.z); PrefabUtility.RecordPrefabInstancePropertyModifications(go.transform);
            var deck = go.transform.Find("Deck").GetComponent<SpriteRenderer>(); deck.size = new Vector2(deckWidth, 1.34375f); PrefabUtility.RecordPrefabInstancePropertyModifications(deck); return go;
        }
        static void Elevators(Transform root)
        {
            var edit = root.Find("00_MAP_EDIT"); var old = edit.Find("ELEVATORS_VISUAL"); Need(old != null, "Existing elevator container"); var group = Fresh(edit, "ELEVATORS");
            var archive = Node(root.Find("90_DEV_HANDOFF_NOT_FOR_MAP_EDIT"), "R4_ORIGINAL_GROUPS");
            foreach (Transform child in old.Cast<Transform>().ToArray())
            {
                if (child.name == "ROOM_ENTRY_ELEVATORS_OPTIONAL")
                {
                    foreach (Transform room in child.Cast<Transform>().ToArray())
                    {
                        Need(new[] { "RABBIT", "RAT", "FROG", "BEAR" }.Contains(room.name), "Unknown optional slot");
                        var oldArt = room.Find("ENTRY_ELEVATOR_OPTIONAL_VISUAL"); Need(oldArt != null, "Original optional prefab");
                        room.SetParent(group, true); RetainOldArt(oldArt);
                        var x = 24.5f + Array.IndexOf(new[] { "RABBIT", "RAT", "FROG", "BEAR" }, room.name) * 28f;
                        Assembly(room, new Vector3(x, 26, 0), 4);
                    }
                    child.SetParent(archive, true);
                }
                else
                {
                    var collider = child.GetComponentsInChildren<BoxCollider2D>(true).SingleOrDefault(x => x.name == "DeckCollider_Layout"); Need(collider != null, "Existing deck collider");
                    var b = collider.bounds; child.SetParent(group, true); RetainOldArt(child); Assembly(child, new Vector3(b.center.x, b.max.y, 0), b.size.x);
                }
            }
            old.SetParent(archive, true);
        }
        static void Background(Transform root, bool malkuth)
        {
            var edit = root.Find("00_MAP_EDIT"); var old = edit.Find("ENVIRONMENT"); Need(old != null, "Existing environment container"); var group = Fresh(edit, "BACKGROUND"); var archive = Node(root.Find("90_DEV_HANDOFF_NOT_FOR_MAP_EDIT"), "R4_ORIGINAL_GROUPS");
            foreach (Transform child in old.Cast<Transform>().ToArray())
            {
                if (malkuth && (child.name == "R2_FINAL_BACKGROUND" || child.name == "R3_UPPER_BACKGROUND")) { RetainOldArt(child); child.SetParent(archive, true); }
                else child.SetParent(group, true);
            }
            old.SetParent(archive, true);
            if (malkuth)
            {
                var coverage = Fresh(group, "R4_COVERAGE"); var sprite = AssetDatabase.LoadAllAssetsAtPath("Assets/Resources/img/BG/stage1-1.png").OfType<Sprite>().Single();
                float height = 28 * sprite.rect.height / sprite.rect.width;
                for (int y = 0; y < 3; y++) for (int x = 0; x < 6; x++)
                {
                    var sr = Art(coverage, "BG_" + y + "_" + x, sprite, new Vector2(-19 + 28 * (x + .5f), -8 + height * (y + .5f)), new Vector2(28, height), Color.white, -100);
                    sr.drawMode = SpriteDrawMode.Simple; float scale = 28 / sprite.bounds.size.x; sr.transform.localScale = new Vector3(scale, scale, 1); sr.transform.localPosition = new Vector3(-19 + 28 * (x + .5f), -8 + height * (y + .5f), 5) - Vector3.Scale(sprite.bounds.center, sr.transform.localScale);
                }
                Log("BG 18 uniform modules; X[-19,149], Y[-8," + (-8 + height * 3) + "]; terrain/Collider additions 0");
            }
        }
        static void SaveScene(Scene s, int index)
        {
            Need(s.path == PathFor(index), "Exact approved existing scene path"); var initial = Read<Identity>("OBJECT_IDENTITY_MAP.json"); var pair = initial.scenes.Single(x => x.scene == s.path);
            Preserve(pair.before, Snapshot(s)); Need(EditorSceneManager.SaveScene(s, s.path, false), "SaveScene failed"); var hash = Hash(s.path);
            s = EditorSceneManager.OpenScene(s.path, OpenSceneMode.Single); Need(Hash(s.path) == hash, "Reopen changed disk hash"); var rows = Snapshot(s); Preserve(pair.before, rows);
            var qa = new SceneQA { path = s.path, guid = AssetDatabase.AssetPathToGUID(s.path), sha256 = hash, objects = rows.Length, activeObjects = rows.Count(x => x.activeHierarchy), renderers = rows.Count(x => x.sprite != ""), activeRenderers = rows.Count(x => x.sprite != "" && x.rendererEnabled && x.activeHierarchy), colliders = rows.Count(x => x.colliderEnabled && x.activeHierarchy), missingScripts = rows.Count(x => x.componentTypes.Contains("MISSING")), missingSprites = rows.Count(x => x.componentTypes.Contains(typeof(SpriteRenderer).FullName) && x.sprite == ""), reopened = true, rows = rows };
            Need(qa.missingScripts == 0 && qa.missingSprites == 0, "Missing component/sprite"); pair.after = rows;
            pair.changes = pair.before.Where(x => x.path != rows.Single(y => y.id == x.id).path || x.rendererEnabled != rows.Single(y => y.id == x.id).rendererEnabled).Select(x => x.path + " -> " + rows.Single(y => y.id == x.id).path + (x.rendererEnabled != rows.Single(y => y.id == x.id).rendererEnabled ? " [old visual renderer disabled; object retained]" : " [world pose preserved]")).ToArray();
            Write("OBJECT_IDENTITY_MAP.json", initial); Write("HIERARCHY_CHANGELOG.json", initial); var q = GetQA(); q.scenes.RemoveAll(x => x.path == s.path); q.scenes.Add(qa); Write("QA_RESULTS.json", q); Log(s.path + " saved/reopened/ref/Collider/marker PASS");
        }
        [MenuItem("Tools/C-IMPL-R4/03 Apply five scenes")]
        public static void ApplyScenes()
        {
            Stage("R4-03", () =>
            {
                Need(GetQA().completed.Contains("R4-02"), "Validate clips first");
                for (int i = 0; i < 5; i++)
                {
                    Guard(); if (GetQA().scenes.Any(x => x.path == PathFor(i))) continue;
                    var s = EditorSceneManager.OpenScene(PathFor(i), OpenSceneMode.Single); var r = Root(s); Elevators(r); Background(r, i == 0); SaveScene(s, i);
                }
            });
        }
        [MenuItem("Tools/C-IMPL-R4/04 Verify preservation and toggles")]
        public static void Verify()
        {
            Stage("R4-04", () =>
            {
                Need(GetQA().scenes.Count == 5, "Five saved scenes required");
                foreach (var pair in Read<Identity>("OBJECT_IDENTITY_MAP.json").scenes) Preserve(pair.before, pair.after);
                var s = EditorSceneManager.OpenScene(PathFor(0)); var root = Root(s); var elevator = root.Find("00_MAP_EDIT/ELEVATORS");
                var slots = new[] { "RABBIT", "RAT", "FROG", "BEAR" }.Select(n => elevator.Find(n)).ToArray(); Need(slots.All(x => x != null && x.gameObject.activeSelf), "Four ON slots");
                var baseline = Snapshot(s).Where(x => x.marker || x.box.Length > 0).ToArray(); var q = GetQA();
                for (int test = -1; test < 4; test++)
                {
                    try
                    {
                        foreach (var slot in slots) slot.gameObject.SetActive(true); if (test < 0) foreach (var slot in slots) slot.gameObject.SetActive(false); else slots[test].gameObject.SetActive(false);
                        var rows = Snapshot(s).Where(x => x.marker || x.box.Length > 0).ToDictionary(x => x.id);
                        foreach (var old in baseline.Where(x => !x.path.Contains("ENTRY_ELEVATOR_OPTIONAL_VISUAL/DEV_MARKER")))
                        {
                            var now = rows[old.id]; Need(Equal(old.matrix, now.matrix) && old.active == now.active && old.activeHierarchy == now.activeHierarchy && Equal(old.box, now.box), "Toggle affected operational marker/Collider");
                        }
                        q.toggleChecks.Add((test < 0 ? "ALL_OFF" : slots[test].name + "_OFF") + ": operational Collider/marker unchanged; legacy inactive DEV_MARKER is non-operational");
                    }
                    finally { foreach (var slot in slots) slot.gameObject.SetActive(true); }
                }
                Need(!s.isDirty, "Preview toggles unexpectedly dirtied scene; no implicit save"); Write("QA_RESULTS.json", q);
            });
        }
        public static void AdoptAtlas()
        {
            Guard(); Need(File.Exists(Atlas), "Generated atlas not present");
            if (!AssetDatabase.LoadAllAssetsAtPath(Atlas).OfType<Sprite>().Any(x => x.name == "Frame_Static"))
            {
            AssetDatabase.ImportAsset(Atlas, ImportAssetOptions.ForceSynchronousImport);
            var ti = (TextureImporter)AssetImporter.GetAtPath(Atlas); ti.textureType = TextureImporterType.Sprite; ti.spriteImportMode = SpriteImportMode.Multiple; ti.spritePixelsPerUnit = 32; ti.filterMode = FilterMode.Point; ti.mipmapEnabled = false; ti.textureCompression = TextureImporterCompression.Uncompressed; ti.alphaIsTransparency = true; ti.npotScale = TextureImporterNPOTScale.None;
            var settings = new TextureImporterSettings(); ti.ReadTextureSettings(settings); settings.spriteMeshType = SpriteMeshType.FullRect; ti.SetTextureSettings(settings); ti.SaveAndReimport();
            var factory = new SpriteDataProviderFactories(); factory.Init(); var provider = factory.GetSpriteEditorDataProviderFromObject(ti); provider.InitSpriteEditorDataProvider();
            var rects = new[] { new SpriteRect { name = "Frame_Static", rect = new Rect(0, 0, 128, 112), alignment = SpriteAlignment.Custom, pivot = new Vector2(.5f, 0), spriteID = GUID.Generate() }, new SpriteRect { name = "LeftLeaf", rect = new Rect(136, 0, 32, 96), alignment = SpriteAlignment.Center, pivot = new Vector2(.5f, .5f), spriteID = GUID.Generate() }, new SpriteRect { name = "RightLeaf", rect = new Rect(176, 0, 32, 96), alignment = SpriteAlignment.Center, pivot = new Vector2(.5f, .5f), spriteID = GUID.Generate() } };
            provider.SetSpriteRects(rects); provider.GetDataProvider<ISpriteNameFileIdDataProvider>().SetNameFileIdPairs(rects.Select(x => new SpriteNameFileIdPair(x.name, x.spriteID))); provider.Apply(); ti.SaveAndReimport();
            }
            var go = PrefabUtility.LoadPrefabContents(DoorPrefab);
            try
            {
                var frame = go.transform.Find("Frame_Static"); frame.localPosition = Vector3.zero; var sr = frame.GetComponent<SpriteRenderer>(); sr.sprite = Sprite(Atlas, "Frame_Static"); sr.drawMode = SpriteDrawMode.Simple; sr.color = Color.white; frame.localScale = Vector3.one;
                foreach (var n in new[] { "LeftPocketCover", "RightPocketCover" }) { var cover = frame.Find(n); cover.localPosition = new Vector3(n.StartsWith("Left") ? -1.5f : 1.5f, 1.5f, 0); var coverSR = cover.GetComponent<SpriteRenderer>(); coverSR.color = new Color(.12f, .16f, .20f); coverSR.sortingOrder = 4; }
                foreach (var n in new[] { "LeftLeaf", "RightLeaf" }) { var leaf = go.transform.Find(n + "/ART_VISUAL").GetComponent<SpriteRenderer>(); leaf.sprite = Sprite(Atlas, n); leaf.drawMode = SpriteDrawMode.Simple; leaf.color = Color.white; leaf.transform.localScale = Vector3.one; }
                Need(Mathf.Abs(sr.bounds.size.x - 4) < .0001f && Mathf.Abs(sr.bounds.size.y - 3.5f) < .0001f, "Frame must retain 4x3.5 physical size after drawMode switch");
                NoFunction(go); Need(PrefabUtility.SaveAsPrefabAsset(go, DoorPrefab) != null, "Updated atlas door save");
            }
            finally { PrefabUtility.UnloadPrefabContents(go); }
            var assembly = PrefabUtility.LoadPrefabContents(VisualPrefab);
            try
            {
                // Match visible central beam pixels, not the deck sprite's transparent lower bounding box.
                assembly.transform.Find("Column").localPosition = new Vector3(0, -4.8f, 0);
                Need(PrefabUtility.SaveAsPrefabAsset(assembly, VisualPrefab) != null, "Column contact save");
            }
            finally { PrefabUtility.UnloadPrefabContents(assembly); }
            RecordCreated();
            for (int i = 0; i < 5; i++) { Guard(); SaveScene(EditorSceneManager.OpenScene(PathFor(i)), i); }
            RecordCreated(); Log("Generated atlas adopted; three exact slices/PPU32/Point/FullRect; original scene objects/Colliders/markers preserved on re-save/reopen");
        }
        public static void VisualSeams()
        {
            Stage("R4-05", () =>
            {
                for (int i = 0; i < 3; i++)
                {
                    Guard(); var s = EditorSceneManager.OpenScene(PathFor(i)); var root = Root(s);
                    var names = i == 0 ? new[] { "R2_GATHERING_PROPOSED/ExitApproach" } : new[] { "R2_APPROACHES/EntryApproach", "R2_APPROACHES/UpperExitApproach_PROPOSED" };
                    foreach (var n in names)
                    {
                        var parent = Find(root, "00_MAP_EDIT/TERRAIN/" + n); var existing = parent.Find("Visual").GetComponent<SpriteRenderer>(); var b = existing.bounds;
                        Need((parent.lossyScale - Vector3.one).sqrMagnitude < 1e-8f, "Approach unit scale");
                        var center = parent.InverseTransformPoint(new Vector3(b.center.x, b.max.y - .11f, 0));
                        Art(parent, "R4_VISUAL_JOINT_NO_COLLIDER", Module("Ceiling"), new Vector2(center.x, center.y), new Vector2(b.size.x + .4f, .22f), Color.white, 0);
                    }
                    SaveScene(s, i);
                }
                Log("Five approach visual joints added only at pre-existing approach bounds +/-0.2. Intentional Yesod gap untouched; Collider/enabled states unchanged.");
            });
        }
        public static void FinalizeHierarchy()
        {
            Stage("R4-06", () =>
            {
                var s = EditorSceneManager.OpenScene(PathFor(3)); var root = Root(s); var p = Find(root, "00_MAP_EDIT/TERRAIN/ENTRY_EXIT_GROUND/SafeLeftApproach_PROPOSED"); var b = p.Find("Visual").GetComponent<SpriteRenderer>().bounds;
                Need((p.lossyScale - Vector3.one).sqrMagnitude < 1e-8f, "Approach unit scale"); var center = p.InverseTransformPoint(new Vector3(b.center.x, b.max.y - .11f, 0));
                Art(p, "R4_VISUAL_JOINT_NO_COLLIDER", Module("Ceiling"), new Vector2(center.x, center.y), new Vector2(b.size.x + .4f, .22f), Color.white, 0);
                var frame = Find(root, "00_MAP_EDIT/ELEVATORS/ELEVATOR_EXIT_VISUAL/R4_VISUAL/DoorRoot/Frame_Static").GetComponent<SpriteRenderer>(); frame.color = new Color(.78f, .90f, 1f, 1); PrefabUtility.RecordPrefabInstancePropertyModifications(frame); SaveScene(s, 3);
                foreach (var pair in Read<Identity>("OBJECT_IDENTITY_MAP.json").scenes)
                {
                    Preserve(pair.before, pair.after); Need(pair.after.Any(x => x.path.EndsWith("/00_MAP_EDIT/BACKGROUND")) && pair.after.Any(x => x.path.EndsWith("/00_MAP_EDIT/ELEVATORS")), "Simple editing groups missing");
                }
                Log("Hierarchy/ref/worldMatrix/active/Collider preservation PASS; Hod exit frame tint distinguishes existing candidate only, no gameplay direction changed.");
            });
        }
        public static void TuneOwnedSeams()
        {
            Guard(); Need(GetQA().completed.Contains("R4-06"), "Finish hierarchy checks first");
            for (int i = 0; i < 4; i++)
            {
                Guard(); var s = EditorSceneManager.OpenScene(PathFor(i)); var root = Root(s);
                var owned = GetQA().scenes.Single(x => x.path == s.path).rows.Where(x => x.name == "R4_VISUAL_JOINT_NO_COLLIDER").Select(x => x.id).ToArray();
                var joints = root.GetComponentsInChildren<Transform>(true).Where(x => x.name == "R4_VISUAL_JOINT_NO_COLLIDER").ToArray(); Need(joints.Length == (i == 0 || i == 3 ? 1 : 2), "Expected run-owned joint count");
                foreach (var joint in joints)
                {
                    Need(owned.Contains(Id(joint.gameObject)), "Only recorded R4 visual joints may be tuned"); var old = joint.parent.Find("Visual").GetComponent<SpriteRenderer>().bounds; var sr = joint.GetComponent<SpriteRenderer>();
                    sr.size = new Vector2(i == 3 ? 14f : 3.8f, .5f); joint.position = new Vector3(i == 3 ? -6.2f : old.center.x, old.max.y - .25f, 0);
                }
                SaveScene(s, i);
            }
            RecordCreated(); Log("Final alpha-aware joint contact: original colliders unchanged; original tile-end bevels bridged by run-owned 0.5-high metal only.");
        }
        static void Inspect(int kind)
        {
            PreviewGuard(false); foreach (var old in Resources.FindObjectsOfTypeAll<R4ReviewWindow>()) old.Close();
            if (SceneManager.GetActiveScene().path != PathFor(0)) EditorSceneManager.OpenScene(PathFor(0)); var root = Root(SceneManager.GetActiveScene());
            var target = kind == 0 ? root.Find("00_MAP_EDIT") : kind == 1 ? Find(root, "00_MAP_EDIT/TERRAIN/R3_ENTRY_ACCESS_STATIC/RABBIT/ENTRY_BYPASS_BLOCKING_TERRAIN/Dock/STATIC_COLLIDER") : Find(root, "00_MAP_EDIT/ELEVATORS/RABBIT/R4_VISUAL/DoorRoot");
            Selection.activeGameObject = target.gameObject; EditorGUIUtility.PingObject(target); EditorApplication.ExecuteMenuItem("Window/General/Hierarchy");
            if (kind != 0 && SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.Frame(new Bounds(new Vector3(24.5f, 27, 0), new Vector3(9, 7, 1)), true);
        }
        [MenuItem("Tools/C-IMPL-R4/Inspect editing hierarchy")] public static void InspectHierarchy() { Inspect(0); }
        [MenuItem("Tools/C-IMPL-R4/Inspect preserved Dock Collider")] public static void InspectCollider() { Inspect(1); }
        [MenuItem("Tools/C-IMPL-R4/Inspect Closed DoorRoot")] public static void InspectDoor() { Inspect(2); }
        public static void ReviewScene(int index)
        {
            PreviewGuard(false); Need(GetQA().scenes.Count == 5, "Finish scene edits first"); if (SceneManager.GetActiveScene().path != PathFor(index)) EditorSceneManager.OpenScene(PathFor(index));
            foreach (var old in Resources.FindObjectsOfTypeAll<R4ReviewWindow>()) old.Close();
            var w = EditorWindow.GetWindow<R4ReviewWindow>(true, "R4 Actual Unity Camera QA"); w.sceneIndex = index; w.mode = 0; w.group = 0; w.ShowUtility();
            var main = EditorGUIUtility.GetMainWindowPosition(); w.position = new Rect(main.x + 10, main.y + 80, Mathf.Min(1100, main.width - 20), Mathf.Min(1000, main.height - 120)); w.RenderViews();
        }
        [MenuItem("Tools/C-IMPL-R4/Review Malkuth")]
        public static void ReviewMalkuth() { ReviewScene(0); }
        [MenuItem("Tools/C-IMPL-R4/00 Record current identity")]
        public static void RecordBaseline()
        {
            Guard(); Need(!File.Exists(Run + "/OBJECT_IDENTITY_MAP.json"), "Initial identity already exists; do not overwrite");
            var identity = new Identity(); var previous = SceneManager.GetActiveScene().path;
            for (int i = 0; i < 5; i++)
            {
                var s = EditorSceneManager.OpenScene(PathFor(i), OpenSceneMode.Single); var rows = Snapshot(s);
                Need(!s.isDirty && rows.All(x => !x.componentTypes.Contains("MISSING")), "Invalid initial scene");
                identity.scenes.Add(new Pair { scene = PathFor(i), before = rows });
            }
            Write("OBJECT_IDENTITY_MAP.json", identity); if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
            var q = GetQA(); q.completed.Add("R4-00"); Write("QA_RESULTS.json", q); Log("R4-00 identity saved; no scene writes");
        }
        [MenuItem("Tools/C-IMPL-R4/Open Control Panel")]
        public static void OpenPanel() { var w = EditorWindow.GetWindow<R4ControlPanel>("C-IMPL-R4"); w.minSize = new Vector2(420, 300); }
    }
    public class R4ControlPanel : EditorWindow
    {
        void OnGUI()
        {
            GUILayout.Label("C-IMPL-R4 | explicit commands only", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Existing user scenes are never saved or discarded by this panel. End preview before edits. No Play or Controller.", MessageType.Info);
            if (GUILayout.Button("Close R4 review window", GUILayout.Height(24))) foreach (var view in Resources.FindObjectsOfTypeAll<R4ReviewWindow>()) view.Close();
            if (GUILayout.Button("00 Record current identity", GUILayout.Height(40))) PCS_MapElevatorR4.RecordBaseline();
            if (GUILayout.Button("01 Create PCS visual assets", GUILayout.Height(36))) PCS_MapElevatorR4.CreateAssets();
            if (GUILayout.Button("02 Validate clips and restore Closed", GUILayout.Height(36))) PCS_MapElevatorR4.ValidateClips();
            if (GUILayout.Button("06 Final polish / hierarchy checks", GUILayout.Height(28))) PCS_MapElevatorR4.FinalizeHierarchy();
            if (GUILayout.Button("03 Apply and save five scenes", GUILayout.Height(36))) PCS_MapElevatorR4.ApplyScenes();
            if (GUILayout.Button("04 Verify Collider / markers / toggles", GUILayout.Height(36))) PCS_MapElevatorR4.Verify();
            if (GUILayout.Button("Finalize verified approach artwork", GUILayout.Height(32))) PCS_MapElevatorR4.TuneOwnedSeams();
            if (GUILayout.Button("05 Finish approach visual seams (no Collider)", GUILayout.Height(32))) PCS_MapElevatorR4.VisualSeams();
            GUILayout.Space(10);
            for (int i = 0; i < 5; i++) { int index = i; if (GUILayout.Button("Review " + PCS_MapElevatorR4.Names[i], GUILayout.Height(30))) PCS_MapElevatorR4.ReviewScene(index); }
        }
    }
    public class R4ReviewWindow : EditorWindow
    {
        public int sceneIndex, mode, group;
        readonly List<Texture2D> frames = new List<Texture2D>();
        readonly List<string> labels = new List<string>();
        int columns = 1;
        string previewNote = "Preview ended; scene is Closed and optional entry visuals are ON.";
        void ClearFrames() { foreach (var texture in frames) DestroyImmediate(texture); frames.Clear(); labels.Clear(); }
        void OnDestroy() { ClearFrames(); }
        static string[] Rooms = { "RABBIT", "RAT", "FROG", "BEAR" };
        void Capture(Vector3 center, float size, float aspect, string label)
        {
            var cam = PCS_MapElevatorR4.Root(SceneManager.GetActiveScene()).GetComponentsInChildren<Camera>(true).Single();
            string originalCamera = EditorJsonUtility.ToJson(cam); var originalPosition = cam.transform.position; var originalRotation = cam.transform.rotation; float originalAspect = cam.aspect; var originalTarget = cam.targetTexture;
            var rt = new RenderTexture(Mathf.RoundToInt(270 * aspect), 270, 24) { hideFlags = HideFlags.HideAndDontSave };
            PCS_MapElevatorR4.Need(Mathf.Abs(rt.width / (float)rt.height - aspect) < .0001f, "Exact aspect pixel dimensions required");
            var originalActive = RenderTexture.active;
            try
            {
                cam.transform.position = new Vector3(center.x, center.y, -10); cam.transform.rotation = Quaternion.identity; cam.orthographic = true; cam.orthographicSize = size; cam.aspect = aspect; cam.targetTexture = rt;
                cam.ResetWorldToCameraMatrix(); cam.ResetProjectionMatrix(); cam.ResetCullingMatrix(); rt.Create();
                RenderPipeline.SubmitRenderRequest(cam, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
                RenderTexture.active = rt; var texture = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point }; texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); texture.Apply();
                PCS_MapElevatorR4.Need(texture.GetPixels32().Select(c => (c.r << 16) | (c.g << 8) | c.b).Distinct().Take(17).Count() > 16, "Camera review returned an empty image");
                frames.Add(texture); labels.Add(label + " | aspect=" + aspect.ToString("F4") + " | center=" + center.ToString("F2") + " | ortho=" + size);
            }
            finally { RenderTexture.active = originalActive; cam.targetTexture = originalTarget; cam.transform.SetPositionAndRotation(originalPosition, originalRotation); EditorJsonUtility.FromJsonOverwrite(originalCamera, cam); cam.aspect = originalAspect; rt.Release(); DestroyImmediate(rt); PCS_MapElevatorR4.Need(EditorJsonUtility.ToJson(cam) == originalCamera, "Camera preview failed to restore saved fields"); }
        }
        public void RenderViews()
        {
            ClearFrames(); previewNote = "Preview ended; the saved pose is restored."; PCS_MapElevatorR4.PreviewGuard(); var r = PCS_MapElevatorR4.Root(SceneManager.GetActiveScene());
            if (mode == 0)
            {
                columns = 1; var centers = new[] { new Vector3(59, 15), new Vector3(12, 4), new Vector3(12, 4), new Vector3(7, 3), new Vector3(14, 4) }; var sizes = new[] { 21f, 14f, 14f, 17f, 19f }; Capture(centers[sceneIndex], sizes[sceneIndex], sceneIndex == 0 ? 3.2f : 16f / 9, "EDIT map overview " + PCS_MapElevatorR4.Names[sceneIndex]);
            }
            else if (mode == 1 || mode == 4 || mode == 5)
            {
                columns = 3; var doors = r.GetComponentsInChildren<Transform>(true).Where(t => t.name == "DoorRoot" && t.gameObject.activeInHierarchy).OrderBy(t => t.position.x).ThenBy(t => t.position.y).ToArray();
                var door = doors[group % doors.Length]; var pos = door.position + Vector3.up * 1.3f;
                var originalLeft = door.Find("LeftLeaf").localPosition; var originalRight = door.Find("RightLeaf").localPosition;
                AnimationMode.StartAnimationMode();
                try { string clip = mode == 4 ? "Door_Close_R4" : mode == 5 ? "Door_ClosedIdle_R4" : "Door_Open_R4"; foreach (float t in new[] { 0f, .3f, .6f }) { PCS_MapElevatorR4.Sample(door.gameObject, clip, t); Capture(pos, 3.6f, 4f / 3, "PREVIEW " + clip + " t=" + t.ToString("F1")); } }
                finally { AnimationMode.StopAnimationMode(); }
                PCS_MapElevatorR4.Need((door.Find("LeftLeaf").localPosition - originalLeft).sqrMagnitude < 1e-8f && (door.Find("RightLeaf").localPosition - originalRight).sqrMagnitude < 1e-8f, "Visual preview did not restore saved pose");
            }
            else if (mode == 2)
            {
                PCS_MapElevatorR4.Need(sceneIndex == 0, "Coverage review is Malkuth"); columns = 3;
                for (int i = 0; i < 4; i++)
                {
                    Vector3 center = group == 0 ? new Vector3(24.5f + 28 * i, 28) : group == 1 ? new Vector3(9 + 28 * i, 17) : group == 2 ? new Vector3(20.5f + 28 * i, 8) : new Vector3(new[] { 0f, 56f, 112f, 118f }[i], 0);
                    foreach (var aspect in new[] { 16f / 9, 4f / 3, 21f / 9 }) Capture(center, 6, aspect, "EDIT " + (group == 0 ? "Dock " + Rooms[i] : group == 1 ? "Room " + Rooms[i] : group == 2 ? "Drop " + Rooms[i] : "Gather " + i));
                }
            }
            Repaint(); PCS_MapElevatorR4.Need(!SceneManager.GetActiveScene().isDirty && !AnimationMode.InAnimationMode(), "Review dirtied saved scene");
        }
        void RenderOptionalOff()
        {
            PCS_MapElevatorR4.PreviewGuard(); var root = PCS_MapElevatorR4.Root(SceneManager.GetActiveScene()); var slots = Rooms.Select(n => root.Find("00_MAP_EDIT/ELEVATORS/" + n)).ToArray();
            PCS_MapElevatorR4.Need(sceneIndex == 0 && slots.All(t => t != null && t.gameObject.activeSelf), "Four ON Malkuth slots required");
            try { foreach (var t in slots) t.gameObject.SetActive(false); mode = 2; group = 0; RenderViews(); }
            finally { foreach (var t in slots) t.gameObject.SetActive(true); }
            previewNote = "Images: optional entry visuals ALL OFF; mandatory static terrain retained. Live saved scene restored to ALL ON.";
        }
        void OnGUI()
        {
            GUILayout.Label("Actual Unity Camera rendering | " + PCS_MapElevatorR4.Names[sceneIndex] + " | EDIT / Play OFF", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Overview", GUILayout.Height(28))) { mode = 0; group = 0; RenderViews(); }
            if (GUILayout.Button("Door Closed / Half / Open", GUILayout.Height(28))) { mode = 1; group = 0; RenderViews(); }
            if (GUILayout.Button("Next door", GUILayout.Height(28))) { mode = 1; group++; RenderViews(); }
            if (sceneIndex == 0) for (int i = 0; i < 4; i++) { int n = i; if (GUILayout.Button(new[] { "Docks 3 aspects", "Rooms 3 aspects", "Drops 3 aspects", "Gather 3 aspects" }[i], GUILayout.Height(28))) { mode = 2; group = n; RenderViews(); } }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Close clip", GUILayout.Height(24))) { mode = 4; group = 0; RenderViews(); }
            if (GUILayout.Button("ClosedIdle clip", GUILayout.Height(24))) { mode = 5; group = 0; RenderViews(); }
            if (GUILayout.Button("Sprite 100 / 200 / 400 percent", GUILayout.Height(24))) { ClearFrames(); mode = 3; Repaint(); }
            if (sceneIndex == 0 && GUILayout.Button("Optional ALL OFF preview", GUILayout.Height(24))) RenderOptionalOff();
            EditorGUILayout.EndHorizontal();
            GUILayout.Label(previewNote);
            if (mode == 3)
            {
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(PCS_MapElevatorR4.Atlas); float y = 120;
                foreach (int zoom in new[] { 1, 2, 4 })
                {
                    GUI.Label(new Rect(12, y, 250, 22), "Imported PNG | " + zoom * 100 + "% | Point | PPU32"); y += 24; float x = 12;
                    foreach (var rect in new[] { new Rect(0, 0, 128, 112), new Rect(136, 0, 32, 96), new Rect(176, 0, 32, 96) }) { GUI.DrawTextureWithTexCoords(new Rect(x, y, rect.width * zoom, rect.height * zoom), texture, new Rect(rect.x / 256, rect.y / 128, rect.width / 256, rect.height / 128), true); x += rect.width * zoom + 24; }
                    y += 112 * zoom + 12;
                }
                return;
            }
            float top = 112, gap = 8; int rows = Mathf.CeilToInt(frames.Count / (float)columns); float w = (position.width - gap * (columns + 1)) / columns; float h = (position.height - top - gap * (rows + 1)) / Mathf.Max(1, rows);
            for (int i = 0; i < frames.Count; i++)
            {
                float x = gap + i % columns * (w + gap), y = top + i / columns * (h + gap);
                GUI.Label(new Rect(x, y, w, 22), labels[i], EditorStyles.miniLabel); EditorGUI.DrawPreviewTexture(new Rect(x, y + 22, w, h - 24), frames[i], null, ScaleMode.ScaleToFit);
            }
        }
    }
}

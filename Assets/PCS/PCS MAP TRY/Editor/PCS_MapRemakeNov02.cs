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
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace PCS.MapPlanning.Nov02.EditorTools
{
    public static class PCS_MapRemakeNov02
    {
        public const string Project = "C:/FOUR-PAWS-ASCEND-NEWPCS";
        public const string Run = "C:/FOUR-PAWS-PCS-BRIEF/CIMPL_REMAKE_NOV02_20261102/run-001";
        public const string Base = "Assets/PCS/PCS MAP TRY/";
        public const string Helper = Base + "Editor/PCS_MapRemakeNov02.cs";
        public const string OriginalElevator = "Assets/Prefabs/Elevator.prefab";
        public const string OriginalFloor = "Assets/Prefabs/Floor.prefab";
        public const string DoorPrefab = Base + "Art/R4/Prefabs/PCS_ElevatorDoor_R4.prefab";
        public static readonly string[] Names = { "PCS_MAP00_Malkuth_ONE MORE VERSION", "PCS_MAP01_Yesod_SourceA_Edit", "PCS_MAP02_Hod_Blockout", "PCS_MAP03_Netzach_Blockout" };
        [Serializable] public class FileRecord { public string path, sha256, guid, operation; public long bytes, mtime_ns; }
        [Serializable] public class Baseline { public string run, project; public FileRecord[] files; public BackupRecord[] backups; }
        [Serializable] public class BackupRecord { public string source, backup, sha256, readback; public long bytes; }
        [Serializable] public class AssetList { public List<FileRecord> records = new List<FileRecord>(); }
        [Serializable] public class Control { public string[] projectFiles; }
        [Serializable] public class Row
        {
            public string id, transformId, path, parent, name, sprite, material, drawMode, prefab;
            public long localId;
            public string[] componentIds, componentTypes, references;
            public float[] matrix, localPosition, position, scale, localScale, tint, size, box, rendererBounds, colliderBounds;
            public bool active, activeHierarchy, rendererEnabled, colliderEnabled, marker;
            public int layer, order;
        }
        [Serializable] public class SceneRecord
        {
            public string path, guid, sha256; public Row[] before, after;
            public List<string> changes = new List<string>();
            public int missingScripts, missingSprites; public bool reopened;
        }
        [Serializable] public class Identity { public List<SceneRecord> scenes = new List<SceneRecord>(); }
        [Serializable] public class Probe
        {
            public string status, originalPrefabGuid, originalSha, originalFloorSha, floorPath, columnPath;
            public bool originalFloorFieldWasNull, nativeFloorResolved, nestedObjectsPreserved, behaviourReferencePreserved;
            public int networkObjectCount; public float[] floorWorld, floorBox, floorBounds, columnSize;
            public string runtime = "NOT_TESTED", network = "NOT_TESTED";
        }
        [Serializable] public class Elevators
        {
            public Probe probe;
            public List<Slot> slots = new List<Slot>();
        }
        [Serializable] public class Slot
        {
            public string scene, name, instanceId, sourceGuid, rootPath, floorId, floorPath, columnId, columnPath, doorId, doorPath, status;
            public float[] target, floorBounds, columnSize; public string runtime = "NOT_TESTED";
        }
        public static T Read<T>(string name) { return JsonUtility.FromJson<T>(File.ReadAllText(Run + "/" + name)); }
        public static void Write(string name, object data) { File.WriteAllText(Run + "/" + name, JsonUtility.ToJson(data, true), new UTF8Encoding(false)); }
        public static string Hash(string path) { using (var f = File.OpenRead(path)) using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(f)).Replace("-", "").ToLowerInvariant(); }
        public static void Need(bool condition, string reason) { if (!condition) throw new InvalidOperationException("NOV02 GUARD: " + reason); }
        public static string ScenePath(int i) { return Base + "Scenes/" + Names[i] + ".unity"; }
        public static string Id(Object o) { return o == null ? "MISSING" : GlobalObjectId.GetGlobalObjectIdSlow(o).ToString(); }
        public static long LocalId(Object o) { return o == null ? 0 : unchecked((long)GlobalObjectId.GetGlobalObjectIdSlow(o).targetObjectId); }
        public static string Full(Transform t) { return t.parent == null ? t.name : Full(t.parent) + "/" + t.name; }
        public static float[] V(Vector3 v) { return new[] { v.x, v.y, v.z }; }
        public static float[] B(Bounds b) { return new[] { b.center.x, b.center.y, b.center.z, b.size.x, b.size.y, b.size.z }; }
        public static string AssetId(Object o) { if (o == null) return ""; AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o, out string g, out long id); return g + ":" + id; }
        public static Transform Root(Scene scene) { var roots = scene.GetRootGameObjects(); Need(roots.Length == 1, "One map root: " + scene.path); return roots[0].transform; }
        public static Transform Find(Transform root, string suffix)
        {
            var found = root.GetComponentsInChildren<Transform>(true).Where(t => Full(t).EndsWith("/" + suffix, StringComparison.Ordinal)).ToArray();
            Need(found.Length == 1, "Unique path " + suffix + ": " + found.Length); return found[0];
        }
        public static Transform FindId(Transform root, long id)
        {
            var found = root.GetComponentsInChildren<Transform>(true).Where(t => LocalId(t.gameObject) == id).ToArray();
            Need(found.Length == 1, "Unique original GO " + id + ": " + found.Length); return found[0];
        }
        public static BoxCollider2D FindBox(Transform root, long id)
        {
            var found = root.GetComponentsInChildren<BoxCollider2D>(true).Where(t => LocalId(t) == id).ToArray();
            Need(found.Length == 1, "Unique original Box " + id + ": " + found.Length); return found[0];
        }
        public static string[] References(Component[] components)
        {
            var refs = new List<string>();
            foreach (var c in components.Where(x => x != null && !(x is Transform)))
            {
                var it = new SerializedObject(c).GetIterator();
                while (it.Next(true)) if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue != null) refs.Add(Id(c) + "/" + it.propertyPath + "=" + Id(it.objectReferenceValue));
            }
            return refs.OrderBy(x => x).ToArray();
        }
        public static Row Record(Transform t)
        {
            var sr = t.GetComponent<SpriteRenderer>(); var box = t.GetComponent<BoxCollider2D>(); var cs = t.GetComponents<Component>(); var matrix = t.localToWorldMatrix; var path = Full(t);
            return new Row { id = Id(t.gameObject), localId = LocalId(t.gameObject), transformId = Id(t), path = path, name = t.name, parent = t.parent == null ? "" : Id(t.parent.gameObject),
                matrix = Enumerable.Range(0, 16).Select(i => matrix[i]).ToArray(), localPosition = V(t.localPosition), position = V(t.position), scale = V(t.lossyScale), localScale = V(t.localScale),
                componentIds = cs.Select(Id).ToArray(), componentTypes = cs.Select(c => c == null ? "MISSING" : c.GetType().FullName).ToArray(), references = References(cs),
                sprite = sr == null ? "" : AssetId(sr.sprite), material = sr == null ? "" : AssetId(sr.sharedMaterial), drawMode = sr == null ? "" : sr.drawMode.ToString(),
                tint = sr == null ? new float[0] : new[] { sr.color.r, sr.color.g, sr.color.b, sr.color.a }, size = sr == null ? new float[0] : new[] { sr.size.x, sr.size.y },
                rendererBounds = sr == null ? new float[0] : B(sr.bounds), colliderBounds = box == null ? new float[0] : B(box.bounds), box = box == null ? new float[0] : new[] { box.size.x, box.size.y, box.offset.x, box.offset.y },
                active = t.gameObject.activeSelf, activeHierarchy = t.gameObject.activeInHierarchy, rendererEnabled = sr != null && sr.enabled, colliderEnabled = box != null && box.enabled,
                marker = path.Contains("90_DEV") || path.Contains("Anchor") || path.Contains("MARKER") || path.Contains("Marker"), layer = t.gameObject.layer, order = sr == null ? 0 : sr.sortingOrder,
                prefab = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject) };
        }
        public static Row[] Snapshot(Scene scene) { return scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Select(Record).ToArray(); }
        public static void StableEdit()
        {
            Need(Path.GetFullPath(Application.dataPath + "/..").Replace('\\', '/') == Project, "Wrong project");
            Need(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !AnimationMode.InAnimationMode(), "Stable Edit, no user AnimationMode");
            for (int i = 0; i < SceneManager.sceneCount; i++) Need(!SceneManager.GetSceneAt(i).isDirty, "Unsaved user scene: " + SceneManager.GetSceneAt(i).path);
            Need(PrefabStageUtility.GetCurrentPrefabStage() == null, "Close user Prefab stage manually before implementation");
            // Unity's built-in materials and virtual PackageCache shaders report dirty during import.
            // They are not editable project assets; never save them. Keep the dirty guard for real source files.
            var dirty = Resources.FindObjectsOfTypeAll<Object>().Where(o => o != null && EditorUtility.IsPersistent(o) && EditorUtility.IsDirty(o) && !ImportedDisplayCache(o)).Select(AssetDatabase.GetAssetPath)
                .Where(IsEditableProjectAsset).Distinct().ToArray();
            Need(dirty.Length == 0, "Dirty user asset(s): " + string.Join(",", dirty));
        }
        static bool ImportedDisplayCache(Object asset)
        {
            // Compiled shaders/fonts and TMP's editor glyph cache are not serialized by any N02 operation.
            // Never call SaveAssets: their existing dirty state is left untouched, while source hashes stay guarded.
            string path = AssetDatabase.GetAssetPath(asset);
            if (asset is Texture2D && path.EndsWith("/Photon/Fusion/Editor/EditorResources/Fusion-logo-2x.png", StringComparison.Ordinal))
            {
                // Fusion's read-only custom Inspector marks its loaded logo texture dirty.
                // Accept only identical source+meta with a clean importer; do not clear/save its dirty flag.
                var importer = AssetImporter.GetAtPath(path); var baseline = Read<Baseline>("BASELINE.json");
                return importer is TextureImporter && !EditorUtility.IsDirty(importer) && new[] { path, path + ".meta" }.All(p => baseline.files.Any(f => f.path == p && File.Exists(p) && Hash(p) == f.sha256));
            }
            return asset is Shader || asset is Font || asset.GetType().FullName == "TMPro.TMP_FontAsset" || AssetImporter.GetAtPath(path) is TrueTypeFontImporter;
        }
        static bool IsEditableProjectAsset(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (path.StartsWith("Assets/", StringComparison.Ordinal)) return true;
            if (!path.StartsWith("Packages/", StringComparison.Ordinal)) return false;
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(path);
            return package != null && package.resolvedPath.Replace('\\', '/').StartsWith(Project + "/Packages/", StringComparison.OrdinalIgnoreCase);
        }
        public static void Guard()
        {
            StableEdit(); var baseline = Read<Baseline>("BASELINE.json"); Need(baseline.backups.Length == 8 && baseline.backups.All(b => b.readback == "PASS" && File.Exists(b.backup) && Hash(b.backup) == b.sha256), "Eight verified initial backups");
            var created = Read<AssetList>("CREATED_ASSETS.json"); var owned = created.records.ToDictionary(x => Path.GetFullPath(x.path), x => x, StringComparer.OrdinalIgnoreCase);
            var scenePaths = new HashSet<string>(Enumerable.Range(0, 4).Select(i => Path.GetFullPath(ScenePath(i))), StringComparer.OrdinalIgnoreCase);
            foreach (var original in baseline.files)
            {
                string path = Path.GetFullPath(Project + "/" + original.path);
                string expected = scenePaths.Contains(path) && owned.ContainsKey(path) ? owned[path].sha256 : original.sha256;
                Need(File.Exists(path) && Hash(path) == expected, "Source drift: " + original.path);
            }
            foreach (var x in created.records) Need(File.Exists(x.path) && Hash(x.path) == x.sha256, "Owned file drift: " + x.path);
            var known = new HashSet<string>(baseline.files.Select(x => Path.GetFullPath(Project + "/" + x.path)), StringComparer.OrdinalIgnoreCase);
            foreach (var dir in new[] { "Assets", "Packages", "ProjectSettings" }) foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories)) Need(known.Contains(Path.GetFullPath(file)) || owned.ContainsKey(Path.GetFullPath(file)), "Unexpected source: " + file);
        }
        public static void TrackOwned()
        {
            var paths = Read<Control>("_work/CONTROL.json").projectFiles;
            Write("CREATED_ASSETS.json", new AssetList { records = paths.Where(File.Exists).Select(p => new FileRecord { path = Path.GetFullPath(p).Replace('\\', '/'), sha256 = Hash(p), bytes = new FileInfo(p).Length,
                guid = AssetDatabase.AssetPathToGUID(p), operation = p.EndsWith(".unity", StringComparison.Ordinal) ? "MODIFY_SCENE" : "CREATE_FILE" }).ToList() });
        }
        public static void Log(string text) { File.AppendAllText(Run + "/_work/native.log", DateTimeOffset.Now.ToString("o") + " " + text + "\n"); Debug.Log("NOV02 " + text); }
        public static void Checkpoint(string stage) { File.AppendAllText(Run + "/CHECKPOINT.md", "\n" + stage + " native data saved " + DateTimeOffset.Now.ToString("o") + ".\n"); }
        public static void BatchI00()
        {
            try { CaptureBaseline(); ProbeElevator(); Log("I00 native identity/stable Edit/protected hashes PASS"); }
            catch (Exception ex) { Log(ex.ToString()); if (Application.isBatchMode) EditorApplication.Exit(1); throw; }
        }
        [MenuItem("Tools/NOV02 Remake/I00 Capture native baseline")]
        public static void CaptureBaseline()
        {
            Guard(); Need(!File.Exists(Run + "/SCENE_IDENTITY_MAP.json"), "Native baseline already exists; do not overwrite");
            var identity = new Identity();
            for (int i = 0; i < 4; i++)
            {
                var scene = EditorSceneManager.OpenScene(ScenePath(i), OpenSceneMode.Single); var before = Snapshot(scene);
                identity.scenes.Add(new SceneRecord { path = ScenePath(i), guid = AssetDatabase.AssetPathToGUID(ScenePath(i)), sha256 = Hash(ScenePath(i)), before = before, after = before });
                Need(!scene.isDirty, "Read-only baseline dirtied " + scene.path); Log("Native baseline " + scene.path + " GO=" + before.Length);
            }
            Write("SCENE_IDENTITY_MAP.json", identity); Checkpoint("I00_BASELINE"); Guard();
        }
        public static Component ElevatorComponent(GameObject root) { return root.GetComponents<Component>().Single(c => c != null && c.GetType().Name == "Elevator"); }
        public static Component[] NetworkObjects(GameObject root) { return root.GetComponentsInChildren<Component>(true).Where(c => c != null && c.GetType().FullName == "Fusion.NetworkObject").ToArray(); }
        public static GameObject NestedFloor(GameObject elevator)
        {
            var floors = elevator.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Floor" && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject) == OriginalFloor).ToArray();
            Need(floors.Length == 1, "Resolve exactly one source-linked nested Floor"); return floors[0].gameObject;
        }
        public static Bounds BoxWorld(BoxCollider2D b)
        {
            var center = b.transform.TransformPoint(b.offset); var result = new Bounds(center, Vector3.zero);
            foreach (float x in new[] { -.5f, .5f }) foreach (float y in new[] { -.5f, .5f }) result.Encapsulate(b.transform.TransformPoint(b.offset + new Vector2(b.size.x * x, b.size.y * y)));
            return result;
        }
        [MenuItem("Tools/NOV02 Remake/I01 Probe original Elevator")]
        public static void ProbeElevator()
        {
            Guard(); var originalHash = Hash(OriginalElevator); var floorHash = Hash(OriginalFloor); var preview = EditorSceneManager.NewPreviewScene(); var probe = new Probe();
            try
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(OriginalElevator); Need(asset != null, "Original Elevator asset");
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, preview); var mb = ElevatorComponent(instance); var so = new SerializedObject(mb); var fp = so.FindProperty("floor");
                probe.originalFloorFieldWasNull = fp.objectReferenceValue == null;
                var floor = NestedFloor(instance); var column = so.FindProperty("column").objectReferenceValue as SpriteRenderer; Need(column != null && column.transform.IsChildOf(instance.transform), "Original Column same instance");
                var nos = NetworkObjects(instance); Need(nos.Length == 2 && NetworkObjects(floor).Length == 1, "Root and nested Floor NetworkObjects preserved");
                fp.objectReferenceValue = floor; so.ApplyModifiedPropertiesWithoutUndo(); Need(new SerializedObject(mb).FindProperty("floor").objectReferenceValue == floor, "Native floor assignment");
                var rootNO = nos.Single(n => n.gameObject == instance); var networkSO = new SerializedObject(rootNO);
                var nested = networkSO.FindProperty("NestedObjects"); var behaviours = networkSO.FindProperty("NetworkedBehaviours");
                Need(nested.arraySize == 1 && nested.GetArrayElementAtIndex(0).objectReferenceValue == NetworkObjects(floor)[0], "Native nested NetworkObject array");
                Need(behaviours.arraySize == 1 && behaviours.GetArrayElementAtIndex(0).objectReferenceValue == mb, "Native behaviour array");
                var box = floor.GetComponentsInChildren<BoxCollider2D>(true).Single(); var effector = box.GetComponent<PlatformEffector2D>();
                Need(effector != null && effector.useOneWay && box.usedByEffector, "Original one-way Floor");
                probe.status = "NATIVE_PREVIEW_INSTANCE_REFERENCE_PASS"; probe.nativeFloorResolved = true; probe.nestedObjectsPreserved = true; probe.behaviourReferencePreserved = true;
                probe.originalPrefabGuid = AssetDatabase.AssetPathToGUID(OriginalElevator); probe.originalSha = originalHash; probe.originalFloorSha = floorHash; probe.floorPath = Full(floor.transform); probe.columnPath = Full(column.transform);
                probe.networkObjectCount = nos.Length; probe.floorWorld = V(floor.transform.position); probe.floorBox = new[] { box.size.x, box.size.y, box.offset.x, box.offset.y }; probe.floorBounds = B(BoxWorld(box)); probe.columnSize = new[] { column.size.x, column.size.y };
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
            Need(Hash(OriginalElevator) == originalHash && Hash(OriginalFloor) == floorHash, "Original prefab hashes");
            Write("ELEVATOR_REUSE_QA.json", new Elevators { probe = probe }); Guard(); Checkpoint("I01_NATIVE_PROBE"); Log("Native Elevator nested Floor/Column/NO/behaviour refs PASS; no runtime calls");
        }
        public const string Art = Base + "Art/N02Remake/";
        public const string Anim = Base + "Animation/N02Remake/";
        static Material Lit { get { return AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath("a97c105638bdf8b4a8650670310a4cd3")); } }
        public static Sprite Sprite(string guid, string name = null)
        {
            var all = AssetDatabase.LoadAllAssetsAtPath(guid.StartsWith("Assets/") ? guid : AssetDatabase.GUIDToAssetPath(guid)).OfType<Sprite>().ToArray();
            var sprite = name == null ? all.Single() : all.Single(x => x.name == name); Need(sprite != null, "Sprite " + guid + "/" + name); return sprite;
        }
        static Sprite Tile { get { return Sprite("9cac4b34d8bf0f742bfbbf52ab59ea89", "Tiles_0"); } }
        static Sprite Metal { get { return Sprite("220545a652a206641beb4ad37faab603", "Wall"); } }
        static Transform New(Transform parent, string name, Vector3 world)
        {
            Need(parent.Find(name) == null, "New N02 object must not overwrite " + Full(parent) + "/" + name);
            var t = new GameObject(name).transform; t.SetParent(parent, false); t.position = world; return t;
        }
        static void OffVisual(Transform parent)
        {
            foreach (var sr in parent.GetComponentsInChildren<SpriteRenderer>(true))
                if (!Full(sr.transform).Contains("90_DEV")) { sr.enabled = false; if (PrefabUtility.IsPartOfPrefabInstance(sr)) PrefabUtility.RecordPrefabInstancePropertyModifications(sr); }
        }
        static SpriteRenderer Rect(Transform parent, string name, Sprite sprite, float x, float y, float width, float height, int order = -1, Color? tint = null, bool sliced = false)
        {
            var t = New(parent, name, new Vector3(x, y, 0)); var sr = t.gameObject.AddComponent<SpriteRenderer>(); sr.sprite = sprite; sr.sharedMaterial = Lit; sr.sortingOrder = order; sr.color = tint ?? Color.white;
            var scale = parent.lossyScale; t.localScale = new Vector3(1 / scale.x, 1 / scale.y, 1 / scale.z);
            sr.drawMode = sliced ? SpriteDrawMode.Sliced : SpriteDrawMode.Tiled; sr.size = new Vector2(width, height); return sr;
        }
        static SpriteRenderer Prop(Transform parent, string name, Sprite sprite, float x, float foot, float width, float height, int order = 2)
        {
            var t = New(parent, name, new Vector3(x, foot, 0)); var sr = t.gameObject.AddComponent<SpriteRenderer>(); sr.sprite = sprite; sr.sharedMaterial = Lit; sr.sortingOrder = order;
            float scale = Mathf.Min(width / sprite.bounds.size.x, height / sprite.bounds.size.y); t.localScale = Vector3.one * scale;
            t.position += new Vector3(-sprite.bounds.center.x * scale, -sprite.bounds.min.y * scale, 0); return sr;
        }
        static BoxCollider2D StaticBox(Transform p, string name, float x, float top, float width, float height)
        {
            var t = New(p, name, new Vector3(x, top - height / 2, 0)); t.gameObject.layer = 6; var b = t.gameObject.AddComponent<BoxCollider2D>(); b.size = new Vector2(width, height); return b;
        }
        static void BoxAt(BoxCollider2D box, float x, float top, float width, float height)
        {
            Need(box.transform.lossyScale == Vector3.one && box.transform.rotation == Quaternion.identity, "World box adjustment requires identity scale/rotation");
            box.transform.position = new Vector3(x, top - height / 2, box.transform.position.z); box.offset = Vector2.zero; box.size = new Vector2(width, height);
        }
        static void Platform(Transform parent, float left, float top, float width, float visualHeight, long colliderId = 0)
        {
            OffVisual(parent); Rect(parent, "N02_PLATFORM_ART", Tile, left + width / 2, top - visualHeight / 2, width, visualHeight);
            if (colliderId != 0) BoxAt(FindBox(Root(parent.gameObject.scene), colliderId), left + width / 2, top, width, .4f);
        }
        static void Ladder(Transform parent, string name, float x, float foot, float height)
        {
            var sr = Rect(parent, name, Sprite(Art + "Ladder_N02.png"), x, foot + height / 2, .5f, height / .8f, 1);
            sr.transform.localScale = Vector3.one * .8f;
        }
        [MenuItem("Tools/NOV02 Remake/I01 Import selected N02 art")]
        public static void ImportArt()
        {
            Guard(); Need(Read<Elevators>("ELEVATOR_REUSE_QA.json").probe.nativeFloorResolved, "Native probe first");
            foreach (string dir in new[] { Art.TrimEnd('/'), Anim.TrimEnd('/') })
            { Need(!AssetDatabase.IsValidFolder(dir), "New art folder already exists; use recorded resume, do not rebuild"); Need(AssetDatabase.IsValidFolder(dir.Substring(0, dir.LastIndexOf('/'))), "Existing direct parent"); AssetDatabase.CreateFolder(dir.Substring(0, dir.LastIndexOf('/')), "N02Remake"); }
            string[] names = { "GatePanel_N02", "Ladder_N02", "PressurePad_N02", "Generator_N02", "CrusherHousing_N02", "CrusherPlate_N02", "CoverPlate_N02", "RobotThreat_N02", "HookRing_N02", "Battery_N02" };
            int[] widths = { 56, 16, 64, 96, 128, 128, 27, 40, 26, 30 }; int[] heights = { 256, 100, 8, 74, 57, 15, 80, 64, 32, 48 }; int[] canvasW = { 64, 16, 64, 96, 128, 128, 32, 48, 32, 32 };
            for (int i = 0; i < names.Length; i++)
            {
                string path = Art + names[i] + ".png"; Need(!File.Exists(path), "No overwrite of art"); File.Copy(Run + "/_work/" + names[i] + ".png", path);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport); var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                ti.textureType = TextureImporterType.Sprite; ti.spriteImportMode = SpriteImportMode.Multiple; ti.spritePixelsPerUnit = i == 0 ? 64 : 32; ti.filterMode = FilterMode.Point; ti.mipmapEnabled = false;
                ti.textureCompression = TextureImporterCompression.Uncompressed; ti.alphaIsTransparency = true; ti.sRGBTexture = true; ti.npotScale = TextureImporterNPOTScale.None;
                var settings = new TextureImporterSettings(); ti.ReadTextureSettings(settings); settings.spriteMeshType = SpriteMeshType.FullRect; ti.SetTextureSettings(settings); ti.SaveAndReimport();
                var fac = new SpriteDataProviderFactories(); fac.Init(); var provider = fac.GetSpriteEditorDataProviderFromObject(ti); provider.InitSpriteEditorDataProvider();
                var rect = new SpriteRect { name = names[i], rect = new UnityEngine.Rect((canvasW[i] - widths[i]) / 2, 0, widths[i], heights[i]), alignment = SpriteAlignment.Center, pivot = new Vector2(.5f, .5f), spriteID = GUID.Generate(), border = i == 0 ? new Vector4(8, 8, 8, 8) : i == 1 ? new Vector4(2, 0, 2, 0) : Vector4.zero };
                provider.SetSpriteRects(new[] { rect }); provider.GetDataProvider<ISpriteNameFileIdDataProvider>().SetNameFileIdPairs(new[] { new SpriteNameFileIdPair(rect.name, rect.spriteID) }); provider.Apply(); ti.SaveAndReimport();
                Need(Sprite(path) != null, "Imported selected sprite");
            }
            TrackOwned(); Guard(); Checkpoint("I01_ART_IMPORTED"); Log("10 selected N02 PNG imported, exact crop/PPU/Point/FullRect/uncompressed");
        }
        static void Save(Scene scene, string stage)
        {
            string savedPath = scene.path;
            Need(Enumerable.Range(0, 4).Any(i => scene.path == ScenePath(i)), "Exact approved scene only");
            var identity = Read<Identity>("SCENE_IDENTITY_MAP.json"); var record = identity.scenes.Single(x => x.path == scene.path); var now = Snapshot(scene); var ids = now.ToDictionary(x => x.id);
            foreach (var old in record.before)
            {
                Need(ids.ContainsKey(old.id), "Original object removed: " + old.path);
                var current = ids[old.id]; Need(old.componentIds.All(current.componentIds.Contains), "Original component removed " + old.path);
                if (old.path.Contains("90_DEV")) Need(JsonUtility.ToJson(old) == JsonUtility.ToJson(current), "Protected developer branch altered " + old.path);
            }
            Need(!now.Any(r => r.componentTypes.Contains("MISSING")), "Missing script in approved scene");
            Need(!now.Any(r => r.componentTypes.Contains("UnityEngine.SpriteRenderer") && r.sprite == ""), "Missing sprite");
            Need(EditorSceneManager.SaveScene(scene, scene.path), "Save exact scene"); TrackOwned();
            var reopened = EditorSceneManager.OpenScene(savedPath, OpenSceneMode.Single); Need(!reopened.isDirty, "Reopen saved scene"); record.after = Snapshot(reopened); record.reopened = true; record.sha256 = Hash(savedPath); record.changes.Add(stage);
            Write("SCENE_IDENTITY_MAP.json", identity); Guard(); Checkpoint(stage + "_SAVED_REOPENED"); Log(stage + " stored/reopened " + savedPath);
        }
        static bool ConvertSlot(Transform root, long boxId, string name, float x, float top, float width)
        {
            var qa = Read<Elevators>("ELEVATOR_REUSE_QA.json"); Need(!qa.slots.Any(s => s.scene == root.gameObject.scene.path && s.name == name), "Slot already recorded");
            var old = FindBox(root, boxId); Need(old.enabled, "Old dock remains enabled before verified replacement");
            bool upperMalkuth = root.gameObject.scene.path == ScenePath(0) && name != "LOWER_EXIT";
            var p = Find(root, "00_MAP_EDIT/ELEVATORS" + (upperMalkuth ? "/" + name : ""));
            if (upperMalkuth) OffVisual(p);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(OriginalElevator), p);
            go.name = name + "_ORIGINAL_ELEVATOR_N02"; go.transform.position = new Vector3(x, top - 2.30f, 0); go.transform.rotation = Quaternion.identity; go.transform.localScale = Vector3.one;
            var floor = NestedFloor(go); var mb = ElevatorComponent(go); var so = new SerializedObject(mb); so.FindProperty("floor").objectReferenceValue = floor;
            so.FindProperty("originPos").floatValue = floor.transform.position.y; so.FindProperty("dest").floatValue = floor.transform.position.y; so.ApplyModifiedPropertiesWithoutUndo(); PrefabUtility.RecordPrefabInstancePropertyModifications(mb);
            var column = so.FindProperty("column").objectReferenceValue as SpriteRenderer; Need(column != null, "Native column"); var box = floor.GetComponentsInChildren<BoxCollider2D>(true).Single(); box.size = new Vector2(width, box.size.y); PrefabUtility.RecordPrefabInstancePropertyModifications(box);
            var deck = box.GetComponent<SpriteRenderer>(); Need(deck != null, "Nested original deck renderer"); deck.drawMode = SpriteDrawMode.Tiled; deck.size = new Vector2(width, deck.size.y); PrefabUtility.RecordPrefabInstancePropertyModifications(deck);
            var door = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(DoorPrefab), floor.transform); door.name = "DoorRoot"; door.transform.localPosition = new Vector3(0, .55f, 0); door.transform.localRotation = Quaternion.identity; door.transform.localScale = Vector3.one;
            var bounds = BoxWorld(box); Need(Mathf.Abs(bounds.max.y - top) < .0001f && Mathf.Abs(bounds.size.x - width) < .0001f, "Native Floor top/width");
            Need(NetworkObjects(go).Length == 2 && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go) == OriginalElevator, "Original linked components preserved");
            var nos = new SerializedObject(NetworkObjects(go).Single(n => n.gameObject == go)); Need(nos.FindProperty("NestedObjects").GetArrayElementAtIndex(0).objectReferenceValue == NetworkObjects(floor)[0], "Nested reference after scene placement");
            Need(nos.FindProperty("NetworkedBehaviours").GetArrayElementAtIndex(0).objectReferenceValue == mb, "Original behaviour reference");
            var oldSlot = old.transform.parent;
            // Retain the original paths because existing local anchors and path-based references must remain stable.
            foreach (var sr in oldSlot.GetComponentsInChildren<SpriteRenderer>(true)) if (!Full(sr.transform).Contains("90_DEV")) { sr.enabled = false; if (PrefabUtility.IsPartOfPrefabInstance(sr)) PrefabUtility.RecordPrefabInstancePropertyModifications(sr); }
            old.enabled = false;
            qa.slots.Add(new Slot { scene = root.gameObject.scene.path, name = name, instanceId = Id(go), sourceGuid = AssetDatabase.AssetPathToGUID(OriginalElevator), rootPath = Full(go.transform), floorId = Id(floor), floorPath = Full(floor.transform), columnId = Id(column), columnPath = Full(column.transform), doorId = Id(door), doorPath = Full(door.transform), status = "INSTANCE_NATIVE_REFERENCE_OK_PENDING_SAVE", target = new[] { x, top, width }, floorBounds = B(bounds), columnSize = new[] { column.size.x, column.size.y } });
            Write("ELEVATOR_REUSE_QA.json", qa); return true;
        }
        public static void ApplyElevators(int i)
        {
            Guard(); Need(Read<Elevators>("ELEVATOR_REUSE_QA.json").probe.nativeFloorResolved, "Probe gate"); var s = EditorSceneManager.OpenScene(ScenePath(i), OpenSceneMode.Single); var root = Root(s);
            var previous = Read<Elevators>("ELEVATOR_REUSE_QA.json");
            if (previous.slots.Any(x => x.scene == ScenePath(i)))
            {
                Need(previous.slots.Where(x => x.scene == ScenePath(i)).All(x => x.status == "INSTANCE_NATIVE_REFERENCE_OK_PENDING_SAVE"), "Already saved slots; do not rerun");
                // A prior explicit save succeeded but recording used an invalidated Scene handle after reopening.
                // Read back that owned saved result; never create another instance or restore an old scene.
                foreach (var slot in previous.slots.Where(x => x.scene == ScenePath(i)))
                {
                    var instance = Find(root, slot.rootPath.Substring(root.name.Length + 1)).gameObject; var mb = ElevatorComponent(instance); var floor = NestedFloor(instance);
                    Need(new SerializedObject(mb).FindProperty("floor").objectReferenceValue == floor && NetworkObjects(instance).Length == 2, "Saved native Floor/NO references");
                    slot.status = "SAVED_INSTANCE_NATIVE_REFERENCE_OK";
                }
                var identity = Read<Identity>("SCENE_IDENTITY_MAP.json"); var record = identity.scenes.Single(x => x.path == ScenePath(i)); record.after = Snapshot(s); record.sha256 = Hash(ScenePath(i)); record.reopened = true; record.changes.Add("I01_ELEVATORS_" + i + "_RECORDING_RESUMED");
                Write("SCENE_IDENTITY_MAP.json", identity); Write("ELEVATOR_REUSE_QA.json", previous); Guard(); Checkpoint("I01_ELEVATORS_" + i + "_SAVED_REOPENED_RECORDING_RESUMED"); return;
            }
            if (i == 0)
            {
                long[] ids = { 37854447, 1908105199, 858749602, 1071567029 }; string[] rooms = { "RABBIT", "RAT", "FROG", "BEAR" };
                for (int n = 0; n < 4; n++) ConvertSlot(root, ids[n], rooms[n], 24.5f + 28 * n, 26, 4);
                ConvertSlot(root, 24232482, "LOWER_EXIT", 118, 0, 6);
            }
            if (i == 1) { ConvertSlot(root, 826932511, "YESOD_ENTRY", -6, 0, 6); ConvertSlot(root, 974642957, "YESOD_EXIT_PROPOSED", 33.484375f, 9.9375f, 6); }
            if (i == 2) { ConvertSlot(root, 551660898, "HOD_ENTRY", -4, 0, 6); ConvertSlot(root, 1507326166, "HOD_EXIT_PROPOSED", -12, 0, 6); }
            if (i == 3) ConvertSlot(root, 501513757, "NETZACH_ENTRY", -8, 0, 6);
            Save(s, "I01_ELEVATORS_" + i); var qa = Read<Elevators>("ELEVATOR_REUSE_QA.json"); foreach (var slot in qa.slots.Where(x => x.scene == ScenePath(i))) slot.status = "SAVED_INSTANCE_NATIVE_REFERENCE_OK"; Write("ELEVATOR_REUSE_QA.json", qa);
        }
        [MenuItem("Tools/NOV02 Remake/I02 Rebuild SourceA visual from source")]
        public static void ApplyYesod()
        {
            Guard(); var s = EditorSceneManager.OpenScene(ScenePath(1), OpenSceneMode.Single); var root = Root(s); var pz = FindId(root, 123506200);
            Need(!pz.GetComponentsInChildren<Transform>(true).Any(t => t.name == "Source_Shuttle_VISUAL_ONLY_NOT_A_MOVING_PLATFORM"), "SourceA already applied");
            Platform(FindId(root, 1042884431), 0, .015625f, 3.671875f, 1.90625f, 1227903251);
            Platform(FindId(root, 661335837), 14.859375f, .09375f, 8.203125f, 1.90625f, 722850042);
            Platform(FindId(root, 1090652447), 0, 5.390625f, 3.671875f, 1.90625f, 1752485050);
            Platform(FindId(root, 1541813825), 18.234375f, 5.640625f, 4.828125f, 1.90625f, 279599239);
            Platform(FindId(root, 354346679), 6.15625f, 12.40625f, 2.546875f, 1.109375f, 1465982563);
            Platform(FindId(root, 572397042), 22.28125f, 9.9375f, 8.203125f, 1.90625f, 1097483644);
            long[] pads = { 1022222070, 1448294894, 1762620364, 764243136 }; float[] px = { 5.15625f, 7.875f, 10.53125f, 13.15625f };
            for (int k = 0; k < 4; k++)
            { var p = FindId(root, pads[k]); OffVisual(p); Rect(p, "N02_STATIC_PAD_ART_SINKING_NOT_IMPLEMENTED", Metal, px[k], .109375f - 1.453125f / 2, 1.4375f, 1.453125f); StaticBox(p, "N02_STATIC_NEUTRAL_COLLIDER", px[k], .109375f, 1.4375f, 1.453125f); }
            var platforms = FindId(root, 755898078); var box = New(platforms, "MetalBox_LeftUpper_STATIC_N02", new Vector3(4.03125f, 7.8828125f, 0)); Rect(box, "Visual", Metal, 4.03125f, 7.8828125f, 1.4375f, 1.453125f); StaticBox(box, "STATIC_COLLIDER", 4.03125f, 8.609375f, 1.4375f, 1.453125f);
            foreach (var pair in new[] { new Vector2(13.1953125f, 2), new Vector2(19.2890625f, 3) })
            { var t = New(platforms, "Source_UpperSmall_" + pair.y + "_N02", new Vector3(pair.x, 12.40625f, 0)); Rect(t, "Visual", Tile, pair.x, 12.40625f - 1.109375f / 2, 2.546875f, 1.109375f); StaticBox(t, "STATIC_COLLIDER", pair.x, 12.40625f, 2.546875f, .4f); }
            var shuttle = New(platforms, "Source_Shuttle_VISUAL_ONLY_NOT_A_MOVING_PLATFORM", new Vector3(15.1015625f, 5.578125f - 1.90625f / 2, 0)); var moving = New(shuttle, "MovingVisual", shuttle.position);
            Rect(moving, "Platform_ART", Tile, shuttle.position.x, shuttle.position.y, 6.265625f, 1.90625f); StaticBox(shuttle, "STATIC_NEUTRAL_COLLIDER", 15.1015625f, 5.578125f, 6.265625f, .4f);
            var clip = new AnimationClip { name = "Yesod_ShuttlePreview_N02", frameRate = 30 }; float[] positions = { 766, 766, 721, 439, 229, 478, 679 };
            clip.SetCurve("MovingVisual", typeof(Transform), "localPosition.x", new AnimationCurve(positions.Select((x, k) => new Keyframe(k, (x - 766) / 64)).ToArray())); AssetDatabase.CreateAsset(clip, Anim + clip.name + ".anim"); AssetDatabase.SaveAssetIfDirty(clip);
            FindId(root, 1440255727).position = new Vector3(.75f, 6.0625f, 0); FindId(root, 1931613436).position = new Vector3(19.1875f, 6.28125f, 0);
            long[] buttonIds = { 225464774, 1114779810 }; for (int k = 0; k < 2; k++) { var t = FindId(root, buttonIds[k]); var sr = t.GetComponent<SpriteRenderer>(); sr.transform.localScale = Vector3.one * (1.3125f / sr.sprite.bounds.size.x); t.position = new Vector3(k == 0 ? .765625f : 2.59375f, 1.875f, 0); }
            Rect(pz, "Source_RedHorizontalLine_MEANING_OPEN_N02", Metal, 15, 10.6484375f, 30, .234375f, 1, new Color(.75f, .13f, .15f));
            var lift = FindId(root, 822529744); OffVisual(lift); Rect(lift, "N02_SPECIAL_LIFT_PLATE_VISUAL_ONLY", Tile, 26.4375f, .0625f - .953125f / 2, 6.4375f, .953125f); Rect(lift, "N02_SPECIAL_LIFT_COLUMN_VISUAL_ONLY", Sprite("9cac4b34d8bf0f742bfbbf52ab59ea89", "Tiles_5"), 26.390625f, -1.8984375f, 1.53125f, 3.390625f, -2);
            Prop(pz, "Battery_TARGET_PROPOSED_MEANING_OPEN_N02", Sprite(Art + "Battery_N02.png"), 29.05f, 12.11f, 1, 1.5f);
            Need(Read<Elevators>("ELEVATOR_REUSE_QA.json").slots.Any(x => x.name == "YESOD_EXIT_PROPOSED" && x.status.StartsWith("SAVED")), "Exit Bay verified before approach archive"); FindBox(root, 751397661).enabled = false; OffVisual(FindBox(root, 751397661).transform.parent);
            var bg = FindId(root, 1290669041).GetComponent<SpriteRenderer>(); var extension = Rect(Find(root, "00_MAP_EDIT"), "N02_EXIT_BACKGROUND_EXTENSION", bg.sprite, 34, 8, 15, 24, -10); extension.transform.position = new Vector3(34, 8, 10);
            TrackOwned(); Save(s, "I02_SOURCE_7FRAME_VISUAL");
        }
        [MenuItem("Tools/NOV02 Remake/I03 Build Hod full visuals")]
        public static void ApplyHod()
        {
            Guard(); var s = EditorSceneManager.OpenScene(ScenePath(2), OpenSceneMode.Single); var root = Root(s); var map = Find(root, "00_MAP_EDIT");
            Need(!map.GetComponentsInChildren<Transform>(true).Any(t => t.name == "PCS_HodCrusher_N02_EDITOR_PREVIEW_ONLY_NO_DAMAGE"), "Hod already applied");
            var rabbit = FindId(root, 1983108065); var frog = FindId(root, 542412001); var bear = FindId(root, 706480048); var rat = FindId(root, 1944775393); var threat = FindId(root, 1193641243);
            foreach (var t in map.GetComponentsInChildren<Transform>(true).Where(t => t.name.Contains("FRAME_ONLY") || t.name == "R2_ROLE_VISUAL")) OffVisual(t);
            Platform(FindId(root, 791422689), 8, 4.5f, 18, .4f, 366590464);
            Prop(rabbit, "Generator_N02_VISUAL_ONLY", Sprite(Art + "Generator_N02.png"), 11.5f, 4.5f, 3, 2.5f); Ladder(rabbit, "Rabbit_Access_Ladder_VISUAL_ONLY_N02", 8.5f, 0, 4.5f);
            Prop(rabbit, "CrusherControl_N02_VISUAL_ONLY", Sprite("31841eeb4c020f24cbdf27597590d5f9", "objects_7"), 20.5f, 4.5f, 1, 1);
            var prefabRoot = new GameObject("PCS_HodCrusher_N02"); SceneManager.MoveGameObjectToScene(prefabRoot, s);
            var frame = New(prefabRoot.transform, "FrameStatic", Vector3.zero); var housing = Prop(frame, "Housing", Sprite(Art + "CrusherHousing_N02.png"), 0, .5f, 4, 2); housing.transform.position = new Vector3(0, 1.5f, 0);
            Rect(frame, "GuideLeft", Metal, -1.9f, -1.6f, .18f, 4.6f, -2); Rect(frame, "GuideRight", Metal, 1.9f, -1.6f, .18f, 4.6f, -2);
            var plateMoving = New(prefabRoot.transform, "PlateMoving", new Vector3(0, -.7f, 0)); Rect(plateMoving, "PlateVisual", Sprite(Art + "CrusherPlate_N02.png"), 0, -.7f, 4, .5f, 3);
            Need(prefabRoot.GetComponentsInChildren<Component>(true).All(c => c is Transform || c is SpriteRenderer), "Pure visual crusher only"); Need(!File.Exists(Art + "PCS_HodCrusher_N02.prefab"), "New crusher prefab");
            var prefab = PrefabUtility.SaveAsPrefabAsset(prefabRoot, Art + "PCS_HodCrusher_N02.prefab"); Object.DestroyImmediate(prefabRoot);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, rabbit); inst.name = "PCS_HodCrusher_N02_EDITOR_PREVIEW_ONLY_NO_DAMAGE"; inst.transform.position = new Vector3(22, 4.5f, 0);
            var cycle = new AnimationClip { name = "Hod_CrusherCycle_N02", frameRate = 30 }; cycle.SetCurve("PlateMoving", typeof(Transform), "localPosition.y", new AnimationCurve(new Keyframe(0, -.7f), new Keyframe(.6f, -4.2f), new Keyframe(.8f, -4.2f), new Keyframe(1.6f, -.7f))); AssetDatabase.CreateAsset(cycle, Anim + cycle.name + ".anim"); AssetDatabase.SaveAssetIfDirty(cycle);
            Prop(frog, "CoverPlate_N02_STATIC_VISUAL_ONLY", Sprite(Art + "CoverPlate_N02.png"), 12, 1.25f, 1, 2.5f); Prop(frog, "HookRing_N02_VISUAL_ONLY", Sprite(Art + "HookRing_N02.png"), 14, 2.8f, 1, 1); Rect(frog, "HookCable_N02_VISUAL_ONLY", Metal, 14, 3.95f, .10f, .7f, -2); Rect(frog, "CoverCable_N02_VISUAL_ONLY", Metal, 12, 4.1f, .10f, .7f, -2);
            var console = FindId(root, 1737793640); var csr = console.GetComponent<SpriteRenderer>(); console.localScale = Vector3.one * (1.4f / csr.sprite.bounds.size.y); console.position = new Vector3(1.5f, -csr.sprite.bounds.min.y * console.localScale.y, 0);
            Rect(rat, "InterferenceLine_X4_VISUAL_ONLY_N02", Metal, 4, 1.0f, .12f, 2, 1, new Color(.8f, .25f, .18f)); Prop(bear, "ThrowProp_STATIC_NO_THROWER_N02", Sprite("31841eeb4c020f24cbdf27597590d5f9", "objects_4"), 7, 0, .9f, .9f);
            var spawn = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(DoorPrefab), threat); spawn.name = "ThreatSpawnDoor_STATIC_NO_SPAWNER_N02"; spawn.transform.position = new Vector3(27, 0, 0);
            Prop(threat, "RobotThreat_N02_STATIC_NO_AI", Sprite(Art + "RobotThreat_N02.png"), 25.5f, 0, 1.5f, 2); var drum = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Base + "Art/R3/UNUSED_CANDIDATES/Drum_R3_UNUSED.prefab"), threat); drum.name = "Drum_R3_REUSED_STATIC_N02"; var ds = drum.GetComponentInChildren<SpriteRenderer>(); drum.transform.position = new Vector3(23.8f, -ds.sprite.bounds.min.y * drum.transform.localScale.y, 0);
            var qa = Read<Elevators>("ELEVATOR_REUSE_QA.json"); Need(qa.slots.Count(x => x.scene == s.path && x.status.StartsWith("SAVED")) == 2, "Hod both A slots saved before approach split");
            var bridge = FindBox(root, 1845465644); BoxAt(bridge, -8, 0, 2, .4f); OffVisual(bridge.transform.parent); Rect(bridge.transform.parent, "N02_MID_BRIDGE_ART", Tile, -8, -.2f, 2, .4f); var terrain = Find(root, "00_MAP_EDIT/TERRAIN"); Rect(terrain, "N02_LANE_BRIDGE_ART", Tile, -.5f, -.2f, 1, .4f); StaticBox(terrain, "N02_STATIC_LANE_BRIDGE", -.5f, 0, 1, .4f);
            TrackOwned(); Save(s, "I03_HOD_ALL_VISUALS");
        }
        [MenuItem("Tools/NOV02 Remake/I04 Improve Malkuth existing visuals")]
        public static void ApplyMalkuth()
        {
            Guard(); var s = EditorSceneManager.OpenScene(ScenePath(0), OpenSceneMode.Single); var root = Root(s);
            long[] gates = { 1030788826, 260144288, 1103881095, 1130593111, 561959511, 299893613 };
            foreach (long id in gates)
            {
                var gate = FindId(root, id); Need(gate.GetComponentsInChildren<Collider2D>(true).Length == 0, "Existing static gate has no gameplay collider"); OffVisual(gate); bool rat = id == 260144288 || id == 1103881095 || id == 1130593111;
                Rect(gate, "Gate_ART_N02_STATIC_NO_OPEN_DIRECTION", Sprite(Art + "GatePanel_N02.png"), gate.position.x, gate.position.y, .8f, rat ? 2.2f : 3, 2, null, true);
            }
            var pad = FindId(root, 454519004); OffVisual(pad); Prop(pad, "ExistingActuator_ART_N02_NO_DUPLICATE_DEVICE", Sprite(Art + "PressurePad_N02.png"), pad.position.x, 14.1f, 2, .5f);
            var ratDevice = FindId(root, 260144288).parent; Ladder(ratDevice, "Ladder_VISUAL_N02_NO_CLIMB", 36.25f, 14.1f, 5.55f);
            foreach (var pair in new[] { new long[] { 1613133711, 1814827121 }, new long[] { 430089353, 280429176 }, new long[] { 13139398, 1401020984 } })
            { var visual = FindId(root, pair[0]); OffVisual(visual); var b = BoxWorld(FindBox(root, pair[1])); Rect(visual, "N02_ART_ALIGNED_TO_UNCHANGED_BOX", Tile, b.center.x, b.center.y, b.size.x, b.size.y, -1); }
            Save(s, "I04_MALKUTH_VISUAL_ONLY");
        }
        [MenuItem("Tools/NOV02 Remake/I05 Verify Netzach preserved state")]
        public static void VerifyNetzach()
        {
            Guard(); var scene = EditorSceneManager.OpenScene(ScenePath(3), OpenSceneMode.Single); var map = Root(scene); var identity = Read<Identity>("SCENE_IDENTITY_MAP.json").scenes[3]; var rows = Snapshot(scene).ToDictionary(x => x.id);
            foreach (var old in identity.before.Where(r => r.path.Contains("PuzzleChamber"))) Need(JsonUtility.ToJson(rows[old.id]) == JsonUtility.ToJson(old), "Netzach puzzle preserved");
            Log("I05 Netzach geometry/tint/neutral blocks/DEV all unchanged. Bear1.9 vs gap1.5/1.75 static OPEN_TEAM; movement NOT_TESTED"); Checkpoint("I05_NETZACH_STATIC_PRESERVED");
        }
        public static Texture2D Render(Camera camera, Vector2 center, float halfHeight, float aspect = 16f / 9, int height = 540)
        {
            string serialized = EditorJsonUtility.ToJson(camera); var position = camera.transform.position; var rotation = camera.transform.rotation; var target = camera.targetTexture; float oldAspect = camera.aspect; bool dirty = camera.gameObject.scene.isDirty;
            var rt = new RenderTexture(Mathf.RoundToInt(height * aspect), height, 24); var active = RenderTexture.active;
            try
            {
                camera.transform.SetPositionAndRotation(new Vector3(center.x, center.y, -30), Quaternion.identity); camera.orthographic = true; camera.orthographicSize = halfHeight; camera.aspect = aspect; camera.targetTexture = rt;
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = rt }); RenderTexture.active = rt;
                var texture = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point }; texture.ReadPixels(new UnityEngine.Rect(0, 0, rt.width, rt.height), 0, 0); texture.Apply(); return texture;
            }
            finally
            {
                RenderTexture.active = active; camera.targetTexture = target; camera.transform.SetPositionAndRotation(position, rotation); EditorJsonUtility.FromJsonOverwrite(serialized, camera); camera.aspect = oldAspect; rt.Release(); Object.DestroyImmediate(rt);
                Need(EditorJsonUtility.ToJson(camera) == serialized && camera.gameObject.scene.isDirty == dirty, "Review camera restoration");
            }
        }
        public static Camera MapCamera(Scene scene) { return scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Camera>(true)).Single(); }
        public static void VisualFile(string name, Texture2D texture)
        {
            Need(name.EndsWith(".png") && !name.Contains("/") && !name.Contains("\\"), "Bounded PNG filename");
            Need(File.Exists(Run + "/visual/" + name) || Directory.GetFiles(Run + "/visual", "*.png").Length < 64, "Visual PNG cap64"); File.WriteAllBytes(Run + "/visual/" + name, texture.EncodeToPNG());
        }
        public static bool SameScene(Row[] before, Row[] after)
        {
            if (before.Length != after.Length) return false; var current = after.ToDictionary(r => r.id);
            foreach (var a in before)
            {
                if (!current.TryGetValue(a.id, out var b) || a.path != b.path || a.active != b.active || a.rendererEnabled != b.rendererEnabled || a.colliderEnabled != b.colliderEnabled || a.sprite != b.sprite || a.material != b.material || a.layer != b.layer || !a.references.SequenceEqual(b.references) || !a.box.SequenceEqual(b.box)) return false;
                for (int i = 0; i < 16; i++) if (Mathf.Abs(a.matrix[i] - b.matrix[i]) > .0001f) return false;
            }
            return true;
        }
        [Serializable] public class PreviewQA
        {
            public List<PreviewResult> previews = new List<PreviewResult>(); public string play = "NOT_TESTED", network = "NOT_TESTED", runtime = "NOT_TESTED";
        }
        [Serializable] public class PreviewResult
        {
            public string kind, scene, sceneSha, path, id, clip, status, evidence, label, stopCause, recordedAt; public string[] sampledClipPaths;
            public float[] times; public bool restored, dirtyBefore, dirtyAfter, colliderUnchanged, sourceFilesUnchanged;
        }
        public static void PreviewRecord(PreviewResult result)
        {
            var q = File.Exists(Run + "/ANIMATION_QA.json") ? Read<PreviewQA>("ANIMATION_QA.json") : new PreviewQA();
            q.previews.Add(result); Write("ANIMATION_QA.json", q);
        }
        public static void NativeFinalQA()
        {
            Guard(); var identity = Read<Identity>("SCENE_IDENTITY_MAP.json"); var qa = Read<Elevators>("ELEVATOR_REUSE_QA.json");
            for (int i = 0; i < 4; i++)
            {
                var s = EditorSceneManager.OpenScene(ScenePath(i), OpenSceneMode.Single); var root = Root(s); var rows = Snapshot(s); var dict = rows.ToDictionary(r => r.id); var old = identity.scenes[i];
                Need(old.before.All(r => dict.ContainsKey(r.id)), "Every original ID retained");
                foreach (var dev in old.before.Where(r => r.path.Contains("90_DEV"))) Need(JsonUtility.ToJson(dev) == JsonUtility.ToJson(dict[dev.id]), "Developer marker branch unchanged");
                foreach (var slot in qa.slots.Where(x => x.scene == s.path))
                {
                    var go = Find(root, slot.rootPath.Substring(root.name.Length + 1)).gameObject; var floor = NestedFloor(go); var mb = ElevatorComponent(go); var so = new SerializedObject(mb); var column = so.FindProperty("column").objectReferenceValue as SpriteRenderer; var bounds = BoxWorld(floor.GetComponentsInChildren<BoxCollider2D>(true).Single());
                    Need(so.FindProperty("floor").objectReferenceValue == floor && column != null && NetworkObjects(go).Length == 2, "Saved original native refs");
                    Need(Mathf.Abs(so.FindProperty("originPos").floatValue - floor.transform.position.y) < .0001f && Mathf.Abs(so.FindProperty("dest").floatValue - floor.transform.position.y) < .0001f, "Parked initial state");
                    Need(Mathf.Abs(bounds.max.y - slot.target[1]) < .0001f && Mathf.Abs(bounds.size.x - slot.target[2]) < .0001f, "Saved floor top/width");
                    var no = new SerializedObject(NetworkObjects(go).Single(n => n.gameObject == go)); Need(no.FindProperty("NestedObjects").GetArrayElementAtIndex(0).objectReferenceValue == NetworkObjects(floor)[0] && no.FindProperty("NetworkedBehaviours").GetArrayElementAtIndex(0).objectReferenceValue == mb, "Saved original NetworkObject arrays");
                    slot.instanceId = Id(go); slot.floorId = Id(floor); slot.columnId = Id(column); slot.doorId = Id(floor.transform.Find("DoorRoot").gameObject); slot.status = "SAVED_REOPENED_NATIVE_REFERENCES_AND_INITIAL_POSE_PASS";
                }
                old.after = rows; old.sha256 = Hash(s.path); old.reopened = true; Need(!s.isDirty, "Final QA does not dirty scene");
            }
            Write("SCENE_IDENTITY_MAP.json", identity); Write("ELEVATOR_REUSE_QA.json", qa); Guard(); Checkpoint("I06_NATIVE_FINAL_4SCENES_10SLOTS"); Log("4 saved scenes / 10 original instances / protected DEV native PASS");
        }
        static void Band(SpriteRenderer old, float x, float top, float width, float height)
        {
            old.enabled = false; var plate = Sprite("220545a652a206641beb4ad37faab603", "Plate");
            if (height > 1.2f) Rect(old.transform, "N02_BODY_FILL", plate, x, top - height / 2, width, height, -2);
            float scale = Mathf.Min(1, height / Tile.bounds.size.y); var cap = Rect(old.transform, "N02_SINGLE_CAP_NO_VERTICAL_REPEAT", Tile, x, top - Tile.bounds.size.y * scale / 2 + scale / 32, width / scale, Tile.bounds.size.y, -1);
            cap.transform.localScale *= scale;
        }
        public static void PolishObserved(int index)
        {
            Guard(); var s = EditorSceneManager.OpenScene(ScenePath(index), OpenSceneMode.Single); var root = Root(s);
            if (index == 1)
            {
                foreach (var sr in root.GetComponentsInChildren<SpriteRenderer>(true).Where(r => r.enabled && r.sprite == Tile && (r.name == "N02_PLATFORM_ART" || r.name == "Platform_ART" || r.name == "Visual" && r.transform.parent.name.StartsWith("Source_UpperSmall_") || r.name == "N02_SPECIAL_LIFT_PLATE_VISUAL_ONLY")).ToArray())
                    Band(sr, sr.bounds.center.x, sr.bounds.max.y, sr.bounds.size.x, sr.bounds.size.y);
                foreach (long id in new long[] { 225464774, 1114779810 })
                { var t = FindId(root, id); OffVisual(t); Rect(t, "N02_BUTTON_FRAME", Sprite("220545a652a206641beb4ad37faab603", "Plate"), t.position.x, 1.875f, 1.3125f, 1.3125f, 1); Rect(t, "N02_RED_BUTTON_FACE", Sprite("220545a652a206641beb4ad37faab603", "Plate"), t.position.x, 1.875f, 1.03f, 1.03f, 2, new Color(.78f, .17f, .18f)); }
                foreach (var sr in root.GetComponentsInChildren<SpriteRenderer>(true).Where(r => r.name == "N02_STATIC_PAD_ART_SINKING_NOT_IMPLEMENTED" || r.name == "Visual" && r.transform.parent.name == "MetalBox_LeftUpper_STATIC_N02")) sr.sprite = Sprite("220545a652a206641beb4ad37faab603", "Plate");
            }
            if (index == 0)
            {
                foreach (var pair in new[] { new long[] { 1613133711, 1814827121 }, new long[] { 430089353, 280429176 }, new long[] { 13139398, 1401020984 } })
                { var sr = FindId(root, pair[0]).Find("N02_ART_ALIGNED_TO_UNCHANGED_BOX").GetComponent<SpriteRenderer>(); var box = BoxWorld(FindBox(root, pair[1])); Band(sr, box.center.x, box.max.y, box.size.x, box.size.y); }
            }
            if (index == 2)
            { var sr = FindId(root, 791422689).Find("N02_PLATFORM_ART").GetComponent<SpriteRenderer>(); Band(sr, 17, 4.5f, 18, .4f); }
            Save(s, "I06_OBSERVED_SINGLE_CAP_POLISH_" + index);
        }
        public static void PolishSourceContacts()
        {
            Guard(); var s = EditorSceneManager.OpenScene(ScenePath(1), OpenSceneMode.Single); var root = Root(s);
            var crate = Sprite("9cac4b34d8bf0f742bfbbf52ab59ea89", "Tiles_3");
            foreach (var sr in root.GetComponentsInChildren<SpriteRenderer>(true).Where(r => r.name == "N02_STATIC_PAD_ART_SINKING_NOT_IMPLEMENTED" || r.name == "Visual" && r.transform.parent.name == "MetalBox_LeftUpper_STATIC_N02"))
            {
                sr.sprite = crate; sr.drawMode = SpriteDrawMode.Simple; float k = 1.4375f / (74f / 32); var ps = sr.transform.parent.lossyScale;
                sr.transform.localScale = new Vector3(k / ps.x, k / ps.y, 1 / ps.z);
            }
            long[] ids = { 1440255727, 1931613436 }; float[] tops = { 5.390625f, 5.640625f }; float[] xs = { .75f, 19.1875f };
            for (int i = 0; i < 2; i++)
            {
                var t = FindId(root, ids[i]); float k = (86f / 64) / (29f / 16); var ps = t.parent.lossyScale;
                t.localScale = new Vector3(k / ps.x, k / ps.y, 1 / ps.z); t.position = new Vector3(xs[i] + k / 32, tops[i] + k, 0);
                // objects_7's 32px rect has an opaque 31x29 region, with its bottom at the rect bottom.
                Need(Mathf.Abs(t.GetComponent<SpriteRenderer>().bounds.min.y - tops[i]) < .0001f, "Console opaque bottom meets existing platform");
            }
            var column = Find(root, "N02_SPECIAL_LIFT_COLUMN_VISUAL_ONLY").GetComponent<SpriteRenderer>();
            column.transform.position += new Vector3(26.390625f, -1.8984375f, 0) - column.bounds.center;
            Find(root, "Source_RedHorizontalLine_MEANING_OPEN_N02").GetComponent<SpriteRenderer>().color = new Color(5.6f, .15f, .15f);
            var battery = Find(root, "Battery_TARGET_PROPOSED_MEANING_OPEN_N02").GetComponent<SpriteRenderer>();
            battery.transform.position += Vector3.up * (9.9375f - battery.bounds.min.y);
            if (!root.GetComponentsInChildren<Transform>(true).Any(t => t.name == "Source_GrayTarget_REFERENCE_OPEN_N02"))
                New(FindId(root, 123506200), "Source_GrayTarget_REFERENCE_OPEN_N02", new Vector3(29.05f, 12.859375f, 0));
            if (!root.GetComponentsInChildren<Transform>(true).Any(t => t.name == "N02_EXIT_BAY_VISUAL_COUPLING"))
            {
                var slot = Read<Elevators>("ELEVATOR_REUSE_QA.json").slots.Single(x => x.name == "YESOD_EXIT_PROPOSED");
                var floor = Find(root, slot.floorPath.Substring(root.name.Length + 1));
                Rect(floor, "N02_EXIT_BAY_VISUAL_COUPLING", Metal, 30.484375f, 9.8675f, .7f, .14f, 0);
            }
            Save(s, "I06_SOURCE_CONSOLE_CONTACT_AND_ORIGINAL_CRATE_ART");
        }
        public static void PolishHodContacts()
        {
            Guard(); var s = EditorSceneManager.OpenScene(ScenePath(2), OpenSceneMode.Single); var root = Root(s);
            string[] names = { "N02_MID_BRIDGE_ART", "N02_LANE_BRIDGE_ART" }; float[] widths = { 2.5f, 1.5f };
            for (int i = 0; i < 2; i++)
            { var cap = Find(root, names[i]).Find("N02_SINGLE_CAP_NO_VERTICAL_REPEAT").GetComponent<SpriteRenderer>(); cap.size = new Vector2(widths[i] / cap.transform.lossyScale.x, cap.size.y); }
            var stone = Find(root, "ThrowProp_STATIC_NO_THROWER_N02").GetComponent<SpriteRenderer>();
            stone.transform.position += Vector3.up * (-stone.bounds.min.y - stone.bounds.size.y * 3 / 16);
            var coupling = root.GetComponentsInChildren<SpriteRenderer>(true).SingleOrDefault(t => t.name == "N02_ENTRY_LANE_VISUAL_COUPLING");
            if (coupling == null) Rect(Find(root, "00_MAP_EDIT/TERRAIN"), "N02_ENTRY_LANE_VISUAL_COUPLING", Metal, 0, -.07f, 2.4f, .14f, 0);
            else coupling.size = new Vector2(2.4f, .14f);
            Save(s, "I06_HOD_OPAQUE_CONTACTS_VISUAL_ONLY");
        }
        public static void PolishHodVisibility()
        {
            Guard(); var s = EditorSceneManager.OpenScene(ScenePath(2), OpenSceneMode.Single); var root = Root(s);
            var robot = Find(root, "RobotThreat_N02_STATIC_NO_AI"); robot.position = new Vector3(24.25f, robot.position.y, robot.position.z);
            var drum = Find(root, "Drum_R3_REUSED_STATIC_N02"); drum.position = new Vector3(23, drum.position.y, drum.position.z); PrefabUtility.RecordPrefabInstancePropertyModifications(drum);
            foreach (string name in new[] { "N02_MID_BRIDGE_ART", "N02_LANE_BRIDGE_ART" }) { var sr = Find(root, name).GetComponent<SpriteRenderer>(); var b = sr.bounds; Band(sr, b.center.x, 0, b.size.x, .4f); }
            OffVisual(Find(root, "00_MAP_EDIT/BACKGROUND/R2_DECORATION/ZoneBoundaryRail")); Save(s, "I06_HOD_SAMPLE_VISIBILITY_AND_BRIDGE_CAP");
        }
    }
    public class N02ReviewWindow : EditorWindow
    {
        readonly List<Texture2D> images = new List<Texture2D>(); readonly List<string> labels = new List<string>(); Vector2 scroll; string sceneInfo;
        public static void Open(int index)
        {
            PCS_MapRemakeNov02.Guard(); var s = EditorSceneManager.OpenScene(PCS_MapRemakeNov02.ScenePath(index), OpenSceneMode.Single); var w = GetWindow<N02ReviewWindow>(true, "NOV02 Actual Unity Render"); w.Clean();
            var rect = EditorGUIUtility.GetMainWindowPosition(); w.position = new UnityEngine.Rect(rect.x + 15, rect.y + 60, Mathf.Min(1450, rect.width - 30), Mathf.Min(920, rect.height - 100));
            w.sceneInfo = s.path + "\nSHA256 " + PCS_MapRemakeNov02.Hash(s.path) + " | Unity " + Application.unityVersion + " | Edit / not Play | " + DateTime.Now.ToString("yyyy-MM-dd");
            if (index == 0)
            {
                w.Add(s, "Malkuth_overview", new Vector2(64, 15), 25, 2.6f);
                for (int k = 0; k < 4; k++) w.Add(s, "Malkuth_room_" + new[] { "Rabbit", "Mouse", "Frog", "Bear" }[k], new Vector2(13 + 28 * k, 18), 7.5f);
                w.Add(s, "Malkuth_gather", new Vector2(105, 3), 9);
            }
            if (index == 1) { w.Add(s, "SourceA_F2_comparison", new Vector2(15, 4.84375f), 8.4375f); w.Add(s, "SourceA_separated_pads", new Vector2(9, 0), 3.6f); w.Add(s, "SourceA_exit_Bay", new Vector2(30, 10.5f), 6); }
            if (index == 2)
            {
                w.Add(s, "Hod_overview", new Vector2(7, 3), 12, 2); w.Add(s, "Hod_generator_walkway", new Vector2(14, 5.5f), 4.5f); w.Add(s, "Hod_crusher_cover_hook", new Vector2(17, 2.7f), 4.8f); w.Add(s, "Hod_rat_bear", new Vector2(5, 1.5f), 3.8f); w.Add(s, "Hod_threat_samples", new Vector2(25.8f, 1.5f), 3.2f); w.Add(s, "Hod_docks_bridges", new Vector2(-7, 1.5f), 5.5f);
            }
            if (index == 3) { w.Add(s, "Netzach_preserved_puzzle", new Vector2(9, 4), 9, 2); w.Add(s, "Netzach_entry", new Vector2(-8, 1.5f), 5); }
            w.Show(); w.Repaint();
        }
        void Add(Scene scene, string label, Vector2 center, float height, float aspect = 16f / 9)
        { var texture = PCS_MapRemakeNov02.Render(PCS_MapRemakeNov02.MapCamera(scene), center, height, aspect); images.Add(texture); labels.Add(label); PCS_MapRemakeNov02.VisualFile(label + ".png", texture); }
        void Clean() { foreach (var texture in images) if (texture != null) DestroyImmediate(texture); images.Clear(); labels.Clear(); }
        void OnDisable() { Clean(); }
        void OnGUI()
        {
            EditorGUILayout.HelpBox("ACTUAL UNITY CAMERA RENDER / EDIT MODE. Saved camera restored after each view. Runtime movement and networking NOT_TESTED.", MessageType.Info); EditorGUILayout.LabelField(sceneInfo, EditorStyles.wordWrappedLabel, GUILayout.Height(44));
            scroll = EditorGUILayout.BeginScrollView(scroll);
            for (int i = 0; i < images.Count; i++) { EditorGUILayout.LabelField(labels[i], EditorStyles.boldLabel); var r = GUILayoutUtility.GetRect(position.width - 35, (position.width - 35) * images[i].height / images[i].width); GUI.DrawTexture(r, images[i], ScaleMode.ScaleToFit); }
            EditorGUILayout.EndScrollView();
        }
    }
    public class N02PreviewWindow : EditorWindow
    {
        int slotIndex, kind, doorClip; float time; Transform target; string scenePath, hash, targetPath, targetId; PCS_MapRemakeNov02.Row[] before; bool dirty, ownsAnimation, showSlotDetails; int proxyWarmup; Texture2D picture;
        PreviewRenderUtility utility; GameObject proxy; Transform proxyFloor; SpriteRenderer proxyColumn; Vector3 floorStart; Vector2 columnStart; readonly List<float> samples = new List<float>(); readonly HashSet<string> sampledClips = new HashSet<string>();
        const string ElevatorLabel = "SLOT_MATCHED_RENDER_PROXY / EDITOR_PREVIEW_ONLY / NOT_RUNTIME_TEST";
        [MenuItem("Tools/NOV02 Remake/Open Preview Window")]
        public static void Open() { var w = GetWindow<N02PreviewWindow>(true, "NOV02 Editor Preview"); var r = EditorGUIUtility.GetMainWindowPosition(); w.position = new UnityEngine.Rect(r.x + 20, r.y + 50, Mathf.Min(1120, r.width - 40), Mathf.Min(900, r.height - 100)); w.Show(); }
        void OnEnable() { Selection.selectionChanged += SelectionChanged; AssemblyReloadEvents.beforeAssemblyReload += ReloadStop; }
        void OnInspectorUpdate() { if (proxyWarmup > 0 && target != null && kind == 0) { proxyWarmup--; EditorApplication.QueuePlayerLoopUpdate(); Render(); } }
        void OnDisable() { Selection.selectionChanged -= SelectionChanged; AssemblyReloadEvents.beforeAssemblyReload -= ReloadStop; StopReason("WINDOW_CLOSED"); }
        void SelectionChanged() { if (target != null) StopReason("SELECTION_CHANGED"); }
        // Domain reload restores this window-owned preview before Unity unloads the assembly.
        void ReloadStop() { StopReason("ASSEMBLY_RELOAD"); }
        void Begin(int mode)
        {
            // Preview is read-only and accepts the owner's latest saved Scene. Builder writes still require Guard().
            Stop(); PCS_MapRemakeNov02.StableEdit(); kind = mode; doorClip = 0; samples.Clear(); sampledClips.Clear(); time = 0;
            if (mode <= 1)
            {
                var slot = PCS_MapRemakeNov02.Read<PCS_MapRemakeNov02.Elevators>("ELEVATOR_REUSE_QA.json").slots[slotIndex]; PCS_MapRemakeNov02.Need(slot.status.StartsWith("SAVED"), "A_INSTANCE_BLOCKED");
                var s = EditorSceneManager.OpenScene(slot.scene, OpenSceneMode.Single); target = PCS_MapRemakeNov02.Find(PCS_MapRemakeNov02.Root(s), (mode == 0 ? slot.rootPath : slot.doorPath).Substring(PCS_MapRemakeNov02.Root(s).name.Length + 1));
            }
            else
            {
                var s = EditorSceneManager.OpenScene(PCS_MapRemakeNov02.ScenePath(mode == 2 ? 2 : 1), OpenSceneMode.Single);
                target = PCS_MapRemakeNov02.Root(s).GetComponentsInChildren<Transform>(true).Single(t => t.name == (mode == 2 ? "PCS_HodCrusher_N02_EDITOR_PREVIEW_ONLY_NO_DAMAGE" : "Source_Shuttle_VISUAL_ONLY_NOT_A_MOVING_PLATFORM"));
            }
            scenePath = target.gameObject.scene.path; hash = PCS_MapRemakeNov02.Hash(scenePath); targetPath = PCS_MapRemakeNov02.Full(target); targetId = PCS_MapRemakeNov02.Id(target.gameObject); dirty = target.gameObject.scene.isDirty; before = PCS_MapRemakeNov02.Snapshot(target.gameObject.scene);
            if (mode == 0)
            {
                utility = new PreviewRenderUtility(); proxy = Clone(target, null); utility.AddSingleGO(proxy); proxyFloor = proxy.transform.Find("Floor"); proxyColumn = proxy.transform.Find("Column").GetComponent<SpriteRenderer>(); floorStart = proxyFloor.localPosition; columnStart = proxyColumn.size;
                // A local preview light avoids URP's cross-scene Global Light registry collision.
                var lamp = new GameObject("PreviewLocalLight_ONLY"); lamp.transform.position = target.position + Vector3.up;
                var light = lamp.AddComponent<Light2D>(); light.lightType = Light2D.LightType.Point; light.pointLightInnerRadius = 100; light.pointLightOuterRadius = 101; light.pointLightInnerAngle = 360; light.pointLightOuterAngle = 360; light.intensity = 1; utility.AddSingleGO(lamp);
                var camera = utility.camera; camera.CopyFrom(PCS_MapRemakeNov02.MapCamera(target.gameObject.scene)); camera.scene = proxy.scene; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.08f, .12f, .16f); camera.orthographic = true;
                PCS_MapRemakeNov02.Need(proxy.GetComponentsInChildren<Component>(true).All(c => c is Transform || c is SpriteRenderer), "Proxy contains only Transform/Renderer; no NO/MB/Collider");
                VerifyProxy(target, proxy.transform); PCS_MapRemakeNov02.Write("_work/slot_preview_initial.json", new ProxyMap { sourceScene = scenePath, sceneSha = hash, sourceRoot = targetPath, sourceId = targetId, sourceGuid = AssetDatabase.AssetPathToGUID(PCS_MapRemakeNov02.OriginalElevator), source = before.Where(r => r.path == targetPath || r.path.StartsWith(targetPath + "/")).ToArray(), proxy = proxy.GetComponentsInChildren<Transform>(true).Select(PCS_MapRemakeNov02.Record).ToArray(), status = ElevatorLabel });
                proxyWarmup = 6; EditorApplication.QueuePlayerLoopUpdate();
            }
            else { AnimationMode.StartAnimationMode(); ownsAnimation = true; }
            Sample(0);
        }
        [Serializable] class ProxyMap { public string sourceScene, sceneSha, sourceRoot, sourceId, sourceGuid, status; public PCS_MapRemakeNov02.Row[] source, proxy; }
        GameObject Clone(Transform source, Transform parent)
        {
            var go = new GameObject(source.name) { hideFlags = HideFlags.HideAndDontSave }; var t = go.transform; t.SetParent(parent, false);
            if (parent == null) { t.SetPositionAndRotation(source.position, source.rotation); t.localScale = source.lossyScale; } else { t.localPosition = source.localPosition; t.localRotation = source.localRotation; t.localScale = source.localScale; }
            var original = source.GetComponent<SpriteRenderer>(); if (original != null)
            { var sr = go.AddComponent<SpriteRenderer>(); sr.sprite = original.sprite; sr.sharedMaterial = original.sharedMaterial; sr.color = original.color; sr.sortingLayerID = original.sortingLayerID; sr.sortingOrder = original.sortingOrder; sr.drawMode = original.drawMode; sr.size = original.size; sr.flipX = original.flipX; sr.flipY = original.flipY; sr.enabled = original.enabled; }
            foreach (Transform child in source) Clone(child, t); go.SetActive(source.gameObject.activeSelf); return go;
        }
        void VerifyProxy(Transform original, Transform copy)
        {
            PCS_MapRemakeNov02.Need((original.position - copy.position).sqrMagnitude < .00000001f && original.childCount == copy.childCount, "Slot-matched proxy hierarchy/world placement");
            for (int i = 0; i < 16; i++) PCS_MapRemakeNov02.Need(Mathf.Abs(original.localToWorldMatrix[i] - copy.localToWorldMatrix[i]) <= .0001f, "Slot-matched proxy world matrix");
            PCS_MapRemakeNov02.Need(original.gameObject.activeSelf == copy.gameObject.activeSelf, "Slot-matched proxy active state");
            var a = original.GetComponent<SpriteRenderer>(); var b = copy.GetComponent<SpriteRenderer>(); if (a != null) PCS_MapRemakeNov02.Need(b.sprite == a.sprite && b.sharedMaterial == a.sharedMaterial && b.size == a.size && b.color == a.color && b.sortingOrder == a.sortingOrder && b.sortingLayerID == a.sortingLayerID && b.drawMode == a.drawMode && b.flipX == a.flipX && b.flipY == a.flipY && b.enabled == a.enabled, "Slot-matched proxy renderer refs/size/tint/sort/flip/visibility");
            for (int i = 0; i < original.childCount; i++) VerifyProxy(original.GetChild(i), copy.GetChild(i));
        }
        AnimationClip CurrentClip()
        {
            string path = kind == 1 ? PCS_MapRemakeNov02.Base + "Animation/R4/" + new[] { "Door_Open_R4.anim", "Door_Close_R4.anim", "Door_ClosedIdle_R4.anim" }[doorClip] : PCS_MapRemakeNov02.Anim + (kind == 2 ? "Hod_CrusherCycle_N02.anim" : "Yesod_ShuttlePreview_N02.anim");
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path); PCS_MapRemakeNov02.Need(clip != null, "Exact preview clip " + path); return clip;
        }
        float Duration { get { return kind == 0 ? 2 : kind == 1 ? .6f : kind == 2 ? 1.6f : 6; } }
        void Sample(float value)
        {
            if (target == null) return; time = value; samples.Add(value);
            try
            {
                if (float.IsNaN(value)) throw new ArgumentException("N02_EXPECTED_SAMPLING_FAULT");
                if (kind == 0)
                { float offset = 3 * Mathf.Sin(Mathf.PI * value / 2); proxyFloor.localPosition = floorStart + Vector3.up * offset; proxyColumn.size = columnStart + Vector2.up * offset; PCS_MapRemakeNov02.Need(PCS_MapRemakeNov02.SameScene(before, PCS_MapRemakeNov02.Snapshot(target.gameObject.scene)), "Actual Floor/Collider/NO unchanged during proxy"); }
                else
                { AnimationMode.BeginSampling(); try { var clip = CurrentClip(); sampledClips.Add(AssetDatabase.GetAssetPath(clip)); AnimationMode.SampleAnimationClip(target.gameObject, clip, value); } finally { AnimationMode.EndSampling(); } }
                if (kind == 3)
                { var collider = target.Find("STATIC_NEUTRAL_COLLIDER").GetComponent<BoxCollider2D>(); var row = before.Single(x => x.id == PCS_MapRemakeNov02.Id(collider.gameObject)); PCS_MapRemakeNov02.Need((collider.transform.position - new Vector3(row.position[0], row.position[1], row.position[2])).sqrMagnitude < 1e-8f, "Shuttle neutral collider never moves"); }
                Render();
            }
            catch { StopReason("SAMPLING_EXCEPTION"); throw; }
        }
        void Render()
        {
            if (picture != null) DestroyImmediate(picture);
            var center = kind == 0 ? (Vector2)target.position + new Vector2(0, 3.2f) : kind == 1 ? (Vector2)target.position + new Vector2(0, 1.5f) : kind == 2 ? (Vector2)target.position + new Vector2(0, -1.3f) : (Vector2)target.position + new Vector2(-3, 0);
            picture = PCS_MapRemakeNov02.Render(kind == 0 ? utility.camera : PCS_MapRemakeNov02.MapCamera(target.gameObject.scene), center, kind == 0 ? 6.5f : kind == 1 ? 2.5f : kind == 2 ? 4 : 6);
            Repaint();
        }
        void Stop() { StopReason("STOP_OR_MODE_SWITCH"); }
        void StopReason(string cause)
        {
            if (target == null && !ownsAnimation && utility == null) return;
            var scene = target == null ? default : target.gameObject.scene; string clip = kind == 0 ? "RENDER_PROXY_NO_RUNTIME_CALL" : AssetDatabase.GetAssetPath(CurrentClip());
            try { if (ownsAnimation) AnimationMode.StopAnimationMode(); }
            finally { ownsAnimation = false; proxyWarmup = 0; if (utility != null) { utility.Cleanup(); utility = null; proxy = null; } }
            if (target != null)
            {
                bool same = PCS_MapRemakeNov02.SameScene(before, PCS_MapRemakeNov02.Snapshot(scene)); bool sameDirty = scene.isDirty == dirty; bool sameHash = PCS_MapRemakeNov02.Hash(scenePath) == hash;
                PCS_MapRemakeNov02.PreviewRecord(new PCS_MapRemakeNov02.PreviewResult { kind = new[] { "ELEVATOR_PROXY", "DOOR", "CRUSHER_NO_DAMAGE", "SHUTTLE_VISUAL_ONLY" }[kind], scene = scenePath, sceneSha = hash, path = targetPath, id = targetId, clip = clip, status = same && sameDirty && sameHash ? "PREVIEW_RESTORED_NATIVE_PASS" : "PREVIEW_RESTORE_FAIL", restored = same, dirtyBefore = dirty, dirtyAfter = scene.isDirty, colliderUnchanged = same, sourceFilesUnchanged = sameHash, times = samples.Where(v => !float.IsNaN(v)).Distinct().ToArray(), stopCause = cause, sampledClipPaths = sampledClips.ToArray(), recordedAt = DateTimeOffset.Now.ToString("o"), label = kind == 0 ? ElevatorLabel : kind == 2 ? "NO_DAMAGE / EDITOR_PREVIEW_ONLY" : kind == 3 ? "VISUAL_ONLY / STATIC_NEUTRAL_COLLIDER / NOT_A_MOVING_PLATFORM" : "EDITOR_PREVIEW_ONLY / NO_RUNTIME_TRIGGER" });
                target = null; PCS_MapRemakeNov02.Need(same && sameDirty && sameHash, "Preview failed to restore; do not save"); PCS_MapRemakeNov02.StableEdit();
            }
            if (picture != null) { DestroyImmediate(picture); picture = null; } Repaint();
        }
        void OnGUI()
        {
            EditorGUILayout.HelpBox("Editor Preview only. 실제 승강/운반/압사/네트워크/퍼즐 판정은 구현·검증하지 않았습니다.", MessageType.Info);
            var slots = PCS_MapRemakeNov02.Read<PCS_MapRemakeNov02.Elevators>("ELEVATOR_REUSE_QA.json").slots; slotIndex = EditorGUILayout.Popup("실제 저장된 A 슬롯", Mathf.Clamp(slotIndex, 0, slots.Count - 1), slots.Select(s => s.name + " | " + Path.GetFileNameWithoutExtension(s.scene)).ToArray());
            EditorGUILayout.BeginHorizontal(); if (GUILayout.Button("1 Elevator visual proxy", GUILayout.Height(30))) Begin(0); if (GUILayout.Button("2 Door Open / Close", GUILayout.Height(30))) Begin(1); if (GUILayout.Button("3 Hod Crusher", GUILayout.Height(30))) Begin(2); if (GUILayout.Button("4 SourceA Shuttle", GUILayout.Height(30))) Begin(3); EditorGUILayout.EndHorizontal();
            if (target == null) { EditorGUILayout.LabelField("대상을 선택하면 초기 자세를 읽습니다. Stop 후 원본·미저장 상태를 검사합니다."); return; }
            EditorGUILayout.LabelField(kind == 0 ? ElevatorLabel : kind == 2 ? "NO_DAMAGE / EDITOR_PREVIEW_ONLY" : kind == 3 ? "VISUAL_ONLY / STATIC_NEUTRAL_COLLIDER / NOT_A_MOVING_PLATFORM" : "DOOR / EDITOR_PREVIEW_ONLY", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(targetPath, EditorStyles.wordWrappedLabel); EditorGUILayout.LabelField("Scene SHA256 " + hash, EditorStyles.miniLabel);
            if (kind <= 1)
            {
                showSlotDetails = EditorGUILayout.Foldout(showSlotDetails, "Native slot details: GUID / object IDs / saved dimensions", true);
                if (showSlotDetails)
                {
                    var slot = PCS_MapRemakeNov02.Read<PCS_MapRemakeNov02.Elevators>("ELEVATOR_REUSE_QA.json").slots[slotIndex];
                    var floor = before.Single(r => r.path == slot.floorPath); var column = before.Single(r => r.path == slot.columnPath);
                    var deck = before.Single(r => r.path == slot.floorPath + "/Tiles_0");
                    var left = before.Single(r => r.path == slot.doorPath + "/LeftLeaf"); var right = before.Single(r => r.path == slot.doorPath + "/RightLeaf");
                    EditorGUILayout.HelpBox("Prefab GUID " + slot.sourceGuid + "\nRoot " + slot.instanceId + "\nFloor " + floor.id + "\nColumn " + column.id + "\nDoor " + slot.doorId + "\nFloor " + floor.path + "\nColumn " + column.path + "\nDoor " + slot.doorPath + "\nSaved deck top / width: " + (deck.colliderBounds[1] + deck.colliderBounds[4] / 2).ToString("0.######") + " / " + deck.colliderBounds[3].ToString("0.######") + "; Column.size " + string.Join(",", column.size.Select(v => v.ToString("0.######"))) + "\nSaved leaf local XYZ: Left(" + string.Join(",", left.localPosition.Select(v => v.ToString("0.######"))) + ") Right(" + string.Join(",", right.localPosition.Select(v => v.ToString("0.######"))) + ")", MessageType.None);
                }
            }
            EditorGUILayout.BeginHorizontal(); if (GUILayout.Button("Initial / Closed")) { doorClip = 2; Sample(0); } if (GUILayout.Button("Upper / Open / Down / F5")) { doorClip = 0; Sample(kind == 0 ? 1 : kind == 1 ? .6f : kind == 2 ? .6f : 4); } if (GUILayout.Button("Return / Close / F7")) { doorClip = 1; Sample(Duration); } if (GUILayout.Button("STOP + RESTORE")) { Stop(); EditorGUILayout.EndHorizontal(); return; } EditorGUILayout.EndHorizontal();
            float next = EditorGUILayout.Slider("Preview time", time, 0, Duration); if (Mathf.Abs(next - time) > .00001f) Sample(next);
            if (kind == 3) { EditorGUILayout.BeginHorizontal(); for (int i = 0; i < 7; i++) { int n = i; if (GUILayout.Button("F" + (i + 1))) Sample(n); } EditorGUILayout.EndHorizontal(); }
            if (GUILayout.Button("Capture current Unity Preview PNG")) PCS_MapRemakeNov02.VisualFile("Preview_" + kind + "_" + slotIndex + "_" + time.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + ".png", picture);
            EditorGUILayout.BeginHorizontal(); if (GUILayout.Button("QA: selection cleanup")) Selection.activeObject = target.gameObject; if (GUILayout.Button("QA: sampling exception cleanup")) { try { Sample(float.NaN); } catch (ArgumentException ex) when (ex.Message == "N02_EXPECTED_SAMPLING_FAULT") { Debug.Log("N02 controlled sampling fault restored safely"); } } EditorGUILayout.EndHorizontal();
            if (picture != null) { var r = GUILayoutUtility.GetRect(position.width - 25, (position.width - 25) * picture.height / picture.width); GUI.DrawTexture(r, picture, ScaleMode.ScaleToFit); }
        }
    }
    public class N02ControlPanel : EditorWindow
    {
        [MenuItem("Tools/NOV02 Remake/Open Control Panel")]
        public static void OpenPanel()
        {
            var w = GetWindow<N02ControlPanel>(true, "NOV02 Remake"); w.minSize = new Vector2(430, 260); w.Show();
        }
        void OnGUI()
        {
            minSize = new Vector2(430, 650);
            EditorGUILayout.HelpBox("Approved NOV02 v1.1 only. Never saves/discards user dirty scenes. No Play, runtime or network test.", MessageType.Info);
            if (GUILayout.Button("I00 Native identity baseline (once)", GUILayout.Height(44))) PCS_MapRemakeNov02.CaptureBaseline();
            if (GUILayout.Button("I01 Probe original nested Elevator references", GUILayout.Height(44))) PCS_MapRemakeNov02.ProbeElevator();
            if (GUILayout.Button("I01 Import selected N02 art", GUILayout.Height(32))) PCS_MapRemakeNov02.ImportArt();
            for (int i = 0; i < 4; i++) { int n = i; if (GUILayout.Button("I01 Apply original Elevator slots: " + PCS_MapRemakeNov02.Names[i], GUILayout.Height(28))) PCS_MapRemakeNov02.ApplyElevators(n); }
            if (GUILayout.Button("I02 SourceA source 7-frame visual", GUILayout.Height(32))) PCS_MapRemakeNov02.ApplyYesod();
            if (GUILayout.Button("I03 Hod complete visual + crusher", GUILayout.Height(32))) PCS_MapRemakeNov02.ApplyHod();
            if (GUILayout.Button("I04 Malkuth 6 gates / pad / ladder / ledges", GUILayout.Height(32))) PCS_MapRemakeNov02.ApplyMalkuth();
            if (GUILayout.Button("I05 Netzach preservation review", GUILayout.Height(32))) PCS_MapRemakeNov02.VerifyNetzach();
            if (GUILayout.Button("I06 Native final 4 scenes / 10 slots", GUILayout.Height(28))) PCS_MapRemakeNov02.NativeFinalQA();
            EditorGUILayout.BeginHorizontal(); for (int i = 0; i < 4; i++) { int n = i; if (GUILayout.Button("View " + i, GUILayout.Height(28))) N02ReviewWindow.Open(n); } EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("Open EDITOR PREVIEW (no runtime)", GUILayout.Height(32))) N02PreviewWindow.Open();
            EditorGUILayout.BeginHorizontal(); foreach (int i in new[] { 0, 1, 2 }) { int n = i; if (GUILayout.Button("Single cap polish " + i, GUILayout.Height(24))) PCS_MapRemakeNov02.PolishObserved(n); } EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("Hod sample visibility / bridge cap", GUILayout.Height(24))) PCS_MapRemakeNov02.PolishHodVisibility();
            if (GUILayout.Button("Source console contact / crate art", GUILayout.Height(24))) PCS_MapRemakeNov02.PolishSourceContacts();
            if (GUILayout.Button("Hod opaque contacts (visual only)", GUILayout.Height(24))) PCS_MapRemakeNov02.PolishHodContacts();
        }
    }
}

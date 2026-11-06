using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using Newtonsoft.Json.Linq;
using Object = UnityEngine.Object;

namespace PCS.Nov04.EditorTools
{
    public static class PCS_MapVisualNov04Editor
    {
        public const string Root = "Assets/PCS/PCS MAP TRY/";
        public const string Run = "C:/FOUR-PAWS-PCS-BRIEF/NOV04_CIMPL_20261105/run-001";
        public const string CorrectionBackup = Run + "/backups/CORRECTION_20261106_A";
        public static readonly string[] SceneNames = { "PCS_MAP00_Malkuth", "PCS_MAP01_Yesod", "PCS_MAP02_Hod", "PCS_MAP03_Netzach" };

        [Serializable] public class FileRecord { public string path, sha256; public long bytes; }
        [Serializable] public class BackupRecord { public string source, backup, sha256; public long bytes; public bool readback_verified; }
        [Serializable] public class SourceBaseline { public FileRecord[] files; public BackupRecord[] backups; public string[] approvedProjectFiles; }
        [Serializable] public class CorrectionBaseline { public FileRecord[] files; public BackupRecord[] backups; public string[] editable; public string head; }
        [Serializable] public class ObjectRecord
        {
            public string id, path, type, json, source;
            public bool active;
            public float[] worldMatrix;
        }
        [Serializable] public class ElevatorRecord
        {
            public string scene, id, path, source, floor, column, status;
            public bool nestedNetwork, sameInstance, oneWay;
            public string[] networkObjects;
        }
        [Serializable] public class SceneRecord
        {
            public string path, guid, sha256, physics;
            public int objects, missingScripts, missingSprites;
            public List<ObjectRecord> records = new List<ObjectRecord>();
            public List<ElevatorRecord> elevators = new List<ElevatorRecord>();
        }
        [Serializable] public class NativeBaseline
        {
            public string originalScene, modes, frogSprite;
            public List<SceneRecord> scenes = new List<SceneRecord>();
        }
        [Serializable] public class QARecord { public string stage, status, detail, time; }
        [Serializable] public class QAFile
        {
            public List<QARecord> records = new List<QARecord>();
            public List<string> holds = new List<string>();
            public string runtime = "NOT_TESTED";
        }
        [Serializable] public class Change
        {
            public string stage, scene, id, path, before, after;
        }
        [Serializable] public class Changes { public List<Change> records = new List<Change>(); }

        public static string ScenePath(int index) { return Root + "Scenes/" + SceneNames[index] + ".unity"; }
        public static string Hash(string path)
        {
            using (var file = File.OpenRead(path))
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
        }
        public static T Read<T>(string name) { return JsonUtility.FromJson<T>(File.ReadAllText(Run + "/" + name)); }
        public static void Write(string name, object value)
        {
            File.WriteAllText(Run + "/" + name, JsonUtility.ToJson(value, true), new UTF8Encoding(false));
        }
        public static void Require(bool condition, string reason)
        {
            if (!condition) throw new InvalidOperationException("NOV04 HOLD: " + reason);
        }
        public static string ID(Object value)
        {
            if (value == null) return "NULL";
            var id = GlobalObjectId.GetGlobalObjectIdSlow(value);
            return id.targetObjectId == 0 ? "UNSAVED_INSTANCE_" + value.GetInstanceID() : id.ToString();
        }
        public static long LocalID(Object value)
        {
            return value == null ? 0 : unchecked((long)GlobalObjectId.GetGlobalObjectIdSlow(value).targetObjectId);
        }
        public static string FullPath(Transform transform)
        {
            return transform.parent == null ? transform.name : FullPath(transform.parent) + "/" + transform.name;
        }
        public static Transform[] Transforms(Scene scene)
        {
            return scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
        }
        public static T ComponentID<T>(Scene scene, long id) where T : Object
        {
            var values = Transforms(scene).SelectMany(t => t.GetComponents<Component>()).OfType<T>()
                .Where(c => LocalID(c) == id).ToArray();
            Require(values.Length == 1, "Exact component " + typeof(T).Name + " " + id + ", found " + values.Length);
            return values[0];
        }
        public static GameObject GameObjectID(Scene scene, long id)
        {
            var values = Transforms(scene).Where(t => LocalID(t.gameObject) == id).ToArray();
            Require(values.Length == 1, "Exact GameObject " + id);
            return values[0].gameObject;
        }
        public static void Stable()
        {
            Require(Path.GetFullPath(Application.dataPath + "/..").Replace('\\', '/') == "C:/FOUR-PAWS-ASCEND-NEWPCS", "Project path");
            Require(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling, "Play or compilation");
            Require(PrefabStageUtility.GetCurrentPrefabStage() == null, "User Prefab Stage");
            Require(!AnimationMode.InAnimationMode(), "User AnimationMode");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                Require(!SceneManager.GetSceneAt(i).isDirty, "Unsaved Scene " + SceneManager.GetSceneAt(i).path);
        }
        public static void ProtectedGuard()
        {
            if (File.Exists(CorrectionBackup + "/BASELINE.json"))
            {
                CorrectionGuard();
                return;
            }
            var baseline = Read<SourceBaseline>("SOURCE_BASELINE.json");
            var editableScenes = Enumerable.Range(0, 4).Select(ScenePath).ToArray();
            foreach (var file in baseline.files)
            {
                if (editableScenes.Contains(file.path)) continue;
                Require(File.Exists(file.path) && Hash(file.path) == file.sha256, "Protected source " + file.path);
            }
            foreach (var backup in baseline.backups)
                Require(backup.readback_verified && File.Exists(backup.backup) &&
                    new FileInfo(backup.backup).Length == backup.bytes && Hash(backup.backup) == backup.sha256,
                    "Backup verification " + backup.source);
        }
        public static void CorrectionGuard()
        {
            var baseline = JsonUtility.FromJson<CorrectionBaseline>(File.ReadAllText(CorrectionBackup + "/BASELINE.json"));
            Require(baseline.head == "ba72b87b36f948ddb9627923ca2c24b48b835cfd", "Current user correction baseline");
            foreach (var file in baseline.files)
                if (!baseline.editable.Contains(file.path))
                    Require(File.Exists(file.path) && Hash(file.path) == file.sha256, "Correction protected source " + file.path);
            foreach (var backup in baseline.backups)
                Require(backup.readback_verified && File.Exists(backup.backup) &&
                    new FileInfo(backup.backup).Length == backup.bytes && Hash(backup.backup) == backup.sha256,
                    "Latest correction backup " + backup.source);
        }
        [Serializable] public class GeometryRecord
        {
            public string id, path, type, sprite, source;
            public bool active, enabled;
            public int order;
            public Vector3 center, size, scale;
            public Vector4 rect;
        }
        [Serializable] public class GeometryScene
        {
            public string path;
            public List<GeometryRecord> records = new List<GeometryRecord>();
        }
        [Serializable] public class GeometryAudit { public List<GeometryScene> scenes = new List<GeometryScene>(); }
        public static GeometryScene Geometry(Scene scene)
        {
            var output = new GeometryScene { path = scene.path };
            foreach (var transform in Transforms(scene))
            foreach (var component in transform.GetComponents<Component>().Where(c => c is SpriteRenderer || c is Collider2D))
            {
                var sprite = component as SpriteRenderer;
                var collider = component as Collider2D;
                var bounds = sprite != null ? sprite.bounds : collider.bounds;
                output.records.Add(new GeometryRecord { id = ID(component), path = FullPath(transform),
                    type = component.GetType().Name, active = transform.gameObject.activeInHierarchy,
                    enabled = sprite != null ? sprite.enabled : collider.enabled,
                    sprite = sprite == null || sprite.sprite == null ? null : sprite.sprite.name + " | " + AssetDatabase.GetAssetPath(sprite.sprite),
                    source = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(transform.gameObject),
                    order = sprite == null ? 0 : sprite.sortingOrder,
                    center = bounds.center, size = bounds.size, scale = transform.lossyScale,
                    rect = new Vector4(bounds.min.x, bounds.min.y, bounds.max.x, bounds.max.y) });
            }
            return output;
        }
        public static void CorrectionCapture(Scene scene, string name, Vector2 center, float halfHeight, float aspect = 16f / 9)
        {
            bool dirty = scene.isDirty;
            string sha = Hash(scene.path), physics = Physics(scene);
            var image = Render(MapCamera(scene), center, halfHeight, aspect);
            try { File.WriteAllBytes(Run + "/work/generation_staging/" + name + ".png", image.EncodeToPNG()); }
            finally { Object.DestroyImmediate(image); }
            Require(scene.isDirty == dirty && Hash(scene.path) == sha && Physics(scene) == physics, "Correction capture restores camera");
        }
        public static void CorrectionCaptureSet(Scene scene, int slot, string suffix)
        {
            if (slot == 0)
            {
                CorrectionCapture(scene, "CORR_M0_Overview_" + suffix, new Vector2(52, 24), 25, 3);
                CorrectionCapture(scene, "CORR_M0_StartRabbit_" + suffix, new Vector2(-2, 33), 7);
                CorrectionCapture(scene, "CORR_M0_StartMouse_" + suffix, new Vector2(26, 33), 7);
                CorrectionCapture(scene, "CORR_M0_Rabbit_" + suffix, new Vector2(13, 22), 12);
                CorrectionCapture(scene, "CORR_M0_Bear_" + suffix, new Vector2(94, 18), 8);
                CorrectionCapture(scene, "CORR_M0_Assemble_" + suffix, new Vector2(58, 9), 9, 4);
            }
            if (slot == 2)
            {
                CorrectionCapture(scene, "CORR_M2_Overview_" + suffix, new Vector2(18, 3), 11, 3);
                CorrectionCapture(scene, "CORR_M2_Rabbit_" + suffix, new Vector2(17, 4), 5, 3.5f);
                CorrectionCapture(scene, "CORR_M2_Entry_" + suffix, new Vector2(-4, 1), 7);
            }
            if (slot == 3) CorrectionCapture(scene, "CORR_M3_Overview_" + suffix, new Vector2(0, 4), 13, 2.5f);
            if (slot == 4) CorrectionCapture(scene, "CORR_Intro_Overview_" + suffix, new Vector2(40, 4), 15, 4);
        }
        [MenuItem("Tools/NOV04 Visual/21 Correction geometry and before views")]
        public static void CorrectionGeometryBefore()
        {
            Stable(); CorrectionGuard();
            string output = CorrectionBackup + "/GEOMETRY_BEFORE.json";
            Require(!File.Exists(output), "Correction geometry exists; reuse saved evidence");
            string original = SceneManager.GetActiveScene().path;
            var audit = new GeometryAudit();
            try
            {
                for (int i = 0; i < 5; i++)
                {
                    var scene = i == 4 ? EditorSceneManager.OpenScene(Root + "Scenes/PCS_MAP00_StartRooms4_NOV04.unity", OpenSceneMode.Single) : Open(i);
                    audit.scenes.Add(Geometry(scene));
                    CorrectionCaptureSet(scene, i, "before");
                }
                File.WriteAllText(output, JsonUtility.ToJson(audit, true), new UTF8Encoding(false));
            }
            finally { Stable(); EditorSceneManager.OpenScene(original, OpenSceneMode.Single); }
            CorrectionGuard();
            Note("CORRECTION_A", "GEOMETRY_BEFORE_PASS", "Current Native bounds and before views; no Scene writes");
        }
        static SceneRecord CorrectionBefore(int slot)
        {
            var baseline = JsonUtility.FromJson<NativeBaseline>(File.ReadAllText(CorrectionBackup + "/NATIVE_BEFORE.json"));
            return baseline.scenes.Single(s => s.path == (slot == 4 ? Root + "Scenes/PCS_MAP00_StartRooms4_NOV04.unity" : ScenePath(slot)));
        }
        static void CorrectionSceneGate(int slot)
        {
            Stable(); CorrectionGuard();
            var before = CorrectionBefore(slot);
            Require(Hash(before.path) == before.sha256, "User Scene changed after correction snapshot: " + before.path);
        }
        static void SameRecords(SceneRecord before, Scene scene, Func<ObjectRecord, bool> predicate)
        {
            var current = Snapshot(scene).records.ToDictionary(r => r.id);
            foreach (var old in before.records.Where(predicate))
                Require(current.TryGetValue(old.id, out var value) && old.json == value.json && old.worldMatrix.SequenceEqual(value.worldMatrix),
                    "Protected Native component: " + old.path + " / " + old.type);
        }
        static bool FunctionalOrDoor(ObjectRecord record)
        {
            return record.path.Contains("/ELEVATORS/") || record.path.Contains("/START ELAVATOR/") ||
                record.path.Contains("ASCEND ELAVATOR") || record.path.Contains("DoorRoot") ||
                record.path.Contains("Gate") || record.path.Contains("AbilityBoard_SLOT");
        }
        static void RecordOverride(Object value)
        {
            if (PrefabUtility.IsPartOfPrefabInstance(value)) PrefabUtility.RecordPrefabInstancePropertyModifications(value);
        }
        static void CrispRect(SpriteRenderer renderer, Sprite sprite, float left, float bottom, float right, float top)
        {
            Undo.RecordObject(renderer, "NOV04 correction art"); Undo.RecordObject(renderer.transform, "NOV04 correction art transform");
            Require(renderer.GetComponent<Collider2D>() == null && renderer.GetComponent<MonoBehaviour>() == null,
                "CrispRect requires separate visual child: " + FullPath(renderer.transform));
            renderer.sprite = sprite;
            renderer.transform.rotation = Quaternion.identity;
            var scale = renderer.transform.lossyScale;
            renderer.transform.localScale = Vector3.Scale(renderer.transform.localScale,
                new Vector3(1 / scale.x, 1 / scale.y, 1 / scale.z));
            renderer.drawMode = SpriteDrawMode.Tiled; renderer.tileMode = SpriteTileMode.Continuous;
            SetWorldRect(renderer, left, bottom, right, top);
            RecordOverride(renderer); RecordOverride(renderer.transform);
        }
        static void RoomArt(SpriteRenderer renderer, Sprite sprite, Vector4 rect)
        {
            renderer.sprite = sprite; renderer.drawMode = SpriteDrawMode.Tiled;
            renderer.tileMode = SpriteTileMode.Continuous;
            renderer.transform.localScale = Vector3.one;
            renderer.transform.rotation = Quaternion.identity;
            renderer.size = new Vector2(rect.z - rect.x, rect.w - rect.y);
            renderer.transform.position = new Vector3((rect.x + rect.z) * .5f, (rect.y + rect.w) * .5f, 0);
        }
        [MenuItem("Tools/NOV04 Visual/22 Correct shared startroom art and grouping")]
        public static void CorrectSharedStartRooms()
        {
            Stable(); CorrectionGuard(); CorrectionSceneGate(0); CorrectionSceneGate(4);
            string path = Root + "PCS_Resources/Public/PCS_StartRooms4_NOV04.prefab";
            var baseline = JsonUtility.FromJson<CorrectionBaseline>(File.ReadAllText(CorrectionBackup + "/BASELINE.json"));
            Require(Hash(path) == baseline.files.Single(f => f.path == path).sha256, "Shared correction already applied or user Prefab changed");
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var oldIDs = source.GetComponentsInChildren<Transform>(true).ToDictionary(t => t.name, t => ID(t.gameObject));
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var wall = Sprite(Root + "PCS_Resources/Public/MetalModules_R3.png", "Wall");
                var ceiling = Sprite(Root + "PCS_Resources/Public/MetalModules_R3.png", "Ceiling");
                var rabbit = root.transform.Find("RabbitStart");
                Require(rabbit != null && rabbit.childCount == 5, "Current user Rabbit grouping");
                var offsets = rabbit.Cast<Transform>().ToDictionary(t => t.name.Substring("Rabbit_".Length), t => t.localPosition);
                var roleNames = new[] { "Rabbit", "Mouse", "Frog", "Bear" };
                for (int i = 0; i < roleNames.Length; i++)
                {
                    string role = roleNames[i];
                    var group = i == 0 ? rabbit : new GameObject(role + "Start").transform;
                    if (i > 0) group.SetParent(root.transform, false);
                    group.localPosition = new Vector3(7 + 28 * i, 12.56f, 0);
                    foreach (string piece in offsets.Keys)
                    {
                        var leaf = i == 0 ? group.Find(role + "_" + piece) : root.transform.Find(role + "_" + piece);
                        Require(leaf != null, "Current role piece " + role + "/" + piece);
                        leaf.SetParent(group, false); leaf.localPosition = offsets[piece]; leaf.localScale = Vector3.one;
                        var art = leaf.GetComponent<SpriteRenderer>(); var box = leaf.GetComponent<BoxCollider2D>();
                        Require(art != null && box != null, "Owned role visual / static box");
                        if (piece == "RoomRight") box.size = new Vector2(.5f, 4.5f * 1.1922f);
                        var center = leaf.position; var size = box.size;
                        Vector4 rect = new Vector4(center.x - size.x / 2, center.y - size.y / 2, center.x + size.x / 2, center.y + size.y / 2);
                        if (piece == "LeftWall") rect.x -= 1;
                        if (piece == "RoomCeiling") rect.w += 2;
                        if (piece == "Floor") rect.y = rect.w - .9f;
                        if (piece == "RoomRight") rect.z = 3.5f + 28 * i - 3;
                        RoomArt(art, piece == "Floor" || piece == "RoomCeiling" ? ceiling : wall, rect);
                        // The visual expands outward; the box retains its original center and shape.
                        box.offset = new Vector2(center.x - leaf.position.x, center.y - leaf.position.y);
                    }
                }
                var background = Sprite(Root + "PCS_Resources/Public/IndustrialBackground_NOV04.png");
                foreach (var art in root.GetComponentsInChildren<SpriteRenderer>(true).Where(s => s.name.StartsWith("Background_")))
                {
                    string[] parts = art.name.Split('_'); int role = int.Parse(parts[1]); int row = int.Parse(parts[2]);
                    if (row != 0) { art.enabled = false; continue; }
                    var group = root.transform.Find(roleNames[role] + "Start");
                    art.transform.SetParent(group, true);
                    art.sprite = background; art.drawMode = SpriteDrawMode.Simple;
                    art.transform.localScale = Vector3.one * .375f;
                    art.transform.position = new Vector3(-6 + role * 28, 3.5f, .1f);
                    art.color = new Color(.54f, .65f, .7f, 1); art.sortingOrder = -90; art.enabled = true;
                    art.flipX = role % 2 == 1;
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            foreach (var pair in oldIDs)
                Require(source.GetComponentsInChildren<Transform>(true).Any(t => t.name == pair.Key && ID(t.gameObject) == pair.Value), "Shared Prefab existing fileID retained: " + pair.Key);
            Note("CORR_SHARED", "PREFAB_SAVED_NATIVE_PASS", "Current Rabbit pattern; other three grouped; outward tiled art; four restrained panels,12 old panel renderers OFF; original leaf IDs and GUID preserved");
            CorrectionGuard();
        }
        [MenuItem("Tools/NOV04 Visual/27 Fix shared background sampling only")]
        public static void FixSharedBackgroundSampling()
        {
            Stable(); CorrectionGuard(); CorrectionSceneGate(0); CorrectionSceneGate(4);
            string path = Root + "PCS_Resources/Public/PCS_StartRooms4_NOV04.prefab";
            Require(Hash(path) == "23c9b8765a552a1b320b363d8eadd161cc4244a72efe574ced9e08632ddd92ca", "Expected own first correction only");
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var panels = root.GetComponentsInChildren<SpriteRenderer>(true).Where(s => s.name.StartsWith("Background_") && s.enabled).ToArray();
                Require(panels.Length == 4 && root.transform.Find("BearStart") != null, "Previously saved correction, four panels");
                foreach (var art in panels) { art.drawMode = SpriteDrawMode.Simple; art.transform.localScale = Vector3.one * .375f; }
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            Note("CORR_SHARED_SAMPLING", "SIMPLE_UNIFORM_PASS", "Four owned panels use Simple,uniform0.375; original PNG/import meta protected; SHA256=" + Hash(path));
            CorrectionGuard();
        }
        [MenuItem("Tools/NOV04 Visual/23 Correct Malkuth saved scene")]
        public static void CorrectMalkuthMenu() { Attempt("CORR_M0", CorrectMalkuth); }
        static void CorrectMalkuth()
        {
            CorrectionSceneGate(0);
            var scene = BeginStage(0, "CORR_M0"); var before = Snapshot(scene);
            var ceiling = Sprite(Root + "PCS_Resources/Public/MetalModules_R3.png", "Ceiling");
            var wall = Sprite(Root + "PCS_Resources/Public/MetalModules_R3.png", "Wall");
            var plate = Sprite(Root + "PCS_Resources/Public/MetalModules_R3.png", "Plate");
            var column = Sprite(Root + "PCS_Resources/Public/MetalModules_R3.png", "Column");
            var own = Transforms(scene).Single(t => t.name == "StartRooms4_NOV04_OPTIONAL");
            // Existing user activation is kept. Only the shared sample's own disabled boxes are normalized.
            foreach (var renderer in own.GetComponentsInChildren<SpriteRenderer>(true).Where(s => !s.name.StartsWith("Background_")))
            {
                var sourceArt = PrefabUtility.GetCorrespondingObjectFromSource(renderer);
                Require(sourceArt != null, "Existing shared role source");
                renderer.transform.localScale = Vector3.one;
                renderer.transform.localPosition = sourceArt.transform.localPosition;
                var box = renderer.GetComponent<BoxCollider2D>(); var sourceBox = sourceArt.GetComponent<BoxCollider2D>();
                bool enabled = box.enabled; box.size = sourceBox.size; box.offset = sourceBox.offset; box.enabled = enabled;
                RecordOverride(box); RecordOverride(renderer.transform);
            }
            long[] roofArt = { 1222576186, 1007925305, 823386158, 744615598 };
            long[] roofBoxes = { 17913349, 1159775673, 1836950455, 16008586 };
            var physicalIDs = PhysicsIDs(scene, roofBoxes.Select(id => (Object)ComponentID<BoxCollider2D>(scene, id))
                .Concat(own.GetComponentsInChildren<Collider2D>(true).Cast<Object>()));
            string physical = ExistingPhysics(scene, physicalIDs);
            for (int i = 0; i < 4; i++)
            {
                float left = 3.5f + i * 28, right = 27 + i * 28;
                CrispRect(ComponentID<SpriteRenderer>(scene, roofArt[i]), ceiling, left, 29.5f, right, 30);
                var box = ComponentID<BoxCollider2D>(scene, roofBoxes[i]); Undo.RecordObject(box, "NOV04 startroom drop opening");
                box.size = new Vector2(right - left, .5f);
                box.offset = box.transform.InverseTransformPoint(new Vector3((left + right) * .5f, 29.75f, box.transform.position.z));
                RecordOverride(box);
                Require(Mathf.Abs(box.bounds.min.x - left) < .002f, "Three-and-half unit static drop opening");
            }
            var support = Transforms(scene).Single(t => t.name == "Bear_Supports_NOV04");
            foreach (var art in support.GetComponentsInChildren<SpriteRenderer>(true))
            {
                var b = art.bounds;
                CrispRect(art, art.name == "Rail" ? ceiling : column, b.min.x, b.min.y, b.max.x, b.max.y);
            }
            var leftGround = ComponentID<SpriteRenderer>(scene, 434329203);
            CrispRect(leftGround, leftGround.sprite, 84, 12.65625f, 102, 14);
            var rightGround = ComponentID<SpriteRenderer>(scene, 996052306); Undo.RecordObject(rightGround, "NOV04 continuous ground visual"); rightGround.enabled = false;
            var rabbitGround = ComponentID<SpriteRenderer>(scene, 142566646);
            CrispRect(rabbitGround, rabbitGround.sprite, 0, 12.65625f, 18, 14);
            var exterior = NewRoot(scene, "Malkuth_ExteriorFinish_NOV04");
            var fill = Visual(exterior.transform, "Assemble_Underfill", plate, Vector3.zero, Vector2.one, -2, new Color(.28f, .36f, .4f, 1));
            CrispRect(fill, plate, -3.18f, -7, 111.82f, 8.056245f);
            fill = Visual(exterior.transform, "Rabbit_GroundUnderfill", wall, Vector3.zero, Vector2.one, -2, new Color(.46f, .55f, .59f, 1));
            CrispRect(fill, wall, 0, 9.65f, 18, 13);
            // The ascent elevator and bridge remain outside the underfill's right edge.
            Require(ExistingPhysics(scene, physicalIDs) == physical, "M0 all existing physics except approved four opening boxes and owned room boxes");
            SameRecords(before, scene, FunctionalOrDoor);
            SaveStage(scene, "CORR_M0", before);
            CorrectionCaptureSet(SceneManager.GetActiveScene(), 0, "after");
            Note("CORR_M0_DETAIL", "OPENINGS_AND_VISUALS_PASS", "4 roof left edges +3.0;3.5-wide shafts,box top30 unchanged; all functional prefabs/doors/boards preserved; Rabbit top14; Assemble gameplay top unchanged; roof outward thickening HOLD near adjacent startrooms");
        }
        [MenuItem("Tools/NOV04 Visual/24 Save corrected shared intro scene")]
        public static void CorrectIntroMenu() { Attempt("CORR_INTRO", CorrectIntro); }
        static void CorrectIntro()
        {
            CorrectionSceneGate(4);
            var before = CorrectionBefore(4);
            var scene = EditorSceneManager.OpenScene(before.path, OpenSceneMode.Single);
            Require(Transforms(scene).Count(t => new[] { "RabbitStart", "MouseStart", "FrogStart", "BearStart" }.Contains(t.name)) == 4, "Four role groups");
            Require(Transforms(scene).Count(t => t.name.EndsWith("_Floor")) == 4, "Four original role floor leaves");
            var camera = MapCamera(scene);
            Undo.RecordObject(camera.transform, "NOV04 corrected four-room overview");
            camera.transform.position = new Vector3(36, 3, camera.transform.position.z);
            SaveStage(scene, "CORR_INTRO", before);
            CorrectionCaptureSet(SceneManager.GetActiveScene(), 4, "after");
        }
        [MenuItem("Tools/NOV04 Visual/25 Correct Hod visual and entry barrier")]
        public static void CorrectHodMenu() { Attempt("CORR_M2", CorrectHod); }
        static void CorrectHod()
        {
            CorrectionSceneGate(2);
            var scene = BeginStage(2, "CORR_M2"); var before = Snapshot(scene);
            var physicalIDs = PhysicsIDs(scene); string physics = ExistingPhysics(scene, physicalIDs);
            var art = ComponentID<SpriteRenderer>(scene, 1721528771);
            var bounds = art.bounds;
            Require(art.sprite.name == "Tiles_0", "Current upper Rabbit platform source");
            // One source-height band; preserve the saved width and walking top.
            SetWorldRect(art, bounds.min.x, bounds.max.y - art.sprite.bounds.size.y * .29767442f, bounds.max.x, bounds.max.y);
            var wall = NewRoot(scene, "Hod_EntrySafetyWall_NOV04");
            wall.transform.position = new Vector3(-9.864584f, 0, 0);
            wall.layer = ComponentID<BoxCollider2D>(scene, 2144840289).gameObject.layer;
            var renderer = Undo.AddComponent<SpriteRenderer>(wall);
            renderer.sprite = Sprite(Root + "PCS_Resources/Public/MetalModules_R3.png", "Plate");
            renderer.sharedMaterial = art.sharedMaterial;
            renderer.drawMode = SpriteDrawMode.Tiled; renderer.tileMode = SpriteTileMode.Continuous;
            renderer.size = new Vector2(1.5f, 14); renderer.sortingOrder = 0;
            renderer.color = new Color(.55f, .65f, .69f, 1);
            var target = new Bounds(wall.transform.position, new Vector3(1.5f, 14, .1f));
            foreach (var collider in Transforms(scene).SelectMany(t => t.GetComponents<Collider2D>()).Where(c => c.enabled && c.gameObject.activeInHierarchy))
            {
                var b = collider.bounds;
                float x = Mathf.Min(b.max.x, target.max.x) - Mathf.Max(b.min.x, target.min.x);
                float y = Mathf.Min(b.max.y, target.max.y) - Mathf.Max(b.min.y, target.min.y);
                Require(x <= .005f || y <= .005f, "Hod safety barrier intersects current collider " + FullPath(collider.transform));
            }
            var box = Undo.AddComponent<BoxCollider2D>(wall); box.size = new Vector2(1.5f, 14);
            Require(ExistingPhysics(scene, physicalIDs) == physics, "Hod existing collider/rigidbody/effector invariant");
            SameRecords(before, scene, r => FunctionalOrDoor(r) || r.path.Contains("PCS_HodCrusher_N02_EDITOR_PREVIEW_ONLY_NO_DAMAGE"));
            SaveStage(scene, "CORR_M2", before);
            CorrectionCaptureSet(SceneManager.GetActiveScene(), 2, "after");
            Note("CORR_M2_DETAIL", "ONE_BAND_AND_BARRIER_PASS", "Single Rabbit strip top4.5 and old width retained; all old physics and elevator unchanged; new static wall x[-10.614584,-9.114584],y[-7,7] joins current SafeLeftApproach without overlap; actual character escape NOT_TESTED");
        }
        [MenuItem("Tools/NOV04 Visual/26 Correct Netzach options and outside finish")]
        public static void CorrectNetzachMenu() { Attempt("CORR_M3", CorrectNetzach); }
        static void CorrectNetzach()
        {
            CorrectionSceneGate(3);
            var scene = BeginStage(3, "CORR_M3"); var before = Snapshot(scene); string physics = Physics(scene);
            var plate = Sprite(Root + "PCS_Resources/Public/MetalModules_R3.png", "Plate");
            var ceiling = Sprite(Root + "PCS_Resources/Public/MetalModules_R3.png", "Ceiling");
            var wall = Sprite(Root + "PCS_Resources/Public/MetalModules_R3.png", "Wall");
            var bottom = ComponentID<SpriteRenderer>(scene, 1687242053); var b = bottom.bounds;
            CrispRect(bottom, plate, b.min.x, -8, b.max.x, b.max.y);
            bottom.color = new Color(.48f, .58f, .63f, 1);
            var top = ComponentID<SpriteRenderer>(scene, 814113968); b = top.bounds;
            CrispRect(top, plate, b.min.x, b.min.y, b.max.x, 16);
            top.color = bottom.color;
            foreach (long id in new long[] { 1550043218, 1315056298 })
            {
                var art = ComponentID<SpriteRenderer>(scene, id); b = art.bounds;
                CrispRect(art, wall, b.min.x, b.min.y, b.max.x, b.max.y);
            }
            var alternate = NewRoot(scene, "Netzach_Background_Alt_Optional");
            alternate.transform.position = new Vector3(4.5f, 4.5f, .2f);
            alternate.transform.localScale = Vector3.one * 1.5f;
            var background = Undo.AddComponent<SpriteRenderer>(alternate);
            background.sprite = Sprite(Root + "PCS_Resources/Public/IndustrialBackground_NOV04.png");
            background.sharedMaterial = bottom.sharedMaterial;
            background.sortingOrder = -90; background.color = new Color(.59f, .69f, .74f, 1);
            var glass = NewRoot(scene, "Netzach_GlassOverlay_Optional");
            glass.transform.position = new Vector3(7.94f, 4.355f, -.05f);
            var overlay = Undo.AddComponent<SpriteRenderer>(glass);
            overlay.sprite = Sprite(Root + "PCS_Resources/Public/GlassWall_NOV04.png");
            overlay.sharedMaterial = bottom.sharedMaterial; overlay.drawMode = SpriteDrawMode.Sliced;
            overlay.size = new Vector2(18.06f, 8.71f); overlay.sortingOrder = 10;
            overlay.color = new Color(.76f, .95f, 1, .55f);
            Require(Physics(scene) == physics, "Netzach full original physical state invariant");
            SameRecords(before, scene, r => r.path.StartsWith("Netzach/PUZZLE_AREAS/") || r.path.StartsWith("Netzach/ELEVATORS/") || r.path.StartsWith("Netzach/BACKGROUND/") || r.path.StartsWith("PuzzleWall_B_Glass_NOV04/"));
            SaveStage(scene, "CORR_M3", before);
            CorrectionCaptureSet(SceneManager.GetActiveScene(), 3, "after");
            Note("CORR_M3_DETAIL", "OPTIONS_AND_OUTWARD_FINISH_PASS", "Bottom grows only below0; ceiling only above8.71; neutral puzzle/gate/Elevator/all physics preserved; right wall footprint unchanged,additional thickening HOLD; two optional Sprite-only roots default ON; original background retained");
        }
        [MenuItem("Tools/NOV04 Visual/28 Continue remaining correction stages once")]
        public static void ContinueCorrections()
        {
            Stable(); CorrectionGuard();
            Require(Read<QAFile>("NATIVE_QA.json").records.Any(r => r.stage == "CORR_SHARED_SAMPLING" && r.status == "SIMPLE_UNIFORM_PASS"), "Shared source sampling checkpoint");
            Attempt("CORR_M0", CorrectMalkuth);
            if (SceneManager.GetActiveScene().isDirty) return;
            Attempt("CORR_INTRO", CorrectIntro);
            if (SceneManager.GetActiveScene().isDirty) return;
            Attempt("CORR_M2", CorrectHod);
            if (SceneManager.GetActiveScene().isDirty) return;
            Attempt("CORR_M3", CorrectNetzach);
            Stable(); CorrectionGuard();
        }
        [MenuItem("Tools/NOV04 Visual/29 Finish room background coverage and Hod retry")]
        public static void FinishCorrectionCoverage()
        {
            Stable(); CorrectionGuard();
            string path = Root + "PCS_Resources/Public/PCS_StartRooms4_NOV04.prefab";
            Require(Hash(path) == "c7bfda737a44e1444a7e978fc387c5ce3bb2f99273adeec9c705222adbc7bdcd", "Expected own uniform sampling checkpoint");
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var panels = root.GetComponentsInChildren<SpriteRenderer>(true).Where(s => s.name.StartsWith("Background_") && s.enabled).ToArray();
                Require(panels.Length == 4, "Four current room panels");
                foreach (var art in panels)
                {
                    art.transform.localScale = Vector3.one * .55f;
                    var position = art.transform.position; position.y = 5; art.transform.position = position;
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            Note("CORR_ROOM_COVERAGE", "SAVED", "Four uniform0.55 panels,centerY5 in shared source; above-entry player-sized camera coverage; no PNG/import edits; SHA256=" + Hash(path));
            Attempt("CORR_M2", CorrectHod);
            Stable(); CorrectionGuard();
        }
        static string ComparableNativeJSON(ObjectRecord record)
        {
            if (record.type != "UnityEngine.MeshFilter") return record.json;
            var json = JObject.Parse(record.json);
            var mesh = json["MeshFilter"]?["m_Mesh"] as JObject;
            // TMP generates a new hidden mesh on each scene load. Asset fileIDs are never normalized.
            if (mesh?["instanceID"] != null && (int)mesh["instanceID"] < 0) mesh["instanceID"] = 0;
            return json.ToString(Newtonsoft.Json.Formatting.None);
        }
        static void PreservedNativeRecords(SceneRecord original, SceneRecord current, Func<ObjectRecord, bool> predicate)
        {
            var lookup = current.records.ToDictionary(r => r.id);
            foreach (var old in original.records.Where(predicate))
            {
                ObjectRecord value;
                if (old.type == "UnityEngine.MeshFilter" && old.id.StartsWith("UNSAVED_INSTANCE_"))
                {
                    var matches = current.records.Where(r => r.type == old.type && r.path == old.path && r.id.StartsWith("UNSAVED_INSTANCE_")).ToArray();
                    Require(matches.Length == 1, "Exactly one TMP transient MeshFilter at " + old.path);
                    value = matches[0];
                }
                else Require(lookup.TryGetValue(old.id, out value), "Persistent Native identity " + old.id);
                Require(ComparableNativeJSON(old) == ComparableNativeJSON(value) && old.worldMatrix.SequenceEqual(value.worldMatrix),
                    "Final current-user Native preservation " + old.path + " / " + old.type);
            }
        }
        [MenuItem("Tools/NOV04 Visual/31 Finish Hod approach visual then final QA")]
        public static void FinishHodApproachAndQA()
        {
            Attempt("CORR_M2_APPROACH", () =>
            {
                Stable(); CorrectionGuard();
                Require(Hash(ScenePath(2)) == "185db8280de13fd2537edce65306af3817a4b373f7b2f81d9083198c9cb1c7be", "Expected saved own Hod correction");
                var scene = BeginStage(2, "CORR_M2_APPROACH"); var before = Snapshot(scene); string physics = Physics(scene);
                var approach = ComponentID<BoxCollider2D>(scene, 1845465644); var b = approach.bounds;
                var art = Visual(approach.transform.parent, "EntryApproach_Visual", Sprite(Root + "PCS_Resources/Public/MetalModules_R3.png", "Ceiling"),
                    new Vector3(b.center.x, b.max.y - .25f, 0), new Vector2(b.size.x, .5f), 0, Color.white);
                art.drawMode = SpriteDrawMode.Tiled;
                Require(Physics(scene) == physics && Mathf.Abs(art.bounds.max.y - b.max.y) < .001f, "Approach visual only; saved collider top");
                SaveStage(scene, "CORR_M2_APPROACH", before);
                CorrectionCaptureSet(SceneManager.GetActiveScene(), 2, "after");
            });
            if (!SceneManager.GetActiveScene().isDirty) FinalCorrectionQA();
        }
        [MenuItem("Tools/NOV04 Visual/30 Final correction native QA no scene writes")]
        public static void FinalCorrectionQA()
        {
            Stable(); CorrectionGuard();
            Require(new[] { "CORR_M0", "CORR_INTRO", "CORR_M2", "CORR_M3" }.All(stage =>
                Read<QAFile>("NATIVE_QA.json").records.Any(r => r.stage == stage && r.status == "SAVED_REOPENED_NATIVE_PASS")), "All correction save checkpoints");
            VerifySaved(); PreviewQA();
            var audit = new GeometryAudit(); var native = new NativeBaseline();
            for (int i = 0; i < 5; i++)
            {
                var scene = i == 4 ? EditorSceneManager.OpenScene(Root + "Scenes/PCS_MAP00_StartRooms4_NOV04.unity", OpenSceneMode.Single) : Open(i);
                var original = CorrectionBefore(i); var current = Snapshot(scene);
                Require(current.missingScripts == 0 && current.missingSprites == 0, "Final references");
                if (i == 1 || i == 3) Require(original.physics == current.physics, "Final full physics invariant " + i);
                if (i == 0) PreservedNativeRecords(original, current, r =>
                    (r.type.EndsWith("Collider2D") || r.type.EndsWith("Rigidbody2D") || r.type.EndsWith("PlatformEffector2D")) &&
                    !r.path.StartsWith("StartRooms4_") && !new long[] { 17913349, 1159775673, 1836950455, 16008586 }.Any(id => r.id.EndsWith("-" + id + "-0")));
                if (i == 2) PreservedNativeRecords(original, current, r => r.type.EndsWith("Collider2D") || r.type.EndsWith("Rigidbody2D") || r.type.EndsWith("PlatformEffector2D"));
                if (i < 4) PreservedNativeRecords(original, current, r => FunctionalOrDoor(r) && !r.path.StartsWith("StartRooms4_"));
                if (i == 3) PreservedNativeRecords(original, current, r => r.path.StartsWith("Netzach/PUZZLE_AREAS/") || r.path.StartsWith("Netzach/BACKGROUND/") || r.path.StartsWith("PuzzleWall_B_Glass_NOV04/"));
                native.scenes.Add(current); audit.scenes.Add(Geometry(scene));
                CorrectionCaptureSet(scene, i, "after");
                if (i == 3) CaptureNetzachOptions(scene);
                if (i == 0)
                    for (int role = 0; role < 4; role++)
                    foreach (float aspect in new[] { 16f / 9, 4f / 3, 21f / 9 })
                        CorrectionCapture(scene, "CORR_M0_Player_" + role + "_" + aspect.ToString("F3", System.Globalization.CultureInfo.InvariantCulture), new Vector2(-4 + role * 28, 35), 5, aspect);
                Note("CORRECTION_FINAL_M" + i, "SAVED_NATIVE_PROTECTION_PASS", current.sha256 + "; missing0/0; current-user baseline components protected");
            }
            File.WriteAllText(CorrectionBackup + "/NATIVE_AFTER.json", JsonUtility.ToJson(native, true), new UTF8Encoding(false));
            File.WriteAllText(CorrectionBackup + "/GEOMETRY_AFTER.json", JsonUtility.ToJson(audit, true), new UTF8Encoding(false));
            Stable(); CorrectionGuard();
            Note("CORRECTION_FINAL", "NATIVE_AND_PROXY_QA_PASS", "5 saved scenes; original physics restrictions;9 doors,4 abilities,crusher sampling/restoration; Scene/Game visual acceptance separately pending human-readable capture review; Play/Build/network NOT_TESTED");
        }
        [MenuItem("Tools/NOV04 Visual/32 Align saved own Hod approach visual")]
        public static void AlignOwnHodApproach()
        {
            Attempt("CORR_M2_APPROACH_ALIGN", () =>
            {
                Stable(); CorrectionGuard();
                Require(Hash(ScenePath(2)) == "b5749e0146d7459096623e89bfa420f3a594349f04aa8dd3432727916ce49b1a", "Expected own saved approach before world alignment");
                var scene = BeginStage(2, "CORR_M2_APPROACH_ALIGN");
                var before = Snapshot(scene); string physics = Physics(scene);
                var collider = ComponentID<BoxCollider2D>(scene, 1845465644);
                var art = ComponentID<SpriteRenderer>(scene, 1516051613);
                Require(art.name == "EntryApproach_Visual" && art.transform.parent == collider.transform.parent,
                    "Only the owned correction visual under existing approach");
                var bounds = collider.bounds;
                SetWorldRect(art, bounds.min.x, bounds.max.y - .5f, bounds.max.x, bounds.max.y);
                Require(Physics(scene) == physics && Mathf.Abs(art.bounds.min.x - bounds.min.x) < .001f &&
                    Mathf.Abs(art.bounds.max.x - bounds.max.x) < .001f && Mathf.Abs(art.bounds.max.y - bounds.max.y) < .001f,
                    "Owned approach visual matches saved collider span and top; original physics unchanged");
                SaveStage(scene, "CORR_M2_APPROACH_ALIGN", before);
                CorrectionCaptureSet(SceneManager.GetActiveScene(), 2, "after");
            });
            if (!SceneManager.GetActiveScene().isDirty) FinalCorrectionQA();
        }
        static void CaptureNetzachOptions(Scene scene)
        {
            var transforms = Transforms(scene);
            var source = transforms.Single(t => t.name == "Netzach");
            Require(source.position == Vector3.zero && source.rotation == Quaternion.identity && source.lossyScale == Vector3.one, "Netzach proxy world origin");
            var extras = scene.GetRootGameObjects().Select(g => g.transform).Where(t => t.name == "PuzzleWall_B_Glass_NOV04" ||
                t.name == "Netzach_Background_Alt_Optional" || t.name == "Netzach_GlassOverlay_Optional").ToArray();
            Require(extras.Length == 3, "Three saved optional visual roots");
            using (var proxy = new Nov04VisualProxy(source, extras))
            {
                var bg = proxy.root.transform.Find("Netzach_Background_Alt_Optional").gameObject;
                var glass = proxy.root.transform.Find("Netzach_GlassOverlay_Optional").gameObject;
                foreach (string mode in new[] { "Baseline", "AltNoGlass", "AltGlass" })
                {
                    bg.SetActive(mode != "Baseline"); glass.SetActive(mode == "AltGlass");
                    var image = Render(proxy.camera, new Vector2(7.94f, 4.355f), 5.5f, 16f / 9);
                    try { File.WriteAllBytes(Run + "/work/generation_staging/CORR_M3_" + mode + ".png", image.EncodeToPNG()); }
                    finally { Object.DestroyImmediate(image); }
                }
            }
            PreviewNote("CORRECTION_OPTIONS", "ISOLATED_TOGGLE_RESTORE_PASS", "Saved whole-chamber overlay and optional background each ON/OFF in Sprite-only preview Scene; original Scene hash/dirty/physics/full Native state unchanged");
        }
        [MenuItem("Tools/NOV04 Visual/20 Correction read-only native snapshot")]
        public static void CorrectionSnapshot()
        {
            Stable(); CorrectionGuard();
            string output = CorrectionBackup + "/NATIVE_BEFORE.json";
            Require(!File.Exists(output), "Correction Native snapshot already exists; reuse it");
            var snapshot = new NativeBaseline { originalScene = SceneManager.GetActiveScene().path,
                modes = "PLAY_OFF; PREFAB_STAGE_NONE; ANIMATION_MODE_OFF; ALL_OPEN_SCENES_CLEAN; NOT_COMPILING" };
            try
            {
                for (int i = 0; i < 4; i++) snapshot.scenes.Add(Snapshot(Open(i)));
                var intro = EditorSceneManager.OpenScene(Root + "Scenes/PCS_MAP00_StartRooms4_NOV04.unity", OpenSceneMode.Single);
                snapshot.scenes.Add(Snapshot(intro));
                File.WriteAllText(output, JsonUtility.ToJson(snapshot, true), new UTF8Encoding(false));
            }
            finally
            {
                Stable();
                if (!string.IsNullOrEmpty(snapshot.originalScene)) EditorSceneManager.OpenScene(snapshot.originalScene, OpenSceneMode.Single);
            }
            CorrectionGuard();
            Note("CORRECTION_I00", "CURRENT_NATIVE_SNAPSHOT_PASS", snapshot.modes + "; latest user HEAD ba72b87; source and backup protected");
        }
        public static Scene Open(int index)
        {
            Stable();
            return EditorSceneManager.OpenScene(ScenePath(index), OpenSceneMode.Single);
        }
        public static string Physics(Scene scene, params long[] excluded)
        {
            return string.Join("\n", Transforms(scene).SelectMany(t => t.GetComponents<Component>())
                .Where(c => c is Collider2D || c is Rigidbody2D || c is PlatformEffector2D)
                .Where(c => !excluded.Contains(LocalID(c)))
                .Select(c => ID(c) + "|" + EditorJsonUtility.ToJson(c) + "|" +
                    c.transform.localToWorldMatrix.ToString("R") + "|" + c.gameObject.activeInHierarchy + "|" + c.gameObject.layer)
                .OrderBy(s => s));
        }
        public static List<ElevatorRecord> Elevators(Scene scene)
        {
            var records = new List<ElevatorRecord>();
            foreach (var behaviour in Transforms(scene).SelectMany(t => t.GetComponents<MonoBehaviour>())
                .Where(c => c != null && c.GetType().Name == "Elevator"))
            {
                var serialized = new SerializedObject(behaviour);
                var floor = serialized.FindProperty("floor").objectReferenceValue as GameObject;
                var column = serialized.FindProperty("column").objectReferenceValue as SpriteRenderer;
                var network = behaviour.GetComponentsInChildren<Component>(true)
                    .Where(c => c != null && c.GetType().Name == "NetworkObject").ToArray();
                var box = floor == null ? null : floor.GetComponentInChildren<BoxCollider2D>(true);
                var effector = box == null ? null : box.GetComponent<PlatformEffector2D>();
                bool same = floor != null && floor.transform.IsChildOf(behaviour.transform) &&
                    column != null && column.transform.IsChildOf(behaviour.transform);
                bool nested = false;
                var rootNetwork = network.FirstOrDefault(n => n.gameObject == behaviour.gameObject);
                if (rootNetwork != null && floor != null)
                {
                    var property = new SerializedObject(rootNetwork).FindProperty("NestedObjects");
                    nested = property != null && Enumerable.Range(0, property.arraySize)
                        .Any(i => (property.GetArrayElementAtIndex(i).objectReferenceValue as Component)?.gameObject == floor);
                }
                records.Add(new ElevatorRecord
                {
                    scene = scene.path, id = ID(behaviour.gameObject), path = FullPath(behaviour.transform),
                    source = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(behaviour.gameObject),
                    floor = ID(floor), column = ID(column), sameInstance = same, nestedNetwork = nested,
                    networkObjects = network.Select(ID).ToArray(), oneWay = box != null && box.usedByEffector && effector != null && effector.useOneWay,
                    status = same && nested && network.Length >= 2 && box != null ? "NATIVE_REFERENCE_PASS" : "FLOOR_ELEVATOR_HOLD"
                });
            }
            return records;
        }
        public static SceneRecord Snapshot(Scene scene)
        {
            var record = new SceneRecord
            {
                path = scene.path, guid = AssetDatabase.AssetPathToGUID(scene.path), sha256 = Hash(scene.path),
                physics = Physics(scene), objects = Transforms(scene).Length, elevators = Elevators(scene)
            };
            foreach (var transform in Transforms(scene))
            {
                record.missingScripts += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject);
                var sprite = transform.GetComponent<SpriteRenderer>();
                if (sprite != null && sprite.enabled && transform.gameObject.activeInHierarchy && sprite.sprite == null)
                    record.missingSprites++;
                foreach (var value in new Object[] { transform.gameObject }.Concat(transform.GetComponents<Component>().Where(c => c != null)))
                    record.records.Add(new ObjectRecord
                    {
                        id = ID(value), path = FullPath(transform), type = value.GetType().FullName,
                        json = EditorJsonUtility.ToJson(value), active = transform.gameObject.activeSelf,
                        source = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(transform.gameObject),
                        worldMatrix = Enumerable.Range(0, 16).Select(i => transform.localToWorldMatrix[i]).ToArray()
                    });
            }
            return record;
        }
        public static void Note(string stage, string status, string detail)
        {
            var qa = Read<QAFile>("NATIVE_QA.json");
            qa.records.Add(new QARecord { stage = stage, status = status, detail = detail, time = DateTimeOffset.Now.ToString("o") });
            Write("NATIVE_QA.json", qa);
            if (File.Exists(CorrectionBackup + "/BASELINE.json"))
                File.AppendAllText(Run + "/CHECKPOINT.md", "\n" + DateTimeOffset.Now.ToString("o") + " " + stage + " " + status + "\n" + detail + "\n", new UTF8Encoding(false));
            else File.WriteAllText(Run + "/CHECKPOINT.md", stage + " " + status + "\n" + detail + "\n", new UTF8Encoding(false));
            Debug.Log("NOV04 " + stage + " " + status + ": " + detail);
        }
        [MenuItem("Tools/NOV04 Visual/00 Native read-only gate")]
        public static void NativeGate()
        {
            Stable(); ProtectedGuard();
            Require(!File.Exists(Run + "/NATIVE_OBJECT_BASELINE.json"), "Native baseline exists; reuse saved evidence");
            var baseline = new NativeBaseline { originalScene = SceneManager.GetActiveScene().path, modes = "PLAY_OFF; PREFAB_STAGE_NONE; ANIMATION_MODE_OFF; ALL_OPEN_SCENES_CLEAN; NOT_COMPILING" };
            try
            {
                for (int i = 0; i < 4; i++)
                {
                    var scene = Open(i);
                    baseline.scenes.Add(Snapshot(scene));
                    if (i == 0)
                    {
                        var renderer = ComponentID<SpriteRenderer>(scene, 545717357);
                        baseline.frogSprite = renderer.sprite == null ? "NATIVE_SPRITE_NULL" :
                            renderer.sprite.name + " | " + AssetDatabase.GetAssetPath(renderer.sprite);
                    }
                    Stable();
                }
                Write("NATIVE_OBJECT_BASELINE.json", baseline);
                Note("I00", "NATIVE_BACKUP_GATE_PASS", baseline.modes + "; Frog=" + baseline.frogSprite +
                    "; Elevator native passes=" + baseline.scenes.Sum(s => s.elevators.Count(e => e.status == "NATIVE_REFERENCE_PASS")) +
                    "/" + baseline.scenes.Sum(s => s.elevators.Count));
            }
            finally
            {
                Stable();
                if (!string.IsNullOrEmpty(baseline.originalScene))
                    EditorSceneManager.OpenScene(baseline.originalScene, OpenSceneMode.Single);
            }
            ProtectedGuard();
        }

        [Serializable] public class FinalReport { public List<SceneRecord> scenes = new List<SceneRecord>(); }
        public static Sprite Sprite(string path, string name = null)
        {
            var sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().OrderBy(s => s.name).ToArray();
            Require(sprites.Length > 0, "Existing Sprite " + path);
            return name == null ? sprites[0] : sprites.Single(s => s.name == name);
        }
        public static GameObject NewRoot(Scene scene, string name)
        {
            Require(!Transforms(scene).Any(t => t.name == name), "New visual root already exists " + name);
            var root = new GameObject(name);
            SceneManager.MoveGameObjectToScene(root, scene);
            Undo.RegisterCreatedObjectUndo(root, "NOV04 " + name);
            return root;
        }
        public static SpriteRenderer Visual(Transform parent, string name, Sprite sprite, Vector3 position, Vector2 size, int order, Color color)
        {
            var leaf = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(leaf, "NOV04 visual leaf");
            SceneManager.MoveGameObjectToScene(leaf, parent.gameObject.scene);
            leaf.transform.SetParent(parent, false);
            leaf.transform.localPosition = position;
            var renderer = leaf.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.drawMode = SpriteDrawMode.Sliced;
            renderer.size = size;
            renderer.sortingOrder = order;
            renderer.color = color;
            return renderer;
        }
        public static void SetWorldRect(SpriteRenderer renderer, float left, float bottom, float right, float top)
        {
            Require(Mathf.Abs(Mathf.DeltaAngle(renderer.transform.eulerAngles.z, 0)) < .001f, "Horizontal decorative art only");
            Undo.RecordObject(renderer, "NOV04 visual size");
            Undo.RecordObject(renderer.transform, "NOV04 visual position");
            var scale = renderer.transform.lossyScale;
            Require(scale.x > 0 && scale.y > 0, "Positive visual scale");
            renderer.drawMode = SpriteDrawMode.Tiled;
            renderer.size = new Vector2((right - left) / scale.x, (top - bottom) / scale.y);
            renderer.transform.position += new Vector3((left + right) * .5f - renderer.bounds.center.x,
                (bottom + top) * .5f - renderer.bounds.center.y, 0);
            Require(Mathf.Abs(renderer.bounds.max.y - top) < .002f, "Visual top invariant");
        }
        public static string ExistingPhysics(Scene scene, HashSet<string> ids)
        {
            return string.Join("\n", Transforms(scene).SelectMany(t => t.GetComponents<Component>())
                .Where(c => c != null && ids.Contains(ID(c)))
                .Select(c => ID(c) + "|" + EditorJsonUtility.ToJson(c) + "|" +
                    c.transform.localToWorldMatrix.ToString("R") + "|" + c.gameObject.activeInHierarchy + "|" + c.gameObject.layer)
                .OrderBy(s => s));
        }
        public static HashSet<string> PhysicsIDs(Scene scene, IEnumerable<Object> excluded = null)
        {
            var skip = new HashSet<string>((excluded ?? Enumerable.Empty<Object>()).Select(ID));
            return new HashSet<string>(Transforms(scene).SelectMany(t => t.GetComponents<Component>())
                .Where(c => c is Collider2D || c is Rigidbody2D || c is PlatformEffector2D)
                .Where(c => !skip.Contains(ID(c))).Select(ID));
        }
        public static void SaveStage(Scene scene, string stage, SceneRecord before)
        {
            Require(before.path == scene.path, "Stage Scene identity");
            Require(Snapshot(scene).missingScripts == 0 && Snapshot(scene).missingSprites == 0, "Pre-save missing references");
            var changes = Read<Changes>("OBJECT_CHANGE_LOG.json");
            var after = Snapshot(scene);
            var previous = before.records.ToDictionary(r => r.id);
            var current = after.records.ToDictionary(r => r.id);
            foreach (var record in after.records)
            {
                if (!previous.TryGetValue(record.id, out var old) || old.json != record.json ||
                    !old.worldMatrix.SequenceEqual(record.worldMatrix))
                    changes.records.Add(new Change { stage = stage, scene = scene.path, id = record.id, path = record.path,
                        before = old == null ? "NEW" : old.json, after = record.json });
            }
            foreach (var record in before.records.Where(r => !current.ContainsKey(r.id)))
                changes.records.Add(new Change { stage = stage, scene = scene.path, id = record.id, path = record.path,
                    before = record.json, after = "DELETED_APPROVED_SCENE_SUBTREE" });
            Write("OBJECT_CHANGE_LOG.json", changes);
            EditorSceneManager.MarkSceneDirty(scene);
            Require(EditorSceneManager.SaveScene(scene), "Scene save " + scene.path);
            string hash = Hash(scene.path), path = scene.path;
            var reopened = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            Require(Hash(path) == hash && !reopened.isDirty, "Save / reopen exact hash");
            var report = File.Exists(Run + "/work/visual_report.json") ? Read<FinalReport>("work/visual_report.json") : new FinalReport();
            report.scenes.RemoveAll(s => s.path == path);
            report.scenes.Add(Snapshot(reopened));
            Write("work/visual_report.json", report);
            Require(report.scenes.Last().missingScripts == 0 && report.scenes.Last().missingSprites == 0, "Missing script / active Sprite");
            Note(stage, "SAVED_REOPENED_NATIVE_PASS", path + " SHA256 " + hash + "; objects=" + report.scenes.Last().objects);
            transaction = null;
            if (!stage.StartsWith("CORR_"))
                CaptureCurrent(stage == "M0-INTRO" ? "M0INTRO" : stage.StartsWith("M0-") ? "M0" : stage, "after");
            ProtectedGuard(); Stable();
        }
        public static void RequireNewAsset(string path)
        {
            Require(!File.Exists(path) && !File.Exists(path + ".meta"), "Existing user asset collision " + path);
        }
        static bool SafeDelete(GameObject root)
        {
            var subtree = new HashSet<Object>(root.GetComponentsInChildren<Component>(true).Cast<Object>()
                .Concat(root.GetComponentsInChildren<Transform>(true).Select(t => (Object)t.gameObject)));
            foreach (var t in Transforms(root.scene))
            foreach (var component in t.GetComponents<Component>().Where(c => c != null && !(c is Transform) && !subtree.Contains(c)))
            {
                var property = new SerializedObject(component).GetIterator();
                while (property.Next(true))
                    if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue != null &&
                        subtree.Contains(property.objectReferenceValue)) return false;
            }
            foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { "Assets" }))
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(guid));
                if (AnimationUtility.GetCurveBindings(clip).Concat(AnimationUtility.GetObjectReferenceCurveBindings(clip))
                    .Any(b => b.path.Split('/').Contains(root.name))) return false;
            }
            foreach (var source in Directory.GetFiles("Assets", "*.cs", SearchOption.AllDirectories)
                .Where(p => !p.Replace('\\', '/').Contains("/Editor/")))
                if (File.ReadAllText(source).Contains("\"" + root.name + "\"")) return false;
            Undo.DestroyObjectImmediate(root);
            return true;
        }
        static TMP_Text Text(Transform parent, string name, string text, Vector2 center, Vector2 size, float fontSize)
        {
            var leaf = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(leaf, "NOV04 board text");
            SceneManager.MoveGameObjectToScene(leaf, parent.gameObject.scene);
            leaf.transform.SetParent(parent, false);
            leaf.transform.localPosition = new Vector3(center.x, center.y, -.05f);
            var label = leaf.AddComponent<TextMeshPro>();
            label.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Imports/Galmuri7 SDF.asset");
            Require(label.font != null, "Existing Korean font");
            label.text = text.Replace('·', '-');
            label.fontSize = fontSize;
            label.color = new Color(.9f, .97f, 1);
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.rectTransform.sizeDelta = size;
            label.GetComponent<MeshRenderer>().sortingOrder = 8;
            label.ForceMeshUpdate();
            return label;
        }
        static void AbilityBoard(Scene scene, long goID, string role, string title, string body, string spritePath)
        {
            var root = GameObjectID(scene, goID);
            Undo.RecordObject(root.transform, "NOV04 board");
            Vector3 center = root.transform.position;
            Require(root.GetComponent<Animator>() == null, "No preexisting board Animator");
            root.transform.localScale = Vector3.one;
            root.transform.position = center;
            var panel = root.GetComponent<SpriteRenderer>();
            if (role == "Rabbit")
            {
                var visual = ComponentID<Transform>(scene, 1701129442);
                Undo.RecordObject(visual, "NOV04 rabbit board center");
                visual.localPosition = Vector3.zero;
                panel = visual.GetComponent<SpriteRenderer>();
                // The existing panel's nonuniform scale already yields a 4 x 1.6 world panel.
            }
            else
            {
                panel.sprite = Sprite(Root + "PCS_Resources/Public/MetalModules_R3.png", "Plate");
                panel.drawMode = SpriteDrawMode.Sliced;
                panel.size = new Vector2(4, 1.6f);
            }
            panel.sortingOrder = 5;
            panel.color = new Color(.18f, .27f, .34f, 1);
            Text(root.transform, "Title", title, new Vector2(-.45f, .46f), new Vector2(2.7f, .38f), 2.2f);
            Text(root.transform, "Body", body, new Vector2(-.45f, -.17f), new Vector2(2.7f, .78f), 1.65f);
            var frames = AssetDatabase.LoadAllAssetsAtPath(spritePath).OfType<Sprite>().OrderBy(s => s.name).ToArray();
            Require(frames.Length > 0, "Ability Sprite frames " + role);
            var example = Visual(root.transform, "Example", frames[0], new Vector3(1.22f, -.12f, -.05f),
                Vector2.one, 9, Color.white);
            example.drawMode = SpriteDrawMode.Simple;
            float height = frames.Max(s => s.bounds.size.y);
            example.transform.localScale = Vector3.one * (.88f / height);
            var icon = Visual(root.transform, "Icon", frames[0], new Vector3(-1.57f, .46f, -.05f), Vector2.one, 9, Color.white);
            icon.drawMode = SpriteDrawMode.Simple;
            icon.transform.localScale = Vector3.one * (.26f / height);
            string clipPath = Root + "Animation/Other/Ability_" + role + "_Visual_NOV04.anim";
            string controllerPath = Root + "Animation/Other/Ability_" + role + "_Visual_NOV04.controller";
            RequireNewAsset(clipPath); RequireNewAsset(controllerPath);
            var clip = new AnimationClip { name = "Ability_" + role + "_Visual_NOV04", frameRate = 10 };
            var keys = frames.Take(12).Select((sprite, index) => new ObjectReferenceKeyframe { time = index * .1f, value = sprite }).ToList();
            keys.Add(new ObjectReferenceKeyframe { time = Mathf.Max(.4f, keys.Last().time + .1f), value = frames[0] });
            AnimationUtility.SetObjectReferenceCurve(clip, EditorCurveBinding.PPtrCurve("Example", typeof(SpriteRenderer), "m_Sprite"), keys.ToArray());
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Example", typeof(Transform), "m_LocalPosition.y"),
                AnimationCurve.EaseInOut(0, -.12f, .5f, -.04f));
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            AssetDatabase.CreateAsset(clip, clipPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var state = controller.layers[0].stateMachine.AddState("Visual Example");
            state.motion = clip;
            controller.layers[0].stateMachine.defaultState = state;
            Require(root.GetComponent<Animator>() == null, "Board must have no preexisting Animator");
            Undo.AddComponent<Animator>(root).runtimeAnimatorController = controller;
            AssetDatabase.SaveAssetIfDirty(controller);
            root.transform.localScale = Vector3.one;
        }
        static void TutorialBackdrop(Scene scene)
        {
            var root = NewRoot(scene, "TutorialBackdrop_NOV04");
            var sprite = Sprite(Root + "PCS_Resources/Public/IndustrialBackground_NOV04.png");
            for (int i = 0; i < 4; i++)
            {
                var renderer = Visual(root.transform, "IndustrialDepth_" + (i + 1), sprite,
                    new Vector3(10 + i * 28, 20, 0), Vector2.one, -95, new Color(.7f, .83f, .9f, 1));
                renderer.drawMode = SpriteDrawMode.Simple;
                renderer.transform.localScale = Vector3.one * .53588516f;
            }
        }
        [MenuItem("Tools/NOV04 Visual/02 Malkuth terrain and boards")]
        public static void Malkuth()
        {
            Stable(); ProtectedGuard();
            PreflightMalkuthAssets();
            var scene = BeginStage(0, "M0"); var before = Snapshot(scene);
            Require(!Transforms(scene).Any(t => t.name == "Bear_Supports_NOV04"), "M0 already applied");
            var ground = ComponentID<SpriteRenderer>(scene, 434329203);
            var visualTransform = ComponentID<Transform>(scene, 434329202);
            var box = ComponentID<BoxCollider2D>(scene, 1444443633);
            var boxTransform = ComponentID<Transform>(scene, 1444443632);
            var gap = GameObjectID(scene, 2096613685);
            var physicsExceptions = gap.GetComponentsInChildren<Collider2D>(true).Cast<Object>().Concat(new Object[] { box }).ToArray();
            var ids = PhysicsIDs(scene, physicsExceptions);
            string physics = ExistingPhysics(scene, ids);
            Undo.RecordObjects(new Object[] { ground, visualTransform, box, boxTransform }, "NOV04 bear pit fill");
            ground.size = new Vector2(7, ground.size.y);
            visualTransform.localPosition = new Vector3(1.5f, -.671875f, 0);
            box.size = new Vector2(7, .4f);
            boxTransform.localPosition = new Vector3(1.5f, -.2f, 0);
            Physics2D.SyncTransforms();
            Require(Mathf.Abs(box.bounds.max.y - 14) < .001f && Mathf.Abs(box.bounds.min.x - 84) < .001f &&
                Mathf.Abs(box.bounds.max.x - 91) < .001f, "Bear floor Native top14 span84..91; actual=" + box.bounds);
            var supports = NewRoot(scene, "Bear_Supports_NOV04");
            var metal = Sprite(Root + "PCS_Resources/Public/MetalModules_R3.png", "Wall");
            Visual(supports.transform, "Rail", metal, new Vector3(91.5f, 21.94f), new Vector2(9.5f, .125f), 0, Color.white);
            Visual(supports.transform, "AllyHanger", metal, new Vector3(87.68f, 21.25f), new Vector2(.125f, 1.38f), 0, Color.white);
            Visual(supports.transform, "TargetHanger01", metal, new Vector3(91.28f, 21.25f), new Vector2(.125f, 1.37f), 0, Color.white);
            Visual(supports.transform, "TargetHanger02", metal, new Vector3(94.03f, 21.13f), new Vector2(.125f, 1.62f), 0, Color.white);
            bool removedGap = SafeDelete(gap);
            bool removedLadder = SafeDelete(GameObjectID(scene, 1492186868));
            AbilityBoard(scene, 1545253073, "Rabbit", "토끼 · 함께 이동", "동료를 안고 함께 이동하세요", "Assets/Resources/img/Player/Rabbit/Rabbit Idie.png");
            AbilityBoard(scene, 289960204, "Mouse", "쥐 · 단말 해킹", "단말에서 쉬운 해킹을 시작하세요", "Assets/Resources/img/Player/mouse/mouse Idie.png");
            AbilityBoard(scene, 545717355, "Frog", "개구리 · 혀 사용", "혀로 고리를 잡으세요", "Assets/Resources/img/Player/Frog/Frog tongue.png");
            AbilityBoard(scene, 1626549841, "Bear", "곰 · 물건 던지기", "물건을 던져 표적을 맞히세요", "Assets/Resources/img/Player/Bear/Bear Throwing.png");
            TutorialBackdrop(scene);
            Require(ExistingPhysics(scene, ids) == physics, "Only approved pit collision changed");
            Note("M0_DELETE", "REFERENCE_CHECKED", "Gap removed=" + removedGap + "; Ladder removed=" + removedLadder + "; MovableCube retained HOLD_ROLE; ally eye art unchanged HOLD_NO_EXACT_ASSET");
            SaveStage(scene, "M0", before);
        }
        static void CopyIntroBackup()
        {
            foreach (string suffix in new[] { "", ".meta" })
            {
                string source = ScenePath(0) + suffix;
                string destination = Run + "/backups/M0INTRO_PCS_MAP00_Malkuth.unity" + suffix;
                Require(!File.Exists(destination), "M0INTRO backup collision");
                File.Copy(source, destination);
                Require(Hash(source) == Hash(destination) && new FileInfo(source).Length == new FileInfo(destination).Length, "M0INTRO readback");
            }
        }
        static void StartRoomPiece(Transform root, string role, string name, float offset, Vector2 center, Vector2 size, Sprite sprite)
        {
            var renderer = Visual(root, role + "_" + name, sprite, new Vector3(offset + center.x, center.y), size, 0, Color.white);
            renderer.gameObject.layer = 6;
            var collider = renderer.gameObject.AddComponent<BoxCollider2D>();
            collider.size = size;
        }
        [MenuItem("Tools/NOV04 Visual/03 Four start rooms one Scene")]
        public static void StartRooms()
        {
            Stable(); ProtectedGuard();
            string scenePath = Root + "Scenes/PCS_MAP00_StartRooms4_NOV04.unity";
            string prefabPath = Root + "PCS_Resources/Public/PCS_StartRooms4_NOV04.prefab";
            RequireNewAsset(scenePath); RequireNewAsset(prefabPath);
            var main = BeginStage(0, "M0INTRO"); CopyIntroBackup();
            var beforeMain = Snapshot(main); var physicalIDs = PhysicsIDs(main);
            string physical = ExistingPhysics(main, physicalIDs);
            var sampleScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            transientScene = sampleScene;
            var root = NewRoot(sampleScene, "StartRooms4_NOV04");
            var metal = Sprite(Root + "PCS_Resources/Public/MetalModules_R3.png", "Wall");
            string[] roles = { "Rabbit", "Mouse", "Frog", "Bear" };
            for (int i = 0; i < roles.Length; i++)
            {
                float x = i * 28;
                StartRoomPiece(root.transform, roles[i], "LeftWall", x, new Vector2(-.25f, 4), new Vector2(.5f, 8), metal);
                StartRoomPiece(root.transform, roles[i], "ShaftRight", x, new Vector2(2.75f, 6.25f), new Vector2(.5f, 3.5f), metal);
                StartRoomPiece(root.transform, roles[i], "RoomCeiling", x, new Vector2(7.5f, 4.75f), new Vector2(9.5f, .5f), metal);
                StartRoomPiece(root.transform, roles[i], "RoomRight", x, new Vector2(12.25f, 2.25f), new Vector2(.5f, 4.5f), metal);
                StartRoomPiece(root.transform, roles[i], "Floor", x, new Vector2(4.75f, -.25f), new Vector2(9.5f, .5f), metal);
            }
            var background = Sprite(Root + "PCS_Resources/Public/IndustrialBackground_NOV04.png");
            for (int x = 0; x < 4; x++)
            for (int y = 0; y < 4; y++)
            {
                var bg = Visual(root.transform, "Background_" + x + "_" + y, background,
                    new Vector3(6 + 28 * x, -16.7f + 15.758373f * y), Vector2.one, -110, Color.white);
                bg.drawMode = SpriteDrawMode.Simple;
                bg.transform.localScale = Vector3.one * .53588516f;
            }
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Require(asset != null, "New shared source Prefab");
            Object.DestroyImmediate(root);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, sampleScene);
            var cameraGO = new GameObject("StartRooms_OverviewCamera");
            SceneManager.MoveGameObjectToScene(cameraGO, sampleScene);
            var camera = cameraGO.AddComponent<Camera>();
            camera.transform.position = new Vector3(48, 4, -10);
            camera.orthographic = true; camera.orthographicSize = 28.5f; camera.backgroundColor = new Color(.04f, .06f, .08f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            Require(EditorSceneManager.SaveScene(sampleScene, scenePath), "New single startroom Scene save");
            EditorSceneManager.CloseScene(sampleScene, true);
            transientScene = default;
            var optional = (GameObject)PrefabUtility.InstantiatePrefab(asset, main);
            optional.name = "StartRooms4_NOV04_OPTIONAL";
            optional.transform.position = new Vector3(3, 31, 0);
            foreach (var collider in optional.GetComponentsInChildren<Collider2D>(true))
            {
                collider.enabled = false;
                PrefabUtility.RecordPrefabInstancePropertyModifications(collider);
            }
            optional.SetActive(false);
            PrefabUtility.RecordPrefabInstancePropertyModifications(optional);
            PrefabUtility.RecordPrefabInstancePropertyModifications(optional.transform);
            Require(ExistingPhysics(main, physicalIDs) == physical, "Startroom sample original physics invariant");
            SaveStage(main, "M0-INTRO", beforeMain);
            var reopened = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            Require(reopened.GetRootGameObjects().Count(g => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(g) == prefabPath) == 1, "One four-room prefab instance");
            Require(Transforms(reopened).Count(t => t.name.EndsWith("_Floor")) == 4, "Four role rooms");
            CaptureCurrent("M0INTRO", "after");
            Note("M0-INTRO", "SHARED_PREFAB_REOPEN_PASS", "One new Scene, four rooms; main default OFF and20 new boxes disabled; no Spawn/Fade/physical route");
        }
        public static void ImportGlass()
        {
            string path = Root + "PCS_Resources/Public/GlassWall_NOV04.png";
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 32; importer.spritePivot = new Vector2(.5f, .5f);
            importer.spriteBorder = new Vector4(4, 4, 4, 4); importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed; importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true; importer.wrapMode = TextureWrapMode.Clamp;
            var settings = new TextureImporterSettings(); importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect; importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }
        [MenuItem("Tools/NOV04 Visual/04 Netzach glass and exterior")]
        public static void Netzach()
        {
            Stable(); ProtectedGuard();
            ImportGlass();
            var scene = BeginStage(3, "M3"); var before = Snapshot(scene); string physics = Physics(scene);
            Require(!Transforms(scene).Any(t => t.name == "PuzzleWall_B_Glass_NOV04"), "M3 already applied");
            var roots = Transforms(scene).Where(t => t.name == "PuzzleWall" &&
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject).EndsWith("/WallRun_R3.prefab")).OrderBy(t => t.position.x).ToArray();
            Require(roots.Length == 2, "Exactly two current PuzzleWall instances");
            var leftBox = ComponentID<BoxCollider2D>(scene, 1349542736);
            var rightBox = ComponentID<BoxCollider2D>(scene, 1071296918);
            Require(leftBox.transform.IsChildOf(roots[0]) && rightBox.transform.IsChildOf(roots[1]), "Left/Right PI identity, distinct Box");
            var group = NewRoot(scene, "PuzzleWall_B_Glass_NOV04");
            var glass = Sprite(Root + "PCS_Resources/Public/GlassWall_NOV04.png");
            for (int i = 0; i < roots.Length; i++)
            {
                var art = roots[i].Find("ART_VISUAL").GetComponent<SpriteRenderer>();
                Require(art.enabled && art.transform.parent == roots[i], "A art is separate from collider");
                var renderer = Visual(group.transform, i == 0 ? "Left" : "Right", glass, art.transform.position, art.size, 0, new Color(.75f, .94f, 1, 1));
                renderer.transform.rotation = art.transform.rotation;
                renderer.transform.localScale = art.transform.lossyScale;
                renderer.sharedMaterial = art.sharedMaterial;
                Undo.RecordObject(art, "NOV04 preserve A / show B");
                art.enabled = false;
                PrefabUtility.RecordPrefabInstancePropertyModifications(art);
            }
            SetWorldRect(ComponentID<SpriteRenderer>(scene, 1687242053), -16.010317f, -2, 22.528566f, 0);
            SetWorldRect(ComponentID<SpriteRenderer>(scene, 814113968), -16.010317f, 8.71f, 22.528566f, 10.71f);
            Require(Physics(scene) == physics, "M3 collider/effector/Network floor invariant");
            SaveStage(scene, "M3", before);
        }
        [MenuItem("Tools/NOV04 Visual/05 Hod visual clip")]
        public static void Hod()
        {
            Stable(); ProtectedGuard();
            var scene = BeginStage(2, "M2"); var before = Snapshot(scene); string physics = Physics(scene);
            string clipPath = Root + "Animation/Other/Hod_CrusherGuillotine_NOV04.anim";
            RequireNewAsset(clipPath);
            var plate = Transforms(scene).Single(t => t.name == "PCS_HodCrusher_N02_EDITOR_PREVIEW_ONLY_NO_DAMAGE").Find("PlateMoving");
            var visual = plate.GetComponentInChildren<SpriteRenderer>();
            var floor = Transforms(scene).Where(t => FullPath(t).Contains("LowerPuzzlePlatform"))
                .Select(t => t.GetComponent<BoxCollider2D>()).First(c => c != null);
            float endY = plate.parent.InverseTransformPoint(new Vector3(plate.position.x, floor.bounds.max.y + visual.bounds.extents.y, 0)).y;
            Require(Mathf.Abs(endY + 4.32f) < .08f, "Crusher contact differs from proposed range; hold endpoint");
            var clip = new AnimationClip { name = "Hod_CrusherGuillotine_NOV04", frameRate = 30 };
            var curve = new AnimationCurve(new Keyframe(0, -.7f), new Keyframe(.2f, -.7f), new Keyframe(.3f, endY),
                new Keyframe(.4f, endY), new Keyframe(1.6f, -.7f), new Keyframe(1.8f, -.7f));
            AnimationUtility.SetKeyRightTangentMode(curve, 1, AnimationUtility.TangentMode.Linear);
            AnimationUtility.SetKeyLeftTangentMode(curve, 2, AnimationUtility.TangentMode.Linear);
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("PlateMoving", typeof(Transform), "m_LocalPosition.y"), curve);
            AssetDatabase.CreateAsset(clip, clipPath);
            SetWorldRect(ComponentID<SpriteRenderer>(scene, 1721528771), 1.89752373f, 3, 31.87525573f, 4.50000019f);
            Require(Physics(scene) == physics, "Hod physics invariant");
            Note("M2_CONTACT", "NATIVE_MEASURED", "Platehalf=" + visual.bounds.extents.y + " floorTop=" + floor.bounds.max.y + " endpoint=" + endY);
            SaveStage(scene, "M2", before);
        }
        [MenuItem("Tools/NOV04 Visual/06 Yesod exterior only")]
        public static void Yesod()
        {
            Stable(); ProtectedGuard();
            var scene = BeginStage(1, "M1"); var before = Snapshot(scene); string physics = Physics(scene);
            var root = NewRoot(scene, "ExteriorFinish_NOV04");
            var background = Sprite("Assets/Resources/img/BG/stage1-1.png");
            for (int x = 0; x < 3; x++)
            for (int y = 0; y < 2; y++)
            {
                var renderer = Visual(root.transform, "BG_Coverage_" + x + "_" + y, background,
                    new Vector3(-10 + 28 * x, 4.8791865f + 15.758373f * y), Vector2.one, -110, Color.white);
                renderer.drawMode = SpriteDrawMode.Simple; renderer.transform.localScale = Vector3.one * .53588516f;
            }
            var entry = ComponentID<SpriteRenderer>(scene, 1932738182);
            SetWorldRect(entry, -3.24f, -.5f, .49711871f, 0);
            Require(Physics(scene) == physics, "Yesod neutral physics invariant");
            Note("M1_44F", "HOLD_PRESERVED", "11 obsolete clip paths unchanged; UCHAN/Elevator missing floor remains HOLD; exterior only");
            SaveStage(scene, "M1", before);
        }
        public static void Attempt(string stage, Action action)
        {
            var qa = Read<QAFile>("NATIVE_QA.json");
            if (qa.records.Any(r => r.stage == stage && r.status == "SAVED_REOPENED_NATIVE_PASS"))
            {
                Debug.Log("NOV04 skip previously saved " + stage); return;
            }
            try { action(); }
            catch (Exception error)
            {
                RecoverOwnStage();
                var latest = Read<QAFile>("NATIVE_QA.json");
                latest.holds.Add(stage + ": " + error);
                Write("NATIVE_QA.json", latest);
                Note(stage, "HOLD", error.Message);
                Debug.LogException(error);
            }
        }
        [MenuItem("Tools/NOV04 Visual/01 Apply approved stages once")]
        public static void ApplyApproved()
        {
            Stable(); ProtectedGuard();
            Require(!File.Exists(CorrectionBackup + "/BASELINE.json"), "Correction mode: do not rerun the original builder");
            Require(File.Exists(Run + "/NATIVE_OBJECT_BASELINE.json"), "Run Native gate first");
            ImportBackground();
            Attempt("M0", Malkuth);
            if (SceneManager.GetActiveScene().isDirty) return;
            if (Read<QAFile>("NATIVE_QA.json").records.Any(r => r.stage == "M0" && r.status == "SAVED_REOPENED_NATIVE_PASS"))
                Attempt("M0-INTRO", StartRooms);
            if (SceneManager.GetActiveScene().isDirty) return;
            Attempt("M3", Netzach);
            if (SceneManager.GetActiveScene().isDirty) return;
            Attempt("M2", Hod);
            if (SceneManager.GetActiveScene().isDirty) return;
            Attempt("M1", Yesod);
        }

        private sealed class StageTransaction
        {
            public string stage, path, sha;
            public int undoGroup;
            public SceneRecord before;
        }
        private static StageTransaction transaction;
        private static Scene transientScene;
        public static Scene BeginStage(int index, string stage)
        {
            Stable(); ProtectedGuard();
            var scene = Open(index);
            var previous = Read<QAFile>("NATIVE_QA.json");
            Require(!previous.records.Any(r => (r.stage == stage || stage == "M0INTRO" && r.stage == "M0-INTRO") &&
                r.status == "SAVED_REOPENED_NATIVE_PASS"), "Stage already saved; inspect current result");
            Undo.IncrementCurrentGroup();
            transaction = new StageTransaction { stage = stage, path = scene.path, sha = Hash(scene.path), undoGroup = Undo.GetCurrentGroup(), before = Snapshot(scene) };
            Undo.SetCurrentGroupName("NOV04 " + stage);
            foreach (var root in scene.GetRootGameObjects()) Undo.RegisterFullObjectHierarchyUndo(root, "NOV04 " + stage);
            if (!stage.StartsWith("CORR_")) CaptureCurrent(stage, "before");
            return scene;
        }
        private static void RecoverOwnStage()
        {
            if (transientScene.IsValid())
            {
                // Only the new, stage-owned temporary Scene can be closed here.
                EditorSceneManager.CloseScene(transientScene, true);
                transientScene = default;
            }
            if (transaction == null) return;
            var active = SceneManager.GetActiveScene();
            if (active.path == transaction.path && Hash(transaction.path) == transaction.sha)
            {
                Undo.RevertAllDownToGroup(transaction.undoGroup);
                if (active.isDirty)
                {
                    // The stage started clean. Verify exact native state before clearing only its own dirty flag.
                    var original = transaction.before;
                    var current = Snapshot(active);
                    bool identical = original != null && original.records.Count == current.records.Count &&
                        original.records.All(o => current.records.Any(n => n.id == o.id && n.json == o.json && n.worldMatrix.SequenceEqual(o.worldMatrix)));
                    if (identical)
                    {
                        EditorSceneManager.CloseScene(active, true);
                        active = EditorSceneManager.OpenScene(transaction.path, OpenSceneMode.Single);
                    }
                }
                Note(transaction.stage, active.isDirty ? "OWN_STAGE_UNDO_DIRTY_HOLD" : "OWN_STAGE_UNDO_RESTORED",
                    "Reverted only this synchronous stage Undo group. Partial new Assets are preserved and must be inspected before retry.");
            }
            transaction = null;
        }
        private static void PreflightMalkuthAssets()
        {
            foreach (string path in new[]
            {
                "Assets/Resources/img/Player/Rabbit/Rabbit Idie.png",
                "Assets/Resources/img/Player/mouse/mouse Idie.png",
                "Assets/Resources/img/Player/Frog/Frog tongue.png",
                "Assets/Resources/img/Player/Bear/Bear Throwing.png",
                Root + "PCS_Resources/Public/IndustrialBackground_NOV04.png"
            }) Sprite(path);
            foreach (string role in new[] { "Rabbit", "Mouse", "Frog", "Bear" })
            {
                RequireNewAsset(Root + "Animation/Other/Ability_" + role + "_Visual_NOV04.anim");
                RequireNewAsset(Root + "Animation/Other/Ability_" + role + "_Visual_NOV04.controller");
            }
            Require(AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Imports/Galmuri7 SDF.asset") != null, "Korean font");
        }
        [MenuItem("Tools/NOV04 Visual/01a Import owned new background")]
        public static void ImportBackground()
        {
            Stable(); ProtectedGuard();
            string path = Root + "PCS_Resources/Public/IndustrialBackground_NOV04.png";
            Require(File.Exists(path), "Approved generated PNG");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 32;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
            Note("TOOLS_ART", "IMPORT_PASS", "Owned NOV04 background only");
        }
        public static Camera MapCamera(Scene scene)
        {
            return Transforms(scene).Select(t => t.GetComponent<Camera>()).First(c => c != null);
        }
        public static Texture2D Render(Camera camera, Vector2 center, float halfHeight, float aspect, int height = 900)
        {
            string json = EditorJsonUtility.ToJson(camera);
            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            var target = camera.targetTexture;
            float oldAspect = camera.aspect;
            var rt = new RenderTexture(Mathf.RoundToInt(height * aspect), height, 24);
            var active = RenderTexture.active;
            try
            {
                camera.transform.SetPositionAndRotation(new Vector3(center.x, center.y, position.z), Quaternion.identity);
                camera.orthographic = true; camera.orthographicSize = halfHeight; camera.aspect = aspect;
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,
                    new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest { destination = rt });
                RenderTexture.active = rt;
                var image = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); image.Apply();
                return image;
            }
            finally
            {
                RenderTexture.active = active;
                camera.targetTexture = target;
                camera.transform.SetPositionAndRotation(position, rotation);
                EditorJsonUtility.FromJsonOverwrite(json, camera); camera.aspect = oldAspect;
                rt.Release(); Object.DestroyImmediate(rt);
            }
        }
        public static Vector2 OverviewCenter(string stage)
        {
            if (stage == "M0") return new Vector2(64, 16);
            if (stage == "M0INTRO") return new Vector2(48, 4);
            if (stage == "M3") return new Vector2(3, 4);
            if (stage == "M2") return new Vector2(13, 3);
            return new Vector2(11, 5);
        }
        public static float OverviewSize(string stage)
        {
            return stage == "M0" ? 21 : stage == "M0INTRO" ? 28.5f : stage == "M3" ? 13 : 9;
        }
        public static Texture2D CaptureCurrent(string stage, string suffix)
        {
            var scene = SceneManager.GetActiveScene();
            Require(new[] { "M0", "M0INTRO", "M1", "M2", "M3", "DOORS" }.Contains(stage), "Capture slot");
            Require(new[] { "before", "after", "colliders" }.Contains(suffix), "Capture filename");
            var camera = MapCamera(scene);
            bool dirty = scene.isDirty;
            string physics = Physics(scene), sha = Hash(scene.path);
            var image = Render(camera, OverviewCenter(stage), OverviewSize(stage), stage == "M0" ? 4.2f : 16f / 9);
            File.WriteAllBytes(Run + "/visual/" + stage + "_" + suffix + ".png", image.EncodeToPNG());
            Require(scene.isDirty == dirty && Hash(scene.path) == sha && Physics(scene) == physics, "Native camera view restore");
            return image;
        }
        [MenuItem("Tools/NOV04 Visual/07 Open review and preview")]
        public static void OpenReview() { Nov04ReviewWindow.Open(); }

        [MenuItem("Tools/NOV04 Visual/08 Verify saved results without regeneration")]
        public static void VerifySaved()
        {
            Stable(); ProtectedGuard();
            var report = new FinalReport();
            for (int i = 0; i < 4; i++)
            {
                var scene = Open(i);
                var record = Snapshot(scene);
                Require(record.missingScripts == 0 && record.missingSprites == 0, "Saved native references " + i);
                report.scenes.Add(record);
                CaptureCurrent("M" + i, "after");
                Note("FINAL_M" + i, "NATIVE_REOPEN_PASS", record.sha256 + "; objects=" + record.objects +
                    "; elevators=" + record.elevators.Count(e => e.status == "NATIVE_REFERENCE_PASS") + "/" + record.elevators.Count);
            }
            string introPath = Root + "Scenes/PCS_MAP00_StartRooms4_NOV04.unity";
            if (File.Exists(introPath))
            {
                var scene = EditorSceneManager.OpenScene(introPath, OpenSceneMode.Single);
                var roots = scene.GetRootGameObjects().Where(g => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(g) ==
                    Root + "PCS_Resources/Public/PCS_StartRooms4_NOV04.prefab").ToArray();
                Require(roots.Length == 1 && Transforms(scene).Count(t => t.name.EndsWith("_Floor")) == 4, "Shared source plus all four rooms");
                var record = Snapshot(scene);
                Require(record.missingScripts == 0 && record.missingSprites == 0, "Intro native references");
                report.scenes.Add(record);
                CaptureCurrent("M0INTRO", "after");
                Note("M0-INTRO", "SHARED_PREFAB_REOPEN_PASS", "Saved source reused, no regeneration; four role rooms; root=" + roots[0].name + "; SHA256=" + record.sha256);
            }
            Write("work/visual_report.json", report);
            Stable(); ProtectedGuard();
        }

        public static void PreviewStable()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling, "Preview requires Edit mode");
            Require(PrefabStageUtility.GetCurrentPrefabStage() == null && !AnimationMode.InAnimationMode(), "Prefab/Animation mode conflict");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                Require(!SceneManager.GetSceneAt(i).isDirty, "Preview protects unsaved Scene");
        }
        public static void PreviewNote(string stage, string status, string detail)
        {
            // Playback uses project assets only. The approved local Run is optional on another team PC.
            if (!Directory.Exists(Run)) return;
            var qa = File.Exists(Run + "/PREVIEW_QA.json") ? Read<QAFile>("PREVIEW_QA.json") : new QAFile();
            qa.records.Add(new QARecord { stage = stage, status = status, detail = detail, time = DateTimeOffset.Now.ToString("o") });
            Write("PREVIEW_QA.json", qa);
        }
        public static AnimationClip DoorClip(string pose)
        {
            return AssetDatabase.LoadAssetAtPath<AnimationClip>(Root + "Animation/ElavatorDoor/Door_" + pose + "_R4.anim");
        }
        [MenuItem("Tools/NOV04 Visual/10 Finish own saved boards and variation")]
        public static void FinishBoards()
        {
            Attempt("M0-FINISH", FinishBoardsCore);
        }
        [MenuItem("Tools/NOV04 Visual/11 Finish own title font glyphs")]
        public static void FinishText()
        {
            Attempt("M0-TEXT", () =>
            {
                Stable(); ProtectedGuard();
                var scene = Open(0);
                var saved = Read<FinalReport>("work/visual_report.json").scenes.Single(s => s.path == scene.path);
                Require(saved.sha256 == Hash(scene.path), "Concurrent user edit");
                var before = Snapshot(scene); string physics = Physics(scene);
                Undo.IncrementCurrentGroup();
                transaction = new StageTransaction { stage = "M0-TEXT", path = scene.path, sha = Hash(scene.path), undoGroup = Undo.GetCurrentGroup(), before = before };
                foreach (var root in scene.GetRootGameObjects()) Undo.RegisterFullObjectHierarchyUndo(root, "NOV04 own title glyphs");
                foreach (long id in new[] { 1545253073L, 289960204L, 545717355L, 1626549841L })
                {
                    var board = GameObjectID(scene, id);
                    var label = board.transform.Find("Title").GetComponent<TMP_Text>();
                    Require(label != null && label.font.HasCharacter('-'), "Owned title and supported ASCII dash");
                    Undo.RecordObject(label, "NOV04 supported font separator");
                    label.text = label.text.Replace('·', '-');
                    label.ForceMeshUpdate(true, true);
                    foreach (var sub in label.GetComponentsInChildren<TMP_SubMesh>(true))
                        Undo.DestroyObjectImmediate(sub.gameObject);
                }
                Require(Physics(scene) == physics, "Title-only physical invariant");
                SaveStage(scene, "M0-TEXT", before);
                Note("TOOLS_FONT", "RESOLVED", "Middle-dot absent from Galmuri7 caused four deferred TMP fallback meshes. Owned title separators changed to supported dash; original font untouched.");
            });
        }
        static void FinishBoardsCore()
        {
            Stable(); ProtectedGuard();
            var scene = Open(0);
            var saved = Read<FinalReport>("work/visual_report.json").scenes.Single(s => s.path == scene.path);
            Require(saved.sha256 == Hash(scene.path), "Concurrent user Scene change; refinement HOLD");
            var before = Snapshot(scene); string physics = Physics(scene);
            Undo.IncrementCurrentGroup();
            transaction = new StageTransaction { stage = "M0-FINISH", path = scene.path, sha = Hash(scene.path), undoGroup = Undo.GetCurrentGroup(), before = before };
            foreach (var root in scene.GetRootGameObjects()) Undo.RegisterFullObjectHierarchyUndo(root, "NOV04 own visual QA finish");
            foreach (long id in new[] { 1545253073L, 289960204L, 545717355L, 1626549841L })
            {
                var board = GameObjectID(scene, id);
                Require(board.transform.Find("Title") != null && board.transform.Find("Example") != null, "Only the owned new board content");
                Undo.RecordObject(board.transform, "NOV04 board world size");
                board.transform.localScale = Vector3.one;
                var panel = id == 1545253073 ? board.transform.Find("Visual").GetComponent<SpriteRenderer>() : board.GetComponent<SpriteRenderer>();
                Require(Mathf.Abs(panel.bounds.size.x - 4) < .01f && Mathf.Abs(panel.bounds.size.y - 1.6f) < .01f, "Native board world4x1.6 " + id);
            }
            var background = scene.GetRootGameObjects().Single(g => g.name == "TutorialBackdrop_NOV04");
            foreach (var renderer in background.GetComponentsInChildren<SpriteRenderer>())
            {
                Undo.RecordObject(renderer, "NOV04 visible generated background");
                renderer.color = new Color(.7f, .83f, .9f, 1);
            }
            Require(Physics(scene) == physics, "Own visual refinement changes no physics");
            SaveStage(scene, "M0-FINISH", before);
        }
        [MenuItem("Tools/NOV04 Visual/09 Proxy sampling QA no Scene writes")]
        public static void PreviewQA()
        {
            Stable(); ProtectedGuard();
            int count = 0;
            for (int index = 0; index < 4; index++)
            {
                var scene = Open(index);
                foreach (var door in Transforms(scene).Where(t => t.name == "DoorRoot" && t.Find("LeftLeaf") != null && t.Find("RightLeaf") != null).ToArray())
                {
                    using (var proxy = new Nov04VisualProxy(door))
                    {
                        var left = proxy.root.transform.Find("LeftLeaf");
                        var right = proxy.root.transform.Find("RightLeaf");
                        DoorClip("ClosedIdle").SampleAnimation(proxy.root, 0);
                        Require(Mathf.Abs(left.localPosition.x + .5f) < .001f && Mathf.Abs(right.localPosition.x - .5f) < .001f, "Door idle binding");
                        float ly = left.localPosition.y, ry = right.localPosition.y;
                        var clip = DoorClip("Open"); clip.SampleAnimation(proxy.root, clip.length);
                        Require(Mathf.Abs(left.localPosition.x + 1.5f) < .001f && Mathf.Abs(right.localPosition.x - 1.5f) < .001f &&
                            left.localPosition.y == ly && right.localPosition.y == ry, "Door open XY binding");
                        if (count == 0) proxy.SaveImage("DOORS_after", 420);
                        clip = DoorClip("Close"); clip.SampleAnimation(proxy.root, clip.length);
                        Require(Mathf.Abs(left.localPosition.x + .5f) < .001f && Mathf.Abs(right.localPosition.x - .5f) < .001f, "Door close binding");
                        if (count == 0) proxy.SaveImage("DOORS_before", 420);
                    }
                    PreviewNote("DOORS", "NATIVE_PROXY_SAMPLE_RESTORE_PASS", ID(door.gameObject) + " " + FullPath(door));
                    count++;
                }
                if (index == 0)
                {
                    foreach (var board in Transforms(scene).Where(t => t.name == "AbilityBoard_SLOT" && t.Find("Example") != null).ToArray())
                    {
                        var controller = board.GetComponent<Animator>()?.runtimeAnimatorController;
                        Require(controller != null, "Board Animator source");
                        var clip = controller.animationClips.Single();
                        using (var proxy = new Nov04VisualProxy(board))
                        {
                            foreach (float time in new[] { 0f, .1f, .3f, clip.length }) clip.SampleAnimation(proxy.root, time);
                            Require(proxy.root.transform.Find("Example").GetComponent<SpriteRenderer>().sprite != null, "Ability PPtr binding");
                        }
                        PreviewNote("ABILITY", "NATIVE_PROXY_SAMPLE_RESTORE_PASS", FullPath(board) + " " + AssetDatabase.GetAssetPath(clip));
                    }
                }
                if (index == 2)
                {
                    var source = Transforms(scene).Single(t => t.name == "PCS_HodCrusher_N02_EDITOR_PREVIEW_ONLY_NO_DAMAGE");
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Root + "Animation/Other/Hod_CrusherGuillotine_NOV04.anim");
                    using (var proxy = new Nov04VisualProxy(source))
                    {
                        var plate = proxy.root.transform.Find("PlateMoving");
                        foreach (var sample in new[] { new Vector2(0, -.7f), new Vector2(.2f, -.7f), new Vector2(.3f, -4.32f), new Vector2(.4f, -4.32f), new Vector2(1.6f, -.7f), new Vector2(1.8f, -.7f) })
                        {
                            clip.SampleAnimation(proxy.root, sample.x);
                            Require(Mathf.Abs(plate.localPosition.y - sample.y) < .001f, "Crusher key binding " + sample.x);
                        }
                    }
                    PreviewNote("CRUSHER", "NATIVE_PROXY_SAMPLE_RESTORE_PASS", "Six poses; 0.1s linear descent; endpoint -4.32; original physics untouched");
                }
            }
            Require(count == 9, "Expected9 current doors, actual" + count);
            PreviewNote("DOORS_ALL", "NATIVE_PROXY_SAMPLE_RESTORE_PASS", "9/9; STOP destroys isolated Transform/Sprite proxies; no Runtime components copied");
            Stable(); ProtectedGuard();
        }

    }

    public sealed class Nov04VisualProxy : IDisposable
    {
        public GameObject root { get; private set; }
        public Camera camera { get; private set; }
        readonly Scene sourceScene;
        readonly string sourceSha, sourcePhysics, sourceSnapshot;
        readonly bool sourceDirty;
        Scene previewScene;
        public Nov04VisualProxy(Transform source)
        {
            PCS_MapVisualNov04Editor.PreviewStable();
            sourceScene = source.gameObject.scene;
            sourceSha = PCS_MapVisualNov04Editor.Hash(sourceScene.path);
            sourcePhysics = PCS_MapVisualNov04Editor.Physics(sourceScene);
            sourceSnapshot = JsonUtility.ToJson(PCS_MapVisualNov04Editor.Snapshot(sourceScene));
            sourceDirty = sourceScene.isDirty;
            try
            {
                previewScene = EditorSceneManager.NewPreviewScene();
                root = Copy(source, null, true);
                var cam = EditorUtility.CreateGameObjectWithHideFlags("NOV04 isolated camera", HideFlags.HideAndDontSave, typeof(Camera));
                SceneManager.MoveGameObjectToScene(cam, previewScene);
                camera = cam.GetComponent<Camera>();
                camera.scene = previewScene; camera.enabled = false; camera.useOcclusionCulling = false;
                camera.transform.position = new Vector3(0, 0, -30);
                camera.orthographic = true; camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.035f, .055f, .075f);
                var lamp = EditorUtility.CreateGameObjectWithHideFlags("NOV04 isolated point light", HideFlags.HideAndDontSave);
                lamp.SetActive(false);
                SceneManager.MoveGameObjectToScene(lamp, previewScene);
                var light = lamp.AddComponent<UnityEngine.Rendering.Universal.Light2D>();
                light.lightType = UnityEngine.Rendering.Universal.Light2D.LightType.Point;
                light.pointLightInnerRadius = 100; light.pointLightOuterRadius = 101;
                light.pointLightInnerAngle = 360; light.pointLightOuterAngle = 360;
                light.intensity = 1;
                lamp.SetActive(true);
            }
            catch { Dispose(); throw; }
        }
        public Nov04VisualProxy(Transform source, IEnumerable<Transform> extras) : this(source)
        {
            try
            {
                foreach (var extra in extras)
                {
                    var copy = Copy(extra, root.transform, false);
                    copy.transform.localPosition = source.InverseTransformPoint(extra.position);
                    copy.transform.localRotation = Quaternion.Inverse(source.rotation) * extra.rotation;
                    copy.transform.localScale = Vector3.Scale(extra.lossyScale,
                        new Vector3(1 / source.lossyScale.x, 1 / source.lossyScale.y, 1 / source.lossyScale.z));
                }
            }
            catch { Dispose(); throw; }
        }
        GameObject Copy(Transform source, Transform parent, bool first)
        {
            var go = EditorUtility.CreateGameObjectWithHideFlags(source.name, HideFlags.HideAndDontSave);
            SceneManager.MoveGameObjectToScene(go, previewScene);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = first ? Vector3.zero : source.localPosition;
            go.transform.localRotation = first ? source.rotation : source.localRotation;
            go.transform.localScale = first ? source.lossyScale : source.localScale;
            var sr = source.GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                var copy = go.AddComponent<SpriteRenderer>();
                copy.sprite = sr.sprite; copy.sharedMaterials = sr.sharedMaterials;
                copy.drawMode = sr.drawMode; copy.size = sr.size; copy.tileMode = sr.tileMode;
                copy.color = sr.color; copy.flipX = sr.flipX; copy.flipY = sr.flipY;
                copy.sortingLayerID = sr.sortingLayerID; copy.sortingOrder = sr.sortingOrder;
                copy.maskInteraction = sr.maskInteraction; copy.enabled = sr.enabled;
            }
            for (int i = 0; i < source.childCount; i++) Copy(source.GetChild(i), go.transform, false);
            go.SetActive(first || source.gameObject.activeSelf);
            return go;
        }
        public Texture2D Image(int height = 600)
        {
            var renderers = root.GetComponentsInChildren<SpriteRenderer>().Where(r => r.enabled && r.sprite != null).ToArray();
            PCS_MapVisualNov04Editor.Require(renderers.Length > 0, "Preview has visible sprites");
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            return PCS_MapVisualNov04Editor.Render(camera, bounds.center, Mathf.Max(1, bounds.extents.y * 1.18f),
                Mathf.Clamp(bounds.size.x / Mathf.Max(.1f, bounds.size.y), .65f, 2.4f), height);
        }
        public void SaveImage(string name, int height)
        {
            PCS_MapVisualNov04Editor.Require(name == "DOORS_before" || name == "DOORS_after", "Proxy image slot");
            var image = Image(height);
            File.WriteAllBytes(PCS_MapVisualNov04Editor.Run + "/visual/" + name + ".png", image.EncodeToPNG());
            Object.DestroyImmediate(image);
        }
        public void Dispose()
        {
            if (previewScene.IsValid()) EditorSceneManager.ClosePreviewScene(previewScene);
            previewScene = default; root = null; camera = null;
            if (!sourceScene.IsValid()) return;
            var after = PCS_MapVisualNov04Editor.Snapshot(sourceScene);
            if (Directory.Exists(PCS_MapVisualNov04Editor.Run))
                File.WriteAllText(PCS_MapVisualNov04Editor.Run + "/work/preview_snapshot.json", "{\"before\":" + sourceSnapshot + ",\"after\":" + JsonUtility.ToJson(after) + "}");
            PCS_MapVisualNov04Editor.Require(sourceScene.isDirty == sourceDirty, "Proxy STOP dirty flag invariant");
            PCS_MapVisualNov04Editor.Require(PCS_MapVisualNov04Editor.Hash(sourceScene.path) == sourceSha, "Proxy STOP disk SHA invariant");
            PCS_MapVisualNov04Editor.Require(PCS_MapVisualNov04Editor.Physics(sourceScene) == sourcePhysics, "Proxy STOP physics invariant");
            PCS_MapVisualNov04Editor.Require(JsonUtility.ToJson(after) == sourceSnapshot, "Proxy STOP native object invariant; see preview_snapshot");
        }
    }

    public class Nov04ReviewWindow : EditorWindow
    {
        Texture2D image;
        string status = "NOV04 approved visual work";
        Vector2 scroll;
        Nov04VisualProxy proxy;
        Transform target;
        AnimationClip clip;
        float time;
        bool playing;
        double lastTick;
        int actorIndex, clipIndex;
        string[] doorPoses = { "Open", "Close", "ClosedIdle" };
        public static void Open()
        {
            var window = GetWindow<Nov04ReviewWindow>("NOV04 Review");
            window.minSize = new Vector2(700, 550);
            window.Show();
        }
        void Execute(Action action)
        {
            try { string previous = status; action(); if (status == previous) status = "Completed " + DateTime.Now.ToString("HH:mm:ss"); }
            catch (Exception error) { Stop("error"); status = error.ToString(); Debug.LogException(error); }
            Repaint();
        }
        void View(int index)
        {
            Stop("Scene change");
            var scene = PCS_MapVisualNov04Editor.Open(index);
            string slot = "M" + index;
            if (image != null) DestroyImmediate(image);
            image = PCS_MapVisualNov04Editor.CaptureCurrent(slot, "after");
            var center = PCS_MapVisualNov04Editor.OverviewCenter(slot);
            SceneView.lastActiveSceneView?.Frame(new Bounds(center, new Vector3(PCS_MapVisualNov04Editor.OverviewSize(slot) * 3.5f,
                PCS_MapVisualNov04Editor.OverviewSize(slot) * 2, 1)), true);
            status = scene.path + "\nEDIT / Native camera overview. Stored camera restored, not Play.";
        }
        void IntroView()
        {
            Stop("Scene change"); PCS_MapVisualNov04Editor.Stable();
            var scene = EditorSceneManager.OpenScene(PCS_MapVisualNov04Editor.Root + "Scenes/PCS_MAP00_StartRooms4_NOV04.unity", OpenSceneMode.Single);
            if (image != null) DestroyImmediate(image);
            image = PCS_MapVisualNov04Editor.CaptureCurrent("M0INTRO", "after");
            SceneView.lastActiveSceneView?.Frame(new Bounds(new Vector3(48, 4), new Vector3(101, 57, 1)), true);
            status = scene.path + "\nAll four rooms share one owned source Prefab.";
        }
        void Probe(float aspect)
        {
            Stop("camera probe"); PCS_MapVisualNov04Editor.PreviewStable();
            var scene = SceneManager.GetActiveScene();
            var camera = PCS_MapVisualNov04Editor.MapCamera(scene);
            string sha = PCS_MapVisualNov04Editor.Hash(scene.path), state = EditorJsonUtility.ToJson(camera);
            string physics = PCS_MapVisualNov04Editor.Physics(scene);
            if (image != null) DestroyImmediate(image);
            image = PCS_MapVisualNov04Editor.Render(camera, camera.transform.position, camera.orthographicSize, aspect, 600);
            PCS_MapVisualNov04Editor.Require(!scene.isDirty && PCS_MapVisualNov04Editor.Hash(scene.path) == sha &&
                PCS_MapVisualNov04Editor.Physics(scene) == physics && EditorJsonUtility.ToJson(camera) == state, "Camera probe restore");
            PCS_MapVisualNov04Editor.PreviewNote("CAMERA", "NATIVE_RENDER_RESTORE_PASS", scene.path + " aspect=" + aspect + "; stored center=" + camera.transform.position);
            status = "Stored camera aspect probe " + aspect + " / camera restored; not Play";
        }
        void InspectCollider()
        {
            Stop("inspect collider");
            var scene = SceneManager.GetActiveScene();
            var colliders = PCS_MapVisualNov04Editor.Transforms(scene).Select(t => t.GetComponent<BoxCollider2D>()).Where(c => c != null).ToArray();
            var collider = scene.path.EndsWith("Malkuth.unity") ? PCS_MapVisualNov04Editor.ComponentID<BoxCollider2D>(scene, 1444443633) :
                scene.path.EndsWith("Yesod.unity") ? colliders.First(c => PCS_MapVisualNov04Editor.FullPath(c.transform).Contains("EntryApproach")) :
                scene.path.EndsWith("Hod.unity") ? colliders.First(c => PCS_MapVisualNov04Editor.FullPath(c.transform).Contains("UpperPlatform_Rabbit")) :
                scene.path.EndsWith("Netzach.unity") ? PCS_MapVisualNov04Editor.ComponentID<BoxCollider2D>(scene, 1349542736) : colliders.First();
            Selection.activeGameObject = collider.gameObject;
            EditorGUIUtility.PingObject(collider);
            SceneView.lastActiveSceneView?.Frame(new Bounds(collider.bounds.center, collider.bounds.size + new Vector3(4, 4, 1)), true);
            status = "READ ONLY collision inspection\n" + PCS_MapVisualNov04Editor.FullPath(collider.transform) + "\nSize=" + collider.size + "; offset=" + collider.offset;
        }
        Transform[] Actors()
        {
            var scene = SceneManager.GetActiveScene();
            return PCS_MapVisualNov04Editor.Transforms(scene).Where(t => t.name == "DoorRoot" && t.Find("LeftLeaf") != null ||
                t.name == "PCS_HodCrusher_N02_EDITOR_PREVIEW_ONLY_NO_DAMAGE" || t.name == "AbilityBoard_SLOT" && t.Find("Example") != null ||
                t.name == "PuzzleWall" || t.parent != null && t.parent.name == "PuzzleWall_B_Glass_NOV04").ToArray();
        }
        void StartPreview(Transform source)
        {
            Stop("restart");
            PCS_MapVisualNov04Editor.PreviewStable();
            target = source;
            if (source.name == "DoorRoot") clip = PCS_MapVisualNov04Editor.DoorClip(doorPoses[clipIndex]);
            else if (source.name.StartsWith("PCS_HodCrusher")) clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(PCS_MapVisualNov04Editor.Root + "Animation/Other/Hod_CrusherGuillotine_NOV04.anim");
            else
            {
                var animator = source.GetComponent<Animator>();
                clip = animator != null && animator.runtimeAnimatorController != null ? animator.runtimeAnimatorController.animationClips.FirstOrDefault() : null;
            }
            bool wall = source.name == "PuzzleWall" || source.parent != null && source.parent.name == "PuzzleWall_B_Glass_NOV04";
            PCS_MapVisualNov04Editor.Require(clip != null || wall, "Choose a bound visual Clip");
            proxy = new Nov04VisualProxy(source);
            if (source.name == "PuzzleWall")
                foreach (var renderer in proxy.root.GetComponentsInChildren<SpriteRenderer>(true)) renderer.enabled = true;
            time = 0; playing = clip != null; lastTick = EditorApplication.timeSinceStartup;
            Sample();
            status = "ISOLATED EDITOR PREVIEW\n" + PCS_MapVisualNov04Editor.FullPath(source) + "\n" + AssetDatabase.GetAssetPath(clip);
        }
        void Sample()
        {
            if (proxy == null) return;
            if (clip != null) clip.SampleAnimation(proxy.root, time);
            if (image != null) DestroyImmediate(image);
            image = proxy.Image();
            Repaint();
        }
        void Stop(string reason)
        {
            if (proxy == null) return;
            var previous = proxy;
            proxy = null; playing = false;
            previous.Dispose();
            if (image != null) { DestroyImmediate(image); image = null; }
            PCS_MapVisualNov04Editor.PreviewNote("LIFECYCLE", "STOP_SOURCE_INVARIANT_PASS", reason + "; " + (target == null ? "no target" : PCS_MapVisualNov04Editor.FullPath(target)));
            status = "STOP / original Scene unchanged: " + reason;
            Repaint();
        }
        void Tick()
        {
            if (proxy == null || !playing) return;
            try
            {
                if (EditorApplication.isCompiling) { Stop("compilation started"); return; }
                PCS_MapVisualNov04Editor.PreviewStable();
                double now = EditorApplication.timeSinceStartup;
                if (now - lastTick < .066) return;
                time = Mathf.Repeat(time + (float)(now - lastTick), Mathf.Max(.1f, clip.length));
                lastTick = now; Sample();
            }
            catch (Exception error) { Stop("preview error"); status = error.Message; Debug.LogException(error); }
        }
        void SelectionChanged() { Stop("selection changed"); }
        void BeforeReload() { Stop("script reload"); }
        void PlayChanged(PlayModeStateChange state) { Stop("Play mode transition"); }
        void OnEnable()
        {
            Selection.selectionChanged += SelectionChanged;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
            EditorApplication.playModeStateChanged += PlayChanged;
            EditorApplication.update += Tick;
        }
        void OnGUI()
        {
            EditorGUILayout.HelpBox("NOV04 approved slots. No legacy Builder. Preview and Runtime are separate.", MessageType.Info);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply pending approved stages")) Execute(PCS_MapVisualNov04Editor.ApplyApproved);
            if (GUILayout.Button("Import generated background")) Execute(PCS_MapVisualNov04Editor.ImportBackground);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            for (int i = 0; i < 4; i++)
            {
                int index = i;
                if (GUILayout.Button("View MAP0" + i)) Execute(() => View(index));
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("View four start rooms")) Execute(IntroView);
            if (GUILayout.Button("Inspect approved collider")) Execute(InspectCollider);
            if (GUILayout.Button("Camera 16:9")) Execute(() => Probe(16f / 9));
            if (GUILayout.Button("21:9")) Execute(() => Probe(21f / 9));
            if (GUILayout.Button("4:3")) Execute(() => Probe(4f / 3));
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Verify saved files (no rebuild)")) Execute(PCS_MapVisualNov04Editor.VerifySaved);
            if (GUILayout.Button("Run proxy sampling QA")) Execute(() => { Stop("QA start"); PCS_MapVisualNov04Editor.PreviewQA(); });
            EditorGUILayout.EndHorizontal();
            var actors = Actors();
            if (actors.Length > 0)
            {
                actorIndex = Mathf.Clamp(actorIndex, 0, actors.Length - 1);
                int selected = EditorGUILayout.Popup("Visual target", actorIndex, actors.Select(t => PCS_MapVisualNov04Editor.FullPath(t).Replace("/", " > ") + " [x=" + t.position.x.ToString("0.##") + "]").ToArray());
                int pose = EditorGUILayout.Popup("Door pose", clipIndex, doorPoses);
                if (selected != actorIndex || pose != clipIndex) Stop("target or Clip changed");
                actorIndex = selected; clipIndex = pose;
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Preview PLAY (proxy only)")) Execute(() => StartPreview(actors[actorIndex]));
                if (GUILayout.Button("Pause")) playing = false;
                if (GUILayout.Button("STOP / restore")) Execute(() => Stop("STOP button"));
                EditorGUILayout.EndHorizontal();
                if (proxy != null && clip != null)
                {
                    float sample = EditorGUILayout.Slider("Sample seconds", time, 0, clip.length);
                    if (!Mathf.Approximately(sample, time)) { playing = false; time = sample; Execute(Sample); }
                }
            }
            EditorGUILayout.SelectableLabel(status, EditorStyles.wordWrappedLabel, GUILayout.Height(80));
            scroll = EditorGUILayout.BeginScrollView(scroll);
            if (image != null)
            {
                float width = position.width - 30;
                float height = Mathf.Min(width * image.height / image.width, Mathf.Max(180, position.height - 335));
                var rect = GUILayoutUtility.GetRect(width, height);
                GUI.DrawTexture(rect, image, ScaleMode.ScaleToFit);
            }
            EditorGUILayout.EndScrollView();
        }
        void OnDisable()
        {
            Stop("window closed / disabled");
            Selection.selectionChanged -= SelectionChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= BeforeReload;
            EditorApplication.playModeStateChanged -= PlayChanged;
            EditorApplication.update -= Tick;
            if (image != null) DestroyImmediate(image);
        }
    }

}


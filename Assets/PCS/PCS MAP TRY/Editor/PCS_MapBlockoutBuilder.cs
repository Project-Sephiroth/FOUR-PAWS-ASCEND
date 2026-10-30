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

namespace PCS.MapPlanning.EditorTools
{
    // Explicit Edit-mode commands only. No automatic callbacks or runtime components.
    public static class PCS_MapBlockoutBuilder
    {
        private const string Project = @"C:\FOUR-PAWS-ASCEND-NEWPCS";
        private const string Package = @"C:\FOUR-PAWS-PCS-BRIEF\CPLAN_FINAL_20261029";
        private const string Run = Package + @"\CIMPL_RUNS\run-001";
        private const string MapFolder = "Assets/PCS/PCS MAP TRY";
        private const string Builder = MapFolder + "/Editor/PCS_MapBlockoutBuilder.cs";
        private const string LitGuid = "a97c105638bdf8b4a8650670310a4cd3";
        private static readonly string[] SceneNames = { "PCS_MAP00_Malkuth_Blockout", "PCS_MAP01_Yesod_Blockout", "PCS_MAP02_Hod_Blockout", "PCS_MAP03_Netzach_Blockout" };
        private static readonly string[] Groups = { "VIEW", "ENVIRONMENT", "TERRAIN", "PLAYER_SPAWN_MARKERS", "ELEVATOR_ENTRY_VISUAL", "PUZZLE_AREAS", "ELEVATOR_EXIT_VISUAL", "PROPOSED_PLACEHOLDERS", "TEAM_INTEGRATION_REQUIRED" };
        private static readonly float[] EntryX = { -10, 0, -4, -8 };
        private static readonly float[] ExitX = { 62, 78, -12, 0 };
        private static readonly Color Blue = new Color(.30f, .65f, 1f);
        private static readonly Color Green = new Color(.48f, 1f, .25f);
        private static readonly Color Red = new Color(1f, .26f, .23f);
        private static readonly Color Cyan = new Color(.18f, 1f, 1f);
        private static readonly Color Yellow = new Color(1f, .90f, .24f);
        private static readonly Color Purple = new Color(.9f, .28f, 1f);
        private static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();
        private static Material material;

        [Serializable] public class FileRecord { public string path; public string sha256; public long bytes; public string guid; }
        [Serializable] public class MetadataRecord { public string path; public long bytes; public string modifiedNs; }
        [Serializable] public class Baseline { public FileRecord[] files; public MetadataRecord[] metadataOnly; }
        [Serializable] public class Phase { public string name; public string status; public string startedAt; public string finishedAt; public string detail; }
        [Serializable] public class State
        {
            public int schemaVersion;
            public string packageVersion, runId, projectRoot, approvalEvidence, branch, head, baselineGitStatus;
            public FileRecord[] packageHashes;
            public List<FileRecord> createdFiles = new List<FileRecord>();
            public List<Phase> phases = new List<Phase>();
            public string[] ownedSceneGroups, sourceDeviations, pendingIssues;
            public string lastCompletedPhase, pendingSavePath, pendingSavePreviousHash, updatedAt;
        }
        [Serializable] public class SceneReport
        {
            public string path, guid, sha256, checkedAt;
            public int gameObjects, activeRenderers, activeColliders, prefabInstances, missingScripts;
            public string[] hierarchy, componentTypes;
            public bool reopened, detailed, passed;
        }
        [Serializable] public class QA { public string runId = "run-001", status = "IN_PROGRESS"; public List<SceneReport> scenes = new List<SceneReport>(); public string[] captures = new string[0]; }
        [Serializable] public class Inventory { public string runId = "run-001"; public List<FileRecord> files; }

        private static string ScenePath(int map) { return MapFolder + "/Scenes/" + SceneNames[map] + ".unity"; }
        private static string Now() { return DateTimeOffset.Now.ToString("o"); }
        private static string Hash(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException("C-IMPL GUARD: " + message); }
        private static State ReadState() { return JsonUtility.FromJson<State>(File.ReadAllText(Run + @"\CIMPL_STATE.json")); }
        private static void WriteState(State state)
        {
            state.updatedAt = Now();
            string temporary = Run + @"\CIMPL_STATE.json.tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(state, true), new UTF8Encoding(false));
            File.Replace(temporary, Run + @"\CIMPL_STATE.json", null);
        }
        private static void InventoryUpdate(State state)
        {
            foreach (var file in state.createdFiles)
            {
                string absolute = Path.Combine(Project, file.path);
                if (!File.Exists(absolute)) continue;
                file.sha256 = Hash(absolute); file.bytes = new FileInfo(absolute).Length;
                string asset = file.path.EndsWith(".meta", StringComparison.Ordinal) ? file.path.Substring(0, file.path.Length - 5) : file.path;
                file.guid = AssetDatabase.AssetPathToGUID(asset);
            }
            WriteState(state);
            File.WriteAllText(Run + @"\CREATED_ASSETS.json", JsonUtility.ToJson(new Inventory { files = state.createdFiles.Where(f => !string.IsNullOrEmpty(f.sha256)).ToList() }, true), new UTF8Encoding(false));
        }
        private static void Guard()
        {
            Require(string.Equals(Path.GetFullPath(Application.dataPath + "/..").TrimEnd('\\', '/'), Project, StringComparison.OrdinalIgnoreCase), "Wrong project root");
            Require(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling, "Editor must be in stable Edit mode");
            var state = ReadState();
            Require(state.runId == "run-001" && state.packageVersion == "CPLAN-03-v1.0" && state.projectRoot == Project, "Run identity mismatch");
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                var roots = scene.GetRootGameObjects();
                bool ownInterruptedDraft = state.phases.Single(p => p.name == "C01").status == "IN_PROGRESS" && string.IsNullOrEmpty(scene.path) && roots.Length == 1 && state.ownedSceneGroups.Contains(roots[0].name);
                Require(!scene.isDirty || ownInterruptedDraft, "Unsaved user scene: " + scene.path);
            }
            foreach (var file in state.packageHashes) Require(File.Exists(file.path) && Hash(file.path) == file.sha256, "Plan package changed: " + file.path);
            foreach (var file in state.createdFiles)
            {
                string absolute = Path.Combine(Project, file.path);
                if (!string.IsNullOrEmpty(file.sha256)) Require(File.Exists(absolute) && Hash(absolute) == file.sha256, "Owned output changed/missing: " + file.path);
                else Require(!File.Exists(absolute) || file.path.EndsWith(".meta", StringComparison.Ordinal), "Unregistered existing output: " + file.path);
            }
            CheckProtection();
        }
        private static void CheckProtection()
        {
            var baseline = JsonUtility.FromJson<Baseline>(File.ReadAllText(Run + @"\PROTECTED_BASELINE.json"));
            foreach (var file in baseline.files) Require(File.Exists(file.path) && Hash(file.path) == file.sha256, "Protected file changed/missing: " + file.path);
            var known = new HashSet<string>(baseline.files.Select(f => Path.GetFullPath(f.path)), StringComparer.OrdinalIgnoreCase);
            var owned = new HashSet<string>(ReadState().createdFiles.Select(f => Path.GetFullPath(Path.Combine(Project, f.path))), StringComparer.OrdinalIgnoreCase);
            foreach (string folder in new[] { "Assets", "Packages", "ProjectSettings" })
                foreach (string path in Directory.GetFiles(Path.Combine(Project, folder), "*", SearchOption.AllDirectories))
                    Require(known.Contains(Path.GetFullPath(path)) || owned.Contains(Path.GetFullPath(path)), "Unexpected source file: " + path);
            string cursor = Path.Combine(Project, MapFolder);
            while (!string.IsNullOrEmpty(cursor))
            {
                Require((File.GetAttributes(cursor) & FileAttributes.ReparsePoint) == 0, "Reparse path");
                if (string.Equals(cursor, Project, StringComparison.OrdinalIgnoreCase)) break;
                cursor = Path.GetDirectoryName(cursor);
            }
        }
        private static bool Begin(string phase)
        {
            Guard(); var state = ReadState(); var entry = state.phases.Single(p => p.name == phase);
            if (entry.status == "PASS") { Debug.Log("CIMPL " + phase + " already complete; hashes verified; skipped."); return false; }
            int number = int.Parse(phase.Substring(1));
            Require(state.phases.Single(p => p.name == "C" + (number - 1).ToString("00")).status == "PASS", "Previous checkpoint not complete");
            entry.status = "IN_PROGRESS"; entry.startedAt = Now(); entry.detail = "Explicit Editor menu command started"; WriteState(state);
            InventoryUpdate(state); LoadReferences(); return true;
        }
        private static void Complete(string phase)
        {
            CheckProtection(); var state = ReadState(); InventoryUpdate(state);
            var entry = state.phases.Single(p => p.name == phase); entry.status = "PASS"; entry.finishedAt = Now();
            entry.detail = "SaveScene true; generated scene reopened; structural/reference checks and protected hashes passed. Visual QA is separate.";
            state.lastCompletedPhase = phase; state.ownedSceneGroups = SceneNames.Select(n => n + "/{" + string.Join(",", Groups) + "}").ToArray(); WriteState(state);
            string line = Now() + " CIMPL " + phase + " PASS (Edit data only)";
            File.AppendAllText(Run + @"\UnityEditor.log", line + Environment.NewLine);
            File.AppendAllText(Run + @"\UNITY_LOG_HISTORY.md", "\n- " + line + "\n"); Debug.Log(line);
        }
        private static void LoadReferences()
        {
            Sprites.Clear();
            LoadSprite("Assets/Resources/img/BG/stage1-1.png", "e2c77b115d7fe39458f6a1c13b272c86", "stage1-1_0");
            foreach (var name in new[] { "Tiles_0", "Tiles_3", "Tiles_4", "Tiles_5" }) LoadSprite("Assets/Resources/img/Tiles/Tiles.png", "9cac4b34d8bf0f742bfbbf52ab59ea89", name);
            foreach (var name in new[] { "objects_4", "objects_7" }) LoadSprite("Assets/Resources/img/object/objects.png", "31841eeb4c020f24cbdf27597590d5f9", name);
            LoadSprite("Assets/Resources/img/object/Switch-Sheet.png", "a36f828a8a278c449bcdd1bc4b81d68a", "Switch-Sheet_0");
            material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(LitGuid)); Require(material != null, "Existing Sprite-Lit material unavailable");
        }
        private static void LoadSprite(string path, string guid, string name)
        {
            Require(AssetDatabase.AssetPathToGUID(path) == guid, "Sprite texture GUID mismatch: " + path);
            var sprite = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().SingleOrDefault(s => s.name == name);
            Require(sprite != null, "Missing named sub-Sprite: " + name); Sprites.Add(name, sprite);
        }
        private static Transform Node(Transform parent, string path, Vector3 position = default(Vector3))
        {
            Transform current = parent;
            foreach (var part in path.Split('/'))
            {
                Transform child = current.Find(part);
                if (child == null) { child = new GameObject(part).transform; child.SetParent(current, false); }
                current = child;
            }
            current.localPosition = position; current.localRotation = Quaternion.identity; current.localScale = Vector3.one; return current;
        }
        private static SpriteRenderer Picture(Transform parent, string name, string spriteName, float x, float y, float width, float height, Color tint, int order = 1, bool tiled = false, float z = 0)
        {
            var node = Node(parent, name, new Vector3(x, y, z)); var renderer = node.GetComponent<SpriteRenderer>();
            if (renderer == null) renderer = node.gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = Sprites[spriteName]; renderer.sharedMaterial = material; renderer.color = tint; renderer.sortingLayerID = 0; renderer.sortingOrder = order;
            renderer.drawMode = tiled ? SpriteDrawMode.Tiled : SpriteDrawMode.Simple;
            if (tiled)
            {
                float sourceHeight = renderer.sprite.bounds.size.y;
                renderer.size = new Vector2(width, sourceHeight);
                node.localScale = new Vector3(1, height / sourceHeight, 1);
            }
            else node.localScale = new Vector3(width / renderer.sprite.bounds.size.x, height / renderer.sprite.bounds.size.y, 1);
            return renderer;
        }
        private static void Box(Transform parent, string name, float x, float y, float width, float height)
        {
            var node = Node(parent, name, new Vector3(x, y, 0)); node.gameObject.layer = 6;
            var box = node.GetComponent<BoxCollider2D>(); if (box == null) box = node.gameObject.AddComponent<BoxCollider2D>();
            box.size = new Vector2(width, height); box.offset = Vector2.zero; box.isTrigger = false; box.enabled = true;
        }
        private static Transform Block(Transform parent, string name, float x, float y, float width, float height, Color tint, bool collision = false, int order = 1, string sprite = "Tiles_3")
        {
            var node = Node(parent, name, new Vector3(x, y, 0)); Picture(node, "Visual", sprite, 0, 0, width, height, tint, order);
            if (collision) Box(node, "Collider_Layout", 0, 0, width, height); return node;
        }
        private static Transform Platform(Transform parent, string name, float centerX, float top, float width, bool collision = true, float artHeight = .55f)
        {
            var node = Node(parent, name, new Vector3(centerX, top, 0));
            Picture(node, "Visual", width >= 1.7f ? "Tiles_0" : "Tiles_3", 0, -artHeight / 2, width, artHeight, Color.white, -1, width >= 1.7f);
            if (collision) Box(node, "Collider_Layout", 0, -.2f, width, .4f); Node(node, "FloorTop"); return node;
        }
        private static void Background(Transform parent, float x, float y, float scale)
        {
            var sprite = Sprites["stage1-1_0"]; Picture(parent, "BACKGROUND", "stage1-1_0", x, y, sprite.bounds.size.x * scale, sprite.bounds.size.y * scale, Color.white, -2, false, 10);
        }
        private static void Bay(Transform node, bool collision, bool arrival)
        {
            Node(node, "Anchor_FloorTop");
            Picture(node, "Deck_Visual", "Tiles_0", 0, -.671875f, 6, 1.34375f, Color.white, -1, true);
            if (collision) Box(node, "DeckCollider_Layout", 0, -.2f, 6, .4f);
            Picture(node, "SupportColumn_Visual", "Tiles_5", 0, -2, 1.7f, 3, Color.white, -1);
            Picture(node, "LeftRail_Visual", "Tiles_4", -2.55f, 1.7f, .9f, 3.4f, Color.white, 2);
            Picture(node, "RightRail_Visual", "Tiles_4", 2.55f, 1.7f, .9f, 3.4f, Color.white, 2);
            Picture(node, "Header_Visual", "Tiles_0", 0, 3.5f, 6, .4f, Color.white, 2, true);
            Picture(node, "ShaftBack_Visual", "Tiles_3", 0, 1.7f, 5, 3.4f, new Color(.32f, .39f, .47f), -1);
            Node(node, arrival ? "Arrival_Marker" : "Departure_Marker", new Vector3(0, .05f, 0));
        }
        private static Transform Root(Scene scene, int map)
        {
            var roots = scene.GetRootGameObjects(); Require(roots.Length == 1 && roots[0].name == SceneNames[map], "Unexpected scene root"); return roots[0].transform;
        }
        private static void Save(Scene scene, int map)
        {
            string path = ScenePath(map); Require(scene.path == path || string.IsNullOrEmpty(scene.path), "Unexpected save source");
            var state = ReadState(); var record = state.createdFiles.Single(f => f.path == path);
            Require(!File.Exists(path) || (!string.IsNullOrEmpty(record.sha256) && Hash(path) == record.sha256), "Save ownership/hash conflict");
            state.pendingSavePath = path; state.pendingSavePreviousHash = File.Exists(path) ? Hash(path) : "NEW"; WriteState(state);
            Require(EditorSceneManager.SaveScene(scene, path, false), "SaveScene returned false: " + path);
            Require(File.Exists(path), "Saved scene missing"); InventoryUpdate(ReadState());
            state = ReadState(); state.pendingSavePath = ""; state.pendingSavePreviousHash = ""; WriteState(state);
        }
        private static void ReopenCheck(int map, bool detailed)
        {
            var scene = EditorSceneManager.OpenScene(ScenePath(map), OpenSceneMode.Additive);
            try { Validate(scene, map, detailed); } finally { EditorSceneManager.CloseScene(scene, true); }
        }
        private static void Backup(string phase, int map)
        {
            string sceneCopy = Run + @"\backups\" + phase + "_MAP" + map.ToString("00") + ".before.unity";
            string helperCopy = Run + @"\backups\" + phase + "_Builder.before.cs";
            if (!File.Exists(sceneCopy)) File.Copy(ScenePath(map), sceneCopy, false);
            if (!File.Exists(helperCopy)) File.Copy(Builder, helperCopy, false);
        }

        [MenuItem("Tools/C-IMPL/C01 Create common scenes", false, 101)]
        public static void CreateCommonScenes()
        {
            if (!Begin("C01")) return;
            if (!AssetDatabase.IsValidFolder(MapFolder + "/Scenes")) Require(!string.IsNullOrEmpty(AssetDatabase.CreateFolder(MapFolder, "Scenes")), "Scene folder creation failed");
            InventoryUpdate(ReadState()); var previous = SceneManager.GetActiveScene();
            for (int map = 0; map < 4; map++)
            {
                if (File.Exists(ScenePath(map))) { ReopenCheck(map, false); continue; }
                Scene scene = default(Scene); Transform root = null;
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    var candidate = SceneManager.GetSceneAt(i); var roots = candidate.GetRootGameObjects();
                    if (string.IsNullOrEmpty(candidate.path) && roots.Length == 1 && roots[0].name == SceneNames[map]) { scene = candidate; root = roots[0].transform; break; }
                }
                if (root == null) { scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive); SceneManager.SetActiveScene(scene); root = new GameObject(SceneNames[map]).transform; }
                SceneManager.SetActiveScene(scene);
                var ownership = ReadState(); ownership.ownedSceneGroups = ownership.ownedSceneGroups.Concat(new[] { SceneNames[map] }).Distinct().ToArray(); WriteState(ownership);
                foreach (var group in Groups) Node(root, group);
                Node(root, "ENVIRONMENT/BAY_BACKGROUNDS"); Node(root, "ENVIRONMENT/DECORATION");
                Node(root, "TERRAIN/ENTRY_EXIT_GROUND"); Node(root, "TERRAIN/WORLD_BOUNDARY_CANDIDATES");
                var cameraNode = Node(root, "VIEW/MainCamera_Blockout", new Vector3(EntryX[map], 3, -10));
                var camera = cameraNode.GetComponent<Camera>(); if (camera == null) camera = cameraNode.gameObject.AddComponent<Camera>(); camera.orthographic = true; camera.orthographicSize = 5; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.035f, .055f, .075f); camera.nearClipPlane = .1f; camera.farClipPlane = 100;
                var cameraData = cameraNode.GetComponent<UniversalAdditionalCameraData>(); if (cameraData == null) cameraData = cameraNode.gameObject.AddComponent<UniversalAdditionalCameraData>(); cameraData.renderPostProcessing = false; cameraData.renderShadows = false; cameraData.SetRenderer(-1);
                var lightNode = Node(root, "VIEW/GlobalLight2D"); var light = lightNode.GetComponent<Light2D>(); if (light == null) light = lightNode.gameObject.AddComponent<Light2D>();
                // The user's clean scene stays loaded during C01. Activate each new global light only after opening its own scene singly in C02-C05.
                light.enabled = false; light.lightType = Light2D.LightType.Global; light.intensity = 1; light.color = Color.white;
                Bay(Node(root, "ELEVATOR_ENTRY_VISUAL", new Vector3(EntryX[map], 0, 0)), true, true);
                Background(Node(root, "ENVIRONMENT/BAY_BACKGROUNDS/Entry"), EntryX[map], 2.6f, .22f);
                if (map < 3)
                {
                    Bay(Node(root, "ELEVATOR_EXIT_VISUAL", new Vector3(ExitX[map], 0, 0)), true, false);
                    Background(Node(root, "ENVIRONMENT/BAY_BACKGROUNDS/Exit"), ExitX[map], 2.6f, .22f);
                }
                else
                {
                    Node(root, "ELEVATOR_EXIT_VISUAL/ExitAnchor_PENDING");
                    var reference = Node(root, "ELEVATOR_EXIT_VISUAL/VisualReference_INACTIVE"); Bay(reference, false, false); reference.gameObject.SetActive(false);
                }
                string[] characters = { "Bear", "Rabbit", "Mouse", "Frog" };
                for (int i = 0; i < 4; i++) Node(root, "PLAYER_SPAWN_MARKERS/" + characters[i], new Vector3(EntryX[map] - 1.8f + i * 1.2f, .05f, 0));
                Node(root, "PROPOSED_PLACEHOLDERS/COORDINATES_PROPOSED_NOT_PLAY_TESTED"); Node(root, "TEAM_INTEGRATION_REQUIRED/NO_RUNTIME_CONNECTIONS");
                if (map == 0)
                {
                    Area(root, "RABBIT", 0, 0, 18, .4f); Area(root, "RAT", 24, 0, 18, .4f); Area(root, "FROG", 0, -14, 18, .4f); Area(root, "BEAR", 24, -14, 18, .4f); Area(root, "REUNION", 50, 0, 8, .22f);
                    Node(root, "PROPOSED_PLACEHOLDERS/ROOM_CONNECTIONS_PENDING_NO_CORRIDORS");
                }
                if (map == 1)
                {
                    Area(root, "PZ01", 8, 0, 26, .5f); Area(root, "PZ02", 44, 0, 26, .5f); Node(root, "PROPOSED_PLACEHOLDERS/BETWEEN_PUZZLES_CONNECTION_PENDING");
                }
                if (map == 2) { Area(root, "ThreatLane", 0, 0, 28, .6f); Background(Node(root, "PUZZLE_AREAS"), 14, 3, .6f); }
                if (map == 3) Area(root, "PuzzleChamber", 0, 0, 18, .4f);
                Save(scene, map); EditorSceneManager.CloseScene(scene, true); ReopenCheck(map, false);
            }
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous); Complete("C01");
        }
        private static Transform Area(Transform root, string name, float x, float y, float width, float scale)
        {
            var area = Node(root, "PUZZLE_AREAS/" + name, new Vector3(x, y, 0));
            foreach (var group in new[] { "TERRAIN", "PLATFORMS", "DEVICE_VISUALS", "TEAM_MARKERS" }) Node(area, group);
            Background(area, width / 2, width == 26 ? 5 : 4.5f, scale);
            Platform(Node(area, "C01_DRAFT"), "Ground_DRAFT", width / 2, 0, width, true, 1.34375f);
            Platform(Node(area, "C01_DRAFT"), "Platform_DRAFT", width / 2, 2, 3);
            Node(area, "TEAM_MARKERS/DEVICE_SLOT_PENDING"); return area;
        }
        private static void Retire(Transform area) { var draft = area.Find("C01_DRAFT"); if (draft != null) draft.gameObject.SetActive(false); }
        private static void Room(Transform area, bool gap = false)
        {
            Retire(area); var terrain = Node(area, "TERRAIN");
            if (gap) { Platform(terrain, "Ground_Left", 2, 0, 4, true, 1.34375f); Platform(terrain, "Ground_Right", 12.5f, 0, 11, true, 1.34375f); }
            else Platform(terrain, "Ground", 9, 0, 18, true, 1.34375f);
            Block(terrain, "Ceiling", 9, 8.75f, 18, .5f, Color.white, true, -1);
            Block(terrain, "LeftWall", -.25f, 4.25f, .5f, 8.5f, Color.white, true, -1);
            Block(terrain, "RightWall", 18.25f, 4.25f, .5f, 8.5f, Color.white, true, -1);
            Block(Node(area, "DEVICE_VISUALS"), "AbilityBoard_SLOT", 2.7f, 5.4f, 4, 1.6f, new Color(.5f, .85f, .55f));
            Node(area, "TEAM_MARKERS/Start_PROPOSED", new Vector3(1, .05f, 0)); Node(area, "TEAM_MARKERS/Exit_PENDING", new Vector3(16, 3.25f, 0));
        }
        private static void Detailed(int map, string phase, Action<Transform> build)
        {
            if (!Begin(phase)) return; Backup(phase, map);
            var scene = EditorSceneManager.OpenScene(ScenePath(map), OpenSceneMode.Single); SceneManager.SetActiveScene(scene);
            var root = Root(scene, map); root.Find("VIEW/GlobalLight2D").GetComponent<Light2D>().enabled = true;
            build(root); Save(scene, map);
            scene = EditorSceneManager.OpenScene(ScenePath(map), OpenSceneMode.Single); Validate(scene, map, true); Complete(phase);
        }
        [MenuItem("Tools/C-IMPL/C02 Build MAP00 Malkuth", false, 102)] public static void BuildMap00() { Detailed(0, "C02", Map00); }
        [MenuItem("Tools/C-IMPL/C03 Build MAP01 Yesod", false, 103)] public static void BuildMap01() { Detailed(1, "C03", Map01); }
        [MenuItem("Tools/C-IMPL/C04 Build MAP02 Hod", false, 104)] public static void BuildMap02() { Detailed(2, "C04", Map02); }
        [MenuItem("Tools/C-IMPL/C05 Build MAP03 Netzach", false, 105)] public static void BuildMap03() { Detailed(3, "C05", Map03); }
        private static void Map00(Transform root)
        {
            var rabbit = root.Find("PUZZLE_AREAS/RABBIT"); Room(rabbit);
            var terrain = Node(rabbit, "TERRAIN"); var platforms = Node(rabbit, "PLATFORMS"); var devices = Node(rabbit, "DEVICE_VISUALS");
            Block(terrain, "LowStep_PROPOSED", 4.4f, .8f, 1, 1.6f, Color.white, true, -1);
            Platform(platforms, "Ledge_01_PROPOSED", 7, 1, 2); Platform(platforms, "Ledge_02_PROPOSED", 11, 2, 2);
            Block(terrain, "ExitFoundation", 16, 1.6f, 4, 3.2f, Color.white, true, -1);
            Block(devices, "CarryAlly_SLOT", 1.5f, .65f, .75f, .9f, Yellow); Block(devices, "ExitGate_VISUAL_ONLY", 16, 4.7f, .8f, 3, Red);
            var rat = root.Find("PUZZLE_AREAS/RAT"); Room(rat); terrain = Node(rat, "TERRAIN"); devices = Node(rat, "DEVICE_VISUALS");
            float[] partitions = { 5, 9, 13 };
            for (int i = 0; i < 3; i++)
            {
                Block(terrain, "HangingPartition_" + (i + 1), partitions[i], i == 1 ? 7.1f : 5.1f, .8f, i == 1 ? 2.8f : 6.8f, Color.white, true, -1);
                Block(devices, "RedGate_" + (i + 1) + "_VISUAL_ONLY", partitions[i], 1.1f, .8f, 2.2f, Red);
                Picture(devices, "HackTerminal_" + (i + 1).ToString("00"), "objects_7", 3.8f + 4 * i, .45f, .8f, .8f, Color.white);
            }
            Block(terrain, "CentralPartition_Lower", 9, 3.3f, .8f, 2.2f, Color.white, true, -1);
            Node(rat, "TEAM_MARKERS/Ladder_REFERENCE_PENDING", new Vector3(8.4f, 3.5f, 0)); Node(rat, "TEAM_MARKERS/PassageHeight_PENDING", new Vector3(9, 5.1f, 0));
            var frog = root.Find("PUZZLE_AREAS/FROG"); Room(frog); terrain = Node(frog, "TERRAIN"); devices = Node(frog, "DEVICE_VISUALS");
            Platform(Node(frog, "PLATFORMS"), "AirPlatform", 6, 2.45f, 2.4f); Block(terrain, "ExitFoundation", 16, 1.6f, 4, 3.2f, Color.white, true, -1);
            Block(Node(frog, "TargetSlots"), "YellowTarget_01", 6, 3, .8f, .8f, Yellow); Block(Node(frog, "TargetSlots"), "YellowTarget_02", 9, .5f, .8f, .8f, Yellow);
            Block(devices, "ExitGate_VISUAL_ONLY", 16, 4.7f, .8f, 3, Red); Block(devices, "TargetCollectionPad_VISUAL_ONLY", 14, .1f, 2, .2f, Red);
            var bear = root.Find("PUZZLE_AREAS/BEAR"); Room(bear, true); devices = Node(bear, "DEVICE_VISUALS");
            Block(devices, "MovableCube_VISUAL_ONLY", 3, .5f, 1, 1, Red); Node(bear, "TEAM_MARKERS/Gap_x4_to_7_NO_CONTINUOUS_COLLIDER", new Vector3(5.5f, 0, 0));
            Block(devices, "BlueGapFillPad_VISUAL_ONLY", 5.5f, -.15f, 3, .3f, Blue); Block(devices, "CeilingThrowTarget_SLOT", 2.5f, 6.8f, .8f, .8f, Yellow);
            Block(devices, "CeilingButton_01_VISUAL_ONLY", 8, 7.7f, .35f, .7f, Blue); Block(devices, "CeilingButton_02_VISUAL_ONLY", 11, 7.7f, .35f, .7f, Blue);
            Block(devices, "RightFloorActuator_VISUAL_ONLY", 14, .1f, 2, .2f, Red); Block(Node(bear, "TERRAIN"), "ExitFoundation", 16, 1.6f, 4, 3.2f, Color.white, true, -1);
            Block(devices, "ExitGate_VISUAL_ONLY", 16, 4.7f, .8f, 3, Red);
            var reunion = root.Find("PUZZLE_AREAS/REUNION"); Retire(reunion); Platform(Node(reunion, "TERRAIN"), "ReunionFloor_PROPOSED", 4, 0, 8, true, 1.34375f); Node(reunion, "TEAM_MARKERS/CONNECTIONS_PENDING");
        }
        private static void Map01(Transform root)
        {
            var a = root.Find("PUZZLE_AREAS/PZ01"); Retire(a); Node(a, "TEAM_MARKERS/SOURCE_B_PROPOSED_MAPPING_NOT_FINAL_NUMBER");
            var terrain = Node(a, "TERRAIN"); var platforms = Node(a, "PLATFORMS"); Platform(terrain, "Floor_1F", 13, 0, 26, true, 1.34375f);
            foreach (float x in new[] { 4f, 11.5f, 18.5f }) Platform(platforms, "Segment_2F_x" + x.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture), x, 3.2f, 5);
            Platform(platforms, "UpperLeft_3F", 4.5f, 6.4f, 6); Platform(platforms, "UpperMiddle_3F", 13.5f, 6.4f, 6); Platform(platforms, "UpperConsole_3F", 23, 6.4f, 6);
            for (int i = 0; i < 3; i++)
            {
                Picture(Node(a, "SwitchSlots"), "Switch_102" + (char)('A' + i) + "_VISUAL_ONLY", "Switch-Sheet_0", 6 + 7 * i, 1.8f, .7f, .7f, Color.white, 2);
                Block(Node(a, "PartitionSlots"), "Partition_103" + (char)('A' + i) + "_VISUAL_ONLY", 8 + 7 * i, 1.6f, .6f, 3.2f, Red);
            }
            Picture(Node(a, "TEAM_MARKERS"), "ThrowStone_101", "objects_4", 2, .5f, .5f, .5f, Color.white);
            Picture(Node(a, "Hack_Battery"), "UpperTerminal_109_VISUAL_ONLY", "objects_7", 22, 6.9f, .8f, .8f, Color.white);
            Node(a, "Hack_Battery/Battery_112_SLOT", new Vector3(24, 6.5f, 0));
            InternalLift(Node(a, "InternalLift", new Vector3(24, 0, 0)));
            Node(a, "TEAM_MARKERS/MiddleEnemy_SLOT", new Vector3(11.5f, 3.7f, 0)); Node(a, "TEAM_MARKERS/Laser_SLOT", new Vector3(13, 5, 0)); Node(a, "TEAM_MARKERS/FrogGrabEnemy_SLOT", new Vector3(17, 3.7f, 0));
            var b = root.Find("PUZZLE_AREAS/PZ02"); Retire(b); Node(b, "TEAM_MARKERS/SOURCE_A_PROPOSED_MAPPING_NOT_FINAL_NUMBER"); terrain = Node(b, "TERRAIN"); platforms = Node(b, "PLATFORMS");
            Platform(terrain, "Floor_LeftBeforePads", 2.4f, 0, 4.8f, true, 1.34375f); Platform(terrain, "Floor_RightAfterPads", 19.1f, 0, 13.8f, true, 1.34375f);
            for (int i = 0; i < 4; i++) Block(Node(b, "SeparatedPads"), "Pad_203_" + (i + 1) + "_SINKING_UNIMPLEMENTED", 5.5f + 2 * i, -.2f, 1.2f, .4f, new Color(.8f, .85f, 1));
            Platform(platforms, "MiddleLeft_2F", 2, 3.2f, 4); Platform(platforms, "MiddleRight_2F", 18, 3.2f, 9); Platform(platforms, "UpperLeft_3F", 6, 6.4f, 8); Platform(platforms, "UpperRight_3F", 22, 6.4f, 8);
            Node(b, "TEAM_MARKERS/BearControl_SLOT", new Vector3(2, .05f, 0));
            Picture(Node(b, "DEVICE_VISUALS"), "BearButton_A_VISUAL_ONLY", "Switch-Sheet_0", 1, 1, .6f, .6f, Color.white);
            Picture(Node(b, "DEVICE_VISUALS"), "BearButton_B_VISUAL_ONLY", "Switch-Sheet_0", 3, 1, .6f, .6f, Color.white);
            InternalLift(Node(b, "Lift_EnemySlots/CrushLift_VISUAL_ONLY", new Vector3(24, 0, 0)));
            Node(b, "Lift_EnemySlots/Enemy_205_SLOT_A_UNVERIFIED", new Vector3(23.5f, 1, 0)); Node(b, "Lift_EnemySlots/Enemy_205_SLOT_B_UNVERIFIED", new Vector3(25, 1, 0));
            Picture(Node(b, "HackSlots"), "Console_207_VISUAL_ONLY", "objects_7", 16, 3.7f, .8f, .8f, Color.white); Picture(Node(b, "HackSlots"), "Console_209_VISUAL_ONLY", "objects_7", 24, 3.7f, .8f, .8f, Color.white);
            Node(b, "BatteryTarget/Target_212_SLOT", new Vector3(23.5f, 6.5f, 0)); Node(b, "TEAM_MARKERS/RabbitCarryFrog_REFERENCE", new Vector3(17, 6.5f, 0)); Node(b, "TEAM_MARKERS/FrogRemoteRetrieval_REFERENCE", new Vector3(20, 6.5f, 0));
            Node(b, "TEAM_MARKERS/MiddleEnemy_SLOT", new Vector3(18, 3.7f, 0)); Node(b, "TEAM_MARKERS/Laser_SLOT", new Vector3(13, 5, 0)); Node(b, "TEAM_MARKERS/MovingPlatform_SLOT", new Vector3(12, 3.2f, 0));
        }
        private static void InternalLift(Transform node)
        {
            Picture(node, "Column_VISUAL_ONLY", "Tiles_5", 0, 1.6f, 1.2f, 3.2f, Color.white, -1);
            Platform(node, "Deck_VISUAL_ONLY", 0, 0, 3, false); Node(node, "Destination_PENDING", new Vector3(0, 3.2f, 0));
        }
        private static void Map02(Transform root)
        {
            var area = root.Find("PUZZLE_AREAS/ThreatLane"); Retire(area); Platform(Node(area, "TERRAIN"), "LaneGround_x0_to_28", 14, 0, 28, true, 1.34375f);
            var puzzles = root.Find("PUZZLE_AREAS");
            var rat = Node(puzzles, "RatHackZone_Left"); Picture(rat, "HackConsole_VISUAL_ONLY", "objects_7", 1.5f, .8f, 1, 1, Color.white);
            Node(rat, "InterferenceBoundary_PENDING", new Vector3(4, .5f, 0)); Node(rat, "Rat_ROLE", new Vector3(2.5f, .05f, 0));
            Node(puzzles, "BearDefenseZone/ThrowObjectSlot", new Vector3(7, .05f, 0)); Node(puzzles, "BearDefenseZone/DEFENSE_ORDER_PENDING");
            Node(puzzles, "FrogTraversalZone/Hook_01_SLOT", new Vector3(12, 4.5f, 0)); Node(puzzles, "FrogTraversalZone/Hook_02_SLOT", new Vector3(16, 4.5f, 0)); Node(puzzles, "FrogTraversalZone/Lever_TEAM_REVIEW", new Vector3(14, 1.5f, 0));
            Platform(Node(puzzles, "RabbitUpperZone"), "UpperPlatform_PROPOSED", 21, 4.5f, 6); Node(puzzles, "RabbitUpperZone/Press_TEAM_REVIEW", new Vector3(22, 3, 0));
            Node(puzzles, "ThreatOrigin_Slot", new Vector3(27, .5f, 0)); Node(puzzles, "ThreatOrigin_Slot/ALTERNATIVE_208A_OR_208B_NOT_BOTH");
            Node(puzzles, "ThreatLane/Route_START_RIGHT", new Vector3(27, .5f, 0)); Node(puzzles, "ThreatLane/Route_END_LEFT", new Vector3(4, .5f, 0));
            Platform(root.Find("TERRAIN/ENTRY_EXIT_GROUND"), "SafeLeftApproach_PROPOSED", -6.5f, 0, 13, true, 1.34375f);
        }
        private static void Map03(Transform root)
        {
            var chamber = root.Find("PUZZLE_AREAS/PuzzleChamber"); Retire(chamber); Background(chamber, 7.5f, 3, .4f);
            var blue = Node(chamber, "BlueFixedTerrain");
            string[] names = { "B01_LowerFrame", "B02_UpperFrame", "B03_UpperHanging", "B04_LeftShelf", "B05_UpperMiddle", "B06_UpperRight", "B07_CentralVertical", "B08_LeftMiddle", "B09_RightShelf", "B10_LowerLeft" };
            float[,] values = { {7.5f,-.35f,18,.7f}, {7.5f,8.25f,18,1}, {11.25f,7,1.5f,2}, {1,5.25f,4,1.5f}, {8.25f,5.25f,1.5f,1.5f}, {12.75f,5.25f,1.5f,1.5f}, {5.25f,3,1.5f,3}, {2.25f,3.75f,1.5f,1.5f}, {12.75f,2.25f,4.5f,1.5f}, {2.25f,.75f,1.5f,1.5f} };
            for (int i = 0; i < names.Length; i++) Block(blue, names[i], values[i,0], values[i,1], values[i,2], values[i,3], Blue, true, -1);
            float[,] greens = { {6.75f,5.25f}, {9.75f,3.75f}, {3.75f,3.75f}, {6.75f,2.25f}, {9.75f,.75f}, {3.75f,.75f} };
            float[,] reds = { {11.25f,3.75f}, {8.25f,2.25f}, {.75f,2.25f} };
            for (int i = 0; i < 6; i++) Block(Node(chamber, "GreenGroup"), "G" + (i + 1).ToString("00") + "_NEUTRAL_VISUAL", greens[i,0], greens[i,1], 1.5f, 1.5f, Green);
            for (int i = 0; i < 3; i++) Block(Node(chamber, "RedGroup"), "R" + (i + 1).ToString("00") + "_NEUTRAL_VISUAL", reds[i,0], reds[i,1], 1.5f, 1.5f, Red);
            foreach (string direction in new[] { "UP", "DOWN", "LEFT", "RIGHT" }) Node(chamber, "STATE_REFERENCE_MARKERS/" + direction + "_UNBOUND");
            float[] controls = { 1, 3, 13, 15 };
            for (int i = 0; i < controls.Length; i++) Block(Node(root, "PUZZLE_AREAS/LowerControls"), "CyanPad_" + (i + 1) + "_DIRECTION_PENDING", controls[i], -2, 1.4f, .35f, Cyan);
            var consoles = Node(root, "PUZZLE_AREAS/HackingConsoleSlots"); Picture(consoles, "Console_Left_ID_UNBOUND", "objects_7", -.75f, .75f, .8f, .8f, Yellow); Picture(consoles, "Console_Right_ID_UNBOUND", "objects_7", 15, 3.75f, .8f, .8f, Yellow);
            Block(root.Find("PUZZLE_AREAS"), "PurpleGate_Visual", 16.5f, 2.25f, .75f, 1.5f, Purple); Node(root, "PUZZLE_AREAS/RightReturnRoute_Candidate");
            Node(chamber, "STATE_REFERENCE_MARKERS/B_NAMES_NOT_BOUND_TO_SOURCE_007_014");
        }

        private static string HierarchyPath(Transform node) { return node.parent == null ? node.name : HierarchyPath(node.parent) + "/" + node.name; }
        private static void Exact(Transform root, string path, int children) { var node = root.Find(path); Require(node != null && node.childCount == children, path + " count expected " + children); }
        private static void NoFloorAcrossGap(Transform area, float left, float right)
        {
            foreach (var collider in area.GetComponentsInChildren<BoxCollider2D>(false))
            {
                var center = area.InverseTransformPoint(collider.transform.TransformPoint(collider.offset)); var size = collider.size;
                bool floorLevel = center.y - size.y / 2 < .05f && center.y + size.y / 2 >= -.05f;
                Require(!floorLevel || center.x + size.x / 2 <= left + .001f || center.x - size.x / 2 >= right - .001f, "Collider fills planned gap: " + HierarchyPath(collider.transform));
            }
        }
        private static void Validate(Scene scene, int map, bool detailed)
        {
            var root = Root(scene, map); foreach (var group in Groups) Require(root.Find(group) != null, "Missing group " + group);
            Exact(root, "PLAYER_SPAWN_MARKERS", 4); Require(root.Find("VIEW/MainCamera_Blockout").GetComponent<Camera>().orthographic, "Camera must be orthographic");
            var objects = root.GetComponentsInChildren<Transform>(true); int missing = 0, prefabs = 0;
            var permitted = new HashSet<Type> { typeof(Transform), typeof(Camera), typeof(UniversalAdditionalCameraData), typeof(Light2D), typeof(SpriteRenderer), typeof(BoxCollider2D) };
            foreach (var node in objects)
            {
                missing += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(node.gameObject); if (PrefabUtility.IsPartOfPrefabInstance(node)) prefabs++;
                foreach (var component in node.GetComponents<Component>()) Require(component != null && permitted.Contains(component.GetType()), "Forbidden/missing component at " + HierarchyPath(node));
            }
            Require(missing == 0 && prefabs == 0, "Missing script or prefab instance");
            foreach (var renderer in root.GetComponentsInChildren<SpriteRenderer>(true))
            {
                Require(renderer.sprite != null && Sprites.Values.Contains(renderer.sprite), "Unexpected Sprite");
                Require(renderer.sharedMaterial == material && renderer.sortingLayerID == 0 && renderer.sortingOrder >= -2 && renderer.sortingOrder <= 2, "Material/sorting mismatch");
            }
            foreach (var collider in root.GetComponentsInChildren<BoxCollider2D>(true)) Require(collider.gameObject.layer == 6 && !collider.isTrigger && collider.size.x > 0 && collider.size.y > 0 && collider.transform.lossyScale == Vector3.one, "Collider alignment/layer/scale error");
            Require(root.Find("ELEVATOR_ENTRY_VISUAL/DeckCollider_Layout") != null, "Entry deck collider missing");
            if (map < 3) Require(root.Find("ELEVATOR_EXIT_VISUAL/DeckCollider_Layout") != null, "Exit deck collider missing");
            else { var reference = root.Find("ELEVATOR_EXIT_VISUAL/VisualReference_INACTIVE"); Require(!reference.gameObject.activeSelf && reference.GetComponentsInChildren<Collider2D>(true).Length == 0, "Pending exit must be inactive and collider-free"); }
            if (detailed)
            {
                if (map == 0)
                {
                    for (int i = 1; i <= 3; i++) Require(root.Find("PUZZLE_AREAS/RAT/DEVICE_VISUALS/HackTerminal_" + i.ToString("00")) != null, "Rat terminal missing");
                    Require(root.Find("PUZZLE_AREAS/BEAR/DEVICE_VISUALS/RightFloorActuator_VISUAL_ONLY") != null, "Bear actuator missing"); NoFloorAcrossGap(root.Find("PUZZLE_AREAS/BEAR"), 4, 7);
                }
                if (map == 1)
                {
                    Exact(root, "PUZZLE_AREAS/PZ01/SwitchSlots", 3); Exact(root, "PUZZLE_AREAS/PZ01/PartitionSlots", 3); Exact(root, "PUZZLE_AREAS/PZ02/SeparatedPads", 4);
                    var stone = root.Find("PUZZLE_AREAS/PZ01/TEAM_MARKERS/ThrowStone_101"); Require(stone != null && stone.GetComponent<SpriteRenderer>().sprite.name == "objects_4" && stone.GetComponents<Collider2D>().Length == 0, "Stone101 slot error");
                    NoFloorAcrossGap(root.Find("PUZZLE_AREAS/PZ02"), 4.8f, 12.2f); Require(!objects.Any(t => t.name.Contains("301")), "Superseded object generated");
                }
                if (map == 2)
                {
                    Require(root.Find("PUZZLE_AREAS/ThreatLane/Route_START_RIGHT").localPosition.x > root.Find("PUZZLE_AREAS/ThreatLane/Route_END_LEFT").localPosition.x, "Threat direction reversed");
                    Exact(root, "PUZZLE_AREAS/ThreatOrigin_Slot", 1);
                    foreach (string role in new[] { "RatHackZone_Left", "BearDefenseZone", "FrogTraversalZone", "RabbitUpperZone" }) Require(root.Find("PUZZLE_AREAS/" + role) != null, "Missing role area");
                }
                if (map == 3)
                {
                    Exact(root, "PUZZLE_AREAS/PuzzleChamber/BlueFixedTerrain", 10); Exact(root, "PUZZLE_AREAS/PuzzleChamber/GreenGroup", 6); Exact(root, "PUZZLE_AREAS/PuzzleChamber/RedGroup", 3); Exact(root, "PUZZLE_AREAS/LowerControls", 4); Exact(root, "PUZZLE_AREAS/HackingConsoleSlots", 2);
                    Require(root.Find("PUZZLE_AREAS/PuzzleChamber/GreenGroup").GetComponentsInChildren<Collider2D>(true).Length == 0 && root.Find("PUZZLE_AREAS/PuzzleChamber/RedGroup").GetComponentsInChildren<Collider2D>(true).Length == 0, "Moving visuals have colliders");
                }
            }
            var report = new SceneReport { path = ScenePath(map), guid = AssetDatabase.AssetPathToGUID(ScenePath(map)), sha256 = Hash(ScenePath(map)), checkedAt = Now(), gameObjects = objects.Length, activeRenderers = root.GetComponentsInChildren<SpriteRenderer>(false).Length, activeColliders = root.GetComponentsInChildren<BoxCollider2D>(false).Length, prefabInstances = prefabs, missingScripts = missing, hierarchy = objects.Select(HierarchyPath).ToArray(), componentTypes = objects.SelectMany(t => t.GetComponents<Component>()).Select(c => c.GetType().FullName).Distinct().OrderBy(n => n).ToArray(), reopened = true, detailed = detailed, passed = true };
            var qa = JsonUtility.FromJson<QA>(File.ReadAllText(Run + @"\QA_RESULTS.json")); qa.scenes.RemoveAll(r => r.path == report.path); qa.scenes.Add(report);
            File.WriteAllText(Run + @"\QA_RESULTS.json", JsonUtility.ToJson(qa, true), new UTF8Encoding(false));
        }
        [MenuItem("Tools/C-IMPL/C06 Validate read only", false, 106)]
        public static void ValidateReadOnly()
        {
            if (!Begin("C06")) return;
            for (int map = 0; map < 4; map++) { Backup("C06", map); var scene = EditorSceneManager.OpenScene(ScenePath(map), OpenSceneMode.Single); Validate(scene, map, true); }
            CheckProtection(); InventoryUpdate(ReadState());
            var state = ReadState(); var phase = state.phases.Single(p => p.name == "C06"); phase.detail = "Data checks passed; actual UI capture/inspection pending"; WriteState(state);
            Debug.Log("CIMPL C06 DATA_CHECK_PASS; visual inspection still pending.");
        }
        [MenuItem("Tools/C-IMPL/C06 Adjust bay framing", false, 107)]
        public static void AdjustBayFraming()
        {
            Guard(); LoadReferences(); Require(ReadState().phases.Single(p => p.name == "C06").status == "IN_PROGRESS", "C06 backup checkpoint required");
            for (int map = 0; map < 4; map++)
            {
                Backup("C06", map);
                var scene = EditorSceneManager.OpenScene(ScenePath(map), OpenSceneMode.Single); var root = Root(scene, map);
                Background(root.Find("ENVIRONMENT/BAY_BACKGROUNDS/Entry"), EntryX[map], 3, .4f);
                if (map < 3) Background(root.Find("ENVIRONMENT/BAY_BACKGROUNDS/Exit"), ExitX[map], 3, .4f);
                if (map == 2) root.Find("PUZZLE_AREAS/ThreatLane/BACKGROUND").gameObject.SetActive(false);
                foreach (var renderer in root.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    if (renderer.drawMode != SpriteDrawMode.Tiled) continue;
                    float height = renderer.size.y * renderer.transform.localScale.y;
                    float nativeHeight = renderer.sprite.bounds.size.y;
                    renderer.size = new Vector2(renderer.size.x, nativeHeight);
                    renderer.transform.localScale = new Vector3(1, height / nativeHeight, 1);
                }
                Save(scene, map); scene = EditorSceneManager.OpenScene(ScenePath(map), OpenSceneMode.Single); Validate(scene, map, true); CheckProtection();
            }
            var state = ReadState(); state.sourceDeviations = new[] {
                "All 7 placed bay backgrounds enlarged from draft scale0.22/y2.6 to scale0.4/y3 after actual camera observation, covering the entry camera viewport; original Sprite unchanged.",
                "MAP01 upper console center Y6.9 instead of proposed6.5 and two PZ02 console centers Y3.7 instead of3.3, to place 0.8-high artwork above floor tops6.4/3.2. Function/ID unchanged.",
                "MAP02 self-generated duplicate ThreatLane/BACKGROUND disabled in C06; authoritative PUZZLE_AREAS/BACKGROUND at14,3 retained. No objects deleted.",
                "Thin Tiled platform/header artwork uses full native Sprite height and visual-child Y scale to avoid texture cropping; desired visible heights, parent positions and all Collider dimensions retained.",
                "Room envelope, platform widths and other unmeasured details are editable PROPOSED layout, not final gameplay topology." };
            WriteState(state); Debug.Log("CIMPL C06 DISPLAY_ADJUST_PASS; protected hashes unchanged.");
        }
        private static void Inspect(int map, string target, Bounds bounds)
        {
            Guard(); LoadReferences(); var scene = EditorSceneManager.OpenScene(ScenePath(map), OpenSceneMode.Single); var root = Root(scene, map);
            Selection.activeGameObject = string.IsNullOrEmpty(target) ? root.gameObject : root.Find(target).gameObject;
            var view = SceneView.lastActiveSceneView ?? EditorWindow.GetWindow<SceneView>(); view.in2DMode = true; view.sceneLighting = true; view.Frame(bounds, true); view.Repaint();
            Debug.Log("CIMPL INSPECT Edit " + SceneNames[map] + " target=" + target);
        }
        [MenuItem("Tools/C-IMPL/Inspect/1 MAP00 overview")] public static void View00() { Inspect(0, "", new Bounds(new Vector3(26, -2.5f, 0), new Vector3(83, 32, 1))); }
        [MenuItem("Tools/C-IMPL/Inspect/2 MAP01 overview")] public static void View01() { Inspect(1, "", new Bounds(new Vector3(39, 2.5f, 0), new Vector3(87, 16, 1))); }
        [MenuItem("Tools/C-IMPL/Inspect/3 MAP02 overview")] public static void View02() { Inspect(2, "", new Bounds(new Vector3(7, 2, 0), new Vector3(47, 16, 1))); }
        [MenuItem("Tools/C-IMPL/Inspect/4 MAP03 overview")] public static void View03() { Inspect(3, "", new Bounds(new Vector3(4, 3, 0), new Vector3(34, 17, 1))); }
        [MenuItem("Tools/C-IMPL/Inspect/5 MAP00 rat detail")] public static void ViewRat() { Inspect(0, "PUZZLE_AREAS/RAT/DEVICE_VISUALS/HackTerminal_02", new Bounds(new Vector3(33, 3.8f, 0), new Vector3(21, 11, 1))); }
        [MenuItem("Tools/C-IMPL/Inspect/6 MAP01 stone detail")] public static void ViewStone() { Inspect(1, "PUZZLE_AREAS/PZ01/TEAM_MARKERS/ThrowStone_101", new Bounds(new Vector3(12, 1, 0), new Vector3(9, 5.5f, 1))); }
        [MenuItem("Tools/C-IMPL/Inspect/7 MAP03 neutral detail")] public static void ViewNeutral() { Inspect(3, "PUZZLE_AREAS/PuzzleChamber/BlueFixedTerrain/B07_CentralVertical/Collider_Layout", new Bounds(new Vector3(7.5f, 3, 0), new Vector3(21, 13, 1))); }
        [MenuItem("Tools/C-IMPL/Inspect/8 Elevator entry detail")] public static void ViewEntry() { Inspect(0, "ELEVATOR_ENTRY_VISUAL/DeckCollider_Layout", new Bounds(new Vector3(-10, 1, 0), new Vector3(12, 10, 1))); }
    }
}

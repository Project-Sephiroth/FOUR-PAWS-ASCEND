using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using FourPaws.PCS.Hacking;
using FourPaws.PCS.StepPuzzle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace FourPaws.PCS.Editor
{
    public static class PCS_DemoAuthoring
    {
        private const string Project = "C:/FOUR-PAWS-ASCEND-NEWPCS";
        private const string Run = "C:/FOUR-PAWS-PCS-BRIEF/FOUR-PAWS-PCS-IMPLEMENTATION-BRIEF/runs/20261015-030445-f658a87e";
        private const string PanelPath = "Assets/PCS/Hacking/Prefabs/PCS_HackingPanel.prefab";
        private const string RigPath = "Assets/PCS/Hacking/Prefabs/PCS_HackingDemoRig.prefab";
        private const string ScenePath = "Assets/PCS/Hacking/Scenes/PCS_HackingDemo.unity";
        private const string HexPath = "Assets/PCS/Hacking/Prefabs/PCS_HexTile.prefab";
        private const string StepTilePath = "Assets/PCS/StepPuzzle/Prefabs/PCS_StepTile.prefab";
        private const string StepRigPath = "Assets/PCS/StepPuzzle/Prefabs/PCS_StepDemoRig.prefab";
        private const string StepScenePath = "Assets/PCS/StepPuzzle/Scenes/PCS_StepPuzzleDemo.unity";
        private static readonly string[] AssetPaths = { PanelPath, RigPath, ScenePath };
        private static readonly Color Ink = new Color(0.055f, 0.08f, 0.12f);
        private static readonly Color Accent = new Color(0.2f, 0.78f, 0.7f);
        [Serializable] private sealed class Record { public string path; public string sha256; }
        [Serializable] private sealed class Records { public Record[] files; }
        [Serializable] private sealed class Paths { public string[] paths; }

        [MenuItem("Tools/PCS/데모/P1 생성 또는 검사")]
        public static void P1CreateOrValidate()
        {
            Guard();
            RunP1Logic();
            int existing = AssetPaths.Count(File.Exists);
            if (existing == AssetPaths.Length)
            {
                CheckOwnership();
                RepairP1Renderer();
                ValidateSavedP1();
                WriteEvidence("P1_validate", "명시 논리·저장 참조 검사를 통과했습니다. 실제 UI Play와 실제 FPS는 별도 검증입니다. 소유 기록에서 저장 변경을 확인하세요.");
                Debug.Log("PCS P1 저장 참조와 논리 PASS. 실제 UI Play는 별도 검사입니다.");
                return;
            }
            Require(existing == 0, "자산 일부만 존재합니다. 기존 내용을 먼저 확인하세요.");
            foreach (string path in AssetPaths)
            {
                CheckAssetPath(path);
                Require(!File.Exists(path + ".meta"), "기존 또는 대응 자산이 없는 메타: " + path);
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cameraObject = new GameObject("PCS_Camera", typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>();
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.orthographic = true;
            camera.orthographicSize = 3.6f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Ink;
            var events = new GameObject("PCS_EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            events.GetComponent<EventSystem>().sendNavigationEvents = false;

            var rig = new GameObject("PCS_HackingDemoRig", typeof(PCS_HackingDemoController));
            var controller = rig.GetComponent<PCS_HackingDemoController>();
            RectTransform world = Rect("ApproachBoard", rig.transform, Vector2.zero, new Vector2(800f, 340f));
            var worldCanvas = world.gameObject.AddComponent<Canvas>();
            worldCanvas.renderMode = RenderMode.WorldSpace;
            world.localScale = Vector3.one * 0.01f;
            ImageAt("Board", world, Vector2.zero, new Vector2(780f, 330f), new Color(0.09f, 0.15f, 0.21f));
            TextAt("BoardTitle", world, new Vector2(0f, 135f), new Vector2(740f, 40f), "오프라인 단말 접근 데모", 24);
            var actor = ImageAt("DemoActor", world, new Vector2(-220f, 0f), new Vector2(34f, 34f), Accent);
            controller.actor = actor.gameObject.AddComponent<PCS_HackingDemoActor>();
            var terminal = ImageAt("TimingTerminal", world, new Vector2(200f, 0f), new Vector2(60f, 85f), new Color(0.94f, 0.67f, 0.26f));
            controller.timingTerminal = terminal.transform;
            TextAt("TerminalLabel", world, new Vector2(200f, -70f), new Vector2(180f, 35f), "타이밍 / E", 22);
            TextAt("ActorLabel", actor.transform, new Vector2(0f, -35f), new Vector2(130f, 30f), "데모 조작체", 16);

            RectTransform canvasRect = Rect("PCS_UI", rig.transform, Vector2.zero, new Vector2(1280f, 720f));
            var canvas = canvasRect.gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasRect.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasRect.gameObject.AddComponent<GraphicRaycaster>();
            TextAt("Title", canvasRect, new Vector2(0f, 315f), new Vector2(1000f, 44f), "FOUR PAWS, ASCEND! / PCS 해킹 실습", 28);
            controller.beginTiming = ButtonAt("BeginTiming", canvasRect, new Vector2(0f, 245f), new Vector2(330f, 48f), "타이밍 해킹(P1) 바로 시작");
            TextAt("DirectStartNote", canvasRect, new Vector2(0f, 202f), new Vector2(800f, 32f), "바로 시작은 단말 접근 과정을 생략합니다.", 19);
            controller.approach = TextAt("ApproachHint", canvasRect, new Vector2(0f, -225f), new Vector2(900f, 62f), "WASD / 방향키로 이동하고 단말 근처에서 E를 누르세요.", 22);
            controller.result = TextAt("TimingSignal", canvasRect, new Vector2(0f, -315f), new Vector2(1050f, 48f), "타이밍 결과: 대기", 21);

            GameObject panel = CreatePanel(canvasRect);
            SaveNewPrefab(panel, PanelPath);
            Object.DestroyImmediate(panel);
            panel = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PanelPath), canvasRect);
            controller.panel = panel;
            controller.view = panel.GetComponentInChildren<PCS_HackingView>(true);
            controller.restart = panel.transform.Find("Restart").GetComponent<Button>();
            controller.close = panel.transform.Find("Close").GetComponent<Button>();
            controller.cancel = panel.transform.Find("Cancel").GetComponent<Button>();
            SaveNewPrefab(rig, RigPath);
            Object.DestroyImmediate(rig);
            PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(RigPath), scene);
            CheckAssetPath(ScenePath);
            Require(EditorSceneManager.SaveScene(scene, ScenePath), "씬 저장 실패입니다.");
            AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceSynchronousImport);
            WriteOwnership();
            ValidateSavedP1();
            WriteEvidence("P1_create", "명시 논리·저장 참조 검사를 통과했습니다. 실제 UI Play와 실제 FPS는 별도 검증입니다. 소유 기록에서 저장 변경을 확인하세요.");
            Debug.Log("PCS P1 저장 참조와 논리 PASS. 실제 UI Play는 별도 검사입니다.");
        }

        [MenuItem("Tools/PCS/데모/P1 논리 검사")]
        public static void P1LogicChecks()
        {
            Guard();
            RunP1Logic();
            WriteEvidence("P1_logic", "명시 논리·저장 참조 검사를 통과했습니다. 실제 UI Play와 실제 FPS는 별도 검증입니다. 소유 기록에서 저장 변경을 확인하세요.");
            Debug.Log("PCS P1 저장 참조와 논리 PASS. 실제 UI Play는 별도 검사입니다.");
        }

        [MenuItem("Tools/PCS/데모/P2 추가 또는 검사")]
        public static void P2AddOrValidate()
        {
            Guard();
            CheckOwnership();
            RunP1Logic();
            RunP2Logic();
            if (!File.Exists(Run + "/evidence/P2_asset_ownership.json"))
            {
                Require(File.Exists(Run + "/reports/P1_ACTUAL.md"), "P1 실제 Play 증거가 필요합니다.");
                Require(!File.Exists(HexPath) && !File.Exists(HexPath + ".meta"), "기존 육각 자산의 소유권 확인이 필요합니다.");
                RectTransform tile = Rect("PCS_HexTile", null, Vector2.zero, new Vector2(126f, 126f));
                tile.gameObject.AddComponent<PCS_HexTileView>().raycastTarget = true;
                SaveNewPrefab(tile.gameObject, HexPath);
                Object.DestroyImmediate(tile.gameObject);
                WriteOwnership("P2_hex_creation.json");

                GameObject panel = PrefabUtility.LoadPrefabContents(PanelPath);
                try
                {
                    Require(panel.transform.Find("RotationView") == null, "기존 회전 보드 확인이 필요합니다.");
                    var view = panel.GetComponentInChildren<PCS_HackingView>(true);
                    view.title = panel.transform.Find("Title").GetComponent<Text>();
                    var board = Rect("RotationView", panel.transform, new Vector2(-245f, 10f), new Vector2(450f, 430f));
                    view.rotationRoot = board.gameObject;
                    view.rotationTiles = new PCS_HexTileView[7];
                    for (int i = 0; i < 7; i++)
                    {
                        var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(HexPath), board);
                        instance.name = i == 0 ? "Center_Fixed" : "Ring_" + (i - 1);
                        var rect = instance.GetComponent<RectTransform>();
                        rect.anchoredPosition = PCS_RotationPuzzle.Position(i) * 76f;
                        var hex = instance.GetComponent<PCS_HexTileView>();
                        hex.tileIndex = i;
                        hex.raycastTarget = i != 0;
                        view.rotationTiles[i] = hex;
                        TextAt("Label", instance.transform, new Vector2(0f, -37f), new Vector2(65f, 22f), i == 0 ? "고정" : "R" + (i - 1), 15);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(hex);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(rect);
                    }
                    board.gameObject.SetActive(false);
                    SaveOwnedPrefab(panel, PanelPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(panel); }
                WriteOwnership("P2_panel_extension.json");

                GameObject rig = PrefabUtility.LoadPrefabContents(RigPath);
                try
                {
                    var controller = rig.GetComponent<PCS_HackingDemoController>();
                    Require(controller.beginRotation == null && rig.transform.Find("PCS_UI/BeginRotation") == null, "기존 P2 제어기 확장 확인이 필요합니다.");
                    Transform canvas = rig.transform.Find("PCS_UI");
                    controller.beginRotation = ButtonAt("BeginRotation", canvas, new Vector2(380f, 245f), new Vector2(300f, 48f), "회전 연결 해킹(P2)");
                    controller.rotationResult = TextAt("RotationSignal", canvas, new Vector2(0f, -275f), new Vector2(1050f, 32f), "회전 연결 결과: 대기", 20);
                    controller.panel.transform.SetAsLastSibling();
                    Transform world = rig.transform.Find("ApproachBoard");
                    var terminal = ImageAt("RotationTerminal", world, new Vector2(-50f, 0f), new Vector2(60f, 85f), new Color(0.55f, 0.48f, 0.9f));
                    controller.rotationTerminal = terminal.transform;
                    TextAt("RotationTerminalLabel", world, new Vector2(-50f, -70f), new Vector2(200f, 35f), "회전 연결 / E", 21);
                    SaveOwnedPrefab(rig, RigPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(rig); }
                WriteOwnership("P2_asset_ownership.json");
            }
            RepairP2Signal();
            ValidateSavedP2();
            WriteEvidence("P2_validate", "명시 논리·저장 참조 검사를 통과했습니다. 실제 UI Play와 실제 FPS는 별도 검증입니다. 소유 기록에서 저장 변경을 확인하세요.");
            Debug.Log("PCS P2 저장 참조와 논리 PASS. 실제 UI Play는 별도 검사입니다.");
        }

        private static void SaveOwnedPrefab(GameObject prefab, string path)
        {
            Guard();
            CheckAssetPath(path);
            Require(File.Exists(path) && File.Exists(path + ".meta"), "자기 프리팹과 메타가 먼저 존재해야 합니다.");
            string guid = AssetDatabase.AssetPathToGUID(path);
            PrefabUtility.SaveAsPrefabAsset(prefab, path, out bool success);
            Require(success && AssetDatabase.AssetPathToGUID(path) == guid, "자기 프리팹 저장 또는 GUID 보존 실패입니다.");
        }

        [MenuItem("Tools/PCS/데모/P3 생성 또는 검사")]
        public static void P3CreateOrValidate()
        {
            Guard();
            CheckOwnership();
            RunP1Logic();
            RunP2Logic();
            RunP3Logic();
            string[] paths = { StepTilePath, StepRigPath, StepScenePath };
            if (File.Exists(Run + "/evidence/P3_asset_ownership.json"))
            {
                ValidateSavedP3();
                WriteEvidence("P3_validate", "명시 논리·저장 참조 검사를 통과했습니다. 실제 UI Play와 실제 FPS는 별도 검증입니다. 소유 기록에서 저장 변경을 확인하세요.");
                Debug.Log("PCS P3 저장 참조와 논리 PASS. 실제 UI Play는 별도 검사입니다.");
                return;
            }
            Require(File.Exists(Run + "/reports/P2_ACTUAL.md"), "P2 실제 Play 증거가 필요합니다.");
            foreach (string path in paths)
            {
                CheckAssetPath(path);
                Require(!File.Exists(path) && !File.Exists(path + ".meta"), "기존 P3 자산의 소유권 확인이 필요합니다.");
            }
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("PCS_Camera", typeof(Camera)).GetComponent<Camera>();
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.orthographic = true;
            camera.orthographicSize = 3.6f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Ink;
            var events = new GameObject("PCS_EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            events.GetComponent<EventSystem>().sendNavigationEvents = false;
            var rig = new GameObject("PCS_StepDemoRig", typeof(PCS_StepDemoController));
            var controller = rig.GetComponent<PCS_StepDemoController>();
            var canvasRect = Rect("PCS_UI", rig.transform, Vector2.zero, new Vector2(1280f, 720f));
            canvasRect.gameObject.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasRect.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasRect.gameObject.AddComponent<GraphicRaycaster>();
            TextAt("Title", canvasRect, new Vector2(0f, 315f), new Vector2(1100f, 45f), "FOUR PAWS, ASCEND! / PCS 발판 미로", 28);
            TextAt("Rules", canvasRect, new Vector2(0f, 255f), new Vector2(1050f, 60f), "선택한 모드의 목표와 규칙을 확인하세요.\nWASD / 방향키만 사용합니다. 클릭 이동·점프·대각선 이동은 없습니다.", 21);
            var board = Rect("StepBoard", rig.transform, Vector2.zero, new Vector2(430f, 430f));
            board.gameObject.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            board.localScale = Vector3.one * 0.01f;
            board.localPosition = new Vector3(-2.3f, 0f, 0f);
            ImageAt("Frame", board, Vector2.zero, new Vector2(426f, 426f), new Color(0.08f, 0.13f, 0.19f));
            var tileImage = ImageAt("PCS_StepTile", board, Vector2.zero, new Vector2(120f, 120f), new Color(0.15f, 0.21f, 0.31f));
            var tile = tileImage.gameObject.AddComponent<PCS_StepTileView>();
            tile.surface = tileImage;
            tile.label = TextAt("Coordinates", tile.transform, new Vector2(0f, -43f), new Vector2(100f, 25f), "(0, 0)", 17);
            SaveNewPrefab(tile.gameObject, StepTilePath);
            Object.DestroyImmediate(tile.gameObject);
            controller.tiles = new PCS_StepTileView[9];
            for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(StepTilePath), board);
                instance.name = $"Tile_{x}_{y}";
                var view = instance.GetComponent<PCS_StepTileView>();
                view.x = x; view.y = y;
                view.label.text = $"({x}, {y})";
                var rect = instance.GetComponent<RectTransform>();
                rect.anchoredPosition = new Vector2((x - 1) * 132f, (y - 1) * 132f);
                controller.tiles[y * 3 + x] = view;
                PrefabUtility.RecordPrefabInstancePropertyModifications(view);
                PrefabUtility.RecordPrefabInstancePropertyModifications(view.label);
                PrefabUtility.RecordPrefabInstancePropertyModifications(rect);
            }
            controller.actor = ImageAt("VisibleDemoActor", board, new Vector2(-132f, -132f), new Vector2(36f, 36f), Accent).rectTransform;
            TextAt("ActorLabel", controller.actor, new Vector2(0f, 30f), new Vector2(85f, 22f), "조작체", 14);
            controller.progress = TextAt("Progress", canvasRect, new Vector2(260f, 125f), new Vector2(420f, 70f), "활성화한 발판 0 / 9", 30);
            controller.status = TextAt("Status", canvasRect, new Vector2(260f, 5f), new Vector2(430f, 155f), "시작 버튼을 누르세요.", 23);
            TextAt("RouteHint", canvasRect, new Vector2(260f, -130f), new Vector2(430f, 92f), "연습 경로: 오른쪽 2회 → 위 → 왼쪽 2회\n→ 위 → 오른쪽 2회", 21);
            controller.begin = ButtonAt("BeginStep", canvasRect, new Vector2(-240f, -250f), new Vector2(210f, 46f), "미로 시작(P3)");
            controller.restart = ButtonAt("Restart", canvasRect, new Vector2(0f, -250f), new Vector2(210f, 46f), "다시 시작 / R");
            controller.cancel = ButtonAt("Cancel", canvasRect, new Vector2(240f, -250f), new Vector2(210f, 46f), "취소 / Esc");
            controller.result = TextAt("StepSignal", canvasRect, new Vector2(0f, -315f), new Vector2(1100f, 48f), "발판 결과: 대기", 21);
            SaveNewPrefab(rig, StepRigPath);
            Object.DestroyImmediate(rig);
            PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(StepRigPath), scene);
            Require(EditorSceneManager.SaveScene(scene, StepScenePath), "P3 씬 저장 실패입니다.");
            AssetDatabase.ImportAsset(StepScenePath, ImportAssetOptions.ForceSynchronousImport);
            WriteOwnership("P3_asset_ownership.json");
            ValidateSavedP3();
            WriteEvidence("P3_create", "명시 논리·저장 참조 검사를 통과했습니다. 실제 UI Play와 실제 FPS는 별도 검증입니다. 소유 기록에서 저장 변경을 확인하세요.");
            Debug.Log("PCS P3 저장 참조와 논리 PASS. 실제 UI Play는 별도 검사입니다.");
        }

        private static void RunP3Logic()
        {
            var model = new PCS_StepPuzzle(); model.Begin();
            Require(model.Count == 1 && model.IsVisited(0, 0) && model.X == 0 && model.Y == 0, "P3 시작 칸");
            for (int i = 0; i < 20; i++) Require(model.Move(0, 0) == PCS_StepPuzzle.MoveResult.Ignored && model.Count == 1, "P3 머무름은 방문 아님");
            Require(model.Move(-1, 0) == PCS_StepPuzzle.MoveResult.Outside && model.Move(0, -1) == PCS_StepPuzzle.MoveResult.Outside && model.Count == 1 && !model.Failed, "P3 경계");
            Require(model.Move(1, 1) == PCS_StepPuzzle.MoveResult.Ignored && model.Move(2, 0) == PCS_StepPuzzle.MoveResult.Ignored, "P3 대각선·점프 금지");
            Require(model.Move(1, 0) == PCS_StepPuzzle.MoveResult.Visited && model.Move(-1, 0) == PCS_StepPuzzle.MoveResult.Failed, "P3 재방문 실패");
            Require(!model.Running && model.Failed && model.Count == 2 && model.Move(0, 1) == PCS_StepPuzzle.MoveResult.Ignored, "P3 실패 후 잠금");
            Vector2Int[] route = { Vector2Int.right, Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.left, Vector2Int.up, Vector2Int.right, Vector2Int.right };
            for (int attempt = 0; attempt < 10; attempt++)
            {
                model.Begin();
                for (int i = 0; i < route.Length; i++)
                    Require(model.Move(route[i].x, route[i].y) == (i == 7 ? PCS_StepPuzzle.MoveResult.Succeeded : PCS_StepPuzzle.MoveResult.Visited), "P3 연습 완주 경로");
                Require(model.Count == 9 && model.Succeeded && !model.Running && model.X == 2 && model.Y == 2 && model.Move(-1, 0) == PCS_StepPuzzle.MoveResult.Ignored, "P3 성공 후 잠금");
                model.Cancel(); Require(model.Count == 0 && !model.Running && model.Move(1, 0) == PCS_StepPuzzle.MoveResult.Ignored, "P3 취소");
            }
            Require(PCS_StepPuzzle.ResolveDirections(true, false, false, false) == Vector2Int.right, "P3 한 방향 입력");
            Require(PCS_StepPuzzle.ResolveDirections(true, true, false, false) == Vector2Int.zero &&
                PCS_StepPuzzle.ResolveDirections(true, false, true, false) == Vector2Int.zero, "P3 서로 다른 동시 방향 거부");
        }

        private static void ValidateSavedP3()
        {
            Guard();
            foreach (string path in new[] { StepTilePath, StepRigPath })
            {
                var prefab = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    Require(prefab.GetComponentsInChildren<MonoBehaviour>(true).All(c => c != null), "P3 프리팹 스크립트 누락입니다.");
                    if (path == StepTilePath) CheckStepTile(prefab.GetComponent<PCS_StepTileView>());
                    else CheckStepController(prefab.GetComponent<PCS_StepDemoController>());
                }
                finally { PrefabUtility.UnloadPrefabContents(prefab); }
            }
            Scene scene = EditorSceneManager.OpenScene(StepScenePath, OpenSceneMode.Single);
            Require(SceneManager.sceneCount == 1 && scene.path == StepScenePath && !scene.isDirty, "저장된 P3 씬 하나여야 합니다.");
            var roots = scene.GetRootGameObjects();
            Require(roots.Length == 3 && roots.SelectMany(r => r.GetComponentsInChildren<MonoBehaviour>(true)).All(c => c != null), "P3 루트 구성 또는 스크립트 누락입니다.");
            CheckStepController(roots.SelectMany(r => r.GetComponentsInChildren<PCS_StepDemoController>(true)).Single());
            var events = roots.SelectMany(r => r.GetComponentsInChildren<EventSystem>(true)).Single();
            Require(!events.sendNavigationEvents && events.GetComponent<StandaloneInputModule>() != null, "P3 마우스 UI 설정입니다.");
        }

        private static void CheckStepTile(PCS_StepTileView tile)
        {
            Require(tile != null && tile.surface != null && tile.label != null && !tile.surface.raycastTarget && !tile.label.raycastTarget, "P3 타일 참조와 클릭 이동 금지 검사입니다.");
        }

        private static void CheckStepController(PCS_StepDemoController controller)
        {
            Require(controller != null && controller.tiles != null && controller.tiles.Length == 36 && controller.tiles.Distinct().Count() == 36 &&
                controller.actor != null && controller.progress != null && controller.status != null && controller.result != null &&
                controller.begin != null && controller.restart != null && controller.cancel != null && controller.budgets != null && controller.modeAndGoal != null && controller.routeHint != null && controller.newMaze != null && controller.difficulty != null, "P3 제어기 참조 검사입니다.");
            for (int i = 0; i < controller.tiles.Length; i++)
            {
                CheckStepTile(controller.tiles[i]);
                Require(controller.tiles[i].x == i % 6 && controller.tiles[i].y == i / 6 && controller.tiles[i].transform.parent == controller.actor.parent &&
                    controller.tiles[i].transform.IsChildOf(controller.transform), "P3 격자 순서와 조작체 좌표계 검사입니다.");
            }
            Require(controller.actor.GetComponent<Image>() != null && !controller.actor.GetComponent<Image>().raycastTarget, "P3 자기 조작체 표시 검사입니다.");
            Require(controller.actor.parent.GetComponent<Canvas>() != null && controller.actor.parent.GetComponent<Canvas>().renderMode == RenderMode.WorldSpace, "P3 보드는 월드 공간에 있어야 합니다.");
        }

        private static void RepairP2Signal()
        {
            if (File.Exists(Run + "/evidence/P2_layout_revision.json")) return;
            var rig = PrefabUtility.LoadPrefabContents(RigPath);
            try
            {
                var controller = rig.GetComponent<PCS_HackingDemoController>();
                controller.rotationResult.rectTransform.anchoredPosition = controller.result.rectTransform.anchoredPosition;
                controller.rotationResult.rectTransform.sizeDelta = controller.result.rectTransform.sizeDelta;
                controller.rotationResult.fontSize = controller.result.fontSize;
                controller.rotationResult.gameObject.SetActive(false);
                SaveOwnedPrefab(rig, RigPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(rig); }
            WriteOwnership("P2_layout_revision.json");
        }

        private static void ValidateSavedP2()
        {
            ValidateSavedP1();
            foreach (string path in new[] { HexPath, PanelPath, RigPath })
            {
                var prefab = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    Require(prefab.GetComponentsInChildren<MonoBehaviour>(true).All(c => c != null), "P2 프리팹 스크립트 누락입니다.");
                    if (path == HexPath) Require(prefab.GetComponent<PCS_HexTileView>() != null && prefab.GetComponent<CanvasRenderer>() != null, "육각 타일 필수 컴포넌트 누락입니다.");
                    else if (path == PanelPath) CheckRotationView(prefab.GetComponentInChildren<PCS_HackingView>(true));
                    else CheckRotationController(prefab.GetComponent<PCS_HackingDemoController>());
                }
                finally { PrefabUtility.UnloadPrefabContents(prefab); }
            }
            var scene = SceneManager.GetActiveScene();
            CheckRotationController(scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PCS_HackingDemoController>(true)).Single());
            Require(!scene.isDirty, "P2 검사에서 저장 씬이 수정되면 안 됩니다.");
        }

        private static void CheckRotationController(PCS_HackingDemoController controller)
        {
            CheckController(controller);
            Require(controller.beginRotation != null && controller.rotationTerminal != null && controller.rotationResult != null, "P2 제어기 참조 누락입니다.");
            foreach (Transform part in new[] { controller.beginRotation.transform, controller.rotationTerminal, controller.rotationResult.transform })
                Require(part.IsChildOf(controller.transform), "P2 참조는 자기 Rig 안에 있어야 합니다.");
            CheckRotationView(controller.view);
        }

        private static void CheckRotationView(PCS_HackingView view)
        {
            Require(view.title != null && view.rotationRoot != null && view.rotationTiles != null && view.rotationTiles.Length >= 7, "P2 화면 참조 누락입니다.");
            Require(view.rotationTiles.Distinct().Count() == view.rotationTiles.Length, "타일 참조가 중복됐습니다.");
            for (int i = 0; i < 7; i++)
                Require(view.rotationTiles[i] != null && view.rotationTiles[i].tileIndex == i &&
                    view.rotationTiles[i].transform.IsChildOf(view.rotationRoot.transform) &&
                    view.rotationTiles[i].GetComponent<CanvasRenderer>() != null && view.rotationTiles[i].raycastTarget == (i != 0), "타일 참조·인덱스·렌더러가 다릅니다.");
        }

        private static void RunP2Logic()
        {
            int[] answer = { 21, 12, 32, 48, 2, 3, 8 };
            Require(PCS_RotationPuzzle.Evaluate(answer).Solved, "연결 해답 A");
            Require(PCS_RotationPuzzle.Evaluate(new[] { 21, 24, 8, 33, 32, 6, 2 }).Solved, "대안 연결 해답 B");
            int[] broken = (int[])answer.Clone(); broken[2] = 8;
            var state = PCS_RotationPuzzle.Evaluate(broken);
            Require(!state.Solved && state.outside == 0 && state.unmatched == 2 && state.reachable == 6, "이웃 핀 불일치");
            broken[2] = 2; state = PCS_RotationPuzzle.Evaluate(broken);
            Require(!state.Solved && state.outside == 1 && state.unmatched == 1, "바깥으로 열린 핀");
            state = PCS_RotationPuzzle.Evaluate(new[] { 9, 24, 8, 1, 33, 5, 10 });
            Require(!state.Solved && state.outside == 0 && state.unmatched == 0 && state.reachable == 5, "핀 쌍은 맞지만 분리된 그래프");
            for (int mask = 0; mask < 64; mask++) Require(PCS_RotationPuzzle.RotateMask(mask, 6) == mask, "여섯 회전 원상 복귀");
            Require(PCS_RotationPuzzle.RotateMask(21, 2) == 21 && PCS_RotationPuzzle.RotateMask(9, 3) == 9, "회전 대칭");
            var seedRecords = new List<string>();
            for (int seed = 0; seed < 100; seed++)
            {
                var model = new PCS_RotationPuzzle(); model.Begin(seed);
                Require(!model.Succeeded && !model.Status.Solved && model.Running && model.MaskAt(0) == 21, "미완성인 유효 시작");
                int[] restored = new int[7];
                for (int i = 0; i < 7; i++) restored[i] = PCS_RotationPuzzle.RotateMask(model.MaskAt(i), -model.InitialRotation(i));
                Require(PCS_RotationPuzzle.Evaluate(restored).Solved, "모든 시드 역회전 해결 가능");
                seedRecords.Add(seed + ": " + string.Join(",", Enumerable.Range(1, 6).Select(model.InitialRotation)));
            }
            int draws = 0;
            var fallback = new PCS_RotationPuzzle(); fallback.Begin(0, () => { draws++; return 0; });
            Require(draws == 96 && fallback.Candidates == 16 && fallback.UsedFallback && !fallback.Succeeded && !fallback.Status.Solved, "유한 섞기 대체판");
            int[] turns = { 1, 2, 3, 4, 5, 1 };
            for (int i = 1; i < 7; i++) Require(fallback.InitialRotation(i) == turns[i - 1], "대체판 회전값");
            int center = fallback.MaskAt(0); Require(!fallback.Rotate(0) && fallback.MaskAt(0) == center, "중앙 고정");
            for (int i = 1; i < 7; i++) for (int n = 0; n < (6 - turns[i - 1]) % 6; n++) fallback.Rotate(i);
            Require(fallback.Succeeded && !fallback.Running && !fallback.Rotate(1), "성공 후 회전 잠금");
            fallback.Begin(2026); seedRecords.Add("default2026: " + string.Join(",", Enumerable.Range(1, 6).Select(fallback.InitialRotation)));
            fallback.Cancel(); Require(!fallback.Rotate(1), "취소 후 회전 무시");
            WriteEvidence("P2_logic_seeds", string.Join("\n", seedRecords));
        }

        private static GameObject CreatePanel(Transform parent)
        {
            Image panel = ImageAt("PCS_HackingPanel", parent, Vector2.zero, new Vector2(1040f, 560f), new Color(0.07f, 0.105f, 0.16f, 0.99f));
            panel.raycastTarget = true;
            TextAt("Title", panel.transform, new Vector2(0f, 235f), new Vector2(960f, 50f), "타이밍 해킹 / 모든 핀을 연결하세요", 30);
            RectTransform board = Rect("TimingView", panel.transform, new Vector2(-245f, 10f), new Vector2(430f, 430f));
            PCS_HackingView view = board.gameObject.AddComponent<PCS_HackingView>();
            view.raycastTarget = false;
            view.progress = TextAt("Progress", panel.transform, new Vector2(245f, 130f), new Vector2(420f, 85f), "연결한 핀 0 / 6\n오입력 0", 28);
            view.alarm = TextAt("Alarm", panel.transform, new Vector2(245f, 40f), new Vector2(420f, 76f), "모의 경보 0\n실제 적은 나타나지 않습니다", 23);
            view.feedback = TextAt("Status", panel.transform, new Vector2(245f, -75f), new Vector2(440f, 108f), "금색 포인터가 핀과 겹칠 때 Space를 누르세요.", 23);
            ButtonAt("Restart", panel.transform, new Vector2(-240f, -230f), new Vector2(210f, 46f), "다시 시작 / R");
            ButtonAt("Cancel", panel.transform, new Vector2(0f, -230f), new Vector2(210f, 46f), "취소 / Esc");
            ButtonAt("Close", panel.transform, new Vector2(240f, -230f), new Vector2(210f, 46f), "닫기");
            return panel.gameObject;
        }

        private static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return rect;
        }

        private static Image ImageAt(string name, Transform parent, Vector2 position, Vector2 size, Color tint)
        {
            Image image = Rect(name, parent, position, size).gameObject.AddComponent<Image>();
            image.color = tint;
            image.raycastTarget = false;
            return image;
        }

        private static Text TextAt(string name, Transform parent, Vector2 position, Vector2 size, string value, int fontSize)
        {
            Text text = Rect(name, parent, position, size).gameObject.AddComponent<Text>();
            text.font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Imports/Galmuri7.ttf");
            Require(text.font != null, "지정한 기존 한글 글꼴이 없습니다. 새 글꼴은 만들지 않습니다.");
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = new Color(0.9f, 0.94f, 1f);
            text.text = value;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        private static Button ButtonAt(string name, Transform parent, Vector2 position, Vector2 size, string label)
        {
            Image image = ImageAt(name, parent, position, size, new Color(0.13f, 0.28f, 0.35f));
            image.raycastTarget = true;
            Button button = image.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            TextAt("Label", image.transform, Vector2.zero, size - new Vector2(10f, 4f), label, 20);
            return button;
        }

        private static void SaveNewPrefab(GameObject root, string path)
        {
            CheckAssetPath(path);
            Require(!File.Exists(path) && !File.Exists(path + ".meta"), "기존 프리팹을 덮어쓰지 않습니다: " + path);
            PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
            Require(success && File.Exists(path), "프리팹 저장 실패: " + path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        }

        private static void ValidateSavedP1()
        {
            Guard();
            foreach (string path in new[] { PanelPath, RigPath })
            {
                var prefab = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    Require(prefab.GetComponentsInChildren<MonoBehaviour>(true).All(c => c != null), "스크립트 누락 자산: " + path);
                    if (path == PanelPath) CheckView(prefab.GetComponentInChildren<PCS_HackingView>(true));
                    else CheckController(prefab.GetComponent<PCS_HackingDemoController>());
                }
                finally { PrefabUtility.UnloadPrefabContents(prefab); }
            }
            Scene reopened = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Require(SceneManager.sceneCount == 1 && reopened.path == ScenePath, "PCS 씬 하나만 열려 있어야 합니다.");
            var roots = reopened.GetRootGameObjects();
            Require(roots.Length == 3, "씬 루트 구성이 다릅니다.");
            CheckController(roots.SelectMany(r => r.GetComponentsInChildren<PCS_HackingDemoController>(true)).Single());
            EventSystem events = roots.SelectMany(r => r.GetComponentsInChildren<EventSystem>(true)).Single();
            Require(!events.sendNavigationEvents && events.GetComponent<StandaloneInputModule>() != null, "마우스 전용 UI 모듈 설정이 다릅니다.");
            Require(roots.SelectMany(r => r.GetComponentsInChildren<MonoBehaviour>(true)).All(c => c != null), "씬 스크립트가 누락됐습니다.");
        }

        private static void CheckController(PCS_HackingDemoController controller)
        {
            Require(controller != null && controller.actor != null && controller.timingTerminal != null &&
                controller.panel != null && controller.view != null && controller.beginTiming != null &&
                controller.restart != null && controller.close != null && controller.cancel != null &&
                controller.result != null && controller.approach != null, "제어기 필수 참조가 누락됐습니다.");
            Require(controller.panel.transform.IsChildOf(controller.transform) && controller.panel != controller.gameObject, "패널을 꺼도 제어기가 살아 있어야 합니다.");
            CheckView(controller.view);
        }

        private static void CheckView(PCS_HackingView view)
        {
            Require(view != null && view.progress != null && view.feedback != null && view.alarm != null, "화면 필수 참조가 누락됐습니다.");
            Require(view.GetComponent<CanvasRenderer>() != null, "타이밍 화면에 저장된 CanvasRenderer가 필요합니다.");
        }

        private static void RepairP1Renderer()
        {
            var prefab = PrefabUtility.LoadPrefabContents(PanelPath);
            bool changed = false;
            try
            {
                var view = prefab.GetComponentInChildren<PCS_HackingView>(true);
                Require(view != null, "타이밍 화면 누락. 기존 프리팹을 보존하세요.");
                // RequireComponent의 메모리 보완이 저장 자산에 반영됐는지 별도로 확인한다.
                if (view.GetComponent<CanvasRenderer>() == null || !File.Exists(Run + "/evidence/P1_asset_rendering_revision.json"))
                {
                    Guard();
                    CheckAssetPath(PanelPath);
                    if (view.GetComponent<CanvasRenderer>() == null) view.gameObject.AddComponent<CanvasRenderer>();
                    PrefabUtility.SaveAsPrefabAsset(prefab, PanelPath, out bool success);
                    Require(success, "렌더러 컴포넌트 저장 실패입니다.");
                    changed = true;
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
            if (changed) WriteOwnership("P1_asset_rendering_revision.json");
        }

        private static void RunP1Logic()
        {
            PCS_TimingPuzzle ModelAt(float angle)
            {
                var model = new PCS_TimingPuzzle();
                Require(model.Begin(), "기본 설정");
                model.Advance(angle / 90f);
                return model;
            }
            foreach (float angle in new[] { 12f, 348f }) Require(ModelAt(angle).Connect() == PCS_TimingPuzzle.Hit.Connected, "각도 경계 포함");
            foreach (float angle in new[] { 12.01f, 347.99f }) Require(ModelAt(angle).Connect() == PCS_TimingPuzzle.Hit.Miss, "각도 경계 밖");
            Require(Mathf.Approximately(PCS_TimingPuzzle.CircularDistance(359f, 1f), 2f), "원형 각도 순환");
            var model = ModelAt(0f);
            Require(model.Connect() == PCS_TimingPuzzle.Hit.Connected, "첫 핀 연결");
            Require(model.Connect() == PCS_TimingPuzzle.Hit.AlreadyConnected && model.ConnectedCount == 1 && model.MissCount == 0, "중복 핀");
            model.Advance(30f / 90f);
            Require(model.Connect() == PCS_TimingPuzzle.Hit.Miss && model.ConnectedCount == 1 && model.Running, "오입력 후 진행 유지");
            model.Begin();
            for (int i = 0; i < 6; i++)
            {
                if (i > 0) model.Advance(60f / 90f);
                Require(model.Connect() == (i == 5 ? PCS_TimingPuzzle.Hit.Succeeded : PCS_TimingPuzzle.Hit.Connected), "모든 핀 연결");
            }
            float frozen = model.Angle;
            model.Advance(10f);
            Require(model.Connect() == PCS_TimingPuzzle.Hit.Ignored && model.Angle == frozen && model.ConnectedCount == 6, "성공 후 잠금");
            model.Begin(); model.Cancel();
            Require(model.Connect() == PCS_TimingPuzzle.Hit.Ignored, "취소 후 잠금");
            Require(model.Begin() && model.Angle == 0f && model.ConnectedCount == 0 && model.MissCount == 0, "재시작 초기화");
            model.targetCount = 0; model.degreesPerSecond = float.PositiveInfinity;
            model.Advance(1f);
            Require(model.ActiveTargetCount == 6 && Mathf.Approximately(model.Angle, 90f), "Inspector 조정값은 다음 시도 적용");
            Require(!model.Begin() && !model.Running && model.Error.Length > 0, "잘못된 설정");
            model = new PCS_TimingPuzzle { toleranceDegrees = 30f };
            Require(!model.Begin(), "중첩된 허용 각도");
            foreach (int fps in new[] { 30, 60, 120 })
            {
                model = new PCS_TimingPuzzle(); model.Begin();
                for (int i = 0; i < fps; i++) model.Advance(1f / fps);
                Require(Mathf.Abs(model.Angle - 90f) < 0.002f, "시간 기반 회전");
            }
        }

        private static void Guard()
        { PCS_DesignerAuthoring.Guard(false); }

        private static void CheckAssetPath(string path)
        {
            Require(AssetPaths.Contains(path) || path == HexPath || path == StepTilePath || path == StepRigPath || path == StepScenePath, "허용되지 않은 자산 경로: " + path);
            CheckPath(Project + "/" + path, Project);
        }

        private static void CheckPath(string path, string root)
        {
            string full = Path.GetFullPath(path).Replace('\\', '/');
            string normalizedRoot = Path.GetFullPath(root).Replace('\\', '/');
            Require(full == normalizedRoot || full.StartsWith(normalizedRoot + "/", StringComparison.OrdinalIgnoreCase), "허용 루트 밖의 경로입니다.");
            string current = full;
            while (!string.IsNullOrEmpty(current))
            {
                if (File.Exists(current) || Directory.Exists(current)) Require((File.GetAttributes(current) & FileAttributes.ReparsePoint) == 0, "재분석 지점·링크 경로는 사용할 수 없습니다.");
                current = Path.GetDirectoryName(current);
            }
        }

        private static string Hash(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "");
        }

        private static void WriteOwnership(string recordName = "P1_asset_ownership.json")
        {
            string output = Run + "/evidence/" + recordName;
            CheckPath(output, Run);
            Require(!File.Exists(output), "소유 기록이 이미 있습니다. 덮어쓰지 않습니다.");
            var records = new Records { files = AssetPaths.Concat(new[] { HexPath, StepTilePath, StepRigPath, StepScenePath }).Where(File.Exists).SelectMany(p => new[] { p, p + ".meta" }).Select(p => new Record { path = p, sha256 = Hash(Project + "/" + p) }).ToArray() };
            File.WriteAllText(output, JsonUtility.ToJson(records, true));
        }

        private static void CheckOwnership()
        { PCS_DesignerAuthoring.CheckOwnership(); }

        private static void WriteEvidence(string name, string details)
        { PCS_DesignerAuthoring.Evidence(name,details); }

        // 명시 메뉴에서만 실행한다. 기존 원본과 GUID를 보존하며 승인된 자기 자산만 갱신한다.
        internal static void Preflight() { Guard(); CheckOwnership(); }
        internal static void CheckExactPath(string path)
        { PCS_DesignerAuthoring.CheckPath(path); }
        internal static void RecordRevision(string reason, params string[] writtenPaths)
        { PCS_DesignerAuthoring.Record(reason,writtenPaths); }
        internal static void EvidenceP4(string name, string text)
        { PCS_DesignerAuthoring.Evidence(name,text); }
        [MenuItem("Tools/PCS/데모/한국어와 P3 미로 적용")]
        public static void ApplyKoreanAndMaze()
        {
            Preflight(); RunP1Logic(); RunP2Logic(); RunP3Logic(); RunMazeLogic();
            foreach (string path in new[] { PanelPath, HexPath, RigPath, StepTilePath, StepRigPath })
            {
                CheckOwnership(); var prefab = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    Localize(prefab);
                    if (path == PanelPath)
                    {
                        var v = prefab.GetComponentInChildren<PCS_HackingView>(true);
                        v.progress.fontSize = 24; v.alarm.fontSize = 20; v.feedback.fontSize = 21;
                        v.feedback.rectTransform.sizeDelta = new Vector2(440, 125);
                    }
                    if (path == StepRigPath) ExpandStep(prefab.GetComponent<PCS_StepDemoController>());
                    SaveOwnedPrefab(prefab, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(prefab); }
                RecordRevision("localize_" + Path.GetFileNameWithoutExtension(path),path);
            }
            foreach (string path in new[] { ScenePath, StepScenePath })
            {
                Guard(); CheckOwnership(); var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                foreach (var root in scene.GetRootGameObjects()) Localize(root);
                CheckAssetPath(path); Require(EditorSceneManager.SaveScene(scene, path), "자기 씬 저장 실패");
                RecordRevision("scene_" + Path.GetFileNameWithoutExtension(path),path);
            }
            ValidateSavedP2(); ValidateSavedP3();
            EvidenceP4("KOREAN_P3_SAVED", "P1/P2 규칙 회귀 및 P3 연습·생성 논리 PASS. 자기 프리팹5개·씬2개 보존 갱신/재열기. 실제 Play는 별도 검사.");
            Debug.Log("PCS 한국어·P3 미로 저장/재열기/논리 PASS. 실제 화면 검사는 별도로 수행하세요.");
        }
        private static void ExpandStep(PCS_StepDemoController c)
        {
            var board = c.actor.parent; var canvas = c.transform.Find("PCS_UI");
            var list = c.tiles.ToList();
            while (list.Count < 36)
            {
                var tile = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(StepTilePath), board);
                tile.name = "MazeTile_" + list.Count; list.Add(tile.GetComponent<PCS_StepTileView>());
            }
            c.tiles = list.ToArray();
            for (int i = 0; i < c.tiles.Length; i++)
            {
                var tile = c.tiles[i]; tile.x = i % 6; tile.y = i / 6;
                var rect = (RectTransform)tile.transform; rect.sizeDelta = Vector2.one * 59; rect.anchoredPosition = new Vector2((tile.x - 2.5f) * 65, (tile.y - 2.5f) * 65);
                tile.label.fontSize = 14; tile.label.text = $"({tile.x},{tile.y})";
                PrefabUtility.RecordPrefabInstancePropertyModifications(tile); PrefabUtility.RecordPrefabInstancePropertyModifications(rect); PrefabUtility.RecordPrefabInstancePropertyModifications(tile.label);
            }
            c.actor.SetAsLastSibling(); c.actor.GetComponentInChildren<Text>().gameObject.SetActive(false);
            c.progress.rectTransform.anchoredPosition = new Vector2(270, 175); c.progress.fontSize = 26;
            c.status.rectTransform.anchoredPosition = new Vector2(270, -10); c.status.rectTransform.sizeDelta = new Vector2(440, 145); c.status.fontSize = 20;
            c.routeHint = canvas.Find("RouteHint").GetComponent<Text>(); c.routeHint.fontSize = 18; c.routeHint.rectTransform.anchoredPosition = new Vector2(270, -140);
            if (!c.budgets) c.budgets = TextAt("MoveBudgets", canvas, new Vector2(270, 95), new Vector2(440, 90), "오른쪽 / 위쪽 / 왼쪽 / 아래쪽", 23);
            if (!c.modeAndGoal) c.modeAndGoal = TextAt("ModeAndGoal", canvas, new Vector2(0, 218), new Vector2(1100, 35), "보통 5×5 · 목표와 예산 확인", 20);
            if (!c.newMaze) c.newMaze = ButtonAt("NewMaze", canvas, new Vector2(380, -250), new Vector2(220, 46), "새 미로");
            if (!c.difficulty) c.difficulty = ButtonAt("Difficulty", canvas, new Vector2(-435, -250), new Vector2(230, 46), "모드 변경: 보통");
            c.begin.GetComponent<RectTransform>().anchoredPosition = new Vector2(-195, -250); c.begin.GetComponent<RectTransform>().sizeDelta = new Vector2(230,46);
            c.restart.GetComponent<RectTransform>().anchoredPosition = new Vector2(45, -250); c.restart.GetComponent<RectTransform>().sizeDelta = new Vector2(230,46);
            c.restart.GetComponentInChildren<Text>().text = "같은 판 재도전 / R";
            c.cancel.GetComponent<RectTransform>().anchoredPosition = new Vector2(210, -310); c.cancel.GetComponent<RectTransform>().sizeDelta = new Vector2(180,38);
            c.result.rectTransform.anchoredPosition = new Vector2(-150, -310); c.result.rectTransform.sizeDelta = new Vector2(700,40); c.result.fontSize = 18;
            Localize(c.gameObject);
        }
        private static void Localize(GameObject root)
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Imports/Galmuri7.ttf"); Require(font != null, "기존 한글 글꼴 누락");
            foreach (var text in root.GetComponentsInChildren<Text>(true))
            {
                if (KoreanText.TryGetValue(text.text, out string translated)) text.text = translated;
                text.font = font; text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate;
                PrefabUtility.RecordPrefabInstancePropertyModifications(text);
            }
        }
        [MenuItem("Tools/PCS/데모/P3 미로 논리 검사")]
        public static void MazeLogicChecks() { Preflight(); RunP3Logic(); RunMazeLogic(); }
        [MenuItem("Tools/PCS/데모/P3 공통 안내 보완")]
        public static void RepairStepGuidance()
        {
            Preflight();
            var prefab=PrefabUtility.LoadPrefabContents(StepRigPath);
            try { SetStepGuidance(prefab); SaveOwnedPrefab(prefab,StepRigPath); }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
            RecordRevision("p3_mode_guidance",StepRigPath);
            Guard();CheckOwnership();var scene=EditorSceneManager.OpenScene(StepScenePath,OpenSceneMode.Single);
            foreach(var root in scene.GetRootGameObjects())SetStepGuidance(root);
            CheckAssetPath(StepScenePath);Require(EditorSceneManager.SaveScene(scene,StepScenePath),"P3 안내 저장 실패");
            RecordRevision("p3_scene_guidance",StepScenePath);ValidateSavedP3();
            EvidenceP4("P3_GUIDANCE_SAVED","연습과 미로에 공통으로 맞는 상단 안내를 보존 갱신하고 저장 참조를 검사했습니다.");
        }
        private static void SetStepGuidance(GameObject root)
        {
            foreach(var label in root.GetComponentsInChildren<Text>(true))
                if(label.name=="Rules") { label.text="선택한 모드의 목표와 규칙을 확인하세요.\nWASD / 방향키만 사용합니다. 클릭 이동·점프·대각선 이동은 없습니다.";PrefabUtility.RecordPrefabInstancePropertyModifications(label); }
        }
        [MenuItem("Tools/PCS/데모/P1 Play 완료 주입 검사")]
        public static void TimingPlayCompletionCheck()
        {
            Require(EditorApplication.isPlaying&&SceneManager.sceneCount==1&&SceneManager.GetActiveScene().path==ScenePath,"P1/P2 단독 Play에서만 실행하세요.");
            var c=SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<PCS_HackingDemoController>(true)).Single();
            c.Close();c.BeginTiming();var p=c.timing;
            Require(p.Running&&p.degreesPerSecond>0,"P1 설정 오류 상태입니다.");
            for(int i=0;i<p.ActiveTargetCount;i++){p.Advance(Mathf.Repeat(p.TargetAngle(i)-p.Angle,360f)/p.ActiveDegreesPerSecond);p.Connect();}
            Require(p.Succeeded&&c.DeliverSuccess(c.AttemptId)&&!c.DeliverSuccess(c.AttemptId),"P1 완료 단발 전달");
            c.view.Show(c.AlarmCount);
            EvidenceP4("P1_AUTOMATED_PLAY","실제 Play 모델에 시간/연결을 주입하여 완료·결과 1회·중복 전달 거부를 확인했습니다. 실제 키 완주 검사와 구분합니다.");
        }
        private static void RunMazeLogic()
        {
            var report = new System.Text.StringBuilder(); var watch = System.Diagnostics.Stopwatch.StartNew();
            foreach (var difficulty in new[] { PCS_StepPuzzle.Difficulty.Normal, PCS_StepPuzzle.Difficulty.Hard })
            {
                var unique = new HashSet<string>(); int fallback = 0, maxExpanded = 0;
                for (int seed = 0; seed < 100; seed++)
                {
                    var b = PCS_StepPuzzle.Generate(difficulty, seed); Require(PCS_StepPuzzle.VerifyProof(b, out string error), "S09 경로: " + error);
                    Require(PCS_StepPuzzle.Quality(b, difficulty), "S09 품질"); unique.Add(PCS_StepPuzzle.Canonical(b)); if (b.fallback >= 0) fallback++;
                    maxExpanded = Math.Max(maxExpanded, b.expanded); Require(PCS_StepPuzzle.Canonical(b) == PCS_StepPuzzle.Canonical(PCS_StepPuzzle.Generate(difficulty, seed)), "S17 시드 재현");
                    report.AppendLine($"{difficulty} seed={seed} candidate={b.candidate} expanded={b.expanded} fallback={b.fallback} moves={b.proof.Length-1} turns={PCS_StepPuzzle.Turns(b.proof)} branches={PCS_StepPuzzle.Branches(b)} shortest={PCS_StepPuzzle.Shortest(b)} key={PCS_StepPuzzle.Canonical(b)}");
                }
                Require(unique.Count >= 80, "S10 구성 다양성 80/100 미달");
                for (int i = 0; i < 3; i++) { var b = PCS_StepPuzzle.Fallback(difficulty, i); Require(PCS_StepPuzzle.VerifyProof(b, out _) && PCS_StepPuzzle.Quality(b, difficulty), "S11 대체판 검증"); }
                var history = new List<string>(); for (int i = 0; i < 10; i++) { var b = PCS_StepPuzzle.Generate(difficulty, i, history, 0, 0); string key = PCS_StepPuzzle.Canonical(b); Require(history.Count == 0 || history.Last() != key, "S11 대체판 직전 중복"); history.Add(key); }
                var fullHistory = Enumerable.Range(0,3).Select(id=>PCS_StepPuzzle.Canonical(PCS_StepPuzzle.Fallback(difficulty,id))).ToList();
                var exhausted=PCS_StepPuzzle.Generate(difficulty,123,fullHistory,7,0);
                Require(exhausted.fallback>=0&&exhausted.candidate==7&&exhausted.expanded==0&&PCS_StepPuzzle.Canonical(exhausted)!=fullHistory.Last()&&PCS_StepPuzzle.VerifyProof(exhausted,out _),"S11 이력 포화 대체판과 후보 기록");
                report.AppendLine($"SUMMARY {difficulty} unique={unique.Count}/100 fallback={fallback} maxExpanded={maxExpanded}");
            }
            RunMazeRules(report);
            report.AppendLine("S03 방향별 소비, S04 벽/경계 무소비, S08 경로 검증, S11 이력 포화, S16 예산 전략 PASS");
            report.AppendLine("S01 연습 회귀, S02 인덱스, S04 예산 우선, S05 재방문, S06 목표 우선, S07 막다른 길, S09~11 생성/대체/다양성, S13 다른 경로, S17 재현 PASS");
            report.AppendLine("elapsedMs="+watch.Elapsed.TotalMilliseconds.ToString("0.###")); EvidenceP4("P3_MAZE_LOGIC",report.ToString());
        }
        public static void MazeRuleChecks(){RunP3Logic();var report=new System.Text.StringBuilder();RunMazeRules(report);EvidenceP4("P3_RULE_REGRESSION","기존 연습·예산·31/32/35 방문·목표 우선·벽/경계·무소비·대안 경로 검사 PASS");}
        private static void RunMazeRules(System.Text.StringBuilder report)
        {
            PCS_StepPuzzle.Board Fixture(int w, int h, Vector2Int goal, int[] budget, Vector2Int[] walls = null) => new PCS_StepPuzzle.Board { width=w,height=h,start=Vector2Int.zero,goal=goal,budgets=budget,walls=walls??Array.Empty<Vector2Int>() };
            var model = new PCS_StepPuzzle(); var board = Fixture(2,2,Vector2Int.one,new[]{1,1,0,0}); model.Begin(board); model.Move(1,0);
            Require(model.Move(-1,0)==PCS_StepPuzzle.MoveResult.BudgetEmpty && !model.Failed && model.X==1,"S04 예산0 재방문 무실패"); Require(model.Move(0,1)==PCS_StepPuzzle.MoveResult.Succeeded,"S06 마지막 예산 목표 우선");
            board.budgets=new[]{1,1,1,0}; model.Begin(board); model.Move(1,0); Require(model.Move(-1,0)==PCS_StepPuzzle.MoveResult.Failed&&model.X==1&&model.Remaining(2)==1,"S05 재방문 무이동 무소비");
            board.budgets=new[]{2,2,1,1};model.Begin(board);model.Move(1,0);model.Move(0,1);Require(model.Succeeded&&model.Remaining(0)==1&&model.Remaining(1)==1,"S06 남은 예산 성공");
            board=Fixture(3,3,new Vector2Int(2,2),new[]{2,2,0,0},new[]{Vector2Int.one});
            foreach(string route in new[]{"RRUU","UURR"}){model.Begin(board);foreach(var d in route){var v=PCS_StepPuzzle.Directions["RULD".IndexOf(d)];model.Move(v.x,v.y);}Require(model.Succeeded,"S13 대안 경로 성공");}
            board=Fixture(3,3,new Vector2Int(2,2),new[]{2,2,2,2},new[]{new Vector2Int(1,1),new Vector2Int(1,2)});model.Begin(board);model.Move(0,1);model.Move(0,1);Require(model.Failed&&model.FailureReason.Contains("새 칸"),"S07 막다른 길");
            board=Fixture(6,6,new Vector2Int(5,5),new[]{6,6,6,6});board.start=new Vector2Int(1,5);model.Begin(board);model.Move(1,0);model.Move(1,0);model.Move(1,0);model.Move(1,0);Require(model.IsVisited(1,5)&&model.IsVisited(2,5)&&model.IsVisited(5,5)&&model.Count==5&&!model.IsVisited(0,0)&&!model.IsVisited(1,0)&&!model.IsVisited(2,0)&&!model.IsVisited(3,0),"S02 31·32·35 방문 독립");
            void Walk(string steps){foreach(char step in steps){var d=PCS_StepPuzzle.Directions["RULD".IndexOf(step)];model.Move(d.x,d.y);}}
            board=Fixture(4,4,new Vector2Int(3,0),new[]{3,2,0,1},new[]{new Vector2Int(1,0),new Vector2Int(2,0)});
            model.Begin(board);Walk("URRRD");Require(model.Succeeded&&model.Remaining(1)==1,"S16 짧은 경로 예산 안 성공");
            model.Begin(board);Walk("UURRRD");Require(model.Failed&&model.X==3&&model.Y==1&&model.Move(0,-1)==PCS_StepPuzzle.MoveResult.Ignored,"S16 긴 경로 아래 예산 부족 및 잠금");
            board.budgets[3]=2;model.Begin(board);Walk("UURRRDD");Require(model.Succeeded,"S16 아래 예산 변경으로 같은 긴 경로 성공");
            board=Fixture(3,3,new Vector2Int(2,2),new[]{2,2,2,2},new[]{new Vector2Int(1,0)});model.Begin(board);
            Require(model.Move(-1,0)==PCS_StepPuzzle.MoveResult.Outside&&model.Move(1,0)==PCS_StepPuzzle.MoveResult.Wall&&model.Count==1&&Enumerable.Range(0,4).All(d=>model.Remaining(d)==2),"S04 벽과 경계 무이동 무소비");
            Require(model.Move(0,1)==PCS_StepPuzzle.MoveResult.Visited&&model.Remaining(0)==2&&model.Remaining(1)==1&&model.Remaining(2)==2&&model.Remaining(3)==2,"S03 이동 방향만 소비");
            board=Fixture(2,2,Vector2Int.one,new[]{1,1,0,0});board.proof=PCS_StepPuzzle.Route(Vector2Int.zero,"RU");Require(PCS_StepPuzzle.VerifyProof(board,out _),"S08 정상 경로");
            var invalid=board.Copy();invalid.proof[1]=Vector2Int.one;Require(!PCS_StepPuzzle.VerifyProof(invalid,out _),"S08 대각선 경로 거부");
            invalid=board.Copy();invalid.walls=new[]{Vector2Int.right};Require(!PCS_StepPuzzle.VerifyProof(invalid,out _),"S08 경로 벽 거부");invalid=board.Copy();invalid.budgets[1]=0;Require(!PCS_StepPuzzle.VerifyProof(invalid,out _),"S08 예산 부족 거부");
        }
        private static readonly Dictionary<string,string> KoreanText = new Dictionary<string,string> {
            { "Settings error: 2-24 pins, speed (0,720], tolerance (0, half spacing).", "설정 오류: 핀 2~24개, 속도 0 초과 720 이하, 허용 각도 0 초과 핀 간격 절반 미만이어야 합니다." },
            { "Seven tile masks required.", "타일 7개의 핀 마스크가 필요합니다." },
            { "A mask must contain six bits.", "핀 마스크는 0~63 범위여야 합니다." },
            { "TIMING  /  CONNECT EVERY PIN", "타이밍 해킹 / 모든 핀을 연결하세요" },
            { "ROTATION  /  CONNECT ALL SEVEN TILES", "회전 연결 해킹 / 7개 타일을 연결하세요" },
            { "REACHED  {state.reachable} / 7\nOPEN {state.outside}  /  UNPAIRED {state.unmatched}", "연결된 타일 {state.reachable} / 7\n바깥 핀 {state.outside} / 맞지 않는 핀 {state.unmatched}" },
            { "MOCK ALARM  {alarmCount}\nNo real enemies", "모의 경보 {alarmCount}\n실제 적은 나타나지 않습니다" },
            { "CONNECTED  {lastConnected} / {puzzle.ActiveTargetCount}\nMISS  {lastMiss}", "연결한 핀 {lastConnected} / {puzzle.ActiveTargetCount}\n오입력 {lastMiss}" },
            { "TIMING SIGNAL: READY", "타이밍 결과: 대기" },
            { "ROTATION SIGNAL: READY", "회전 연결 결과: 대기" },
            { "WASD / arrows: move the cyan actor.\nE near a terminal ({entryDistance:0.##} units).", "WASD / 방향키: 민트색 조작체 이동\n단말에서 {entryDistance:0.##} 이내일 때 E로 시작" },
            { "{SignalName} SIGNAL: CANCELLED | attempt {AttemptId} | delivered 0", "{SignalName} 결과: 취소됨 | 시도 {AttemptId} | 결과 전달 0회" },
            { "{SignalName} SIGNAL: RUNNING | attempt {AttemptId} | delivered 0", "{SignalName} 결과: 진행 중 | 시도 {AttemptId} | 결과 전달 0회" },
            { "{SignalName} SIGNAL: SUCCESS | attempt {AttemptId} | delivered 1", "{SignalName} 결과: 성공 | 시도 {AttemptId} | 결과 전달 1회" },
            { "Settings error: alarm times must be finite and positive.", "설정 오류: 경보 시간은 양의 유한 값이어야 합니다." },
            { "Click an outer tile: 60 degrees clockwise.\nCenter fixed. Seed {rotationSeed}.", "바깥 타일을 누르면 시계 방향으로 60도 회전합니다.\n중앙은 고정입니다. 시드 {rotationSeed}" },
            { "Press SPACE when the gold pointer meets a pin.", "금색 포인터가 핀과 겹칠 때 Space를 누르세요." },
            { "SUCCESS — result delivered once.\nClose to release the demo actor.", "성공! 결과를 한 번 전달했습니다.\n닫으면 데모 조작체를 다시 움직일 수 있습니다." },
            { "MISS — connections are kept. Try again.", "오입력입니다. 연결은 유지됩니다. 다시 시도하세요." },
            { "Already connected — no penalty.", "이미 연결한 핀입니다. 불이익은 없습니다." },
            { "Pin connected.", "핀을 연결했습니다." },
            { "TIMING terminal in range — press E.", "타이밍 단말에 접근했습니다. E를 누르세요." },
            { "ROTATION terminal in range — press E.", "회전 연결 단말에 접근했습니다. E를 누르세요." },
            { "The small epsilon absorbs float roundoff at an inclusive boundary only.", "경계 포함 판정에서만 작은 부동소수점 반올림 오차를 흡수한다." },
            { "Mouse callbacks precede LateUpdate; cancellation/restart wins the frame.", "마우스 콜백이 먼저 실행되므로 같은 프레임의 취소·재시작을 우선한다." },
            { "A mouse-down from the previous attempt must end before new tile clicks are accepted.", "이전 시도의 마우스 버튼을 놓은 뒤 새 타일 클릭을 받는다." },
            { "The success input wins over an alarm threshold in the same frame.", "같은 프레임에서 성공 입력을 경보보다 우선한다." },
            { "OFFLINE APPROACH DEMO", "오프라인 단말 접근 데모" },
            { "TIMING / E", "타이밍 / E" },
            { "ROTATION / E", "회전 연결 / E" },
            { "DEMO ACTOR", "데모 조작체" },
            { "FOUR PAWS, ASCEND!   /   PCS TIMING LAB", "FOUR PAWS, ASCEND! / PCS 해킹 실습" },
            { "TIMING (P1) — direct start", "타이밍 해킹(P1) 바로 시작" },
            { "ROTATION (P2)", "회전 연결 해킹(P2)" },
            { "Direct start skips the approach requirement.", "바로 시작은 단말 접근 과정을 생략합니다." },
            { "WASD / arrows to move. E near the Timing terminal.", "WASD / 방향키로 이동하고 단말 근처에서 E를 누르세요." },
            { "CONNECTED 0 / 6\nMISS 0", "연결한 핀 0 / 6\n오입력 0" },
            { "MOCK ALARM 0\nNo real enemies", "모의 경보 0\n실제 적은 나타나지 않습니다" },
            { "RESTART / R", "다시 시작 / R" },
            { "CANCEL / ESC", "취소 / Esc" },
            { "FOUR PAWS, ASCEND!   /   PCS STEP LAB", "FOUR PAWS, ASCEND! / PCS 발판 미로" },
            { "3 x 3 grid — visit every tile once.\nWASD / arrow keys only. No clicking tiles, jumping or diagonal moves.", "선택한 모드의 목표와 규칙을 확인하세요.\nWASD / 방향키만 사용합니다. 클릭 이동·점프·대각선 이동은 없습니다." },
            { "ACTIVATED 0 / 9", "활성화한 발판 0 / 9" },
            { "Begin a new 3 x 3 attempt.", "시작 버튼을 누르세요." },
            { "Example route:\nRight x2, Up, Left x2, Up, Right x2", "연습 경로: 오른쪽 2회 → 위 → 왼쪽 2회\n→ 위 → 오른쪽 2회" },
            { "BEGIN (P3)", "미로 시작(P3)" },
            { "STEP SIGNAL: READY", "발판 결과: 대기" },
            { "Tools/PCS/Demos/P1 Create or Validate", "Tools/PCS/데모/P1 생성 또는 검사" },
            { "Tools/PCS/Demos/P1 Logic Checks", "Tools/PCS/데모/P1 논리 검사" },
            { "Tools/PCS/Demos/P2 Add or Validate", "Tools/PCS/데모/P2 추가 또는 검사" },
            { "Tools/PCS/Demos/P3 Create or Validate", "Tools/PCS/데모/P3 생성 또는 검사" },
            { "RequireComponent may repair the loaded object without persisting it.", "RequireComponent의 메모리 보완이 저장 자산에 반영됐는지 별도로 확인한다." },
            { "FIXED", "고정" },
            { "CLOSE", "닫기" },
            { "ACTOR", "조작체" },
        };

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("PCS 자산 작성 중단: " + message);
        }
    }
}

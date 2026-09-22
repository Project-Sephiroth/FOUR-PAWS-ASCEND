using System;
using System.Linq;
using Fusion;
using Fusion.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static partial class PcsSecondPassTools
{
    private static void ConnectPlayerPrefabs()
    {
        var roles = new[] { MyEnum.CharacterType.Rabbit, MyEnum.CharacterType.Bear, MyEnum.CharacterType.Mouse, MyEnum.CharacterType.Frog };
        float[] speeds = { 4, 2.6f, 3.2f, 3.4f };
        float[] jumps = { 12, 8, 8, 9 };
        for (int i = 0; i < roles.Length; i++)
        {
            string path = "Assets/Prefabs/Player/Player_" + roles[i] + ".prefab";
            var prefab = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var abilities = Component<PcsPlayerAbilities>(prefab);
                Property(abilities, "characterType", (int)roles[i]);
                Property(abilities, "pullRange", 7f);
                var mover = prefab.GetComponent<Mover>();
                Property(mover, "speed", speeds[i]);
                Property(mover, "jumpForce", jumps[i]);
                new NetworkObjectBakerEditTime().Bake(prefab);
                PrefabUtility.SaveAsPrefabAsset(prefab, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
        }
        var camera = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).First(c => c.gameObject.scene == SceneManager.GetActiveScene());
        ApplyCameraRegions(camera);
    }

    private static void ConnectNetworkPrefabs()
    {
        const string runnerPath = "Assets/Prefabs/Network/Network Runner.prefab";
        var runner = PrefabUtility.LoadPrefabContents(runnerPath);
        try
        {
            // Replace the obsolete sample callback, preserving the existing Runner and scene manager.
            var oldSpawner = runner.GetComponents<MonoBehaviour>().FirstOrDefault(c => c != null && c.GetType().Name == "PlayerSpawner");
            if (oldSpawner != null) Object.DestroyImmediate(oldSpawner);
            var spawner = Component<PcsPlayerSpawner>(runner);
            var serialized = new SerializedObject(spawner);
            var characters = serialized.FindProperty("characters");
            string[] names = { "Rabbit", "Bear", "Mouse", "Frog" };
            characters.arraySize = names.Length;
            for (int i = 0; i < names.Length; i++)
                characters.GetArrayElementAtIndex(i).objectReferenceValue = AssetDatabase.LoadAssetAtPath<CharacterSO>("Assets/Scripts/EoJin/CharacterSO/" + names[i] + ".asset");
            serialized.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(runner, runnerPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(runner); }
        const string managerPath = "Assets/Prefabs/Network/NetworkGameManager.prefab";
        var manager = PrefabUtility.LoadPrefabContents(managerPath);
        try
        {
            var networkObject = manager.GetComponent<NetworkObject>();
            networkObject.Flags = (networkObject.Flags | NetworkObjectFlags.MasterClientObject) & ~NetworkObjectFlags.DestroyWhenStateAuthorityLeaves;
            new NetworkObjectBakerEditTime().Bake(manager);
            PrefabUtility.SaveAsPrefabAsset(manager, managerPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(manager); }
        var lobby = EditorSceneManager.OpenScene("Assets/Scenes/LobbyScene.unity", OpenSceneMode.Additive);
        try
        {
            foreach (var root in lobby.GetRootGameObjects())
            foreach (var start in root.GetComponentsInChildren<GameStartUI>(true))
            {
                Property(start, "gameplayScene", "Puzzle");
                PrefabUtility.RecordPrefabInstancePropertyModifications(start);
            }
            EditorSceneManager.MarkSceneDirty(lobby);
            EditorSceneManager.SaveScene(lobby);
        }
        finally { EditorSceneManager.CloseScene(lobby, true); }
    }

    // Called by the existing prefab connection step and by the bounded camera-only command.
    private static void ApplyCameraRegions(Camera camera)
    {
        var follow = Component<PcsLocalCameraFollow>(camera.gameObject);
        var values = new[]
        {
            new PcsLocalCameraFollow.CameraRegion { name = "Late join waiting", selectionBounds = new Rect(-49, -5, 6, 9), viewBounds = new Rect(-49.5f, -4.2f, 7, 8.2f), orthographicSize = 3.5f },
            new PcsLocalCameraFollow.CameraRegion { name = "Rabbit tutorial", role = MyEnum.CharacterType.Rabbit, selectionBounds = new Rect(-43.5f, -4, 23.5f, 9.5f), viewBounds = new Rect(-43.6f, -3.8f, 24, 9.2f), orthographicSize = 4.2f },
            new PcsLocalCameraFollow.CameraRegion { name = "Mouse tutorial", role = MyEnum.CharacterType.Mouse, selectionBounds = new Rect(-43.5f, 5, 23.5f, 9.5f), viewBounds = new Rect(-43.6f, 5.2f, 24, 9.2f), orthographicSize = 4.2f },
            new PcsLocalCameraFollow.CameraRegion { name = "Frog tutorial", role = MyEnum.CharacterType.Frog, selectionBounds = new Rect(-43.5f, 14, 23.5f, 9.5f), viewBounds = new Rect(-43.6f, 14.2f, 24, 9.2f), orthographicSize = 4.2f },
            new PcsLocalCameraFollow.CameraRegion { name = "Bear tutorial", role = MyEnum.CharacterType.Bear, selectionBounds = new Rect(-43.5f, 23, 23.5f, 9.5f), viewBounds = new Rect(-43.6f, 23.2f, 24, 9.2f), orthographicSize = 4.2f },
            new PcsLocalCameraFollow.CameraRegion { name = "Tutorial return lane", selectionBounds = new Rect(-20.5f, -4, 4.3f, 37), viewBounds = new Rect(-22, -3.8f, 10.5f, 37.3f), orthographicSize = 4.2f },
            new PcsLocalCameraFollow.CameraRegion { name = "Stage 1-1", selectionBounds = new Rect(-13, -5, 27, 12), viewBounds = new Rect(-13.5f, -3.9f, 27.5f, 10.1f), orthographicSize = 6f },
            new PcsLocalCameraFollow.CameraRegion { name = "Connection ladder", selectionBounds = new Rect(-18.5f, -4, 10.9f, 22.8f), viewBounds = new Rect(-18.7f, -3.8f, 20.7f, 22.8f), orthographicSize = 5.3f },
            new PcsLocalCameraFollow.CameraRegion { name = "Stage 1-2 and exit", selectionBounds = new Rect(-13, 12, 26, 54), viewBounds = new Rect(-12, 13.7f, 24, 50.9f), orthographicSize = 6f }
        };
        var serialized = new SerializedObject(follow);
        serialized.FindProperty("fallbackOrthographicSize").floatValue = 5.6f;
        serialized.FindProperty("playerViewMargin").floatValue = 1.2f;
        var regions = serialized.FindProperty("regions");
        regions.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
        {
            var entry = regions.GetArrayElementAtIndex(i);
            entry.FindPropertyRelative("name").stringValue = values[i].name;
            entry.FindPropertyRelative("section").intValue = -1;
            entry.FindPropertyRelative("role").intValue = (int)values[i].role;
            entry.FindPropertyRelative("selectionBounds").rectValue = values[i].selectionBounds;
            entry.FindPropertyRelative("viewBounds").rectValue = values[i].viewBounds;
            entry.FindPropertyRelative("orthographicSize").floatValue = values[i].orthographicSize;
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
        camera.orthographicSize = 5.6f;
        EditorUtility.SetDirty(follow);
        EditorUtility.SetDirty(camera);
    }

    public static void ConfigureCameraRegions()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play before configuring the camera.");
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/Puzzle.unity")
            throw new InvalidOperationException("Open the saved Puzzle scene before configuring its camera.");
        if (scene.isDirty)
            throw new InvalidOperationException("Save existing Puzzle edits before configuring its camera.");
        var camera = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>(true))
            .Single(c => c.enabled && c.gameObject.activeInHierarchy && c.CompareTag("MainCamera"));
        ApplyCameraRegions(camera);
        ValidateCameraRegions();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save Puzzle camera regions.");
    }

    public static void ValidateCameraRegions()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/Puzzle.unity" || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Camera validation requires Puzzle in Edit mode.");
        var follow = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PcsLocalCameraFollow>(true))
            .Single(c => c.gameObject.activeInHierarchy && c.CompareTag("MainCamera"));
        var camera = follow.GetComponent<Camera>();
        Vector3 before = camera.transform.position;
        float beforeSize = camera.orthographicSize;
        var settings = new SerializedObject(follow);
        var regions = settings.FindProperty("regions");
        if (regions.arraySize != 9) throw new InvalidOperationException("Expected nine configured spatial camera regions.");
        int checks = 0;
        var lines = new System.Collections.Generic.List<string>();
        System.Action<bool, string> require = (condition, label) =>
        {
            if (!condition) throw new InvalidOperationException("Camera validation failed: " + label);
            checks++;
        };
        Vector2[] route =
        {
            new Vector2(-40.5f, -2.85f), new Vector2(-40.5f, 6.15f), new Vector2(-40.5f, 15.15f), new Vector2(-40.5f, 24.15f),
            new Vector2(-23, .85f), new Vector2(-22.5f, 6.15f), new Vector2(-22.5f, 15.15f), new Vector2(-22.5f, 24.15f),
            new Vector2(-17.4f, 26), new Vector2(-17.4f, -2.85f), new Vector2(-14.6f, -2.85f),
            new Vector2(-11, -2.85f), new Vector2(-7, -2.85f), new Vector2(0, -.65f), new Vector2(10, -.35f),
            new Vector2(3.8f, .8f), new Vector2(-9.5f, -.05f), new Vector2(-15.65f, .15f), new Vector2(-15.65f, 14.9f),
            new Vector2(-7.7f, 15.15f), new Vector2(-6, 15.15f), new Vector2(-4, 35.15f), new Vector2(-4, 60.15f),
            new Vector2(6, 15.15f), new Vector2(2.7f, 17.35f), new Vector2(7.7f, 19.55f), new Vector2(7.7f, 59.15f),
            new Vector2(6, 60.15f), new Vector2(-9, 60.15f), new Vector2(-46, -2.85f), new Vector2(100, 100)
        };
        float[] aspects = { 4f / 3f, 16f / 9f, 21f / 9f };
        foreach (float aspect in aspects)
        for (int role = 1; role <= 4; role++)
        foreach (Vector3 actor in route)
        {
            Vector3 result = follow.CalculateView(actor, aspect, -1, (MyEnum.CharacterType)role, out float size);
            require(!float.IsNaN(result.x) && !float.IsInfinity(result.x) && !float.IsNaN(result.y) && !float.IsInfinity(result.y), "finite position");
            require(Mathf.Abs(result.z - before.z) < .0001f, "camera Z preserved");
            require(Mathf.Abs(actor.x - result.x) <= size * aspect - 1.2f + .0001f &&
                Mathf.Abs(actor.y - result.y) <= size - 1.2f + .0001f, "route actor and margin stay visible");
        }
        var limiter = typeof(PcsLocalCameraFollow).GetMethod("ConstrainPosition", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        require(limiter != null, "runtime smoothing limiter exists");
        for (int i = 0; i < regions.arraySize; i++)
        {
            var entry = regions.GetArrayElementAtIndex(i);
            var definition = new PcsLocalCameraFollow.CameraRegion
            {
                section = entry.FindPropertyRelative("section").intValue,
                role = (MyEnum.CharacterType)entry.FindPropertyRelative("role").intValue,
                selectionBounds = entry.FindPropertyRelative("selectionBounds").rectValue,
                viewBounds = entry.FindPropertyRelative("viewBounds").rectValue,
                orthographicSize = entry.FindPropertyRelative("orthographicSize").floatValue
            };
            require(definition.selectionBounds.width > 0 && definition.selectionBounds.height > 0 &&
                definition.viewBounds.width > 0 && definition.viewBounds.height > 0, "positive bounds");
            foreach (float aspect in aspects)
            {
                Vector3 actor = definition.selectionBounds.center;
                follow.CalculateView(actor, aspect, -1, definition.role, out float selectedSize);
                require(Mathf.Abs(selectedSize - definition.orthographicSize) < .0001f, "region center selects intended zoom");
                // Use a far previous camera position and a shrinking viewport, as at a region transition.
                Vector3 result = (Vector3)limiter.Invoke(null, new object[] {
                    new Vector3(1000, -1000, before.z), actor, definition, definition.orthographicSize, aspect, 1.2f });
                require(Mathf.Abs(actor.x - result.x) <= definition.orthographicSize * aspect - 1.2f + .0001f &&
                    Mathf.Abs(actor.y - result.y) <= definition.orthographicSize - 1.2f + .0001f, "post-smoothing actor visibility");
                require(Mathf.Abs(result.z - before.z) < .0001f, "post-smoothing Z");
            }
            lines.Add(entry.FindPropertyRelative("name").stringValue + ": size=" + definition.orthographicSize + ", world bounds=" + definition.viewBounds);
        }
        var temporary = new GameObject("PCS camera fallback validation") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            temporary.transform.position = new Vector3(9, 8, -17);
            var fallbackCamera = temporary.AddComponent<Camera>();
            fallbackCamera.orthographic = true;
            fallbackCamera.orthographicSize = 6.75f;
            var fallback = temporary.AddComponent<PcsLocalCameraFollow>();
            Vector3 result = fallback.CalculateView(new Vector3(100, -50, 0), 16f / 9f, 2, MyEnum.CharacterType.Mouse, out float size);
            require(result == new Vector3(100, -49, -17) && Mathf.Abs(size - 6.75f) < .0001f, "empty regions preserve legacy follow and zoom");
            require(temporary.transform.position == new Vector3(9, 8, -17) && Mathf.Abs(fallbackCamera.orthographicSize - 6.75f) < .0001f, "calculation does not move or zoom legacy camera");
        }
        finally { Object.DestroyImmediate(temporary); }
        require(camera.transform.position == before && Mathf.Abs(camera.orthographicSize - beforeSize) < .0001f, "saved camera was not moved or zoomed by validation");
        lines.Insert(0, "PASS " + checks + " camera geometry checks. Editor calculations only; real Play transitions and readability remain manual checks.");
        System.IO.Directory.CreateDirectory(EvidenceDirectory);
        System.IO.File.WriteAllLines(System.IO.Path.Combine(EvidenceDirectory, "camera-regions-validation.txt"), lines);
    }

}

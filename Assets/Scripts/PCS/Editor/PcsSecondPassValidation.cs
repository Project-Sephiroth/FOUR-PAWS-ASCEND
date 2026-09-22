using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Fusion;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public static partial class PcsSecondPassTools
{
    [Serializable] private class ValidationResult
    {
        public string utc;
        public string unity;
        public string scene;
        public string scope = "Editor references, geometry and Fusion bake only; not Play or multiplayer proof.";
        public bool passed;
        public List<string> checks = new List<string>();
        public List<string> failures = new List<string>();
    }

    private static void ValidatePuzzle()
    {
        var scene = SceneManager.GetActiveScene();
        var result = new ValidationResult { utc = DateTime.UtcNow.ToString("O"), unity = Application.unityVersion, scene = scene.path };
        Action<bool, string> check = (ok, message) => { (ok ? result.checks : result.failures).Add(message); };
        try
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != "Assets/Scenes/Puzzle.unity")
                throw new InvalidOperationException("Validation needs the active Puzzle scene outside Play mode.");

            var transforms = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
            var active = transforms.Where(t => t.gameObject.activeInHierarchy).ToArray();
            foreach (var transform in active)
            {
                check(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) == 0,
                    "Missing-script check: " + ValidationPath(transform));
                check(Finite(transform.position) && Finite(transform.lossyScale), "Finite transform: " + ValidationPath(transform));
                foreach (var component in transform.GetComponents<Component>())
                {
                    if (component == null) continue;
                    var iterator = new SerializedObject(component).GetIterator();
                    while (iterator.Next(true))
                    {
                        if (iterator.propertyType != SerializedPropertyType.ObjectReference) continue;
                        var reference = iterator.objectReferenceValue;
                        if (reference == null)
                        {
                            if (iterator.objectReferenceInstanceIDValue != 0)
                                check(false, "Missing object reference: " + ValidationPath(transform) + "." + iterator.propertyPath);
                            continue;
                        }
                        var referenceObject = reference as GameObject;
                        if (reference is Component referencedComponent) referenceObject = referencedComponent.gameObject;
                        if (referenceObject != null && !EditorUtility.IsPersistent(referenceObject))
                            check(referenceObject.scene == scene, "Scene-local reference: " + ValidationPath(transform) + "." + iterator.propertyPath);
                    }
                }
            }

            var directors = active.Select(t => t.GetComponent<PcsPuzzleDirector>()).Where(d => d != null).ToArray();
            check(directors.Length == 1, "Exactly one active PcsPuzzleDirector");
            if (directors.Length != 1) throw new InvalidOperationException("Cannot inspect the device registry without one director.");
            var puzzle = directors[0];
            var registry = puzzle.Devices ?? Array.Empty<PcsPuzzleDevice>();
            check(registry.Length > 0, "Nonempty device registry");
            check(registry.Length <= 128, "Device registry fits the network-state capacity of 128");
            check(registry.Where(d => d != null).Select(d => d.DeviceId).Distinct().Count() == registry.Length, "Unique, non-null device IDs");
            var sceneDevices = active.Select(t => t.GetComponent<PcsPuzzleDevice>()).Where(d => d != null).ToArray();
            check(sceneDevices.Length == registry.Length && sceneDevices.All(registry.Contains), "Every active device belongs to the director registry");

            for (int index = 0; index < registry.Length; index++)
            {
                var device = registry[index];
                if (device == null) continue;
                check(device.DeviceId == index, "Device ID matches network-array index: " + device.name);
                check(device.Section >= -1 && device.Section <= 2, "Device section range: " + device.name);
                check(device.Links != null && device.Links.All(d => d != null && registry.Contains(d)), "Device links stay in registry: " + device.name);
                check(device.Channel >= 0 && device.Channel < 4, "Channel index range: " + device.name);
                check(Finite(device.Bounds.center) && Finite(device.Bounds.size), "Finite device bounds: " + device.name);
                if (device.Trigger != null)
                    check(device.Trigger.isTrigger && device.Trigger.enabled && device.Trigger.bounds.size.sqrMagnitude > 0f, "Live trigger volume: " + device.name);
                if (device.Solid != null)
                    check(!device.Solid.isTrigger && device.Solid.bounds.size.sqrMagnitude > 0f, "Solid collision volume: " + device.name);

                if (device.Kind == PcsDeviceKind.Elevator || device.Kind == PcsDeviceKind.SlidingWall)
                {
                    check(device.Body != null && device.Body.bodyType == RigidbodyType2D.Kinematic && device.Body.simulated,
                        "Driven kinematic Rigidbody2D: " + device.name);
                    check(device.Body != null && (device.Body.constraints & RigidbodyConstraints2D.FreezePositionY) == 0,
                        "Motion body has no Y-position constraint: " + device.name);
                    check(device.LowerStop != null && device.UpperStop != null && device.Speed > 0f, "Configured motion stops/speed: " + device.name);
                    if (device.LowerStop != null && device.UpperStop != null)
                    {
                        check(!device.UpperStop.IsChildOf(device.transform) && !device.LowerStop.IsChildOf(device.transform), "Independent motion targets: " + device.name);
                        check(Mathf.Abs(device.LowerStop.position.x - device.UpperStop.position.x) < 0.01f &&
                              device.UpperStop.position.y > device.LowerStop.position.y, "Upward vertical stop order: " + device.name);
                    }
                    if (device.LiftPolicy == PcsLiftPolicy.StageOneThreeStop)
                        check(device.MiddleStop != null && device.LowerStop != null && device.UpperStop != null &&
                              device.MiddleStop.position.y > device.LowerStop.position.y && device.MiddleStop.position.y < device.UpperStop.position.y,
                            "Stage-one middle stop lies between endpoints: " + device.name);
                    if (device.Elevator != null)
                    {
                        var properties = new SerializedObject(device.Elevator);
                        var column = properties.FindProperty("column").objectReferenceValue as SpriteRenderer;
                        check(properties.FindProperty("upperTarget").objectReferenceValue == device.UpperStop,
                            "Elevator upper-target reference: " + device.name);
                        check(column != null && column.transform.parent == device.transform && column.sprite != null &&
                              column.drawMode == SpriteDrawMode.Simple && !column.flipY,
                            "Elevator direct-child column contract: " + device.name);
                    }
                    if (device.SlidingWall != null)
                        check(new SerializedObject(device.SlidingWall).FindProperty("openTarget").objectReferenceValue == device.UpperStop,
                            "Sliding-wall open-target reference: " + device.name);
                }
                if (device.PressureButton != null)
                {
                    var properties = new SerializedObject(device.PressureButton);
                    check(properties.FindProperty("supportCollider").objectReferenceValue == device.Solid && device.Solid != null,
                        "Pressure support collider reference: " + device.name);
                    var sensor = device.PressureButton.GetComponent<BoxCollider2D>();
                    check(sensor != null && sensor.isTrigger && sensor.gameObject.layer == 0 && sensor != device.Solid,
                        "Separate pressure trigger uses Default layer: " + device.name);
                    if (sensor != null && device.Solid != null)
                        check(sensor.bounds.min.y <= device.Solid.bounds.max.y + 0.05f && sensor.bounds.max.y > device.Solid.bounds.max.y,
                            "Pressure sensor covers the support top: " + device.name);
                    check(device.RequiredRole == MyEnum.CharacterType.Bear || device.Section == 0, "Bear stage pressure role: " + device.name);
                }
            }

            foreach (var networkObject in active.Select(t => t.GetComponent<NetworkObject>()).Where(n => n != null))
                ValidateNetworkBake(networkObject, check);
            var directorObject = puzzle.GetComponent<NetworkObject>();
            check(directorObject != null && (directorObject.Flags & NetworkObjectFlags.MasterClientObject) != 0 &&
                  (directorObject.Flags & NetworkObjectFlags.DestroyWhenStateAuthorityLeaves) == 0,
                "Director belongs to the Shared master and survives authority departure");

            ValidateSpawnArray(puzzle.TutorialSpawns, "TutorialSpawns", scene, check);
            ValidateSpawnArray(puzzle.StageOneSpawns, "StageOneSpawns", scene, check);
            ValidateSpawnArray(puzzle.StageTwoSpawns, "StageTwoSpawns", scene, check);
            check(puzzle.LateJoinWaiting != null && puzzle.LateJoinWaiting.gameObject.scene == scene, "Late-join safe waiting marker");
            check(active.All(t => t.GetComponent<PlayerInput>() == null), "No active authored test player duplicates the network-spawned player");

            var mainLifts = registry.Where(d => d != null && d.Kind == PcsDeviceKind.Elevator && d.LiftPolicy == PcsLiftPolicy.MainContinuous).ToArray();
            check(mainLifts.Length == 1, "Exactly one continuous main elevator");
            if (mainLifts.Length == 1 && mainLifts[0].Solid != null && mainLifts[0].UpperStop != null)
            {
                var lift = mainLifts[0];
                float upperDeckY = lift.Solid.bounds.max.y + lift.UpperStop.position.y - lift.transform.position.y;
                check(Mathf.Abs(upperDeckY - 60f) < 0.03f, "Main elevator final collision surface is Y=60");
                foreach (string floorName in new[] { "Arrival_1_2_Left", "Arrival_1_2_Right" })
                {
                    var floor = active.FirstOrDefault(t => t.name == floorName)?.GetComponent<Collider2D>();
                    check(floor != null && Mathf.Abs(floor.bounds.max.y - upperDeckY) < 0.03f, "End floor aligns with final elevator: " + floorName);
                }
            }

            for (int role = 1; role <= 4; role++)
                check(registry.Any(d => d != null && d.Kind == PcsDeviceKind.TutorialExit && (int)d.RequiredRole == role), "Tutorial exit exists for role " + role);
            check(registry.Any(d => d != null && d.Kind == PcsDeviceKind.Exit && d.Section == 1 && d.RequireAllRoles), "Stage-one team exit");
            check(registry.Any(d => d != null && d.Kind == PcsDeviceKind.Checkpoint && d.RequireAllRoles), "Separate team checkpoint");
            check(registry.Any(d => d != null && d.Kind == PcsDeviceKind.Exit && d.Section == 2 && d.RequireAllRoles), "Four-player final exit");
            check(registry.Any(d => d != null && d.Kind == PcsDeviceKind.Arrival && d.RequiredRolesMask == 28) &&
                  registry.Any(d => d != null && d.Kind == PcsDeviceKind.Arrival && d.RequiredRolesMask == 2), "Separate left-three and Rabbit arrival conditions");

            var timedPlatforms = registry.Where(d => d != null && d.Kind == PcsDeviceKind.TimedPlatform && d.Section == 2).ToArray();
            for (int channel = 0; channel < 4; channel++)
                check(timedPlatforms.Any(d => d.Channel == channel), "A shaft platform exists on channel " + channel);
            foreach (var platform in timedPlatforms)
                check(registry.Any(d => d != null && d.Kind == PcsDeviceKind.RemoteButton && d.Channel == platform.Channel && d.Links != null && d.Links.Contains(platform)),
                    "D22 same-channel remote button reaches platform: " + platform.name);
            check(registry.Any(d => d != null && d.Kind == PcsDeviceKind.ChannelConsole && d.RequiredRole == MyEnum.CharacterType.Mouse), "Mouse channel console exists");

            ValidatePlayerAndRunnerAssets(check);
            foreach (string path in new[] { "Assets/Scenes/LobbyScene.unity", "Assets/Scenes/GameScene.unity", "Assets/Scenes/Puzzle.unity" })
                check(EditorBuildSettings.scenes.Any(s => s.enabled && s.path == path), "Enabled build scene: " + path);
        }
        catch (Exception exception)
        {
            result.failures.Add(exception.GetType().Name + ": " + exception.Message);
        }
        result.passed = result.failures.Count == 0;
        Directory.CreateDirectory(EvidenceDirectory);
        File.WriteAllText(Path.Combine(EvidenceDirectory, "validation.json"), JsonUtility.ToJson(result, true));
        if (!result.passed)
            throw new InvalidOperationException("Puzzle validation failed: " + string.Join(" | ", result.failures));
        Debug.Log("Puzzle reference/bake validation passed. Runtime and multiplayer validation remain separate.");
    }

    private static void ValidateSpawnArray(Transform[] spawns, string name, Scene scene, Action<bool, string> check)
    {
        check(spawns != null && spawns.Length >= 5, name + " has enum-indexed role slots");
        if (spawns == null || spawns.Length < 5) return;
        for (int role = 1; role <= 4; role++)
            check(spawns[role] != null && spawns[role].gameObject.scene == scene && Finite(spawns[role].position), name + " role " + role + " local marker");
    }

    private static void ValidateNetworkBake(NetworkObject networkObject, Action<bool, string> check)
    {
        var behaviours = networkObject.GetComponentsInChildren<NetworkBehaviour>(true)
            .Where(b => b.GetComponentInParent<NetworkObject>(true) == networkObject).ToArray();
        var baked = networkObject.NetworkedBehaviours;
        bool valid = baked != null && baked.All(b => b != null) && behaviours.Length == baked.Length && behaviours.All(baked.Contains);
        string[] expectedDescriptions = behaviours.Select(b => b.GetType().Name + "@" + ValidationPath(b.transform)).ToArray();
        string[] bakedDescriptions = baked == null ? new[] { "<null-array>" } :
            baked.Select(b => b == null ? "<null-entry>" : b.GetType().Name + "@" + ValidationPath(b.transform)).ToArray();
        check(valid, "Fusion baked behaviour table: " + networkObject.name +
            " asset=" + AssetDatabase.GetAssetPath(networkObject) +
            " expectedCount=" + behaviours.Length + " bakedCount=" + (baked == null ? -1 : baked.Length) +
            " expected=[" + string.Join(",", expectedDescriptions) + "] baked=[" + string.Join(",", bakedDescriptions) + "]");
    }

    private static void ValidatePlayerAndRunnerAssets(Action<bool, string> check)
    {
        var roleNames = new[] { "Rabbit", "Bear", "Mouse", "Frog" };
        for (int index = 0; index < roleNames.Length; index++)
        {
            string role = roleNames[index];
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player/Player_" + role + ".prefab");
            check(prefab != null, "Player prefab exists: " + role);
            if (prefab == null) continue;
            var ability = prefab.GetComponent<PcsPlayerAbilities>();
            check(ability != null && (int)ability.CharacterType == index + 1, "Prefab role matches selection enum: " + role);
            var capsule = prefab.GetComponent<CapsuleCollider2D>();
            check(capsule != null && !capsule.isTrigger && capsule.size.x > 0 && capsule.size.y > 0, "Player capsule geometry: " + role);
            var body = prefab.GetComponent<Rigidbody2D>();
            check(body != null && body.bodyType == RigidbodyType2D.Dynamic && body.simulated && body.gravityScale > 0, "Player dynamic body: " + role);
            var mover = prefab.GetComponent<Mover>();
            check(mover != null && mover.MoveSpeed > 0 && mover.JumpSpeed > 0, "Configured player movement: " + role);
            var character = AssetDatabase.LoadAssetAtPath<CharacterSO>("Assets/Scripts/EoJin/CharacterSO/" + role + ".asset");
            check(character != null && character.Prefab == prefab && (int)character.CharacterType == index + 1, "CharacterSO selects the matching prefab: " + role);
            var networkObject = prefab.GetComponent<NetworkObject>();
            check(networkObject != null, "Player NetworkObject: " + role);
            if (networkObject != null) ValidateNetworkBake(networkObject, check);
        }

        var runner = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Network/Network Runner.prefab");
        check(runner != null, "Runner prefab exists");
        if (runner != null)
        {
            check(runner.GetComponents<NetworkSceneManagerDefault>().Length == 1, "Exactly one Runner scene manager");
            var spawners = runner.GetComponents<PcsPlayerSpawner>();
            check(spawners.Length == 1 && runner.GetComponents<MonoBehaviour>().All(c => c == null || c.GetType().Name != "PlayerSpawner"), "Exactly one project-owned player spawner");
            if (spawners.Length == 1)
            {
                var serialized = new SerializedObject(spawners[0]);
                var characters = serialized.FindProperty("characters");
                var roles = new HashSet<MyEnum.CharacterType>();
                if (characters != null)
                    for (int i = 0; i < characters.arraySize; i++)
                        if (characters.GetArrayElementAtIndex(i).objectReferenceValue is CharacterSO character) roles.Add(character.CharacterType);
                check(roles.Count == 4 && !roles.Contains(MyEnum.CharacterType.None), "Runner spawner has all four CharacterSO references");
            }
        }
        var manager = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Network/NetworkGameManager.prefab");
        var managerObject = manager != null ? manager.GetComponent<NetworkObject>() : null;
        check(managerObject != null && (managerObject.Flags & NetworkObjectFlags.MasterClientObject) != 0 &&
              (managerObject.Flags & NetworkObjectFlags.DestroyWhenStateAuthorityLeaves) == 0, "Persistent Shared master NetworkGameManager flags");
        if (managerObject != null) ValidateNetworkBake(managerObject, check);
    }

    private static bool Finite(Vector3 value) => !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
        !float.IsNaN(value.y) && !float.IsInfinity(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.z);

    private static string ValidationPath(Transform transform) => transform.parent == null ? transform.name : ValidationPath(transform.parent) + "/" + transform.name;

}

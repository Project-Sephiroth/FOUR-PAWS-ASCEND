using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static partial class PcsSecondPassTools
{
    private static readonly string LayoutEvidence = Path.Combine(Path.GetTempPath(), "FourPawsLayoutRefinement");
    [Serializable] private class LayoutSnapshot { public string utc, scene; public bool dirty; public LayoutNode[] nodes; public string director; public string[] registry; }
    [Serializable] private class LayoutNode
    {
        public string path, id, name, text, sprite, device, elevator; public bool active;
        public Vector3 position, localPosition, rotation, scale, spriteMin, spriteMax;
        public string[] components, links; public LayoutCollider[] colliders; public int deviceId = -1; public Vector3 lower, middle, upper;
    }
    [Serializable] private class LayoutCollider { public string type; public bool enabled, trigger; public Vector2 offset, size; public Vector3 min, max; }
    private static string LayoutPath(Transform t) => t.parent == null ? t.name : LayoutPath(t.parent) + "/" + t.name;
    private static Transform[] LayoutTransforms() => SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
    private static void InspectCurrentLayout()
    {
        Physics2D.SyncTransforms();
        var scene = SceneManager.GetActiveScene();
        var snapshot = new LayoutSnapshot { utc = DateTime.UtcNow.ToString("O"), scene = scene.path, dirty = scene.isDirty };
        snapshot.nodes = LayoutTransforms().Select(t =>
        {
            var sprite = t.GetComponent<SpriteRenderer>(); var device = t.GetComponent<PcsPuzzleDevice>(); var text = t.GetComponent<TextMesh>();
            return new LayoutNode
            {
                path = LayoutPath(t), name = t.name, id = GlobalObjectId.GetGlobalObjectIdSlow(t).ToString(), active = t.gameObject.activeInHierarchy,
                position = t.position, localPosition = t.localPosition, rotation = t.eulerAngles, scale = t.lossyScale,
                components = t.GetComponents<Component>().Select(c => c == null ? "MISSING" : c.GetType().Name).ToArray(),
                text = text != null ? text.text : null, sprite = sprite != null && sprite.sprite != null ? sprite.sprite.name : null,
                spriteMin = sprite != null ? sprite.bounds.min : Vector3.zero, spriteMax = sprite != null ? sprite.bounds.max : Vector3.zero,
                device = device != null ? EditorJsonUtility.ToJson(device) : null,
                deviceId = device != null ? device.DeviceId : -1, links = device != null ? device.Links.Select(l => l != null ? LayoutPath(l.transform) : "NULL").ToArray() : null,
                lower = device != null && device.LowerStop != null ? device.LowerStop.position : Vector3.zero,
                middle = device != null && device.MiddleStop != null ? device.MiddleStop.position : Vector3.zero,
                upper = device != null && device.UpperStop != null ? device.UpperStop.position : Vector3.zero,
                elevator = t.GetComponent<PcsElevator>() != null ? EditorJsonUtility.ToJson(t.GetComponent<PcsElevator>()) : null,
                colliders = t.GetComponents<Collider2D>().Select(c => new LayoutCollider { type = c.GetType().Name, enabled = c.enabled, trigger = c.isTrigger, offset = c.offset, size = c is BoxCollider2D b ? b.size : Vector2.zero, min = c.bounds.min, max = c.bounds.max }).ToArray()
            };
        }).ToArray();
        var d = UnityEngine.Object.FindFirstObjectByType<PcsPuzzleDirector>();
        snapshot.director = d != null ? EditorJsonUtility.ToJson(d) : null;
        snapshot.registry = d != null ? d.Devices.Select(v => v != null ? LayoutPath(v.transform) : "NULL").ToArray() : null;
        Directory.CreateDirectory(LayoutEvidence);
        File.WriteAllText(Path.Combine(LayoutEvidence, "layout-snapshot.json"), JsonUtility.ToJson(snapshot, true));
    }

    private static Transform LayoutObject(long fileId)
    {
        return LayoutTransforms().Single(t => (long)GlobalObjectId.GetGlobalObjectIdSlow(t).targetObjectId == fileId);
    }
    private static Transform LayoutNamed(string name) => LayoutTransforms().Single(t => t.name == name);
    private static Transform LayoutStop(string name, Transform parent, Vector3 position)
    {
        var existing = parent.Find(name);
        var result = existing != null ? existing : new GameObject(name).transform;
        result.SetParent(parent, true); result.position = position; return result;
    }
    private static void LayoutOneWay(BoxCollider2D collider)
    {
        var effector = collider.GetComponent<PlatformEffector2D>();
        if (effector == null) effector = collider.gameObject.AddComponent<PlatformEffector2D>();
        effector.useOneWay = true; effector.useOneWayGrouping = true; effector.useSideFriction = false;
        effector.useSideBounce = false; effector.surfaceArc = 150; collider.usedByEffector = true;
    }
    private static void LayoutSmallLanding(string name, Transform parent, Vector2 center, float width)
    {
        Transform t = parent.Find(name);
        if (t == null) t = new GameObject(name).transform;
        t.SetParent(parent, true); t.rotation = Quaternion.identity;
        t.localScale = new Vector3(width / 7.86f, .22f, 1);
        t.position = new Vector3(center.x - .33f * t.localScale.x, center.y - 2.125f * t.localScale.y, 0);
        var source = LayoutObject(1093438917).GetComponent<SpriteRenderer>();
        var renderer = Component<SpriteRenderer>(t.gameObject);
        renderer.sprite = source.sprite; renderer.sharedMaterial = source.sharedMaterial; renderer.sortingOrder = 2;
        var solid = Component<BoxCollider2D>(t.gameObject);
        solid.offset = new Vector2(.33f, 1.925f); solid.size = new Vector2(7.86f, .4f);
        t.gameObject.layer = LayerMask.NameToLayer("Ground"); LayoutOneWay(solid);
    }
    private static void LayoutRetractDoor(PcsPuzzleDevice gate)
    {
        Physics2D.SyncTransforms();
        float travel = gate.Solid.bounds.max.y - gate.transform.position.y;
        if (travel <= .1f || gate.LowerStop == null || gate.UpperStop == null)
            throw new InvalidOperationException("Door frame cannot retract: " + gate.name);
        gate.LowerStop.position = gate.transform.position;
        gate.UpperStop.position = gate.transform.position + Vector3.up * travel;
        Property(gate, "RetractWithinFrame", true); gate.DisableColliderWhenOpen = true;
    }

    // Explicit, one-time refinement of the user's saved objects, identified by scene file IDs.
    // It never calls BuildPuzzle or any of the old scene/layout builders.
    private static void ApplyCurrentLayoutRefinement()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/Puzzle.unity" || (scene.isDirty && !SessionState.GetBool("PCS.Layout.OwnedMigration", false)))
            throw new InvalidOperationException("Open the latest saved Puzzle before applying the refinement.");
        var d = UnityEngine.Object.FindFirstObjectByType<PcsPuzzleDirector>();
        if (d.Devices.Length != 85) throw new InvalidOperationException("This one-time migration expects the inspected 85-slot registry; inspect instead of re-running it.");
        InspectCurrentLayout();
        if (!File.Exists(Path.Combine(LayoutEvidence, "layout-before.json")))
            File.Copy(Path.Combine(LayoutEvidence, "layout-snapshot.json"), Path.Combine(LayoutEvidence, "layout-before.json"), false);
        SessionState.SetBool("PCS.Layout.OwnedMigration", true);
        Undo.RegisterFullObjectHierarchyUndo(scene.GetRootGameObjects().Single(g => g.name == "Puzzle_Prototype"), "Refine saved PCS layout");
        var registered = d.Devices.ToList();
        var lift = LayoutObject(1991489900).GetComponent<PcsPuzzleDevice>();
        registered[2] = lift;
        d.Devices[3].Links = new[] { lift }; d.Devices[4].Links = new[] { lift };
        Property(d.Devices[4], "RequiresRemoteHold", true);
        lift.UpperStop.position = lift.transform.position;
        Physics2D.SyncTransforms();
        float topOffset = lift.Solid.bounds.max.y - lift.transform.position.y;
        lift.MiddleStop.position = new Vector3(lift.transform.position.x, 2.9f - topOffset, 0);
        lift.LowerStop.position = new Vector3(lift.transform.position.x, -2.85f - topOffset, 0);
        Property(lift.Elevator, "columnFixedAtTop", true);
        // The same authored wall now opens vertically at its new position.
        var wall = d.Devices[0]; wall.LowerStop.position = wall.transform.position;
        wall.UpperStop.position = wall.transform.position + Vector3.up * (wall.Solid.bounds.size.y + .15f);
        Property(wall.SlidingWall, "openTarget", wall.UpperStop);
        d.Devices[1].Links = new[] { wall };
        // Duplicate button artwork remains in the Bear room; it must not operate a different room's door.
        var copy = LayoutObject(331477350).GetComponent<PcsPuzzleDevice>();
        copy.enabled = false; copy.DeviceId = -1; copy.Links = Array.Empty<PcsPuzzleDevice>();
        copy.PressureButton.enabled = false;
        foreach (var sensor in copy.GetComponents<Collider2D>()) if (sensor.isTrigger) sensor.enabled = false;
        // Reuse the user's ceiling lever copy as the Frog tutorial's distinct one-shot lever.
        var frogLever = LayoutObject(2051156750).GetComponent<PcsPuzzleDevice>();
        frogLever.transform.SetParent(LayoutNamed("Tutorial_Frog"), true);
        frogLever.name = "Tutorial_Frog_CeilingLever"; frogLever.DeviceId = registered.Count;
        frogLever.Section = 0; frogLever.RequiredRole = MyEnum.CharacterType.Frog; frogLever.Kind = PcsDeviceKind.Lever;
        Property(frogLever, "LatchOnActivate", true); Property(frogLever, "RequiresRemoteHold", false);
        frogLever.Links = new[] { d.Devices[71] }; registered.Add(frogLever);
        d.Devices[69].Kind = PcsDeviceKind.Anchor;
        d.Devices[70].RequiredDummyCount = 0; d.Devices[70].Links = new[] { d.Devices[68], d.Devices[69], frogLever };
        d.Devices[70].transform.position = new Vector3(-19.35f, 17, 0);
        ((BoxCollider2D)d.Devices[70].Trigger).size = new Vector2(1.6f, 4.4f);
        d.Devices[71].Links = new[] { frogLever }; d.Devices[71].RequireAllLinks = true;
        d.Devices[67].gameObject.SetActive(false);
        LayoutSmallLanding("Tutorial_Frog_AnchorLanding_1", d.Devices[68].transform.parent, new Vector2(-29.88f, 18.35f), 1.7f);
        LayoutSmallLanding("Tutorial_Frog_AnchorLanding_2", d.Devices[69].transform.parent, new Vector2(-25.64f, 19.93f), 1.7f);
        // A second hack deploys the existing ladder beside the wall, including its real trigger.
        var ladder = d.Devices[64];
        Property(ladder, "DeployableLadder", true); ladder.InitiallyActive = false; ladder.Speed = 1.5f;
        ladder.transform.localScale = new Vector3(1, 5.6f / 3.6f, 1);
        ladder.LowerStop = LayoutStop("Tutorial_Mouse_Ladder_Deployed", ladder.transform.parent, new Vector3(-30.8f, 8.7f, 0));
        ladder.UpperStop = LayoutStop("Tutorial_Mouse_Ladder_Stowed", ladder.transform.parent, new Vector3(-30.8f, 11.6f, 0));
        ladder.transform.position = ladder.UpperStop.position;
        ladder.Trigger.enabled = false;
        ladder.Body = Component<Rigidbody2D>(ladder.gameObject);
        ladder.Body.bodyType = RigidbodyType2D.Kinematic; ladder.Body.gravityScale = 0; ladder.Body.constraints = RigidbodyConstraints2D.FreezeRotation;
        d.Devices[61].Links = new[] { ladder };
        d.Devices[60].Kind = PcsDeviceKind.Instruction; d.Devices[60].Links = Array.Empty<PcsPuzzleDevice>();
        d.Devices[63].Links = new[] { d.Devices[66] };
        d.Devices[66].Links = new[] { d.Devices[59], d.Devices[61], d.Devices[63] };
        d.Devices[65].transform.position = new Vector3(-19.35f, 7, 0);
        ((BoxCollider2D)d.Devices[65].Trigger).size = new Vector2(1.6f, 2.4f);
        // Vertical retraction stays inside each room, including during travel.
        foreach (int id in new[] { 57, 58, 66, 71, 80 }) LayoutRetractDoor(d.Devices[id]);
        // Keep the discarded corridor ladder available in the scene hierarchy, but remove its invisible route.
        d.Devices[11].gameObject.SetActive(false); registered[11] = null;
        // The previous left-side door is a boundary in the new route, not its completion gate.
        d.Devices[7].Kind = PcsDeviceKind.Instruction; d.Devices[7].Links = Array.Empty<PcsPuzzleDevice>();
        // Identify all six manually placed Short-sprite decks by stable file ID, not duplicate names.
        long[] decks = { 663977018, 1093438917, 1580872892, 1334145166, 1388043008, 175336356 };
        foreach (long id in decks)
        {
            var t = LayoutObject(id); var box = t.GetComponent<BoxCollider2D>();
            box.offset = new Vector2(.33f, 1.925f); box.size = new Vector2(7.86f, .4f);
            LayoutOneWay(box);
        }
        // The short turn correction closes the gap created by the old oversized collider.
        LayoutObject(1580872892).position += new Vector3(-.28f, -.7f, 0);
        var assembly = d.Devices[12]; var exit = d.Devices[6];
        foreach (var area in new[] { exit, assembly })
        {
            area.transform.position = new Vector3(0, 16, 0);
            ((BoxCollider2D)area.Trigger).size = new Vector2(21, 2);
            area.RequireAllRoles = true; area.RequiredRolesMask = 30;
        }
        assembly.Links = new[] { exit };
        d.Devices = registered.ToArray();
        ApplyLayoutKorean(d);
        ConfigureLayoutCamera();
        Physics2D.SyncTransforms();
        EditorUtility.SetDirty(d);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save refined Puzzle.");
        SessionState.SetBool("PCS.Layout.OwnedMigration", false);
        InspectCurrentLayout();
        File.Copy(Path.Combine(LayoutEvidence, "layout-snapshot.json"), Path.Combine(LayoutEvidence, "layout-after.json"), true);
        ValidateCurrentLayoutRefinement();
    }

    private static string LayoutKorean(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        var exact = new Dictionary<string, string>
        {
            { "G carry / drop ally   SPACE jump twice\nBring your ally to the exit", "토끼 튜토리얼\nG: 동료 들기·내리기   SPACE: 두 번 점프\n동료와 함께 출구로 이동하세요." },
            { "E E E hack   W / S ladder\nClear all three terminals", "쥐 튜토리얼\nE: 세 번 해킹 → 사다리 전개 → 마지막 문\n사다리에서 W/S로 오르내리세요." },
            { "Aim + F pull / grapple\nRetrieve the yellow ally; bring both to exit", "개구리 튜토리얼\n고리 두 개를 조준하고 F로 이동하세요.\n천장 레버를 당겨 출구로 나가세요." },
            { "G carry / drop   Aim + F throw ally / rock\nKeep one ally on a plate to cross the bridge", "곰 튜토리얼\nG: 들기·내리기   조준+F: 동료·돌 던지기\n동료를 압력판에 두고 다리를 건너세요." },
            { "TEAM ALREADY IN PROGRESS\nRejoin a new room for a new team", "이미 출발한 팀입니다.\n새 방에서 네 역할로 다시 모여 주세요." },
            { "CHECKPOINT  |  ALL FOUR TOGETHER", "체크포인트 · 네 명 모두 모여 주세요." },
            { "FOUR PAWS  |  REUNITE & EXIT LEFT", "네 명이 합류한 뒤 왼쪽 출구로 이동하세요." },
            { "G carry - Aim + F throw ally", "G: 동료 들기 → 조준+F: 던지기" },
            { "A different ally here", "다른 동료로 이 장치를 누르세요." },
            { "MOUSE E E E\nStart ascent", "쥐: E 세 번 · 상승 시작" },
            { "MOUSE 1 2 3 4\nR refill", "쥐: 1·2·3·4 색 선택\nR: 전력 보충" },
            { "RABBIT E\nRelease barrier", "토끼: E · 장벽 해제" },
            { "Battery  G / F", "배터리 · G / F" },
            { "W / S  to rendezvous", "W/S: 합류 지점으로 이동" },
            { "W / S  climb", "W/S: 사다리 오르내리기" },
            { "Glass divider", "유리 격벽" },
            { "E E E  hack", "E: 세 번 눌러 해킹" },
            { "Regroup here before entering the shaft", "체크포인트 · 네 명 모두 모여 주세요." }
        };
        if (exact.TryGetValue(value, out var translated)) return translated;
        return value.Replace("Aim + F", "조준 + F").Replace("WAIT FOR ALL FOUR", "네 명 모두 기다려 주세요.")
            .Replace("Orange", "주황").Replace("Green", "초록").Replace("Purple", "보라").Replace("Cyan", "하늘");
    }
    private static void ApplyLayoutKorean(PcsPuzzleDirector d)
    {
        Font font = AssetDatabase.LoadAssetAtPath<Font>(AssetDatabase.GUIDToAssetPath("315654ded588d0d4c9f91a91dd3b3358"));
        if (font == null) throw new InvalidOperationException("Existing Galmuri7 font is missing.");
        Property(d, "HudFont", font);
        foreach (var device in LayoutTransforms().Select(t => t.GetComponent<PcsPuzzleDevice>()))
            if (device != null) device.Instruction = LayoutKorean(device.Instruction);
        d.Devices[3].Instruction = "쥐: E를 세 번 눌러 해킹하세요.";
        d.Devices[4].Instruction = "개구리: 레버를 조준하고 F를 유지하면 발판이 내려갑니다.\nF를 놓으면 곰을 태운 발판이 중단으로 올라갑니다.";
        d.Devices[61].Instruction = "E: 세 번 해킹하면 사다리가 내려옵니다.";
        d.Devices[64].Instruction = "사다리가 완전히 내려온 뒤 W/S로 오르내리세요.";
        d.Devices[68].Instruction = "장애물 앞에서 점프한 뒤 첫 고리를 조준하고 F를 눌러 이동하세요.";
        d.Devices[69].Instruction = "두 번째 고리를 조준하고 F를 눌러 이동하세요.";
        d.Devices[85].Instruction = "천장 레버를 조준하고 F를 눌러 문을 여세요.";
        foreach (var t in LayoutTransforms())
        {
            var text = t.GetComponent<TextMesh>(); if (text == null) continue;
            var owner = t.GetComponentInParent<PcsPuzzleDevice>();
            text.text = owner != null && !string.IsNullOrEmpty(owner.Instruction) ? owner.Instruction : LayoutKorean(text.text);
            if (owner != null)
            {
                switch (owner.DeviceId)
                {
                    case 3: text.text = "쥐 · E 세 번 해킹"; break;
                    case 4: text.text = "개구리 · 조준+F 유지\n놓으면 발판이 올라갑니다."; break;
                    case 61: text.text = "E 세 번 · 사다리 전개"; break;
                    case 64: text.text = "W/S · 사다리"; break;
                    case 68: text.text = "첫 고리 · 조준+F"; break;
                    case 69: text.text = "두 번째 고리 · 조준+F"; break;
                    case 85: text.text = "천장 레버 · 조준+F"; break;
                }
            }
            if (t.name == "Lever_Tutorial_Lift_InteractionPending_Label") t.gameObject.SetActive(false);
            text.font = font; text.fontSize = 64; text.characterSize = .05f / Mathf.Max(.001f, Mathf.Abs(t.lossyScale.y));
            text.lineSpacing = 1.15f; text.alignment = TextAlignment.Center;
            var renderer = text.GetComponent<MeshRenderer>(); renderer.sharedMaterial = font.material; renderer.sortingOrder = 12;
        }
    }
    private static void AdjustLayoutAfterPlay()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/Puzzle.unity" || scene.isDirty) throw new InvalidOperationException("Saved Puzzle required.");
        var d = UnityEngine.Object.FindFirstObjectByType<PcsPuzzleDirector>();
        LayoutSmallLanding("Tutorial_Frog_AnchorLanding_1", d.Devices[68].transform.parent, new Vector2(-29.88f, 17.34f), 1.7f);
        LayoutSmallLanding("Tutorial_Frog_AnchorLanding_2", d.Devices[69].transform.parent, new Vector2(-25.64f, 18.92f), 1.7f);
        // Keep the low obstacle, but leave clearance for the whole capsule on the first pull arc.
        var obstacle = LayoutObject(1878349891);
        obstacle.position = new Vector3(-33.25f, obstacle.position.y, obstacle.position.z);
        LayoutObject(331477350).gameObject.SetActive(false);
        var stage = d.Devices[3].transform.parent;
        LayoutSmallLanding("StageOne_CarryStep_1", stage, new Vector2(-2.8f, -1.1f), 1.4f);
        LayoutSmallLanding("StageOne_CarryStep_2", stage, new Vector2(-1.35f, .85f), 1.4f);
        // The authored ramp ends exceed the Mouse's unmodified jump by a few centimetres.
        LayoutSmallLanding("Zigzag_EntryStep", stage, new Vector2(7.2f, 3.42f), 1.2f);
        LayoutSmallLanding("Zigzag_MiddleTurnStep", stage, new Vector2(5.8f, 11.35f), .75f);
        LayoutSmallLanding("Zigzag_LastEntryStep", stage, new Vector2(.65f, 12.12f), .8f);
        // This single entrance deck crosses above the last ramp; preserve its upper support.
        var crossingDeck = LayoutTransforms().Single(t => LayoutPath(t) == "Puzzle_Prototype/1-2/00_Background/Shaft_RabbitRest_00");
        LayoutOneWay(crossingDeck.GetComponent<BoxCollider2D>());
        ApplyLayoutKorean(d);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save Play corrections.");
        InspectCurrentLayout();
        File.Copy(Path.Combine(LayoutEvidence, "layout-snapshot.json"), Path.Combine(LayoutEvidence, "layout-after.json"), true);
    }
    private static void ConfigureLayoutCamera()
    {
        var follow = Camera.main.GetComponent<PcsLocalCameraFollow>(); var settings = new SerializedObject(follow);
        var regions = settings.FindProperty("regions");
        for (int i = 0; i < regions.arraySize; i++)
        {
            var item = regions.GetArrayElementAtIndex(i); string name = item.FindPropertyRelative("name").stringValue;
            if (name == "Stage 1-1")
            {
                item.FindPropertyRelative("selectionBounds").rectValue = new Rect(-13, -5, 29, 13);
                item.FindPropertyRelative("viewBounds").rectValue = new Rect(-13.5f, -3.9f, 29, 15);
                item.FindPropertyRelative("orthographicSize").floatValue = 6.5f;
            }
            if (name == "Connection ladder")
            {
                item.FindPropertyRelative("name").stringValue = "Zigzag connection";
                item.FindPropertyRelative("selectionBounds").rectValue = new Rect(-13, 8, 29, 10);
                item.FindPropertyRelative("viewBounds").rectValue = new Rect(-13.5f, 3, 29, 16);
                item.FindPropertyRelative("section").intValue = 1;
                item.FindPropertyRelative("orthographicSize").floatValue = 6f;
            }
        }
        settings.ApplyModifiedPropertiesWithoutUndo();
    }
    private static void ValidateCurrentLayoutRefinement()
    {
        var d = UnityEngine.Object.FindFirstObjectByType<PcsPuzzleDirector>(); var lines = new List<string>();
        foreach (var t in LayoutTransforms())
            if (t.GetComponents<Component>().Any(c => c == null)) throw new InvalidOperationException("Missing script: " + LayoutPath(t));
        for (int i = 0; i < d.Devices.Length; i++)
        {
            var device = d.Devices[i]; if (device == null) { lines.Add("RESERVED/EXCLUDED " + i); continue; }
            if (device.DeviceId != i) throw new InvalidOperationException("Registry mismatch: " + device.name);
            if (!device.gameObject.activeInHierarchy) { lines.Add("PRESERVED INACTIVE " + device.name); continue; }
            foreach (var link in device.Links)
                if (link == null)
                {
                    if (device.RequiredRole == MyEnum.CharacterType.Bear && device.Section == 0) lines.Add("KNOWN EXCLUDED BEAR LINK: " + device.name);
                    else throw new InvalidOperationException("Null link: " + device.name);
                }
                else if (link.DeviceId < 0 || link.DeviceId >= d.Devices.Length || d.Devices[link.DeviceId] != link)
                    throw new InvalidOperationException("Unregistered link: " + device.name);
            lines.Add("DEVICE " + i + " " + LayoutPath(device.transform) + " -> " + string.Join(",", device.Links.Select(l => l != null ? l.DeviceId.ToString() : "Bear-null")));
        }
        var active = LayoutTransforms().Select(t => t.GetComponent<PcsPuzzleDevice>()).Where(v => v != null && v.isActiveAndEnabled).ToArray();
        if (active.GroupBy(v => v.DeviceId).Any(g => g.Count() > 1)) throw new InvalidOperationException("Duplicate active device IDs.");
        foreach (var text in LayoutTransforms().Select(t => t.GetComponent<TextMesh>()).Where(t => t != null && t.gameObject.activeInHierarchy))
            if (text.font == null) throw new InvalidOperationException("Label font missing: " + text.name);
        lines.Add("PASS saved reference/ID/font assignment checks; runtime and rendered glyphs require Play evidence.");
        File.WriteAllLines(Path.Combine(LayoutEvidence, "scene-validation.txt"), lines);
    }
}

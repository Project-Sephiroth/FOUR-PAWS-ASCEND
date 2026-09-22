using System;
using System.Collections.Generic;
using System.IO;
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
    private static readonly List<PcsPuzzleDevice> devices = new List<PcsPuzzleDevice>();
    private static Transform puzzleRoot;
    private static Sprite deckSprite, shortSprite, wallSprite, buttonSprite, leverSprite;
    private static Material spriteMaterial;
    private static PcsPuzzleDirector director;

    private static GameObject Find(string name)
    {
        if (name == "Landing_Left_Gate" && FindExact(name) == null) name = "Landing_Right_Console";
        if (name == "Wall_TutorialGate" && FindExact(name) == null) name = "Wall_ConsoleAccess";
        if (name == "Lift_Tutorial_ManualRaised" && FindExact(name) == null) name = "Lift_StageOne_ThreeStops";
        if (name == "Elevator_Main_ManualRaised" && FindExact(name) == null) name = "Elevator_Main_HackToAscend";
        if (name == "Lever_Tutorial_Lift_InteractionPending" && FindExact(name) == null) name = "StageOne_BearPickupLever";
        return FindExact(name);
    }

    private static GameObject FindExact(string name)
    {
        return SceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true))
            .FirstOrDefault(t => t.name == name)?.gameObject;
    }

    private static GameObject Node(string name, Transform parent, Vector3 position)
    {
        var go = Find(name) ?? new GameObject(name);
        go.transform.SetParent(parent, true);
        go.transform.position = position;
        go.transform.rotation = Quaternion.identity;
        return go;
    }

    private static T Component<T>(GameObject go) where T : Component
    {
        var existing = go.GetComponent<T>();
        return existing != null ? existing : go.AddComponent<T>();
    }

    private static Sprite SpriteAt(string file)
    {
        var sprites = AssetDatabase.LoadAllAssetsAtPath("Assets/Resources/img/map/" + file + ".png").OfType<Sprite>().ToArray();
        if (sprites.Length == 0) throw new InvalidOperationException("Missing imported Sprite: " + file);
        return sprites[0];
    }

    private static void Property(Object target, string name, object value)
    {
        var serialized = new SerializedObject(target);
        var property = serialized.FindProperty(name);
        if (property == null) throw new InvalidOperationException(target.GetType().Name + "." + name + " not serialized");
        if (value is Object o) property.objectReferenceValue = o;
        else if (value == null) property.objectReferenceValue = null;
        else if (value is float f) property.floatValue = f;
        else if (value is bool b) property.boolValue = b;
        else if (value is int i) property.intValue = i;
        else if (value is string s) property.stringValue = s;
        else throw new ArgumentException(name);
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static SpriteRenderer Art(GameObject go, Sprite sprite, Color? color = null)
    {
        var renderer = Component<SpriteRenderer>(go);
        renderer.sprite = sprite;
        renderer.sharedMaterial = spriteMaterial;
        renderer.sortingOrder = 2;
        renderer.color = color ?? Color.white;
        renderer.enabled = true;
        return renderer;
    }

    // The solid deck is measured inside the existing image, not its padded texture bounds.
    private static GameObject Platform(string name, Transform parent, float x, float top, float width, bool useShort = false)
    {
        var go = Node(name, parent, Vector3.zero);
        var existingRenderer = go.GetComponent<SpriteRenderer>();
        Sprite sprite = existingRenderer != null ? existingRenderer.sprite : null;
        bool small = sprite != null ? sprite.name.Contains("Short") : useShort;
        float localWidth = small ? 7.86f : 16.9f;
        float localTop = small ? 2.125f : 1.93f;
        go.transform.localScale = new Vector3(width / localWidth, 0.3f, 1);
        go.transform.position = new Vector3(x, top - localTop * 0.3f, 0);
        Art(go, sprite != null ? sprite : small ? shortSprite : deckSprite);
        var collider = Component<BoxCollider2D>(go);
        collider.isTrigger = false;
        collider.offset = new Vector2(small ? 0.33f : 0.08f, localTop - 0.4f);
        collider.size = new Vector2(localWidth, 0.8f);
        go.layer = LayerMask.NameToLayer("Ground");
        go.SetActive(true);
        return go;
    }

    private static GameObject Block(string name, Transform parent, Vector2 center, Vector2 size, Color? color = null)
    {
        var go = Node(name, parent, center);
        go.transform.localScale = new Vector3(size.x / 3.34f, size.y / 12.97f, 1);
        go.transform.position = new Vector3(center.x - .025f * go.transform.localScale.x, center.y + .625f * go.transform.localScale.y, 0);
        Art(go, wallSprite, color);
        var collider = Component<BoxCollider2D>(go);
        collider.offset = new Vector2(0.025f, -0.625f);
        collider.size = new Vector2(3.34f, 12.97f);
        go.layer = LayerMask.NameToLayer("Ground");
        return go;
    }

    private static PcsPuzzleDevice Device(GameObject go, PcsDeviceKind kind, int section, MyEnum.CharacterType role = MyEnum.CharacterType.None)
    {
        var device = Component<PcsPuzzleDevice>(go);
        device.DeviceId = devices.Count;
        device.Kind = kind;
        device.Section = section;
        device.RequiredRole = role;
        device.Solid = go.GetComponents<Collider2D>().FirstOrDefault(c => !c.isTrigger);
        device.Visuals = go.GetComponentsInChildren<SpriteRenderer>(true);
        device.Body = go.GetComponent<Rigidbody2D>();
        device.Elevator = go.GetComponent<PcsElevator>();
        device.SlidingWall = go.GetComponent<PcsSlidingWall>();
        device.PressureButton = go.GetComponent<PcsPressureButton>();
        device.Links = Array.Empty<PcsPuzzleDevice>();
        devices.Add(device);
        return device;
    }

    private static PcsPuzzleDevice Zone(string name, Transform parent, Vector2 center, Vector2 size, PcsDeviceKind kind, int section, MyEnum.CharacterType role = MyEnum.CharacterType.None)
    {
        var go = Node(name, parent, center);
        go.transform.localScale = Vector3.one;
        var trigger = Component<BoxCollider2D>(go);
        trigger.size = size;
        trigger.isTrigger = true;
        var device = Device(go, kind, section, role);
        device.Trigger = trigger;
        if (kind == PcsDeviceKind.Ladder) LadderArtwork(go.transform, size);
        return device;
    }

    private static void LadderArtwork(Transform parent, Vector2 size)
    {
        float width = Mathf.Min(size.x, 0.85f);
        // Reuse the metal artwork without adding solids across the climb volume.
        for (int i = 0; i < 2; i++)
            LadderBar(parent, "Rail_" + i, new Vector2((i == 0 ? -1 : 1) * width * .5f, 0),
                new Vector2(.065f, size.y), 0);
        int count = Mathf.CeilToInt(size.y / .6f);
        for (int i = 0; i <= count; i++)
            LadderBar(parent, "Rung_" + i.ToString("00"), new Vector2(0, Mathf.Lerp(-size.y * .5f, size.y * .5f, (float)i / count)),
                new Vector2(.065f, width), 90);
        Label(parent, "W / S", new Vector3(width + .2f, -size.y * .5f + .8f, 0), .08f);
    }

    private static void LadderBar(Transform parent, string suffix, Vector2 center, Vector2 size, float angle)
    {
        var go = Node(parent.name + "_" + suffix, parent, Vector3.zero);
        go.transform.localRotation = Quaternion.Euler(0, 0, angle);
        go.transform.localScale = new Vector3(size.x / 3.34f, size.y / 12.97f, 1);
        go.transform.localPosition = (Vector3)center - go.transform.localRotation *
            Vector3.Scale(new Vector3(.025f, -.625f, 0), go.transform.localScale);
        Art(go, wallSprite, new Color(.6f, .85f, 1f));
    }

    private static PcsPuzzleDevice Control(string name, Transform parent, Vector2 position, PcsDeviceKind kind, int section, MyEnum.CharacterType role, string instruction)
    {
        var go = Node(name, parent, position);
        Vector3 parentScale = parent.lossyScale;
        go.transform.localScale = new Vector3(.11f / parentScale.x, .11f / parentScale.y, .11f / parentScale.z);
        Art(go, kind == PcsDeviceKind.Lever ? leverSprite : buttonSprite,
            kind == PcsDeviceKind.HackConsole ? new Color(1, 0.68f, 0.25f) : Color.white);
        var device = Device(go, kind, section, role);
        device.Instruction = LayoutKorean(instruction);
        device.InteractionRange = 1.55f;
        Label(go.transform, instruction, new Vector3(0, 8.5f, 0), .7f);
        return device;
    }

    private static void Label(Transform parent, string text, Vector3 localPosition, float characterSize = 0.15f)
    {
        var go = Node(parent.name + "_Label", parent, Vector3.zero);
        go.transform.localPosition = localPosition;
        go.transform.localScale = Vector3.one;
        var mesh = Component<TextMesh>(go);
        mesh.text = LayoutKorean(text);
        mesh.fontSize = 48;
        mesh.characterSize = characterSize;
        mesh.anchor = TextAnchor.MiddleCenter;
        mesh.color = new Color(0.9f, 0.96f, 1);
        mesh.GetComponent<MeshRenderer>().sortingOrder = 12;
    }

    private static Rigidbody2D Kinematic(GameObject go)
    {
        var body = Component<Rigidbody2D>(go);
        body.bodyType = RigidbodyType2D.Kinematic;
        body.simulated = true;
        body.gravityScale = 0;
        body.constraints = RigidbodyConstraints2D.FreezeRotation;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;
        return body;
    }

    private static PcsPuzzleDevice Gate(string name, Transform parent, float x, float bottom, float height, int section)
    {
        var go = Block(name, parent, new Vector2(x, bottom + height / 2), new Vector2(0.45f, height));
        Kinematic(go);
        var mover = Component<PcsSlidingWall>(go);
        var upper = Node(name + "_OpenTarget", parent, go.transform.position + Vector3.up * (height + 0.6f));
        Property(mover, "openTarget", upper.transform);
        Property(mover, "speed", 3f);
        var device = Device(go, PcsDeviceKind.SlidingWall, section);
        device.SlidingWall = mover;
        device.UpperStop = upper.transform;
        device.LowerStop = Node(name + "_ClosedTarget", parent, go.transform.position).transform;
        device.Speed = 3;
        return device;
    }

    private static PcsPuzzleDevice Lift(string name, Transform parent, float x, float lowerTop, float upperTop, float width, int section, PcsLiftPolicy policy)
    {
        var go = Platform(name, parent, x, lowerTop, width);
        Kinematic(go);
        var elevator = Component<PcsElevator>(go);
        var upper = Node(name + "_UpperStop", parent, go.transform.position + Vector3.up * (upperTop - lowerTop));
        var column = go.GetComponentsInChildren<SpriteRenderer>(true).FirstOrDefault(s => s.gameObject != go && s.sprite != null && (s.name.Contains("Column") || s.name.Contains("GIDONG")));
        if (column == null)
        {
            var visual = Node(name + "_Column", go.transform, go.transform.position);
            visual.transform.localScale = new Vector3(0.25f, 0.35f, 1);
            visual.transform.localPosition = new Vector3(0, -2.8f, 0);
            column = Art(visual, SpriteAt("GIDONG"));
        }
        Property(elevator, "upperTarget", upper.transform);
        Property(elevator, "column", column);
        Property(elevator, "raised", false);
        Property(elevator, "speed", policy == PcsLiftPolicy.MainContinuous ? 0.65f : 1.5f);
        var device = Device(go, PcsDeviceKind.Elevator, section);
        device.Elevator = elevator;
        device.LiftPolicy = policy;
        device.Speed = policy == PcsLiftPolicy.MainContinuous ? 0.65f : 1.5f;
        device.LowerStop = Node(name + "_LowerStop", parent, go.transform.position).transform;
        device.UpperStop = upper.transform;
        return device;
    }

    private static void Archive(string name, Transform archive)
    {
        var go = Find(name);
        if (go == null) return;
        go.transform.SetParent(archive, true);
        go.SetActive(false);
    }

    // The authored layout and passive shaft objects must remain unchanged.
    private static void BuildPuzzle()
    {
        throw new InvalidOperationException("전체 씬 재생성은 폐기되었습니다. 저장된 배치를 유지하고 기존 장치 수동 테스트에서 1-2 상승·정지만 사용하세요.");
    }
}

using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static partial class PcsSecondPassTools
{
    private static void Link(PcsPuzzleDevice source, params PcsPuzzleDevice[] targets) { source.Links = targets; }
    private static Transform[] Spawns(Transform parent, string prefix, Vector2[] points)
    {
        var result = new Transform[6];
        for (int role = 1; role <= 4; role++)
            result[role] = Node(prefix + "_" + (MyEnum.CharacterType)role, parent, points[role - 1]).transform;
        return result;
    }

    private static void BuildStageOne()
    {
        var stage = Find("1-1").transform;
        Platform("Floor_Start_Left", stage, -7, -3, 11);
        Platform("Floor_Landing_Right", stage, 7, -3, 11);
        var consoleLanding = Platform("Landing_Left_Gate", stage, 8.25f, -0.5f, 6.5f, true);
        consoleLanding.name = "Landing_Right_Console";
        // Frog's measured single jump is about 0.73U; keep the middle-to-exit rise at 0.6U.
        Platform("StageOne_LeftUpperExit", stage, -7.6f, -0.2f, 9.2f);
        Archive("Ramp_To_Connection", Find("99_Preserved_Prototype_Objects").transform);
        var wall = Gate("Wall_TutorialGate", stage, 8.1f, -0.5f, 3.3f, 1);
        wall.name = "Wall_ConsoleAccess";
        var button = Find("Button_HoldToOpen");
        button.transform.SetParent(stage, true);
        button.transform.position = new Vector3(-7, -2.91f, 0);
        button.layer = LayerMask.NameToLayer("Ground");
        var support = button.GetComponent<BoxCollider2D>();
        var sensor = Find("PressSensor");
        var pressure = sensor.GetComponent<PcsPressureButton>();
        Property(pressure, "wall", wall.SlidingWall);
        Property(pressure, "supportCollider", support);
        var plate = Device(sensor, PcsDeviceKind.PressurePlate, 1, MyEnum.CharacterType.Bear);
        plate.PressureButton = pressure;
        plate.Solid = support;
        Link(plate, wall);
        var lift = Lift("Lift_Tutorial_ManualRaised", stage, 0, -3, 3, 6, 1, PcsLiftPolicy.StageOneThreeStop);
        lift.name = "Lift_StageOne_ThreeStops";
        lift.MiddleStop = Node("StageOneLift_Middle", stage, lift.LowerStop.position + Vector3.up * 2.2f).transform;
        var hack = Control("StageOne_MouseConsole", stage, new Vector2(10, 0.15f), PcsDeviceKind.HackConsole, 1, MyEnum.CharacterType.Mouse, "쥐: E 세 번\n승강기 해킹");
        Link(hack, lift);
        var lever = Control("Lever_Tutorial_Lift_InteractionPending", stage, new Vector2(-5.8f, 0.5f), PcsDeviceKind.Lever, 1, MyEnum.CharacterType.Frog, "개구리: 조준+F 유지 → 발판 하강\nF를 놓으면 중단으로 복귀");
        lever.name = "StageOne_BearPickupLever";
        var previousLabel = lever.transform.Find("Lever_Tutorial_Lift_InteractionPending_Label");
        if (previousLabel != null) previousLabel.gameObject.SetActive(false);
        Link(lever, lift);
        // The fixed anchor lets Frog reach the central tier while Bear holds the pressure plate.
        Control("StageOne_FrogAnchor", stage, new Vector2(3.8f, 0.8f), PcsDeviceKind.Anchor, 1, MyEnum.CharacterType.Frog, "개구리: 조준+F");
        var exit = Zone("StageOne_TeamExit", stage, new Vector2(-9.5f, 0.9f), new Vector2(5, 2), PcsDeviceKind.Exit, 1);
        exit.RequiredRolesMask = 30;
        exit.RequireAllRoles = true;
        Link(exit, hack);
        var exitDoor = Gate("StageOne_TeamDoor", stage, -12.1f, 0f, 3, 1);
        exitDoor.RequireAllLinks = true;
        Link(exitDoor, exit);
        Enemy("StageOne_Enemy_Right", stage, new Vector2(6f, -2.5f), 1);
        Enemy("StageOne_Enemy_Upper", stage, new Vector2(-10.2f, 0.3f), 1);
        director.StageOneSpawns = Spawns(stage, "Spawn_StageOne", new[] { new Vector2(-8, -2.85f), new Vector2(-9, -2.85f), new Vector2(-10, -2.85f), new Vector2(-11, -2.85f) });
        Label(stage, "1-1 · 곰은 압력판, 쥐는 해킹\n개구리 레버로 곰을 태운 뒤 상부에 합류하세요.", new Vector3(0, 4.7f, 0), 0.12f);
        Zone("StageOne_FallReset", stage, new Vector2(0, -6), new Vector2(28, 1), PcsDeviceKind.KillZone, 1);
    }

    private static void BuildCorridor()
    {
        var corridor = Find("CheckPoint-connect").transform;
        // The corridor starts after the left upper team door. It cannot be entered from the right floor.
        Platform("Corridor_Entrance", corridor, -14.5f, 0f, 4.5f);
        for (int i = 0; i < 19; i++)
            Archive("Corridor_Step_" + i.ToString("00"), Find("99_Preserved_Prototype_Objects").transform);
        var ladder = Zone("Corridor_Ladder", corridor, new Vector2(-15.65f, 8f), new Vector2(1f, 16.8f), PcsDeviceKind.Ladder, -1);
        ladder.Instruction = "W/S: 사다리 오르내리기";
        // Keep the ladder shaft clear; the upper landing starts beside it, never across its exit.
        Platform("Corridor_UpperWalkway", corridor, -8.7f, 15, 12.8f);
        Platform("Connection_ElevatorDock_Left", corridor, -4, 15, 7);
        Platform("Connection_ElevatorDock_Right", corridor, 5.6f, 15, 10.8f);
        var checkpoint = Zone("Checkpoint_TeamAssembly", corridor, new Vector2(-7.7f, 15.8f), new Vector2(8, 2), PcsDeviceKind.Checkpoint, 1);
        checkpoint.RequiredRolesMask = 30;
        checkpoint.RequireAllRoles = true;
        checkpoint.Instruction = "체크포인트 · 네 명 모두 모여 주세요.";
        Label(corridor, "체크포인트 · 네 명 모두 모여 주세요.", new Vector3(-9, 17.7f, 0), 0.16f);
        director.StageTwoSpawns = Spawns(corridor, "Spawn_StageTwo", new[] { new Vector2(6, 15.15f), new Vector2(-6, 15.15f), new Vector2(-4, 15.15f), new Vector2(-2, 15.15f) });
    }

    private static PcsPuzzleDevice Enemy(string name, Transform parent, Vector2 position, int section)
    {
        var go = Node(name, parent, position);
        go.transform.localScale = Vector3.one;
        string path = "Assets/Resources/img/Enemy/Ranged Robot/Ranged Robot Idle.png";
        Sprite sprite = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
        Art(go, sprite != null ? sprite : buttonSprite, new Color(1, .7f, .35f));
        if (sprite != null) go.transform.localScale = Vector3.one * (0.9f / sprite.bounds.size.y);
        var collider = Component<BoxCollider2D>(go);
        collider.size = Vector2.one / go.transform.localScale.x * .75f;
        Kinematic(go);
        var enemy = Device(go, PcsDeviceKind.Enemy, section);
        enemy.Health = 2;
        enemy.Duration = 3.5f;
        return enemy;
    }
}

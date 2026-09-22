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
        Enemy("StageOne_Enemy_Right", stage, new Vector2(6f, -2.5f), 1, false);
        Enemy("StageOne_Enemy_Upper", stage, new Vector2(-10.2f, 0.3f), 1, false);
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

    private static readonly Color[] channelColors = { new Color(1, .5f, .16f), new Color(.35f, .85f, .3f), new Color(.72f, .35f, 1), new Color(.2f, .8f, 1) };

    private static void BuildShaft()
    {
        var shaft = Find("1-2").transform;
        var archive = Find("99_Preserved_Prototype_Objects").transform;
        for (int i = 1; i <= 5; i++)
        {
            Archive("Landing_Left_" + i.ToString("00"), archive);
            Archive("Landing_Right_" + i.ToString("00"), archive);
        }
        var lift = Lift("Elevator_Main_ManualRaised", shaft, -4, 15, 60, 7, 2, PcsLiftPolicy.MainContinuous);
        lift.name = "Elevator_Main_HackToAscend";
        var start = Control("Shaft_StartHack", lift.transform, new Vector2(-6.1f, 15.7f), PcsDeviceKind.HackConsole, 2, MyEnum.CharacterType.Mouse, "쥐: E 세 번 · 상승 시작");
        Link(start, lift);
        var console = Control("Shaft_ChannelConsole", lift.transform, new Vector2(-2.4f, 15.7f), PcsDeviceKind.ChannelConsole, 2, MyEnum.CharacterType.Mouse, "쥐: 1·2·3·4 색 선택\nR: 전력 보충");
        console.InteractionRange = 2.5f;
        director.ShaftLeftBoardingArea = Zone("Shaft_LeftBoardingArea", shaft, new Vector2(-4, 15.8f),
            new Vector2(7, 1.8f), PcsDeviceKind.Instruction, 2);
        director.ShaftLeftBoardingArea.RequiredRolesMask = 28;
        director.ShaftRabbitBoardingArea = Zone("Shaft_RabbitBoardingArea", shaft, new Vector2(6, 15.8f),
            new Vector2(10, 1.8f), PcsDeviceKind.Instruction, 2);
        director.ShaftRabbitBoardingArea.RequiredRolesMask = 2;
        director.RequireShaftBoarding = true;
        var glass = Gate("Shaft_GlassDivider", shaft, 0.2f, 15, 47, 2);
        // Leave the entrance open until Rabbit is on the right and the other three are aboard.
        glass.Instruction = "구역 분리벽";
        glass.EntryOpenBeforeShaftStart = true;
        glass.Speed = 12;
        Property(glass.SlidingWall, "speed", 12f);
        glass.Visuals[0].color = new Color(.5f, .85f, 1, .38f);
        for (int i = 0; i < 10; i++)
        {
            float top = 17.2f + i * 4.4f;
            int color = i % 4;
            var platform = Device(Platform("Shaft_ColorStep_" + i.ToString("00"), shaft, 2.7f, top, 3.8f, true), PcsDeviceKind.TimedPlatform, 2);
            platform.Channel = color;
            var rootArtwork = platform.GetComponent<SpriteRenderer>();
            rootArtwork.color = channelColors[color];
            var foldedObject = Node(platform.name + "_FoldVisual", platform.transform, platform.transform.position);
            foldedObject.transform.localPosition = Vector3.zero;
            foldedObject.transform.localRotation = Quaternion.identity;
            foldedObject.transform.localScale = Vector3.one;
            var foldedArtwork = Art(foldedObject, rootArtwork.sprite, channelColors[color]);
            foldedArtwork.sharedMaterial = rootArtwork.sharedMaterial;
            foldedArtwork.sortingLayerID = rootArtwork.sortingLayerID;
            foldedArtwork.sortingOrder = rootArtwork.sortingOrder;
            foldedArtwork.flipX = rootArtwork.flipX;
            foldedArtwork.flipY = rootArtwork.flipY;
            rootArtwork.enabled = false;
            platform.Visuals = new[] { foldedArtwork };
            var button = Control("Shaft_RemoteButton_" + i.ToString("00"), shaft, new Vector2(-.8f, top + .2f), PcsDeviceKind.RemoteButton, 2, MyEnum.CharacterType.None, (color + 1) + " · 조준+F");
            button.Channel = color;
            button.Visuals[0].color = channelColors[color];
            Link(button, platform);
            Platform("Shaft_RabbitRest_" + i.ToString("00"), shaft, 7.7f, top + 2.2f, 4.4f, true);
        }
        for (int i = 0; i < 3; i++)
        {
            float height = 27 + i * 14;
            var barrierGo = Platform("Shaft_Barrier_" + i, shaft, -4, height, 7);
            Kinematic(barrierGo);
            var wall = Component<PcsSlidingWall>(barrierGo);
            var upper = Node("Shaft_BarrierOpen_" + i, shaft, barrierGo.transform.position + Vector3.up * 4).transform;
            Property(wall, "openTarget", upper);
            Property(wall, "speed", 6f);
            var barrier = Device(barrierGo, PcsDeviceKind.SlidingWall, 2);
            barrier.LowerStop = Node("Shaft_BarrierClosed_" + i, shaft, barrierGo.transform.position).transform;
            barrier.UpperStop = upper;
            barrier.Speed = 6;
            barrier.DisableColliderWhenOpen = true;
            barrier.BlocksShaft = true;
            // A released barrier has no remaining collider in the rising lift's path.
            var release = Control("Shaft_RabbitRelease_" + i, shaft, new Vector2(7.7f, 24.5f + i * 13.2f), PcsDeviceKind.BarrierControl, 2, MyEnum.CharacterType.Rabbit, "토끼: E · 장벽 해제");
            Link(release, barrier);
            Platform("Shaft_EnemyLedge_" + i, shaft, -9.3f, 23 + i * 13.2f, 2.5f, true);
            Enemy("Shaft_Enemy_" + i, shaft, new Vector2(-9.3f, 23.5f + i * 13.2f), 2, true);
        }
        // The elevator supplies the arrival floor inside the shaft; the exit deck stays beside it.
        Platform("Arrival_1_2_Left", Find("END").transform, -9.65f, 60, 4.1f);
        Platform("Arrival_1_2_Right", Find("END").transform, 6, 60, 11.5f);
        var left = Zone("End_LeftTeamArrival", shaft, new Vector2(-4, 61), new Vector2(8, 2), PcsDeviceKind.Arrival, 2);
        left.RequiredRolesMask = 28;
        var right = Zone("End_RabbitArrival", shaft, new Vector2(6, 61), new Vector2(8, 2), PcsDeviceKind.Arrival, 2);
        right.RequiredRolesMask = 2;
        Link(glass, left, right);
        glass.RequireAllLinks = true;
        var exit = Zone("End_FourPawsExit", shaft, new Vector2(-9, 61), new Vector2(4.5f, 2), PcsDeviceKind.Exit, 2);
        exit.RequiredRolesMask = 30;
        exit.RequireAllRoles = true;
        Link(exit, left, right);
        Label(shaft, "네 명이 합류한 뒤 왼쪽 출구로 이동하세요.", new Vector3(-4, 63.4f, 0), .2f);
        Zone("Shaft_FallReset", shaft, new Vector2(0, 11.5f), new Vector2(25, 1), PcsDeviceKind.KillZone, 2);
    }

    private static PcsPuzzleDevice Enemy(string name, Transform parent, Vector2 position, int section, bool battery)
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
        if (battery)
        {
            var cell = Control(name + "_Battery", parent, position, PcsDeviceKind.Battery, section, MyEnum.CharacterType.None, "배터리 · G / F");
            cell.BatteryValue = 2;
            cell.InitiallyActive = false;
            Link(enemy, cell);
        }
        return enemy;
    }
}

using System.Linq;
using UnityEditor;
using UnityEngine;

public static partial class PcsSecondPassTools
{
    private static PcsPuzzleDevice Dummy(string name, Transform parent, Vector2 position)
    {
        var go = Node(name, parent, position);
        var sprite = AssetDatabase.LoadAllAssetsAtPath("Assets/Resources/img/Player/mouse/Mouse Idle.png").OfType<Sprite>().First();
        go.transform.localScale = Vector3.one * (.8f / sprite.bounds.size.y);
        Art(go, sprite, new Color(1, .9f, .2f));
        var body = Kinematic(go);
        body.useFullKinematicContacts = true;
        go.layer = LayerMask.NameToLayer("Player");
        var collider = Component<BoxCollider2D>(go);
        collider.size = new Vector2(.65f, .8f) / go.transform.localScale.x;
        var dummy = Device(go, PcsDeviceKind.Dummy, 0);
        dummy.InitiallyActive = true;
        dummy.Body = body;
        return dummy;
    }

    private static PcsPuzzleDevice AllyPlate(string name, Transform parent, float x, float floor)
    {
        var support = Platform(name + "_Support", parent, x, floor + .12f, 1.8f, true);
        var plate = Zone(name, parent, new Vector2(x, floor + .17f), new Vector2(1.8f, .15f), PcsDeviceKind.PressurePlate, 0, MyEnum.CharacterType.Bear);
        var pressure = Component<PcsPressureButton>(plate.gameObject);
        Property(pressure, "supportCollider", support.GetComponent<BoxCollider2D>());
        plate.PressureButton = pressure;
        plate.Solid = support.GetComponent<BoxCollider2D>();
        plate.AcceptDummyOnly = true;
        return plate;
    }

    private static PcsPuzzleDevice TutorialFinish(Transform room, MyEnum.CharacterType role, float floor, int dummyCount, params PcsPuzzleDevice[] lessons)
    {
        var exit = Zone("Tutorial_" + role + "_Finish", room, new Vector2(-22.5f, floor + 1), new Vector2(3.5f, 2.5f), PcsDeviceKind.TutorialExit, 0, role);
        exit.RequiredDummyCount = dummyCount;
        Link(exit, lessons);
        var gate = Gate("Tutorial_" + role + "_ExitGate", room, -20.7f, floor, 3.3f, 0);
        gate.RequireAllLinks = true;
        Link(gate, exit);
        Platform("Tutorial_" + role + "_ExitDeck", room, -21.5f, floor, 4);
        return exit;
    }

    private static void BuildTutorials()
    {
        var tutorial = Node("00_AbilityTutorials", puzzleRoot, Vector3.zero).transform;
        var roles = new[] { MyEnum.CharacterType.Rabbit, MyEnum.CharacterType.Mouse, MyEnum.CharacterType.Frog, MyEnum.CharacterType.Bear };
        var exits = new PcsPuzzleDevice[4];
        for (int i = 0; i < roles.Length; i++)
        {
            float floor = -3 + i * 9;
            var room = Node("Tutorial_" + roles[i], tutorial, Vector3.zero).transform;
            Platform("Tutorial_" + roles[i] + "_Floor", room, -31, floor, 23);
            Platform("Tutorial_" + roles[i] + "_Ceiling", room, -31, floor + 7.6f, 23);
            Block("Tutorial_" + roles[i] + "_BackWall", room, new Vector2(-43, floor + 3.7f), new Vector2(.6f, 8));
            Label(room, PcsPuzzleDirector.RoleDisplayName(roles[i]) + " 튜토리얼", new Vector3(-31, floor + 6.4f, 0), .19f);
            var background = Node("Tutorial_" + roles[i] + "_Background", room, new Vector3(-31, floor + 3.5f, 2));
            background.transform.localScale = new Vector3(1.43f, .85f, 1);
            var bg = Art(background, SpriteAt("1-1MAP"));
            bg.sortingOrder = -20;

            if (roles[i] == MyEnum.CharacterType.Rabbit)
            {
                Dummy("Tutorial_Rabbit_Ally", room, new Vector2(-38.5f, floor + .5f));
                Block("Tutorial_Rabbit_FirstJump", room, new Vector2(-35, floor + .55f), new Vector2(1, 1.1f));
                Platform("Tutorial_Rabbit_Jump_1", room, -31, floor + 1.2f, 2.2f, true);
                Platform("Tutorial_Rabbit_Jump_2", room, -27.8f, floor + 2.5f, 2.2f, true);
                Platform("Tutorial_Rabbit_UpperExit", room, -23.2f, floor + 3.7f, 4.4f, true);
                exits[i] = TutorialFinish(room, roles[i], floor + 3.7f, 1);
                Label(room, "G: 동료 들기·내리기   SPACE: 두 번 점프\n동료와 함께 출구로 이동하세요.", new Vector3(-37.5f, floor + 2.8f, 0), .11f);
            }
            else if (roles[i] == MyEnum.CharacterType.Mouse)
            {
                var lessons = new PcsPuzzleDevice[3];
                for (int gateIndex = 0; gateIndex < 3; gateIndex++)
                {
                    float x = -35 + gateIndex * 5;
                    var gate = Gate("Tutorial_Mouse_HackGate_" + gateIndex, room, x, floor, gateIndex == 1 ? 1 : 3.6f, 0);
                    lessons[gateIndex] = Control("Tutorial_Mouse_Console_" + gateIndex, room, new Vector2(x - 1.2f, floor + .45f), PcsDeviceKind.HackConsole, 0, roles[i], "E: 세 번 눌러 해킹");
                    if (gateIndex > 0) lessons[gateIndex].Links = new[] { gate };
                    else Link(lessons[gateIndex], gate);
                }
                Block("Tutorial_Mouse_LowTunnel", room, new Vector2(-32.3f, floor + 1.7f), new Vector2(2.7f, 1.8f));
                Block("Tutorial_Mouse_RaisedPassage", room, new Vector2(-27.8f, floor + .8f), new Vector2(4.8f, 1.6f));
                lessons[2].transform.position = new Vector3(-26.2f, floor + 2.05f, 0);
                var ladder = Zone("Tutorial_Mouse_Ladder", room, new Vector2(-30.6f, floor + 1.3f), new Vector2(.8f, 3.6f), PcsDeviceKind.Ladder, 0, roles[i]);
                ladder.Instruction = "W/S: 사다리 오르내리기";
                exits[i] = TutorialFinish(room, roles[i], floor, 0, lessons);
                Label(room, "E: 세 번 해킹 → 사다리 전개 → 마지막 문\n사다리에서 W/S로 오르내리세요.", new Vector3(-38, floor + 2.5f, 0), .13f);
            }
            else if (roles[i] == MyEnum.CharacterType.Frog)
            {
                Dummy("Tutorial_Frog_Ally", room, new Vector2(-26, floor + .5f));
                Block("Tutorial_Frog_LowObstacle", room, new Vector2(-35.5f, floor + .6f), new Vector2(1.2f, 1.2f));
                var anchor = Control("Tutorial_Frog_Anchor", room, new Vector2(-33, floor + 2.4f), PcsDeviceKind.Anchor, 0, roles[i], "고리를 조준하고 F로 이동하세요.");
                Platform("Tutorial_Frog_AnchorLanding", room, -32.5f, floor + 1.5f, 3, true);
                var remote = Control("Tutorial_Frog_Remote", room, new Vector2(-27.5f, floor + 2), PcsDeviceKind.RemoteButton, 0, roles[i], "목표를 조준하고 F를 누르세요.");
                exits[i] = TutorialFinish(room, roles[i], floor, 1, anchor, remote);
                Label(room, "고리 두 개를 조준하고 F로 이동하세요.\n천장 레버를 당긴 뒤 출구로 이동하세요.", new Vector3(-37.5f, floor + 3.5f, 0), .11f);
            }
            else
            {
                // Two distinct allies are provided by D14, not counted twice from one object.
                Dummy("Tutorial_Bear_Ally_A", room, new Vector2(-38.5f, floor + .5f));
                Dummy("Tutorial_Bear_Ally_B", room, new Vector2(-29.5f, floor + .5f));
                Platform("Tutorial_Bear_Floor", room, -37.5f, floor, 10);
                Platform("Tutorial_Bear_RightFloor", room, -25.5f, floor, 10);
                var bridge = Device(Platform("Tutorial_Bear_Bridge", room, -31.5f, floor, 2.3f, true), PcsDeviceKind.TimedPlatform, 0);
                var plate = AllyPlate("Tutorial_Bear_AllyPlate", room, -34, floor);
                Link(plate, bridge);
                var secondPlate = AllyPlate("Tutorial_Bear_RightAllyPlate", room, -29, floor);
                Link(secondPlate, bridge);
                var firstButton = Control("Tutorial_Bear_AllyButton_A", room, new Vector2(-25.5f, floor + 3.2f), PcsDeviceKind.RemoteButton, 0, roles[i], "G: 동료 들기 → 조준+F: 던지기");
                var secondButton = Control("Tutorial_Bear_AllyButton_B", room, new Vector2(-22.6f, floor + 3.2f), PcsDeviceKind.RemoteButton, 0, roles[i], "다른 동료로 이 장치를 누르세요.");
                firstButton.AcceptDummyOnly = secondButton.AcceptDummyOnly = true;
                exits[i] = TutorialFinish(room, roles[i], floor, 0, firstButton, secondButton);
                Label(room, "G: 들기·내리기   조준+F: 동료·돌 던지기\n동료를 압력판에 두고 다리를 건너세요.", new Vector3(-37, floor + 3, 0), .11f);
            }
            foreach (var lesson in room.GetComponentsInChildren<PcsPuzzleDevice>(true)) lesson.RequiredRole = roles[i];
            Platform("Tutorial_" + roles[i] + "_ReturnBridge", room, -18.9f, roles[i] == MyEnum.CharacterType.Rabbit ? floor + 3.7f : floor, 1.8f, true);
        }
        // A separate descent lane keeps tutorial exits away from the 1-1 -> shaft corridor.
        Platform("Tutorial_ReturnFloor", tutorial, -16.7f, -3, 9);
        Block("Tutorial_ReturnLaneDivider", tutorial, new Vector2(-16.4f, 16), new Vector2(.45f, 33));
        var returnLadder = Zone("Tutorial_ReturnLadder", tutorial, new Vector2(-17.4f, 13), new Vector2(1, 31), PcsDeviceKind.Ladder, -1);
        returnLadder.Instruction = "W/S: 합류 지점으로 이동";
        var rendezvous = Zone("Tutorial_FourRoleRendezvous", tutorial, new Vector2(-14.6f, -2), new Vector2(4, 2), PcsDeviceKind.Exit, 0);
        rendezvous.RequiredRolesMask = 30;
        rendezvous.RequireAllRoles = true;
        Link(rendezvous, exits);
        var entry = Gate("Tutorial_TeamEntryDoor", tutorial, -12.3f, -3, 3.4f, 0);
        entry.RequireAllLinks = true;
        Link(entry, rendezvous);
        Label(tutorial, "네 명 모두 모여 주세요.", new Vector3(-15, -.2f, 0), .16f);
        director.TutorialSpawns = Spawns(tutorial, "Spawn_Tutorial", new[] { new Vector2(-40.5f, -2.85f), new Vector2(-40.5f, 24.15f), new Vector2(-40.5f, 6.15f), new Vector2(-40.5f, 15.15f) });
        director.LateJoinWaiting = Node("LateJoin_WaitForNextTeam", tutorial, new Vector3(-46, -2.85f, 0)).transform;
        Platform("LateJoin_WaitingDeck", tutorial, -46, -3, 3);
        Label(tutorial, "이미 출발한 팀입니다.\n새 방에서 네 역할로 다시 모여 주세요.", new Vector3(-46, -1, 0), .1f);
        Zone("Tutorial_FallReset", tutorial, new Vector2(-31, -6), new Vector2(31, 1), PcsDeviceKind.KillZone, 0);
    }
}

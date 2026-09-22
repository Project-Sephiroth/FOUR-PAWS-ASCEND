#if DEVELOPMENT_BUILD && !UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

// Opt-in process orchestration only. All selection, scene loading and spawning stay in the normal game path.
public sealed class PcsSharedSmokeBootstrap : MonoBehaviour
{
    private sealed class SmokeFailure : Exception { public SmokeFailure(string reason) : base(reason) { } }
    private sealed class Options
    {
        public MyEnum.CharacterType Role;
        public string Room;
        public string Run = "default";
        public int Players = 4;
        public int Seconds = 20;
        public string Scenario = "initial";
    }

    [Serializable] private sealed class DeviceSample
    {
        public int index, active, phase, counter, window;
        public Vector2 position;
    }
    [Serializable] private sealed class DeviceSet { public DeviceSample[] devices; }
    [Serializable] private sealed class PlayerSample
    {
        public string role;
        public Vector3 position;
        public bool localAuthority, inputEnabled;
        public string bodyType;
        public bool bodySimulated, fullKinematicContacts;
    }
    [Serializable] private sealed class Observation
    {
        public double elapsedSeconds;
        public int networkTick, connectedPlayers, registeredPlayers, localOwnedPlayers;
        public int managerInstances, directorInstances;
        public string managerAuthorityRole;
        public bool checksPassed, sharedMode, teamLocked, isMaster, managerAuthority, directorAuthority;
        public bool localRoleMatchesSelection, localStartingTeamMember, cameraBoundToLocalPlayer, localOverlayAvailable;
        public bool physicsAuthorityMatches;
        public int activeSection, resetEpoch, completedTutorialMask, energy, batteries;
        public bool stageOneHacked, shaftStarted, shaftArrived;
        public string deviceStateSha256;
        public PlayerSample[] players;
        public DeviceSample[] devices;
    }
    [Serializable] private sealed class SmokeReport
    {
        public string utc, unity, role, phase, scenario;
        public string scope = "Shared process smoke via normal Lobby -> selection -> Puzzle -> PcsPlayerSpawner. Initial roster/device replication and camera binding only; no input action, puzzle completion, late join or authority-loss claim.";
        public int requestedPlayers, requestedObservationSeconds;
        public bool developmentBuild, normalFlowReached, normalShutdownCompleted, passed;
        public double elapsedSeconds;
        public List<string> events = new List<string>();
        public List<string> failures = new List<string>();
        public List<Observation> observations = new List<Observation>();
        public Observation lastPendingObservation;
        public bool screenshotRequested, screenshotWritten;
        public List<LifecycleCheck> lifecycleChecks = new List<LifecycleCheck>();
        public List<CarryObservation> carryObservations = new List<CarryObservation>();
        public List<PressureObservation> pressureObservations = new List<PressureObservation>();
        public List<LayoutObservation> layoutObservations = new List<LayoutObservation>();
        public List<string> fixtures = new List<string>();
    }
    [Serializable] private sealed class LifecycleCheck { public string id, detail; public bool passed; }
    [Serializable] private sealed class LayoutObservation
    {
        public string stage, localRole;
        public int tick, deviceId, active, counter;
        public Vector2 replicated, actualRoot, actualBody, actualColliderCenter, expectedColliderCenter, target;
        public bool colliderEnabled, canClimb, leverLower, stageOneHacked;
        public float poseError, colliderError, bearFootGap;
    }
    [Serializable] private sealed class CarryObservation
    {
        public string stage;
        public int tick;
        public bool separateOwners, localOwnsOnlySelectedRole, carrierPointsToPassenger, passengerPointsToCarrier;
        public bool collisionPairsIgnored, passengerInputObservedLocally, passengerOwnerInputCanMove;
        public Vector2 carrierPosition, passengerPosition;
        public float horizontalGap, passengerFootAboveHead;
    }
    [Serializable] private sealed class PressureObservation
    {
        public string stage, bearBodyType;
        public int tick, activeSection, plateIndex, wallIndex, plateActive, wallActive, bearTopContacts;
        public bool localIsDirectorAuthority, localOwnsBear, bearOwnerIsDirectorOwner, bearFullKinematicContacts;
        public Vector2 bearPosition, wallPosition;
        public float footAboveSupport;
        public Vector2 actualWallTransform, actualWallBody, actualWallColliderCenter;
        public float wallRootError, wallBodyError, wallColliderError;
    }
    [Serializable] private sealed class LifecycleMarker
    {
        public string utc, role, stage;
        public int consoleIndex, gateIndex, counter, mouseEpoch, resetEpoch, energy, batteries;
        public Vector2 closed, open, handoffPose;
    }

    private static readonly FieldInfo CameraTarget = typeof(PcsLocalCameraFollow).GetField("target", BindingFlags.Instance | BindingFlags.NonPublic);
    private Vector2 pressureColliderOffset;
    private readonly List<ContactPoint2D> layoutContacts = new List<ContactPoint2D>(12);
    private Options options;
    private SmokeReport report;
    private string outputPath;
    private double startedAt;
    private bool lobbyReady, joined, joinFailed, finished;
    private NetworkLauncher launcher;
    private NetworkRunner runner;
    private string RunDirectory => Path.GetDirectoryName(outputPath);
    private bool IsLateJoinRole => options.Scenario == "lifecycle" &&
        (options.Role == MyEnum.CharacterType.Bear || options.Role == MyEnum.CharacterType.Frog);
    private MyEnum.CharacterType ScenarioLeader => options.Scenario == "pressure-two" || options.Scenario == "shaft-two"
        ? MyEnum.CharacterType.Mouse : MyEnum.CharacterType.Rabbit;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void StartRequestedSmoke()
    {
        string[] arguments = Environment.GetCommandLineArgs();
        if (!arguments.Contains("--pcs-smoke-role") && !arguments.Contains("--pcs-smoke-room")) return;
        if (FindFirstObjectByType<PcsSharedSmokeBootstrap>() != null) return;
        var go = new GameObject("PCS explicit development Shared smoke");
        DontDestroyOnLoad(go);
        var smoke = go.AddComponent<PcsSharedSmokeBootstrap>();
        smoke.StartCoroutine(smoke.Execute(arguments));
    }

    private IEnumerator Execute(string[] arguments)
    {
        startedAt = Time.realtimeSinceStartupAsDouble;
        report = new SmokeReport { utc = DateTime.UtcNow.ToString("O"), unity = Application.unityVersion, developmentBuild = Debug.isDebugBuild, phase = "arguments" };
        outputPath = Path.Combine(Path.GetTempPath(), "FourPawsSecondPass", "SharedSmoke", "invalid-arguments.json");
        var flows = new Stack<IEnumerator>();
        flows.Push(Run(arguments));
        while (flows.Count != 0)
        {
            bool moved;
            object yielded = null;
            try
            {
                var flow = flows.Peek();
                moved = flow.MoveNext();
                if (moved) yielded = flow.Current;
                else
                {
                    (flows.Pop() as IDisposable)?.Dispose();
                    continue;
                }
            }
            catch (Exception exception)
            {
                // SDK exception text can contain connection identifiers. Record only our own reason or the type.
                report.failures.Add(exception is SmokeFailure ? exception.Message : exception.GetType().Name);
                break;
            }
            if (yielded is IEnumerator nested) { flows.Push(nested); continue; }
            yield return yielded;
        }

        while (flows.Count > 0) (flows.Pop() as IDisposable)?.Dispose();
        if (runner != null && runner.IsRunning && runner.TryGetPlayerObject(runner.LocalPlayer, out var owned) && owned != null)
        {
            var input = owned.GetComponent<PlayerInput>();
            if (input != null) input.ClearDevelopmentInput();
        }

        if (launcher != null)
        {
            launcher.OnLobbyJoined -= OnLobbyJoined;
            launcher.OnSessionAccessFinished -= OnSessionAccessFinished;
        }
        Phase("normal-shutdown");
        Task shutdown = null;
        try
        {
            if (runner == null) runner = FindFirstObjectByType<NetworkRunner>();
            if (runner != null) shutdown = runner.Shutdown();
            else report.normalShutdownCompleted = true;
        }
        catch (Exception exception) { report.failures.Add("shutdown_" + exception.GetType().Name); }
        double shutdownDeadline = Time.realtimeSinceStartupAsDouble + 10d;
        while (shutdown != null && !shutdown.IsCompleted && Time.realtimeSinceStartupAsDouble < shutdownDeadline) yield return null;
        if (shutdown != null)
        {
            report.normalShutdownCompleted = shutdown.IsCompleted && !shutdown.IsFaulted && !shutdown.IsCanceled;
            if (!report.normalShutdownCompleted) report.failures.Add("normal_shutdown_failed_or_timed_out");
        }
        report.passed = report.normalFlowReached && report.normalShutdownCompleted && report.failures.Count == 0 &&
            report.observations.Count >= 2 && report.observations.All(o => o.checksPassed) && report.lifecycleChecks.All(c => c.passed);
        report.screenshotWritten = options != null && File.Exists(Path.Combine(RunDirectory, options.Role + ".png"));
        finished = true;
        Phase(report.passed ? "passed" : "failed");
        Application.Quit(report.passed ? 0 : 1);
    }

    private IEnumerator Run(string[] arguments)
    {
        options = ParseOptions(arguments);
        outputPath = Path.Combine(Path.GetTempPath(), "FourPawsSecondPass", "SharedSmoke", options.Run, options.Role + ".json");
        report.role = options.Role.ToString();
        report.requestedPlayers = options.Players;
        report.requestedObservationSeconds = options.Seconds;
        report.scenario = options.Scenario;
        if (options.Scenario != "initial")
        {
            report.scope = options.Scenario == "lifecycle"
                ? "Normal Shared lobby/spawn plus A: Mouse walks and hacks; B: late-join state and denied reset; C: explicit normal tutorial retry and master departure during door motion. No fixture teleport, four-role completion or stage-two channel/ascent proof."
                : options.Scenario == "carry-two"
                ? "Two independent Shared owners after normal lobby/spawn. Explicit runtime-only identical static-floor fixture and one owner-authorized ResetAt relocation per actor. Normal G input proves remote attach, carried motion, passenger drop/remote clear, reboard and carrier release. Not an authored route or four-role completion test."
                : options.Scenario == "shaft-two"
                ? "Two normal Shared lobby/spawn owners observe the authored shaft elevator. Only its state authority issues Raise/Hold through the production API; the proxy must be denied. Both peers check replicated command, root/body/collider motion and upper-stop Hold. No hacking, channel, battery, combat, boarding formation or final-exit puzzle remains; this is not passenger or full-route play proof."
                : options.Scenario == "layout-four"
                ? "Four normal Shared lobby/selection/spawn owners. Explicit owner-only starting-pose fixtures for authored Mouse consoles, deployed ladder, StageOne plate, Frog hold stance and Bear beside the lower lift; master-only ActiveSection=1 preparation. Actual E pulses, W/S sample, dynamic plate contact, aimed held/released F, normal Bear boarding input and riding are tested. All four peers check final physical device poses. No tutorial whole-route, zigzag traversal, Bear puzzle completion or full-game completion claim."
                : "Normal two-peer Shared lobby/spawn, Mouse master and remote Bear owner. Explicit master-only ActiveSection=1 setup plus Bear-owner ResetAt on/off the authored plate. Actual dynamic/kinematic contact and production pressure/wall replication; no forced pressure state or authority override. Not tutorial progression, route traversal or four-player completion.";
            if (File.Exists(MarkerPath("registered", options.Role))) throw new SmokeFailure("lifecycle_run_already_used_for_this_role");
            WriteMarker("registered");
        }
        Application.runInBackground = true;
        if (!Debug.isDebugBuild) throw new SmokeFailure("development_build_required");
        if (SceneManager.GetActiveScene().name != "LobbyScene") throw new SmokeFailure("first_scene_must_be_LobbyScene");
        Phase("waiting-for-lobby");
        double startupDeadline = Time.realtimeSinceStartupAsDouble + (options.Scenario == "lifecycle" ? 180d : 90d);
        while (GameManager.Instance == null || (launcher = FindFirstObjectByType<NetworkLauncher>()) == null)
        {
            CheckDeadline(startupDeadline, "lobby_components_timeout");
            yield return null;
        }
        // This coroutine subscribes during AfterSceneLoad, before the Launcher's asynchronous Start completes.
        launcher.OnLobbyJoined += OnLobbyJoined;
        launcher.OnSessionAccessFinished += OnSessionAccessFinished;
        GameManager.Instance.SetMyCharacter(options.Role);
        while (!lobbyReady)
        {
            runner = FindFirstObjectByType<NetworkRunner>();
            if (runner != null && runner.LobbyInfo.IsValid) lobbyReady = true;
            CheckDeadline(startupDeadline, "shared_lobby_timeout");
            yield return null;
        }
        if (options.Scenario != "initial")
        {
            if (IsLateJoinRole)
            {
                Phase("waiting-for-A-before-late-join");
                yield return WaitForMarker("a-complete", MyEnum.CharacterType.Rabbit, startupDeadline);
            }
            else if (options.Role != ScenarioLeader)
                yield return WaitForMarker("room-created", ScenarioLeader, startupDeadline);
        }
        Phase("joining-existing-launcher");
        launcher.TryAccessSession(options.Room);
        while (!joined)
        {
            if (joinFailed) throw new SmokeFailure("existing_launcher_session_join_failed");
            CheckDeadline(startupDeadline, "session_join_timeout");
            yield return null;
        }
        if (runner == null) runner = FindFirstObjectByType<NetworkRunner>();
        if (options.Scenario != "initial" && options.Role == ScenarioLeader)
        {
            LifecycleAssert("C0_original_master", runner != null && runner.IsSharedModeMasterClient, "The scenario's designated leader must create this fresh test room first.");
            WriteMarker("room-created");
        }
        Phase("waiting-for-role-reservations");
        while (true)
        {
            CheckDeadline(startupDeadline, "role_reservation_or_scene_start_timeout");
            var manager = NetworkGameManager.Instance;
            if (manager != null && manager.IsReady && manager.Runner == runner)
            {
                if (runner.GameMode != GameMode.Shared) throw new SmokeFailure("normal_runner_is_not_Shared");
                int count = 0;
                var roles = new HashSet<MyEnum.CharacterType>();
                foreach (var player in runner.ActivePlayers)
                {
                    count++;
                    if (manager.TryGetSelectedRole(player, out var role)) roles.Add(role);
                }
                int initialCount = options.Scenario == "lifecycle" ? 2 : options.Players;
                if (!manager.TeamLocked && count > initialCount) throw new SmokeFailure("unexpected_extra_initial_participant");
                if (count == initialCount && roles.Count == initialCount && !manager.TeamLocked && manager.CanStartGame)
                    manager.LoadScene("Puzzle");
                if (manager.TeamLocked) break;
            }
            yield return null;
        }
        if (options.Scenario == "lifecycle")
        {
            yield return RunLifecycle();
            yield break;
        }
        if (options.Scenario == "carry-two")
        {
            yield return RunCarryTwo();
            yield break;
        }
        if (options.Scenario == "pressure-two")
        {
            yield return RunPressureTwo();
            yield break;
        }
        if (options.Scenario == "shaft-two")
        {
            yield return RunShaftTwo();
            yield break;
        }
        if (options.Scenario == "layout-four")
        {
            yield return RunLayoutFour();
            yield break;
        }
        Phase("waiting-for-normal-player-spawns");
        Observation first = null;
        double nextPendingRecord = 0d;
        while (!TryObserve(out first) || !first.checksPassed)
        {
            if (first != null && Time.realtimeSinceStartupAsDouble >= nextPendingRecord)
            {
                report.lastPendingObservation = first;
                Save();
                nextPendingRecord = Time.realtimeSinceStartupAsDouble + 2d;
            }
            CheckDeadline(startupDeadline, "Puzzle_registration_spawn_or_camera_timeout");
            yield return null;
        }
        report.normalFlowReached = true;
        report.observations.Add(first);
        CaptureGameView();
        Phase("observing-initial-replication");
        double until = Time.realtimeSinceStartupAsDouble + options.Seconds;
        double nextSample = Time.realtimeSinceStartupAsDouble + 2d;
        while (Time.realtimeSinceStartupAsDouble < until)
        {
            if (Time.realtimeSinceStartupAsDouble >= nextSample)
            {
                if (!TryObserve(out var observation)) throw new SmokeFailure("normal_flow_objects_disappeared");
                report.observations.Add(observation);
                Save();
                if (!observation.checksPassed) throw new SmokeFailure("replication_observation_checks_failed");
                nextSample += 2d;
            }
            yield return null;
        }
        // Small grace period lets peers finish their own final observation before anyone leaves.
        Phase("observation-complete-shutdown-grace");
        double graceDeadline = Time.realtimeSinceStartupAsDouble + 3d;
        while (Time.realtimeSinceStartupAsDouble < graceDeadline) yield return null;
    }

    private IEnumerator RunLayoutFour()
    {
        Phase("layout-waiting-for-normal-four-spawns");
        yield return WaitForObservation(4, 45d);
        report.normalFlowReached = true;
        CaptureGameView();
        var puzzle = PcsPuzzleDirector.Instance;
        var ownActor = LocalActor();
        var ownInput = ownActor.GetComponent<PlayerInput>();
        var ownMover = ownActor.GetComponent<Mover>();
        ownInput.InjectDevelopmentInput(Vector2.zero, false);
        var owners = new HashSet<PlayerRef>(TeamRoles.Select(role => ActorForRole(role).StateAuthority));
        LifecycleAssert("L_four_distinct_owners", owners.Count == 4 && ownActor.HasStateAuthority &&
            puzzle.HasStateAuthority == (options.Role == MyEnum.CharacterType.Rabbit),
            "Rabbit owns the real Shared director; Frog, Mouse and Bear are three distinct remote owners.");

        var mouseConsoles = puzzle.Devices.Where(d => d != null && d.Section == 0 &&
            d.RequiredRole == MyEnum.CharacterType.Mouse && d.Kind == PcsDeviceKind.HackConsole).OrderBy(d => d.DeviceId).ToArray();
        var ladder = LayoutDevice(64, PcsDeviceKind.Ladder);
        LifecycleAssert("L_mouse_contract", mouseConsoles.Length == 3 && ladder.DeployableLadder &&
            ladder.LowerStop != null && ladder.UpperStop != null && ladder.Trigger != null &&
            mouseConsoles[1].Links.Contains(ladder), "Use the three authored Mouse consoles and the second console's deployable ladder.");
        LifecycleAssert("L_ladder_stowed_unclimbable", !ladder.CanClimb && !ladder.Trigger.enabled,
            "The deployed-climb contract is initially false on each actual peer.");
        Vector2 ladderColliderOffset = LayoutColliderOffset(ladder);
        for (int index = 0; index < mouseConsoles.Length; index++)
        {
            var console = mouseConsoles[index];
            string step = "layout-mouse-console-" + index;
            if (options.Role == MyEnum.CharacterType.Mouse)
                yield return LayoutHack(console, step, true);
            yield return WaitForCondition(() => puzzle.States[console.DeviceId].Active != 0, 20d, step + "_replication_timeout");
            LifecycleAssert("L_mouse_console_" + index, puzzle.States[console.DeviceId].Counter == console.RequiredInputs,
                "Each required increment came from the Mouse owner's ordinary E input path.");
            if (index == 1)
            {
                yield return LayoutWaitPose("ladder-deployed", ladder, ladder.LowerStop.position, ladderColliderOffset, 20d);
                yield return WaitForCondition(() => ladder.CanClimb && ladder.Trigger.enabled, 3d,
                    "layout_ladder_arrived_but_not_climbable");
                LifecycleAssert("L_ladder_deployed_climbable", ladder.CanClimb && ladder.Trigger.enabled,
                    "Actual trigger and visual root have descended before climbing becomes available.");
                yield return LayoutBarrier(step + "-pose");
                if (options.Role == MyEnum.CharacterType.Mouse)
                {
                    float foot = ownMover.BodyCollider.bounds.min.y - ownMover.Body.position.y;
                    Vector2 pose = new Vector2(ladder.Trigger.bounds.center.x, ladder.Trigger.bounds.min.y + 0.15f - foot);
                    LayoutRelocate(pose, "deployed ladder lower end; all later vertical motion is normal W/S input");
                    yield return Delay(0.25d);
                    float before = ownMover.Body.position.y;
                    ownInput.InjectDevelopmentInput(Vector2.zero, false, ladder: 1f);
                    yield return Delay(0.65d);
                    ownInput.InjectDevelopmentInput(Vector2.zero, false);
                    float afterUp = ownMover.Body.position.y;
                    LifecycleAssert("L_mouse_actual_W_climb", afterUp - before > 0.7f,
                        "Normal ladder input moved the owner's dynamic body upward; no ladder-motion pose writes were used.");
                    ownInput.InjectDevelopmentInput(Vector2.zero, false, ladder: -1f);
                    yield return Delay(0.3d);
                    ownInput.InjectDevelopmentInput(Vector2.zero, false);
                    LifecycleAssert("L_mouse_actual_S_descend", afterUp - ownMover.Body.position.y > 0.3f,
                        "Normal reverse ladder input moved down the deployed ladder.");
                }
            }
            else
            {
                var gate = console.Links.FirstOrDefault(d => d != null && d.Kind == PcsDeviceKind.SlidingWall);
                LifecycleAssert("L_mouse_door_link_" + index, gate != null && gate.UpperStop != null,
                    "The first/last console links to its actual authored door.");
                yield return LayoutWaitPose("mouse-door-" + index, gate, gate.UpperStop.position, LayoutColliderOffset(gate), 20d);
            }
            yield return LayoutBarrier(step + "-done");
        }

        Phase("layout-prepare-stage-one-without-completion-flags");
        report.fixtures.Add("Master-only ActiveSection=1 bypasses earlier progression. No tutorial completion mask, hack result, hold result, device motion state or physics authority is assigned.");
        if (puzzle.HasStateAuthority)
            typeof(PcsPuzzleDirector).GetProperty("ActiveSection").SetValue(puzzle, 1);
        yield return WaitForCondition(() => puzzle.ActiveSection == 1, 10d, "layout_section_replication_timeout");
        LayoutRelocate(options.Role == MyEnum.CharacterType.Frog ? new Vector2(-4.5f, 3.1f) : puzzle.GetRespawnPosition(options.Role),
            options.Role == MyEnum.CharacterType.Frog ? "authored left upper landing, within the ceiling lever's real aim/range" : "authored StageOne spawn preparation");
        var plate = LayoutDevice(1, PcsDeviceKind.PressurePlate);
        var gateOne = LayoutDevice(0, PcsDeviceKind.SlidingWall);
        var lift = LayoutDevice(2, PcsDeviceKind.Elevator);
        var hack = LayoutDevice(3, PcsDeviceKind.HackConsole);
        var lever = LayoutDevice(4, PcsDeviceKind.Lever);
        var bear = ActorForRole(MyEnum.CharacterType.Bear);
        var bearMover = bear.GetComponent<Mover>();
        var frog = ActorForRole(MyEnum.CharacterType.Frog);
        Vector2 wallOffset = LayoutColliderOffset(gateOne);
        Vector2 liftOffset = LayoutColliderOffset(lift);
        LifecycleAssert("L_stage_one_contract", plate.Solid != null && plate.PressureButton != null &&
            gateOne.LowerStop != null && gateOne.UpperStop != null && lift.Solid != null && lift.Body != null &&
            lift.LowerStop != null && lift.MiddleStop != null && lift.UpperStop != null && lever.RequiresRemoteHold &&
            puzzle.Object.StateAuthority != frog.StateAuthority && puzzle.Object.StateAuthority != bear.StateAuthority,
            "Authored pressure, upward door, three-stop lift and hold lever have distinct real remote owners.");
        yield return LayoutWaitPose("lift-upper-before-hack", lift, lift.UpperStop.position, liftOffset, 15d);
        yield return LayoutBarrier("layout-stage-one-ready");
        if (options.Role == MyEnum.CharacterType.Bear)
        {
            float foot = ownMover.BodyCollider.bounds.min.y - ownMover.Body.position.y;
            LayoutRelocate(new Vector2(plate.Solid.bounds.center.x, plate.Solid.bounds.max.y - foot + 0.05f),
                "above the authored pressure support; gravity creates actual top contact");
        }
        yield return WaitForCondition(() => puzzle.States[plate.DeviceId].Active != 0, 12d, "layout_remote_pressure_timeout");

        Phase("layout-pressure-release-before-open-endpoint");
        yield return WaitForCondition(() => puzzle.States[gateOne.DeviceId].Position.y > gateOne.LowerStop.position.y + .45f,
            6d, "layout_pressure_wall_did_not_start_rising");
        yield return LayoutBarrier("layout-pressure-rising-observed");
        int pressureEpoch = puzzle.ResetEpoch;
        if (options.Role == MyEnum.CharacterType.Bear)
        {
            float offX = plate.Solid.bounds.min.x - ownMover.BodyCollider.bounds.extents.x - .12f;
            double deadline = Deadline(5d);
            while (ownMover.Body.position.x > offX)
            {
                CheckDeadline(deadline, "layout_pressure_normal_step_off_timeout");
                ownInput.InjectDevelopmentInput(Vector2.left, false);
                yield return null;
            }
            ownInput.InjectDevelopmentInput(Vector2.zero, false);
        }
        yield return WaitForCondition(() => puzzle.States[plate.DeviceId].Active == 0, 5d,
            "layout_pressure_normal_step_off_not_replicated");
        Vector2 releaseWallPose = puzzle.States[gateOne.DeviceId].Position;
        LifecycleAssert("L_pressure_released_before_open_endpoint", releaseWallPose.y > gateOne.LowerStop.position.y + .2f &&
            releaseWallPose.y < gateOne.UpperStop.position.y - .2f,
            "Bear stepped off using normal left input while the real wall was between its endpoints. No relocation after initial plate setup.");
        report.events.Add("Pressure release during rise: peer=" + options.Role + "; tick=" + runner.Tick.Raw +
            "; wall=" + releaseWallPose + "; closed=" + gateOne.LowerStop.position + "; open=" + gateOne.UpperStop.position);
        Vector2 previousWallPose = releaseWallPose;
        int previousWallTick = runner.Tick.Raw;
        float maxObservedWallStep = 0f;
        double reverseDeadline = Deadline(4d);
        while (puzzle.States[gateOne.DeviceId].Position.y >= releaseWallPose.y - .2f)
        {
            CheckDeadline(reverseDeadline, "layout_pressure_wall_did_not_reverse_from_current_pose");
            Vector2 nextWallPose = puzzle.States[gateOne.DeviceId].Position;
            int tick = runner.Tick.Raw;
            float step = Vector2.Distance(previousWallPose, nextWallPose);
            float allowance = gateOne.Speed * runner.DeltaTime * Mathf.Max(1, tick - previousWallTick) + .25f;
            if (step > allowance || nextWallPose.y > previousWallPose.y + .12f)
                throw new SmokeFailure("layout_pressure_wall_teleported_or_continued_rising_after_release");
            maxObservedWallStep = Mathf.Max(maxObservedWallStep, step);
            previousWallPose = nextWallPose; previousWallTick = tick;
            yield return null;
        }
        LifecycleAssert("L_pressure_wall_reverses_without_restart", puzzle.ResetEpoch == pressureEpoch &&
            Vector2.Distance(puzzle.States[gateOne.DeviceId].Position, gateOne.LowerStop.position) > .05f,
            "The released wall moved down through an intermediate pose instead of restarting at either endpoint; sampled max step=" + maxObservedWallStep);
        yield return LayoutWaitPose("pressure-interrupted-rise-closed", gateOne, gateOne.LowerStop.position, wallOffset, 12d);
        yield return LayoutBarrier("layout-pressure-reverse-closed");
        if (options.Role == MyEnum.CharacterType.Bear)
        {
            yield return WaitForCondition(() => ownMover.Grounded, 3d, "layout_Bear_not_grounded_before_plate_return");
            ownInput.InjectDevelopmentInput(Vector2.right, true);
            double deadline = Deadline(5d);
            while (ownMover.Body.position.x < plate.Solid.bounds.center.x - .03f)
            {
                CheckDeadline(deadline, "layout_Bear_normal_plate_return_timeout");
                ownInput.InjectDevelopmentInput(Vector2.right, false);
                yield return null;
            }
            ownInput.InjectDevelopmentInput(Vector2.zero, false);
        }
        yield return WaitForCondition(() => puzzle.States[plate.DeviceId].Active != 0, 8d,
            "layout_pressure_normal_return_not_pressed");
        yield return LayoutBarrier("layout-pressure-normal-return");
        yield return LayoutWaitPose("remote-pressure-door-open", gateOne, gateOne.UpperStop.position, wallOffset, 20d);
        yield return LayoutBarrier("layout-pressure-open");
        if (options.Role == MyEnum.CharacterType.Mouse)
            yield return LayoutHack(hack, "layout-stage-one-hack", true);
        yield return WaitForCondition(() => puzzle.StageOneHacked, 20d, "layout_stage_one_actual_E_hack_timeout");
        yield return LayoutWaitPose("lift-middle-after-hack", lift, lift.MiddleStop.position, liftOffset, 20d);
        yield return LayoutBarrier("layout-lift-middle");

        Phase("layout-remote-Frog-owner-held-F");
        if (options.Role == MyEnum.CharacterType.Frog)
            ownInput.InjectDevelopmentInput(Vector2.zero, false, ability: true, aim: lever.InteractionPoint, abilityHeld: true);
        yield return WaitForCondition(() => puzzle.LeverLower && puzzle.States[lever.DeviceId].Active != 0, 8d,
            "layout_remote_aimed_F_hold_rejected");
        yield return LayoutWaitPose("lift-lower-held", lift, lift.LowerStop.position, liftOffset, 20d);
        yield return LayoutBarrier("layout-lift-lower");
        if (options.Role == MyEnum.CharacterType.Bear)
        {
            float foot = ownMover.BodyCollider.bounds.min.y - ownMover.Body.position.y;
            Vector2 beside = new Vector2(lift.Solid.bounds.max.x + ownMover.BodyCollider.bounds.extents.x + 0.08f,
                lift.Solid.bounds.max.y - foot + 0.04f);
            LayoutRelocate(beside, "beside the low lift, not on it; normal horizontal input must board the physical platform");
            float initialX = ownMover.Body.position.x;
            double deadline = Deadline(8d);
            float boardX = lift.Solid.bounds.max.x - ownMover.BodyCollider.bounds.extents.x - 0.4f;
            while (ownMover.Body.position.x > boardX)
            {
                CheckDeadline(deadline, "layout_Bear_normal_boarding_timeout");
                ownInput.InjectDevelopmentInput(Vector2.left, false);
                yield return null;
            }
            ownInput.InjectDevelopmentInput(Vector2.zero, false);
            LifecycleAssert("L_Bear_boarded_with_normal_movement", initialX - ownMover.Body.position.x > 0.4f,
                "Bear moved from the explicit adjacent starting pose using the ordinary movement path.");
        }
        yield return WaitForCondition(() => LayoutBearOnLift(bearMover, lift), 10d, "layout_Bear_physical_support_timeout");
        yield return LayoutWaitPose("pressure-release-closes-door", gateOne, gateOne.LowerStop.position, wallOffset, 20d);
        LifecycleAssert("L_hack_survives_pressure_release", puzzle.StageOneHacked && puzzle.States[hack.DeviceId].Active != 0,
            "Moving Bear off the plate closes its door without erasing the completed Mouse hack.");
        Vector2 boarded = bearMover.Body.position;
        yield return LayoutBarrier("layout-Bear-boarded");
        if (options.Role == MyEnum.CharacterType.Frog)
            ownInput.InjectDevelopmentInput(Vector2.zero, false, aim: lever.InteractionPoint);
        yield return WaitForCondition(() => !puzzle.LeverLower && puzzle.States[lever.DeviceId].Active == 0, 5d,
            "layout_F_release_did_not_clear_remote_hold");
        yield return LayoutWaitPose("lift-middle-release-with-Bear", lift, lift.MiddleStop.position, liftOffset, 20d);
        yield return WaitForCondition(() => LayoutBearOnLift(bearMover, lift), 5d, "layout_Bear_lost_support_on_ascent");
        LifecycleAssert("L_remote_Bear_rides_released_lift", bearMover.Body.position.y - boarded.y >
            (lift.MiddleStop.position.y - lift.LowerStop.position.y) - 0.2f,
            "The remote Bear remained on the real support and ascended after Frog released F; no rider pose fixture occurred during ascent.");
        yield return LayoutBarrier("layout-held-release-complete");

        Phase("layout-Bear-normal-middle-deck-disembark");
        report.scope += " Also tests pressure release before the wall's open endpoint and one normal Bear jump from the middle lift onto the adjacent fixed deck.";
        var landingCandidates = lift.transform.parent.GetComponentsInChildren<Collider2D>().Where(c => c.enabled &&
            !c.isTrigger && c.gameObject.activeInHierarchy && c.name == "Landing_Right_Console" &&
            Mathf.Abs(Mathf.DeltaAngle(c.transform.eulerAngles.z, 0f)) < 1f &&
            c.bounds.min.x > lift.Solid.bounds.max.x && Mathf.Abs(c.bounds.max.y - lift.Solid.bounds.max.y) < .3f).ToArray();
        LifecycleAssert("L_Bear_right_fixed_landing_contract", landingCandidates.Length == 1,
            "Resolve the adjacent horizontal fixed deck from the current physical scene; exclude the same-name rotated ramp.");
        Collider2D rightLanding = landingCandidates[0];
        int disembarkEpoch = puzzle.ResetEpoch;
        if (options.Role == MyEnum.CharacterType.Bear)
        {
            float takeoffX = lift.Solid.bounds.max.x - Mathf.Max(.18f, ownMover.BodyCollider.bounds.extents.x * .5f);
            float stopX = rightLanding.bounds.min.x + ownMover.BodyCollider.bounds.extents.x + .25f;
            double deadline = Deadline(5d);
            while (ownMover.Body.position.x < takeoffX)
            {
                CheckDeadline(deadline, "layout_Bear_disembark_takeoff_timeout");
                ownInput.InjectDevelopmentInput(Vector2.right, false);
                yield return null;
            }
            ownInput.InjectDevelopmentInput(Vector2.zero, false);
            yield return WaitForCondition(() => ownMover.Grounded && LayoutBearOnLift(ownMover, lift), 2d,
                "layout_Bear_disembark_takeoff_not_supported");
            float startFootY = ownMover.BodyCollider.bounds.min.y;
            float peakFootY = startFootY;
            float startX = ownMover.Body.position.x;
            ownInput.InjectDevelopmentInput(Vector2.right, true);
            deadline = Deadline(6d);
            double nextTrace = 0d;
            while (ownMover.Body.position.x < stopX || !ownMover.Grounded ||
                Mathf.Abs(ownMover.BodyCollider.bounds.min.y - rightLanding.bounds.max.y) > .10f)
            {
                CheckDeadline(deadline, "layout_Bear_normal_disembark_timeout");
                peakFootY = Mathf.Max(peakFootY, ownMover.BodyCollider.bounds.min.y);
                ownInput.InjectDevelopmentInput(ownMover.Body.position.x < stopX ? Vector2.right : Vector2.zero, false);
                if (Time.realtimeSinceStartupAsDouble >= nextTrace)
                {
                    report.events.Add("Bear disembark input trace: tick=" + runner.Tick.Raw + "; position=" + ownMover.Body.position +
                        "; feet=" + ownMover.BodyCollider.bounds.min.y + "; velocity=" + ownMover.Body.linearVelocity + "; grounded=" + ownMover.Grounded);
                    nextTrace = Deadline(.15d);
                }
                yield return null;
            }
            ownInput.InjectDevelopmentInput(Vector2.zero, false);
            LifecycleAssert("L_Bear_one_jump_to_fixed_deck", peakFootY - startFootY > .3f && ownMover.Body.position.x - startX > 1f,
                "Exactly one ordinary jump press and right input cross the authored gap; no pose writes during takeoff, flight or landing.");
        }
        yield return LayoutBarrier("layout-Bear-local-disembark-input-complete");
        Func<bool> bearOnRightLanding = () =>
        {
            Bounds feet = bearMover.BodyCollider.bounds;
            Bounds deck = rightLanding.bounds;
            if (feet.min.x < deck.min.x || feet.max.x > deck.max.x || feet.min.x <= lift.Solid.bounds.max.x + .05f ||
                Mathf.Abs(feet.min.y - deck.max.y) > .10f) return false;
            layoutContacts.Clear();
            rightLanding.GetContacts(layoutContacts);
            return layoutContacts.Any(c => c.enabled && Mathf.Abs(c.normal.y) >= .55f &&
                Mathf.Abs(c.point.y - deck.max.y) < .10f &&
                ((c.collider == bearMover.BodyCollider && c.otherCollider == rightLanding) ||
                 (c.otherCollider == bearMover.BodyCollider && c.collider == rightLanding)));
        };
        yield return WaitForCondition(bearOnRightLanding, 10d, "layout_Bear_fixed_deck_contact_not_replicated");
        yield return Delay(.25d);
        LifecycleAssert("L_Bear_disembarked_and_stopped", bearOnRightLanding() && !LayoutBearOnLift(bearMover, lift) &&
            puzzle.ResetEpoch == disembarkEpoch && puzzle.StageOneHacked && !puzzle.LeverLower &&
            (options.Role != MyEnum.CharacterType.Bear || (ownMover.Grounded && Mathf.Abs(ownMover.Body.linearVelocity.x) < .1f)),
            "Every peer sees actual fixed-deck contact, body fully clear of the moving lift and no reset; the owner also verifies stopped horizontal velocity.");
        yield return LayoutBarrier("layout-Bear-disembarked");

        Phase("layout-aim-loss-releases-hold-during-motion");
        if (options.Role == MyEnum.CharacterType.Frog)
            ownInput.InjectDevelopmentInput(Vector2.zero, false, aim: lever.InteractionPoint, abilityHeld: true);
        yield return WaitForCondition(() => puzzle.LeverLower, 6d, "layout_second_hold_timeout");
        yield return LayoutBarrier("layout-second-hold-observed");
        yield return Delay(0.35d);
        if (options.Role == MyEnum.CharacterType.Frog)
            ownInput.InjectDevelopmentInput(Vector2.zero, false, aim: (Vector2)ownMover.BodyCollider.bounds.center + Vector2.down * 2f, abilityHeld: true);
        yield return WaitForCondition(() => !puzzle.LeverLower, 5d, "layout_aim_change_stuck_hold");
        yield return LayoutWaitPose("lift-middle-after-aim-loss", lift, lift.MiddleStop.position, liftOffset, 20d);
        yield return LayoutBarrier("layout-aim-loss-complete");
        if (options.Role == MyEnum.CharacterType.Frog)
            ownInput.InjectDevelopmentInput(Vector2.zero, false, aim: lever.InteractionPoint, abilityHeld: true);
        yield return WaitForCondition(() => puzzle.LeverLower, 6d, "layout_third_hold_timeout");
        yield return LayoutBarrier("layout-disable-hold-observed");
        if (options.Role == MyEnum.CharacterType.Frog) ownInput.enabled = false;
        yield return WaitForCondition(() => !puzzle.LeverLower, 5d, "layout_disabled_input_stuck_hold");
        yield return LayoutWaitPose("lift-middle-after-input-disable", lift, lift.MiddleStop.position, liftOffset, 20d);
        yield return LayoutBarrier("layout-disabled-release-observed");
        if (options.Role == MyEnum.CharacterType.Frog)
        {
            ownInput.enabled = true;
            ownInput.InjectDevelopmentInput(Vector2.zero, false, aim: lever.InteractionPoint, abilityHeld: true);
        }
        yield return WaitForCondition(() => puzzle.LeverLower, 6d, "layout_lease_hold_timeout");
        yield return LayoutBarrier("layout-lease-hold-observed");
        // Stopping only the owner's Mover stops input sampling/renewal without sending an explicit release.
        // Remote Mover components remain enabled, so the director must recover through its lease.
        if (options.Role == MyEnum.CharacterType.Frog) ownMover.enabled = false;
        yield return WaitForCondition(() => !puzzle.LeverLower, 5d, "layout_missing_renewal_did_not_expire");
        yield return LayoutWaitPose("lift-middle-after-lease-expiry", lift, lift.MiddleStop.position, liftOffset, 20d);
        LifecycleAssert("L_missing_owner_renewal_expires", frog.IsValid && runner.ActivePlayers.Contains(frog.StateAuthority),
            "The holder still exists and is connected; bounded lease expiry clears a silently stopped owner input loop.");
        yield return LayoutBarrier("layout-lease-expiry-observed");
        if (options.Role == MyEnum.CharacterType.Frog)
        {
            ownInput.InjectDevelopmentInput(Vector2.zero, false);
            ownMover.enabled = true;
        }
        AddObservation(4);
        ScreenCapture.CaptureScreenshot(Path.Combine(RunDirectory, options.Role + "-layout-stage-one.png"));
        yield return LayoutBarrier("layout-complete");

        Phase("layout-four-role-progression-contract");
        report.scope += " Progression-area contract uses owner-only position fixtures, not a completed route: three actual roles must not advance; the fourth actual role lets normal exit/checkpoint ticks advance. Then only ActiveSection is prepared back to one and saved owner poses are restored; earned completion is left intact until the later normal reset.";
        var teamExit = LayoutDevice(6, PcsDeviceKind.Exit);
        var assembly = LayoutDevice(12, PcsDeviceKind.Checkpoint);
        LifecycleAssert("L_progression_area_contract", teamExit.RequireAllRoles && assembly.RequireAllRoles &&
            teamExit.Trigger != null && assembly.Trigger != null && assembly.Links.Contains(teamExit) &&
            puzzle.ActiveSection == 1 && !puzzle.StageOneComplete && puzzle.StageOneHacked,
            "The untouched normal progression starts incomplete and both authored areas require four roles.");
        Vector2 savedOwnPose = ownMover.Body.position;
        int progressionEpoch = puzzle.ResetEpoch;
        Func<PcsPuzzleDevice, int> observedRolesInside = area =>
        {
            int mask = 0;
            Bounds region = area.Bounds;
            foreach (var role in TeamRoles)
            {
                Vector2 position = ActorForRole(role).transform.position;
                if (position.x >= region.min.x && position.x <= region.max.x &&
                    position.y >= region.min.y && position.y <= region.max.y) mask |= 1 << (int)role;
            }
            return mask;
        };
        float assemblyX = options.Role == MyEnum.CharacterType.Rabbit ? -6.5f :
            options.Role == MyEnum.CharacterType.Mouse ? -4.8f : options.Role == MyEnum.CharacterType.Frog ? -3.2f : -1.6f;
        float ownFootOffset = ownMover.BodyCollider.bounds.min.y - ownMover.Body.position.y;
        Vector2 assemblyPose = new Vector2(assemblyX, assembly.Bounds.min.y + .05f - ownFootOffset);
        if (options.Role != MyEnum.CharacterType.Bear)
            LayoutRelocate(assemblyPose, "progression-only fixture on the existing dock: Rabbit/Mouse/Frog inside both real triggers, Bear remains on the prior fixed deck");
        int threeRoleMask = 30 & ~(1 << (int)MyEnum.CharacterType.Bear);
        yield return WaitForCondition(() => observedRolesInside(teamExit) == threeRoleMask && observedRolesInside(assembly) == threeRoleMask,
            10d, "layout_three_role_trigger_fixture_not_replicated");
        yield return LayoutBarrier("layout-progression-three-inside");
        yield return Delay(.6d);
        LifecycleAssert("L_three_roles_do_not_advance", !puzzle.StageOneComplete && puzzle.ActiveSection == 1 &&
            puzzle.States[teamExit.DeviceId].Active == 0 && puzzle.States[assembly.DeviceId].Active == 0 && puzzle.ResetEpoch == progressionEpoch,
            "Three real independently owned actors occupy both areas while Bear stays outside; no completion flag is assigned.");
        yield return LayoutBarrier("layout-progression-three-denied");
        if (options.Role == MyEnum.CharacterType.Bear)
            LayoutRelocate(assemblyPose, "progression-only fixture: last real owner enters the authored assembly triggers; normal TickDevice must authorize progression");
        yield return WaitForCondition(() => puzzle.StageOneComplete && puzzle.ActiveSection == 2 &&
            observedRolesInside(teamExit) == 30 && observedRolesInside(assembly) == 30, 10d,
            "layout_four_roles_normal_progression_rejected");
        LifecycleAssert("L_four_roles_normal_progression", observedRolesInside(teamExit) == 30 && observedRolesInside(assembly) == 30 &&
            puzzle.States[teamExit.DeviceId].Active != 0 && puzzle.States[assembly.DeviceId].Active != 0 && puzzle.ResetEpoch == progressionEpoch,
            "Normal exit and checkpoint ticks accepted all four actual owners; completion/active flags were never written by this fixture.");
        report.events.Add("Four-role progression: peer=" + options.Role + "; tick=" + runner.Tick.Raw +
            "; section=" + puzzle.ActiveSection + "; complete=" + puzzle.StageOneComplete + "; exitMask=" + observedRolesInside(teamExit));
        yield return LayoutBarrier("layout-progression-four-accepted");
        LayoutRelocate(savedOwnPose, "restore the previously supported owner pose after the isolated progression-area contract");
        yield return LayoutBarrier("layout-progression-owner-poses-restored");
        yield return WaitForCondition(() => observedRolesInside(teamExit) == 0 && observedRolesInside(assembly) == 0, 10d,
            "layout_progression_restored_poses_not_replicated");
        if (puzzle.HasStateAuthority)
            typeof(PcsPuzzleDirector).GetProperty("ActiveSection").SetValue(puzzle, 1);
        yield return WaitForCondition(() => puzzle.ActiveSection == 1, 8d, "layout_progression_fixture_return_timeout");
        LifecycleAssert("L_progression_fixture_preserves_earned_state", puzzle.StageOneComplete && puzzle.StageOneHacked &&
            puzzle.ResetEpoch == progressionEpoch,
            "Only the section selector and owner setup poses were restored. Earned exit state remains until the subsequent production Reset RPC clears it.");
        yield return LayoutBarrier("layout-progression-returned-for-lifecycle");
        yield return RunLayoutHoldLifecycle();
    }


    // Lifecycle extension candidate based on the frozen source; all setup is runtime-only.
    private IEnumerator RunLayoutHoldLifecycle()
    {
        report.scope += " Additional hold lifecycle contract: owner Collider disable and range relocation fixtures; normal Reset RPC and normal E re-hacks; authority-only one-health/existing-enemy contact preparation followed by the untouched TickEnemy damage/death path; normal Rabbit master departure, then normal Frog holder departure. Survivor counts are explicitly three and two. No full-route or natural enemy-approach claim.";
        var puzzle = PcsPuzzleDirector.Instance;
        var actor = LocalActor();
        var input = actor.GetComponent<PlayerInput>();
        var mover = actor.GetComponent<Mover>();
        var lever = LayoutDevice(4, PcsDeviceKind.Lever);
        var lift = LayoutDevice(2, PcsDeviceKind.Elevator);
        var hack = LayoutDevice(3, PcsDeviceKind.HackConsole);
        Vector2 liftOffset = LayoutColliderOffset(lift);
        Vector2 holdPose = options.Role == MyEnum.CharacterType.Frog ? mover.Body.position : new Vector2(-4.5f, 3.1f);
        MyEnum.CharacterType[] four = TeamRoles;
        MyEnum.CharacterType[] three = { MyEnum.CharacterType.Mouse, MyEnum.CharacterType.Bear, MyEnum.CharacterType.Frog };
        MyEnum.CharacterType[] two = { MyEnum.CharacterType.Mouse, MyEnum.CharacterType.Bear };

        yield return LayoutLifecycleBeginHold("lh-collider", four);
        RigidbodyConstraints2D priorConstraints = mover.Body.constraints;
        bool priorCollider = mover.BodyCollider.enabled;
        try
        {
            if (options.Role == MyEnum.CharacterType.Frog)
            {
                report.fixtures.Add("Collider-disable fixture: temporarily FreezePositionY prevents an unrelated fall while only the owner BodyCollider is disabled. Mover/input remain enabled and held F remains true; simulated stays true.");
                mover.Body.constraints |= RigidbodyConstraints2D.FreezePositionY;
                mover.BodyCollider.enabled = false;
            }
            yield return WaitForCondition(() => !puzzle.LeverLower && puzzle.States[4].Active == 0, 5d, "lh_collider_disable_stuck_hold");
            LayoutRecordHoldLifecycle("collider-disabled-release");
            LifecycleAssert("LH_collider_disabled_releases", !puzzle.LeverLower,
                "Actual owner collider disable clears the remote hold despite the owner input loop still running.");
        }
        finally
        {
            if (options.Role == MyEnum.CharacterType.Frog && mover != null)
            {
                mover.BodyCollider.enabled = priorCollider;
                mover.Body.constraints = priorConstraints;
                input.InjectDevelopmentInput(Vector2.zero, false);
            }
        }
        yield return LayoutLifecycleBarrier("lh-collider-restored", four);
        yield return LayoutWaitPose("lh-middle-after-collider-disable", lift, lift.MiddleStop.position, liftOffset, 20d);

        yield return LayoutLifecycleBeginHold("lh-range", four);
        priorConstraints = mover.Body.constraints;
        try
        {
            if (options.Role == MyEnum.CharacterType.Frog)
            {
                Vector2 far = lever.InteractionPoint + Vector2.left * (puzzle.RemoteRange + 3f);
                report.fixtures.Add("Range-loss fixture: owner Rigidbody pose and NetworkTransform teleport moved outside RemoteRange without ResetAt/ClearTransientMotion. Held F/aim stay unchanged; temporary FreezePositionY excludes unrelated fall/reset.");
                mover.Body.constraints |= RigidbodyConstraints2D.FreezePositionY;
                mover.Body.position = far;
                actor.GetComponent<NetworkTransform>().Teleport(new Vector3(far.x, far.y, actor.transform.position.z));
                LifecycleAssert("LH_range_fixture_is_outside", Vector2.Distance(far, lever.InteractionPoint) > puzzle.RemoteRange + 1f,
                    "This fixture changes only the holder pose, not interaction range, role, hold state or epoch.");
            }
            yield return WaitForCondition(() => !puzzle.LeverLower && puzzle.States[4].Active == 0, 5d, "lh_out_of_range_stuck_hold");
            LayoutRecordHoldLifecycle("range-loss-release");
            LifecycleAssert("LH_range_loss_releases", !puzzle.LeverLower && puzzle.StageOneHacked,
                "Range loss releases the held request and retains the completed hack.");
        }
        finally
        {
            if (options.Role == MyEnum.CharacterType.Frog && mover != null)
            {
                mover.Body.constraints = priorConstraints;
                LayoutRelocate(holdPose, "restore the prior supported hold stance after the range-loss contract");
            }
        }
        yield return LayoutLifecycleBarrier("lh-range-restored", four);
        yield return LayoutWaitPose("lh-middle-after-range", lift, lift.MiddleStop.position, liftOffset, 20d);

        int beforeReset = puzzle.ResetEpoch;
        yield return LayoutLifecycleBeginHold("lh-reset", four);
        if (options.Role == MyEnum.CharacterType.Frog)
            puzzle.RequestAction(actor, PcsPuzzleAction.Reset, -1, lever.InteractionPoint);
        yield return WaitForCondition(() => puzzle.ResetEpoch != beforeReset, 8d, "lh_normal_reset_rpc_timeout");
        LifecycleAssert("LH_reset_clears_holder_and_hack", !puzzle.LeverLower && !puzzle.StageOneHacked &&
            puzzle.States[4].Active == 0 && puzzle.States[4].Actor == default(NetworkId),
            "Actual authorized Reset RPC changes the epoch and clears hold actor/active/hack state.");
        LayoutRecordHoldLifecycle("normal-reset-cleared");
        yield return LayoutWaitPose("lh-upper-after-normal-reset", lift, lift.UpperStop.position, liftOffset, 20d);
        yield return LayoutLifecycleBarrier("lh-reset-observed", four);
        yield return LayoutLifecycleRehack("lh-rehack-before-death", holdPose, four);

        int beforeDeath = puzzle.ResetEpoch;
        yield return LayoutLifecycleBeginHold("lh-death", four);
        if (puzzle.HasStateAuthority)
        {
            var frog = ActorForRole(MyEnum.CharacterType.Frog);
            var enemy = puzzle.Devices.FirstOrDefault(d => d != null && d.isActiveAndEnabled &&
                d.Kind == PcsDeviceKind.Enemy && d.Section == 1);
            if (enemy == null) throw new SmokeFailure("lh_no_existing_stage_one_enemy_for_death_contract");
            PropertyInfo healthProperty = typeof(PcsPuzzleDirector).GetProperty("Health", BindingFlags.Instance | BindingFlags.NonPublic);
            PropertyInfo timersProperty = typeof(PcsPuzzleDirector).GetProperty("DamageTimers", BindingFlags.Instance | BindingFlags.NonPublic);
            if (healthProperty == null || timersProperty == null) throw new SmokeFailure("lh_death_contract_fields_changed");
            var health = (NetworkArray<int>)healthProperty.GetValue(puzzle);
            var damageTimers = (NetworkArray<TickTimer>)timersProperty.GetValue(puzzle);
            health.Set((int)MyEnum.CharacterType.Frog, 1);
            damageTimers.Set((int)MyEnum.CharacterType.Frog, default);
            var enemyState = puzzle.States[enemy.DeviceId];
            enemyState.Position = frog.GetComponent<Collider2D>().bounds.center;
            enemyState.Velocity = Vector2.zero; enemyState.Actor = default;
            enemyState.Phase = 0; enemyState.Active = 1; enemyState.Counter = enemy.Health; enemyState.Timer = default;
            puzzle.States.Set(enemy.DeviceId, enemyState);
            enemy.ApplyPose(enemyState.Position); enemy.Present(true);
            report.fixtures.Add("Death-only contract: authority set Frog health=1 and placed an existing stage-one enemy at the held Frog body's center, clearing only its damage cooldown. No ResetSection or damage method was invoked by the fixture; the next normal TickEnemy must apply damage and cause death/reset. This is not evidence that the enemy naturally reached this stance.");
            LifecycleAssert("LH_death_fixture_precondition", puzzle.LeverLower && puzzle.GetHealth(MyEnum.CharacterType.Frog) == 1,
                "Health/contact setup occurs while the legitimate remote hold is still active.");
            LayoutRecordHoldLifecycle("death-fixture-prepared");
        }
        yield return WaitForCondition(() => puzzle.ResetEpoch != beforeDeath, 8d, "lh_production_enemy_death_did_not_reset");
        LifecycleAssert("LH_death_clears_hold", !puzzle.LeverLower && !puzzle.StageOneHacked &&
            puzzle.States[4].Active == 0 && puzzle.States[4].Actor == default(NetworkId) &&
            puzzle.GetHealth(MyEnum.CharacterType.Frog) == puzzle.PlayerHealth,
            "Production enemy damage with one remaining health caused section reset, restored health and cleared the holder.");
        LayoutRecordHoldLifecycle("production-death-reset-observed");
        yield return LayoutWaitPose("lh-upper-after-death", lift, lift.UpperStop.position, liftOffset, 20d);
        yield return LayoutLifecycleBarrier("lh-death-observed", four);
        yield return LayoutLifecycleRehack("lh-rehack-before-handoff", holdPose, four);

        PlayerRef formerAuthority = puzzle.Object.StateAuthority;
        yield return LayoutLifecycleBeginHold("lh-master-handoff", four);
        if (options.Role == MyEnum.CharacterType.Frog)
        {
            mover.enabled = false;
            report.fixtures.Add("Before master handoff, stop only the Frog owner's Mover to cease renewals without sending a release; no state authority override is used.");
            WriteMarker("lh-renewals-stopped");
        }
        yield return WaitForMarker("lh-renewals-stopped", MyEnum.CharacterType.Frog, Deadline(10d));
        if (options.Role == MyEnum.CharacterType.Rabbit)
        {
            LifecycleAssert("LH_master_departure_precondition", runner.IsSharedModeMasterClient && puzzle.HasStateAuthority &&
                puzzle.LeverLower && !puzzle.States[4].Timer.ExpiredOrNotRunning(runner),
                "Original master initiates ordinary Shutdown while a valid held lease remains. The other processes prove the result.");
            LayoutRecordHoldLifecycle("original-master-departure-requested");
            AddObservation(4);
            WriteMarker("lh-master-departure");
            Phase("lh-original-master-normal-shutdown");
            yield break;
        }
        yield return WaitForMarker("lh-master-departure", MyEnum.CharacterType.Rabbit, Deadline(10d));
        yield return WaitForObservation(3, 30d);
        yield return WaitForCondition(() => puzzle.Object.StateAuthority != formerAuthority && !puzzle.LeverLower &&
            puzzle.States[4].Active == 0, 8d, "lh_handoff_stale_hold");
        LayoutRecordHoldLifecycle("three-peers-after-handoff");
        LifecycleAssert("LH_handoff_clears_unrenewed_hold", puzzle.StageOneHacked && puzzle.Object.StateAuthority != formerAuthority,
            "Remaining peers see the sole director transfer to the new master and the unrenewed hold clear. This does not distinguish immediate handoff cleanup from bounded lease expiry during transfer.");
        yield return LayoutWaitPose("lh-middle-after-master-handoff", lift, lift.MiddleStop.position, liftOffset, 20d);
        if (options.Role == MyEnum.CharacterType.Frog)
        {
            input.InjectDevelopmentInput(Vector2.zero, false);
            mover.enabled = true;
        }
        yield return LayoutLifecycleBarrier("lh-three-ready", three);
        yield return Delay(.25d);
        yield return LayoutLifecycleBeginHold("lh-frog-disconnect", three);
        if (options.Role == MyEnum.CharacterType.Frog)
        {
            LifecycleAssert("LH_holder_departure_precondition", puzzle.LeverLower && puzzle.States[4].Actor == actor.Id,
                "The departing Frog has re-established a fresh legitimate hold with the transferred director.");
            LayoutRecordHoldLifecycle("holder-departure-requested");
            AddObservation(3);
            mover.enabled = false;
            WriteMarker("lh-holder-departure");
            Phase("lh-Frog-holder-normal-shutdown");
            yield break;
        }
        yield return WaitForMarker("lh-holder-departure", MyEnum.CharacterType.Frog, Deadline(10d));
        yield return WaitForObservation(2, 30d);
        yield return WaitForCondition(() => !puzzle.LeverLower && puzzle.States[4].Active == 0 &&
            puzzle.States[4].Actor == default(NetworkId), 8d, "lh_departed_holder_stuck_hold");
        LifecycleAssert("LH_departed_holder_releases", puzzle.StageOneHacked && runner.ActivePlayers.Count() == 2,
            "The Frog is disconnected; two survivors observe a cleared holder and retained hack after ordinary shutdown.");
        LayoutRecordHoldLifecycle("two-survivors-holder-cleared");
        yield return LayoutWaitPose("lh-middle-after-holder-disconnect", lift, lift.MiddleStop.position, liftOffset, 20d);
        AddObservation(2);
        yield return LayoutLifecycleBarrier("lh-survivors-complete", two);
        Phase("lh-two-survivors-shutdown-grace");
        yield return Delay(2d);
    }

    private IEnumerator LayoutLifecycleRehack(string stage, Vector2 holdPose, MyEnum.CharacterType[] roles)
    {
        var puzzle = PcsPuzzleDirector.Instance;
        yield return Delay(puzzle.ResetDelay + .35d);
        LayoutRelocate(options.Role == MyEnum.CharacterType.Frog ? holdPose : puzzle.GetRespawnPosition(options.Role),
            "post-reset lifecycle setup; this reuses supported starting poses and does not claim to traverse the access route");
        yield return LayoutLifecycleBarrier(stage + "-positions", roles);
        if (options.Role == MyEnum.CharacterType.Mouse)
            yield return LayoutHack(LayoutDevice(3, PcsDeviceKind.HackConsole), stage, true);
        yield return WaitForCondition(() => puzzle.StageOneHacked, 20d, stage + "_normal_E_hack_timeout");
        var lift = LayoutDevice(2, PcsDeviceKind.Elevator);
        yield return LayoutWaitPose(stage + "-middle", lift, lift.MiddleStop.position, LayoutColliderOffset(lift), 20d);
        yield return LayoutLifecycleBarrier(stage + "-done", roles);
    }

    private IEnumerator LayoutLifecycleBeginHold(string stage, MyEnum.CharacterType[] roles)
    {
        Phase(stage + "-normal-aimed-held-F");
        var puzzle = PcsPuzzleDirector.Instance;
        var lever = LayoutDevice(4, PcsDeviceKind.Lever);
        if (options.Role == MyEnum.CharacterType.Frog)
            LocalActor().GetComponent<PlayerInput>().InjectDevelopmentInput(Vector2.zero, false,
                ability: true, aim: lever.InteractionPoint, abilityHeld: true);
        yield return WaitForCondition(() => puzzle.LeverLower && puzzle.States[4].Active != 0, 8d, stage + "_hold_rejected");
        LayoutRecordHoldLifecycle(stage + "-active");
        yield return LayoutLifecycleBarrier(stage + "-active-observed", roles);
    }

    private IEnumerator LayoutLifecycleBarrier(string stage, MyEnum.CharacterType[] roles)
    {
        WriteMarker(stage);
        double deadline = Deadline(25d);
        foreach (var role in roles) yield return WaitForMarker(stage, role, deadline);
    }

    private void LayoutRecordHoldLifecycle(string stage)
    {
        var puzzle = PcsPuzzleDirector.Instance;
        var state = puzzle.States[4];
        report.events.Add(DateTime.UtcNow.ToString("O") + " " + stage + " tick=" + runner.Tick.Raw +
            " peers=" + runner.ActivePlayers.Count() + " director=" + puzzle.Object.StateAuthority +
            " holder=" + state.Actor + " holderOwner=" + state.HoldOwner + " active=" + state.Active +
            " leverLower=" + puzzle.LeverLower + " hacked=" + puzzle.StageOneHacked + " reset=" + puzzle.ResetEpoch +
            " leaseRemaining=" + (state.Timer.RemainingTime(runner) ?? 0f).ToString("F3") +
            " frogHealth=" + puzzle.GetHealth(MyEnum.CharacterType.Frog) + " lift=" + puzzle.States[2].Position);
        Save();
    }

    private PcsPuzzleDevice LayoutDevice(int id, PcsDeviceKind kind)
    {
        var puzzle = PcsPuzzleDirector.Instance;
        var value = id >= 0 && id < puzzle.Devices.Length ? puzzle.Devices[id] : null;
        LifecycleAssert("L_device_" + id, value != null && value.DeviceId == id && value.Kind == kind,
            "Use the intended registered device ID and type, not an arbitrary same-name scene copy.");
        return value;
    }

    private void LayoutRelocate(Vector2 position, string reason)
    {
        report.fixtures.Add("Owner-only ResetAt " + position + ": " + reason + ". This relocation is preparation, not traversed-route evidence.");
        var actor = LocalActor();
        actor.GetComponent<Mover>().ResetAt(position);
        actor.GetComponent<PlayerInput>().InjectDevelopmentInput(Vector2.zero, false);
        Save();
    }

    private IEnumerator LayoutHack(PcsPuzzleDevice console, string stage, bool relocate)
    {
        var actor = LocalActor();
        var mover = actor.GetComponent<Mover>();
        var input = actor.GetComponent<PlayerInput>();
        var puzzle = PcsPuzzleDirector.Instance;
        if (relocate) LayoutRelocate(console.InteractionPoint + new Vector2(-0.3f, 0.05f), "near authored console " + console.DeviceId);
        yield return Delay(0.45d);
        LifecycleAssert(stage + "_normal_owner_range", options.Role == MyEnum.CharacterType.Mouse && actor.HasStateAuthority &&
            Vector2.Distance(mover.BodyCollider.bounds.center, console.InteractionPoint) < Mathf.Min(1.8f, console.InteractionRange),
            "The owner must be in the real interaction range; no forced progress or range override is used.");
        LifecycleAssert(stage + "_fresh_counter", puzzle.States[console.DeviceId].Counter == 0 &&
            puzzle.States[console.DeviceId].Active == 0, "A fresh console must be completed by this run's normal E inputs.");
        for (int pulse = puzzle.States[console.DeviceId].Counter + 1; pulse <= console.RequiredInputs; pulse++)
        {
            int wanted = pulse;
            input.InjectDevelopmentInput(Vector2.zero, false, interact: true, aim: console.InteractionPoint);
            yield return WaitForCondition(() => puzzle.States[console.DeviceId].Counter >= wanted, 5d, stage + "_E_pulse_rejected_" + pulse);
            LifecycleAssert(stage + "_E_" + pulse, puzzle.States[console.DeviceId].Counter == wanted,
                "One ordinary owner input pulse produced exactly one authoritative increment.");
            yield return Delay(puzzle.HackPulseInterval + 0.1d);
        }
        input.InjectDevelopmentInput(Vector2.zero, false);
    }

    private IEnumerator LayoutBarrier(string stage)
    {
        WriteMarker(stage);
        foreach (var role in TeamRoles) yield return WaitForMarker(stage, role, Deadline(30d));
    }

    private static Vector2 LayoutColliderOffset(PcsPuzzleDevice device)
    {
        Collider2D collider = device.Solid != null ? device.Solid : device.Trigger;
        if (collider == null) return Vector2.zero;
        return (Vector2)collider.transform.TransformPoint(collider.offset) - (Vector2)device.transform.position;
    }

    private IEnumerator LayoutWaitPose(string stage, PcsPuzzleDevice device, Vector2 target, Vector2 colliderOffset, double seconds)
    {
        var puzzle = PcsPuzzleDirector.Instance;
        double deadline = Deadline(seconds);
        double next = 0d;
        while (true)
        {
            Collider2D collider = device.Solid != null ? device.Solid : device.Trigger;
            Vector2 statePosition = puzzle.States[device.DeviceId].Position;
            float error = Mathf.Max(Vector2.Distance(device.transform.position, target), Vector2.Distance(statePosition, target));
            if (device.Body != null) error = Mathf.Max(error, Vector2.Distance(device.Body.position, target));
            float colliderError = collider != null && collider.enabled ? Vector2.Distance(collider.bounds.center, statePosition + colliderOffset) : 0f;
            if (Time.realtimeSinceStartupAsDouble >= next || (error < 0.045f && colliderError < 0.06f))
            {
                report.layoutObservations.Add(new LayoutObservation { stage = stage, localRole = options.Role.ToString(), tick = runner.Tick.Raw,
                    deviceId = device.DeviceId, active = puzzle.States[device.DeviceId].Active, counter = puzzle.States[device.DeviceId].Counter,
                    replicated = statePosition, actualRoot = device.transform.position, actualBody = device.Body != null ? device.Body.position : (Vector2)device.transform.position,
                    actualColliderCenter = collider != null ? (Vector2)collider.bounds.center : Vector2.zero,
                    expectedColliderCenter = statePosition + colliderOffset, target = target, colliderEnabled = collider != null && collider.enabled,
                    canClimb = device.CanClimb, leverLower = puzzle.LeverLower, stageOneHacked = puzzle.StageOneHacked,
                    poseError = error, colliderError = colliderError,
                    bearFootGap = device.Solid != null ? ActorForRole(MyEnum.CharacterType.Bear).GetComponent<Mover>().BodyCollider.bounds.min.y - device.Solid.bounds.max.y : 0f });
                Save();
                next = Deadline(0.5d);
            }
            if (error < 0.045f && colliderError < 0.06f) break;
            CheckDeadline(deadline, "layout_pose_or_collider_timeout_" + stage);
            yield return null;
        }
        LifecycleAssert("L_pose_" + stage, true, "At rest, the replicated root, Rigidbody and enabled physical collider converge on each real peer (0.045/0.06 world units).");
    }

    private bool LayoutBearOnLift(Mover bear, PcsPuzzleDevice lift)
    {
        Bounds feet = bear.BodyCollider.bounds;
        Bounds platform = lift.Solid.bounds;
        if (!lift.Solid.enabled || feet.center.x <= platform.min.x || feet.center.x >= platform.max.x ||
            Mathf.Abs(feet.min.y - platform.max.y) >= 0.16f) return false;
        layoutContacts.Clear();
        lift.Solid.GetContacts(layoutContacts);
        foreach (var contact in layoutContacts)
            if (contact.enabled && Mathf.Abs(contact.normal.y) >= 0.5f &&
                Mathf.Abs(contact.point.y - platform.max.y) < 0.16f &&
                ((contact.collider == bear.BodyCollider && contact.otherCollider == lift.Solid) ||
                 (contact.otherCollider == bear.BodyCollider && contact.collider == lift.Solid))) return true;
        return false;
    }

    private IEnumerator RunCarryTwo()
    {
        Phase("carry-waiting-for-normal-two-spawns");
        yield return WaitForObservation(2, 45d);
        report.normalFlowReached = true;
        CaptureGameView();
        var rabbit = ActorForRole(MyEnum.CharacterType.Rabbit);
        var mouse = ActorForRole(MyEnum.CharacterType.Mouse);
        var lifter = rabbit.GetComponent<Lifter>();
        var passenger = mouse.GetComponent<Mover>();
        LifecycleAssert("carry_two_independent_owners", rabbit.StateAuthority != mouse.StateAuthority &&
            rabbit.HasStateAuthority == (options.Role == MyEnum.CharacterType.Rabbit) &&
            mouse.HasStateAuthority == (options.Role == MyEnum.CharacterType.Mouse),
            "Normal Runner player objects have separate actual Shared state authorities; no authority override is used.");
        LifecycleAssert("carry_fixture_preflight", lifter != null && lifter.Head != null && passenger != null &&
            passenger.Rider != null && passenger.Body.mass <= lifter.Rb.mass,
            "Use the existing Rabbit Lifter and Mouse Rider with the production mass requirement.");

        // Identical non-networked test geometry in each process; only each local owner moves its own actor once.
        Vector2 floorCenter = new Vector2(-120f, -40f);
        Vector2 floorSize = new Vector2(24f, 0.5f);
        LifecycleAssert("carry_fixture_area_empty", Physics2D.OverlapBox(floorCenter + Vector2.up * 3f,
            new Vector2(24f, 6.5f), 0f, Physics2D.AllLayers) == null,
            "The isolated fixture volume must not overlap authored scene colliders.");
        int groundLayer = LayerMask.NameToLayer("Ground");
        LifecycleAssert("carry_fixture_ground_layer", groundLayer >= 0, "Reuse the existing Ground layer.");
        var floor = new GameObject("PCS Smoke isolated carry floor");
        floor.layer = groundLayer;
        floor.transform.position = floorCenter;
        var floorBody = floor.AddComponent<Rigidbody2D>();
        floorBody.bodyType = RigidbodyType2D.Static;
        var floorCollider = floor.AddComponent<BoxCollider2D>();
        floorCollider.size = floorSize;
        var owner = LocalActor();
        var ownMover = owner.GetComponent<Mover>();
        var ownInput = owner.GetComponent<PlayerInput>();
        float footOffset = ownMover.BodyCollider.bounds.min.y - ownMover.Body.position.y;
        float startX = options.Role == MyEnum.CharacterType.Rabbit ? -124f : -123.2f;
        Vector2 ownPose = new Vector2(startX, floorCenter.y + floorSize.y * 0.5f - footOffset + 0.04f);
        report.fixtures.Add("Runtime-only local static Ground floor: center (-120,-40), size (24,0.5). No NetworkObject, scene asset or production device mutation.");
        report.fixtures.Add("One explicit owner-only Mover.ResetAt relocation to " + ownPose + "; remote actor positions remain replicated. All later carry and movement actions use normal PlayerInput.");
        ownMover.ResetAt(ownPose);
        ownInput.InjectDevelopmentInput(Vector2.zero, false);
        yield return Delay(0.8d);
        WriteMarker("carry-fixture-ready");
        yield return CarryBarrier("carry-fixture-ready");
        yield return WaitForCondition(() => Mathf.Abs(rabbit.transform.position.x + 124f) < 0.15f &&
            Mathf.Abs(mouse.transform.position.x + 123.2f) < 0.15f &&
            rabbit.transform.position.y < -38f && mouse.transform.position.y < -38f,
            10d, "carry_fixture_replication_timeout");
        AddObservation(2);
        WriteMarker("carry-fixture-observed");
        yield return CarryBarrier("carry-fixture-observed");

        Phase("carry-normal-Rabbit-G-remote-attach");
        if (options.Role == MyEnum.CharacterType.Rabbit)
            ownInput.InjectDevelopmentInput(Vector2.zero, false, carry: true);
        yield return WaitForCondition(() => CarryLinked(rabbit, mouse) && CarryCollisionPairs(rabbit, mouse, true),
            8d, "carry_remote_attach_timeout");
        yield return Delay(0.3d);
        RecordCarry("first-attach", rabbit, mouse);
        LifecycleAssert("carry_remote_attach", CarryLinked(rabbit, mouse) &&
            (!mouse.HasStateAuthority || !mouse.GetComponent<PlayerInput>().CanMoveInput),
            "Rabbit owner reservation and remote Mouse Rider RPC both acknowledge the same pair; only the Mouse owner disables movement input.");
        Vector2 beforeRabbit = rabbit.transform.position;
        Vector2 beforeMouse = mouse.transform.position;
        WriteMarker("carry-mounted");
        yield return CarryBarrier("carry-mounted");

        Phase("carry-normal-carrier-walking");
        if (options.Role == MyEnum.CharacterType.Rabbit)
        {
            ownInput.InjectDevelopmentInput(Vector2.right, false);
            yield return Delay(0.8d);
            ownInput.InjectDevelopmentInput(Vector2.zero, false);
            yield return Delay(0.35d);
            WriteMarker("carry-move-complete");
        }
        yield return WaitForMarker("carry-move-complete", MyEnum.CharacterType.Rabbit, Deadline(10d));
        yield return Delay(0.25d);
        var moved = RecordCarry("carried-movement", rabbit, mouse);
        LifecycleAssert("carry_remote_follow", CarryLinked(rabbit, mouse) &&
            rabbit.transform.position.x - beforeRabbit.x > 1f && mouse.transform.position.x - beforeMouse.x > 1f &&
            moved.horizontalGap < 0.2f && Mathf.Abs(moved.passengerFootAboveHead - 0.015f) < 0.2f,
            "Both peers observe actual carrier displacement and passenger following the production head pose.");
        WriteMarker("carry-move-verified");
        yield return CarryBarrier("carry-move-verified");

        Phase("carry-normal-Mouse-G-remote-clear");
        if (options.Role == MyEnum.CharacterType.Mouse)
            ownInput.InjectDevelopmentInput(Vector2.zero, false, carry: true);
        yield return WaitForCondition(() => CarryReleased(rabbit, mouse) && CarryCollisionPairs(rabbit, mouse, false),
            8d, "carry_passenger_drop_remote_clear_timeout");
        RecordCarry("passenger-drop", rabbit, mouse);
        LifecycleAssert("carry_passenger_drop_remote_clear", CarryReleased(rabbit, mouse) &&
            (!mouse.HasStateAuthority || mouse.GetComponent<PlayerInput>().CanMoveInput),
            "Mouse owner drop clears its Rider and the remote Rabbit Lifter slot and restores collision pairs and local movement input.");
        WriteMarker("carry-drop-verified");
        yield return CarryBarrier("carry-drop-verified");
        yield return Delay(0.8d);

        Phase("carry-normal-Rabbit-G-reboard");
        LifecycleAssert("carry_reboard_starts_released", CarryReleased(rabbit, mouse),
            "The explicit reboard starts after the production pickup cooldown and with both reservations empty.");
        if (options.Role == MyEnum.CharacterType.Rabbit)
            ownInput.InjectDevelopmentInput(Vector2.zero, false, carry: true);
        yield return WaitForCondition(() => CarryLinked(rabbit, mouse) && CarryCollisionPairs(rabbit, mouse, true),
            8d, "carry_remote_reboard_timeout");
        RecordCarry("reboard", rabbit, mouse);
        LifecycleAssert("carry_reboard", CarryLinked(rabbit, mouse), "A second normal G request can reuse the cleared remote reservation.");
        WriteMarker("carry-reboard-verified");
        yield return CarryBarrier("carry-reboard-verified");

        Phase("carry-normal-Rabbit-G-remote-release");
        if (options.Role == MyEnum.CharacterType.Rabbit)
            ownInput.InjectDevelopmentInput(Vector2.zero, false, carry: true);
        yield return WaitForCondition(() => CarryReleased(rabbit, mouse) && CarryCollisionPairs(rabbit, mouse, false),
            8d, "carry_carrier_release_remote_rpc_timeout");
        RecordCarry("carrier-release", rabbit, mouse);
        LifecycleAssert("carry_carrier_release_remote_rpc", CarryReleased(rabbit, mouse) &&
            (!mouse.HasStateAuthority || mouse.GetComponent<PlayerInput>().CanMoveInput),
            "Rabbit G uses the normal carrier release RPC to the Mouse owner, then clears the reverse reservation and restores collisions/input.");
        yield return ObserveFor(2, 2d);
        WriteMarker("carry-complete");
        yield return CarryBarrier("carry-complete");
        Phase("carry-complete-shutdown-grace");
        yield return Delay(3d);
    }

    private NetworkObject ActorForRole(MyEnum.CharacterType role)
    {
        foreach (var player in runner.ActivePlayers)
            if (runner.TryGetPlayerObject(player, out var actor) && actor != null && actor.IsValid &&
                actor.GetComponent<PcsPlayerAbilities>() != null && actor.GetComponent<PcsPlayerAbilities>().CharacterType == role)
                return actor;
        throw new SmokeFailure("normal_selected_role_actor_missing");
    }
    private IEnumerator CarryBarrier(string stage)
    {
        yield return WaitForMarker(stage, MyEnum.CharacterType.Rabbit, Deadline(15d));
        yield return WaitForMarker(stage, MyEnum.CharacterType.Mouse, Deadline(15d));
    }
    private static bool CarryLinked(NetworkObject rabbit, NetworkObject mouse) =>
        rabbit.GetComponent<Lifter>().CarriedObject == mouse && mouse.GetComponent<Mover>().Rider.CarrierObject == rabbit;
    private static bool CarryReleased(NetworkObject rabbit, NetworkObject mouse) =>
        rabbit.GetComponent<Lifter>().CarriedObject == null && mouse.GetComponent<Mover>().Rider.CarrierObject == null;
    private static bool CarryCollisionPairs(NetworkObject rabbit, NetworkObject mouse, bool expectedIgnore)
    {
        foreach (var own in rabbit.GetComponentsInChildren<Collider2D>())
            foreach (var other in mouse.GetComponentsInChildren<Collider2D>())
                if (Physics2D.GetIgnoreCollision(own, other) != expectedIgnore) return false;
        return true;
    }
    private CarryObservation RecordCarry(string stage, NetworkObject rabbit, NetworkObject mouse)
    {
        var value = new CarryObservation
        {
            stage = stage, tick = runner.Tick.Raw,
            separateOwners = rabbit.StateAuthority != mouse.StateAuthority,
            localOwnsOnlySelectedRole = rabbit.HasStateAuthority == (options.Role == MyEnum.CharacterType.Rabbit) &&
                mouse.HasStateAuthority == (options.Role == MyEnum.CharacterType.Mouse),
            carrierPointsToPassenger = rabbit.GetComponent<Lifter>().CarriedObject == mouse,
            passengerPointsToCarrier = mouse.GetComponent<Mover>().Rider.CarrierObject == rabbit,
            collisionPairsIgnored = CarryCollisionPairs(rabbit, mouse, true),
            passengerInputObservedLocally = mouse.HasStateAuthority,
            passengerOwnerInputCanMove = mouse.HasStateAuthority && mouse.GetComponent<PlayerInput>().CanMoveInput,
            carrierPosition = rabbit.transform.position, passengerPosition = mouse.transform.position,
            horizontalGap = Mathf.Abs(mouse.transform.position.x - rabbit.transform.position.x),
            passengerFootAboveHead = mouse.GetComponent<Mover>().BodyCollider.bounds.min.y - rabbit.GetComponent<Lifter>().Head.Top
        };
        report.carryObservations.Add(value);
        Save();
        return value;
    }
    private static IEnumerator WaitForCondition(Func<bool> condition, double seconds, string failure)
    {
        double deadline = Deadline(seconds);
        while (!condition())
        {
            CheckDeadline(deadline, failure);
            yield return null;
        }
    }

    private static readonly MyEnum.CharacterType[] TeamRoles = { MyEnum.CharacterType.Rabbit, MyEnum.CharacterType.Bear,
        MyEnum.CharacterType.Mouse, MyEnum.CharacterType.Frog };

    private IEnumerator RunShaftTwo()
    {
        Phase("shaft-waiting-for-normal-two-spawns");
        yield return WaitForObservation(2, 45d);
        report.normalFlowReached = true;
        var puzzle = PcsPuzzleDirector.Instance;
        PcsPuzzleDevice lift = puzzle.Devices.Single(d => d != null && d.IsShaftElevator);
        LifecycleAssert("shaft_authored_geometry", lift.Body != null && lift.Solid != null && lift.UpperStop != null && lift.Speed > 0f,
            "Use the unchanged authored platform, solid, speed and independent upper target.");
        Vector2 start = puzzle.States[lift.DeviceId].Position;
        Vector2 colliderOffset = (Vector2)lift.Solid.bounds.center - lift.Body.position;
        LifecycleAssert("shaft_default_hold", puzzle.States[lift.DeviceId].Phase == 0 && lift.UpperStop.position.y > start.y + .3f,
            "A new session starts in Hold at its current pose with measurable upward travel available.");
        yield return Delay(.35d);
        LifecycleAssert("shaft_default_physical_hold", ShaftPhysicalAt(puzzle, lift, start, colliderOffset),
            "Both owners observe a stationary replicated root, Rigidbody and solid before a command.");
        if (!puzzle.HasStateAuthority)
            LifecycleAssert("shaft_proxy_command_rejected", !puzzle.TrySetShaftLiftCommand(lift, PcsShaftLiftCommand.Raise, out string proxyReason), proxyReason);
        yield return ShaftBarrier("shaft-ready");
        if (puzzle.HasStateAuthority)
            LifecycleAssert("shaft_authority_raise_accepted", puzzle.TrySetShaftLiftCommand(lift, PcsShaftLiftCommand.Raise, out string raiseReason), raiseReason);
        yield return WaitForCondition(() => puzzle.States[lift.DeviceId].Position.y > start.y + .15f, 8d, "shaft_ascent_not_replicated");
        LifecycleAssert("shaft_physics_follows_ascent", ShaftPhysicalAt(puzzle, lift, puzzle.States[lift.DeviceId].Position, colliderOffset),
            "Replicated ascent moves the actual local root/body/solid on authority and proxy.");
        yield return ShaftBarrier("shaft-ascent-observed");
        if (puzzle.HasStateAuthority)
            LifecycleAssert("shaft_authority_hold_accepted", puzzle.TrySetShaftLiftCommand(lift, PcsShaftLiftCommand.Hold, out string holdReason), holdReason);
        yield return WaitForCondition(() => puzzle.States[lift.DeviceId].Phase == 0, 6d, "shaft_hold_not_replicated");
        Vector2 held = puzzle.States[lift.DeviceId].Position;
        yield return Delay(.5d);
        LifecycleAssert("shaft_current_position_hold", held.y > start.y + .1f && ShaftPhysicalAt(puzzle, lift, held, colliderOffset),
            "Hold remains at the new intermediate position; no lower-target movement occurs.");
        if (puzzle.HasStateAuthority)
            LifecycleAssert("shaft_removed_lower_command_rejected", !puzzle.TrySetShaftLiftCommand(lift, (PcsShaftLiftCommand)2, out string invalidReason), invalidReason);
        yield return ShaftBarrier("shaft-hold-observed");
        if (puzzle.HasStateAuthority)
            LifecycleAssert("shaft_resume_accepted", puzzle.TrySetShaftLiftCommand(lift, PcsShaftLiftCommand.Raise, out string resumeReason), resumeReason);
        Vector2 upper = lift.UpperStop.position;
        double deadline = Deadline(Vector2.Distance(held, upper) / lift.Speed + 15d);
        float previousY = held.y;
        bool monotonic = true;
        while (Vector2.Distance(puzzle.States[lift.DeviceId].Position, upper) >= .01f || puzzle.States[lift.DeviceId].Phase != 0)
        {
            CheckDeadline(deadline, "shaft_upper_auto_hold_timeout");
            float y = puzzle.States[lift.DeviceId].Position.y;
            monotonic &= y >= previousY - .002f && y <= upper.y + .005f;
            previousY = y;
            yield return null;
        }
        yield return Delay(.4d);
        LifecycleAssert("shaft_upper_auto_hold_physical", monotonic && ShaftPhysicalAt(puzzle, lift, upper, colliderOffset),
            "Both peers reach the authored upper endpoint, remain in Hold and never reverse downward.");
        AddObservation(2);
        yield return ShaftBarrier("shaft-complete");
        Phase("shaft-complete-shutdown-grace");
        yield return Delay(2d);
    }

    private bool ShaftPhysicalAt(PcsPuzzleDirector puzzle, PcsPuzzleDevice lift, Vector2 target, Vector2 colliderOffset)
    {
        Vector2 replicated = puzzle.States[lift.DeviceId].Position;
        float tolerance = Mathf.Max(.03f, lift.Speed * runner.DeltaTime * 3f);
        bool valid = Vector2.Distance(replicated, target) < .01f &&
            Vector2.Distance(lift.transform.position, target) < tolerance &&
            Vector2.Distance(lift.Body.position, target) < tolerance &&
            Vector2.Distance((Vector2)lift.Solid.bounds.center, target + colliderOffset) < tolerance;
        report.events.Add("Shaft pose: role=" + options.Role + "; tick=" + runner.Tick.Raw + "; phase=" + puzzle.States[lift.DeviceId].Phase +
            "; state=" + replicated + "; body=" + lift.Body.position + "; collider=" + lift.Solid.bounds.center + "; target=" + target);
        return valid;
    }

    private IEnumerator ShaftBarrier(string stage)
    {
        WriteMarker(stage);
        yield return WaitForMarker(stage, MyEnum.CharacterType.Mouse, Deadline(15d));
        yield return WaitForMarker(stage, MyEnum.CharacterType.Bear, Deadline(15d));
    }

    private IEnumerator RunPressureTwo()
    {
        Phase("pressure-waiting-for-normal-two-spawns");
        yield return WaitForObservation(2, 45d);
        report.normalFlowReached = true;
        CaptureGameView();
        var puzzle = PcsPuzzleDirector.Instance;
        var mouse = ActorForRole(MyEnum.CharacterType.Mouse);
        var bear = ActorForRole(MyEnum.CharacterType.Bear);
        var bearMover = bear.GetComponent<Mover>();
        var ownInput = LocalActor().GetComponent<PlayerInput>();
        ownInput.InjectDevelopmentInput(Vector2.zero, false);
        Vector2 initialBearPosition = bear.transform.position;
        var plate = puzzle.Devices.FirstOrDefault(d => d != null && d.Section == 1 &&
            d.Kind == PcsDeviceKind.PressurePlate && d.RequiredRole == MyEnum.CharacterType.Bear && !d.AcceptDummyOnly);
        LifecycleAssert("pressure_authored_plate", plate != null && plate.Solid != null &&
            plate.PressureButton != null && plate.PressureButton.enabled,
            "Use the actual StageOne Bear pressure component and serialized solid support.");
        var wall = plate.Links.FirstOrDefault(d => d != null && d.Kind == PcsDeviceKind.SlidingWall);
        LifecycleAssert("pressure_authored_wall", wall != null && wall.Solid != null && wall.Body != null &&
            wall.LowerStop != null && wall.UpperStop != null,
            "Use the actual pressure-linked wall and its authored endpoints.");
        pressureColliderOffset = (Vector2)wall.Solid.bounds.center - (Vector2)wall.transform.position;
        LifecycleAssert("N02_remote_Bear_owner", puzzle.Object.StateAuthority == mouse.StateAuthority &&
            puzzle.Object.StateAuthority != bear.StateAuthority &&
            puzzle.HasStateAuthority == (options.Role == MyEnum.CharacterType.Mouse) &&
            bear.HasStateAuthority == (options.Role == MyEnum.CharacterType.Bear),
            "Mouse is the real Shared master/director owner; Bear belongs to the other actual process.");

        report.fixtures.Add("Explicit test-only master-owned ActiveSection=1 assignment; this bypasses tutorial progression only. No pressure state, wall state, device geometry, physics authority or participation guard is changed.");
        if (options.Role == MyEnum.CharacterType.Mouse)
        {
            var property = typeof(PcsPuzzleDirector).GetProperty("ActiveSection", BindingFlags.Public | BindingFlags.Instance);
            if (property == null || property.GetSetMethod(true) == null) throw new SmokeFailure("pressure_fixture_section_setter_missing");
            property.SetValue(puzzle, 1);
        }
        yield return WaitForCondition(() => puzzle.ActiveSection == 1, 10d, "pressure_fixture_section_replication_timeout");
        yield return WaitForPressure(plate, wall, bear, false, wall.LowerStop.position, "initial-closed", 10d);
        WriteMarker("pressure-fixture-ready");
        yield return PressureBarrier("pressure-fixture-ready");
        int initialEpoch = puzzle.ResetEpoch;
        int initialHealth = puzzle.GetHealth(MyEnum.CharacterType.Bear);

        Phase("pressure-remote-Bear-falls-on-actual-support");
        if (options.Role == MyEnum.CharacterType.Bear)
        {
            float footOffset = bearMover.BodyCollider.bounds.min.y - bearMover.Body.position.y;
            Vector2 pose = new Vector2(plate.Solid.bounds.center.x, plate.Solid.bounds.max.y - footOffset + 0.05f);
            report.fixtures.Add("Bear owner Mover.ResetAt above authored support: " + pose + "; original dynamic gravity and contact simulation remain enabled.");
            bearMover.ResetAt(pose);
            ownInput.InjectDevelopmentInput(Vector2.zero, false);
        }
        yield return WaitForPressure(plate, wall, bear, true, wall.UpperStop.position, "remote-top-contact-opens-wall", 15d);
        var held = RecordPressure("open-held", plate, wall, bear);
        LifecycleAssert("N02_remote_contact_authority", options.Role == MyEnum.CharacterType.Mouse
            ? held.localIsDirectorAuthority && !held.localOwnsBear && held.bearBodyType == "Kinematic" &&
                held.bearFullKinematicContacts && held.bearTopContacts > 0
            : !held.localIsDirectorAuthority && held.localOwnsBear && held.bearBodyType == "Dynamic" && held.bearTopContacts > 0,
            "Master pressure is supported by a real enabled top contact with the remote kinematic Bear proxy; the Bear owner remains dynamic.");
        LifecycleAssert("N02_remote_pressure_and_open_pose", held.plateActive == 1 && held.wallActive == 1 &&
            Vector2.Distance(held.wallPosition, wall.UpperStop.position) < 0.025f && PressureActualWallAt(wall, wall.UpperStop.position),
            "Both peers observe the ordinary pressure result and completed wall motion without writing either state.");
        AddObservation(2);
        WriteMarker("pressure-open-verified");
        yield return PressureBarrier("pressure-open-verified");

        Phase("pressure-Bear-owner-fixture-removal");
        if (options.Role == MyEnum.CharacterType.Bear)
        {
            report.fixtures.Add("Bear owner Mover.ResetAt back to its original pre-fixture tutorial position: " + initialBearPosition + "; removal is explicit setup, not a claimed traversal.");
            bearMover.ResetAt(initialBearPosition);
            ownInput.InjectDevelopmentInput(Vector2.zero, false);
        }
        yield return WaitForPressure(plate, wall, bear, false, wall.LowerStop.position, "remote-removal-closes-wall", 15d);
        var released = RecordPressure("closed-after-removal", plate, wall, bear);
        LifecycleAssert("N02_remote_removal_closes", released.plateActive == 0 && released.wallActive == 0 &&
            Vector2.Distance(released.wallPosition, wall.LowerStop.position) < 0.025f && PressureActualWallAt(wall, wall.LowerStop.position) &&
            (!puzzle.HasStateAuthority || released.bearTopContacts == 0),
            "Master sees no Bear top contact after replicated removal; both peers observe release and the closed endpoint.");
        LifecycleAssert("N02_no_reset_or_damage_confound", puzzle.ActiveSection == 1 && puzzle.ResetEpoch == initialEpoch &&
            puzzle.GetHealth(MyEnum.CharacterType.Bear) == initialHealth,
            "A checkpoint reset or Bear damage did not produce the observed release.");
        yield return ObserveFor(2, 2d);
        WriteMarker("pressure-complete");
        yield return PressureBarrier("pressure-complete");
        Phase("pressure-complete-shutdown-grace");
        yield return Delay(3d);
    }

    private IEnumerator PressureBarrier(string stage)
    {
        yield return WaitForMarker(stage, MyEnum.CharacterType.Mouse, Deadline(15d));
        yield return WaitForMarker(stage, MyEnum.CharacterType.Bear, Deadline(15d));
    }
    private IEnumerator WaitForPressure(PcsPuzzleDevice plate, PcsPuzzleDevice wall, NetworkObject bear,
        bool pressed, Vector2 target, string stage, double seconds)
    {
        double deadline = Deadline(seconds);
        double nextRecord = 0d;
        var puzzle = PcsPuzzleDirector.Instance;
        while (true)
        {
            if (Time.realtimeSinceStartupAsDouble >= nextRecord)
            {
                RecordPressure(stage, plate, wall, bear);
                nextRecord = Deadline(0.5d);
            }
            if (puzzle.States[plate.DeviceId].Active == (pressed ? 1 : 0) &&
                puzzle.States[wall.DeviceId].Active == (pressed ? 1 : 0) &&
                Vector2.Distance(puzzle.States[wall.DeviceId].Position, target) < 0.025f &&
                PressureActualWallAt(wall, target)) yield break;
            CheckDeadline(deadline, stage + "_timeout");
            yield return null;
        }
    }
    private bool PressureActualWallAt(PcsPuzzleDevice wall, Vector2 target) =>
        wall.Body != null && wall.Solid != null && Vector2.Distance(wall.transform.position, target) < 0.04f &&
        Vector2.Distance(wall.Body.position, target) < 0.04f &&
        Vector2.Distance(wall.Solid.bounds.center, target + pressureColliderOffset) < 0.04f;
    private PressureObservation RecordPressure(string stage, PcsPuzzleDevice plate, PcsPuzzleDevice wall, NetworkObject bear)
    {
        var puzzle = PcsPuzzleDirector.Instance;
        var mover = bear.GetComponent<Mover>();
        var contacts = new List<ContactPoint2D>();
        plate.Solid.GetContacts(contacts);
        float top = plate.Solid.bounds.max.y;
        int topContacts = contacts.Count(c => c.enabled && Mathf.Abs(c.normal.y) >= 0.5f &&
            Mathf.Abs(c.point.y - top) <= 0.05f &&
            ((c.collider == mover.BodyCollider && c.otherCollider == plate.Solid) ||
             (c.otherCollider == mover.BodyCollider && c.collider == plate.Solid)));
        var value = new PressureObservation
        {
            stage = stage, tick = runner.Tick.Raw, activeSection = puzzle.ActiveSection,
            plateIndex = plate.DeviceId, wallIndex = wall.DeviceId,
            plateActive = puzzle.States[plate.DeviceId].Active, wallActive = puzzle.States[wall.DeviceId].Active,
            bearTopContacts = topContacts, localIsDirectorAuthority = puzzle.HasStateAuthority,
            localOwnsBear = bear.HasStateAuthority, bearOwnerIsDirectorOwner = bear.StateAuthority == puzzle.Object.StateAuthority,
            bearFullKinematicContacts = mover.Body.useFullKinematicContacts, bearBodyType = mover.Body.bodyType.ToString(),
            bearPosition = mover.Body.position, wallPosition = puzzle.States[wall.DeviceId].Position,
            footAboveSupport = mover.BodyCollider.bounds.min.y - top,
            actualWallTransform = wall.transform.position, actualWallBody = wall.Body.position,
            actualWallColliderCenter = wall.Solid.bounds.center,
            wallRootError = Vector2.Distance(wall.transform.position, puzzle.States[wall.DeviceId].Position),
            wallBodyError = Vector2.Distance(wall.Body.position, puzzle.States[wall.DeviceId].Position),
            wallColliderError = Vector2.Distance(wall.Solid.bounds.center, puzzle.States[wall.DeviceId].Position + pressureColliderOffset)
        };
        report.pressureObservations.Add(value);
        Save();
        return value;
    }

    private IEnumerator RunLifecycle()
    {
        Phase(IsLateJoinRole ? "B-waiting-for-late-spawns" : "A-waiting-for-initial-two-spawns");
        yield return WaitForObservation(IsLateJoinRole ? 4 : 2, 45d);
        report.normalFlowReached = true;
        CaptureGameView();

        if (!IsLateJoinRole)
        {
            yield return ObserveFor(2, 2d);
            if (options.Role == MyEnum.CharacterType.Mouse)
            {
                var actor = LocalActor();
                var console = PcsPuzzleDirector.Instance.Devices.Where(d => d != null && d.Section == 0 &&
                    d.RequiredRole == MyEnum.CharacterType.Mouse && d.Kind == PcsDeviceKind.HackConsole)
                    .OrderBy(d => Vector2.Distance(actor.transform.position, d.InteractionPoint)).FirstOrDefault();
                LifecycleAssert("A_console_exists", console != null, "Use the nearest authored Mouse tutorial console.");
                yield return WalkAndHack(console, "A");
                WriteMarker("a-trigger", MakeDeviceMarker(console));
            }
            yield return WaitForMarker("a-trigger", MyEnum.CharacterType.Mouse, Deadline(35d));
            var marker = ReadMarker("a-trigger", MyEnum.CharacterType.Mouse);
            yield return WaitForDoor(marker, 15d);
            LifecycleAssert("A_replicated_hack_and_door", DeviceStatePreserved(marker, true), "Counter, activation and open pose match the normal Mouse input result.");
            AddObservation(2);
            WriteMarker("a-verified");
            if (options.Role == MyEnum.CharacterType.Rabbit)
            {
                yield return WaitForMarker("a-verified", MyEnum.CharacterType.Mouse, Deadline(15d));
                WriteMarker("a-complete");
            }
        }

        Phase("B-observing-four-with-two-late-joiners");
        yield return WaitForObservation(4, 60d);
        var initialMarker = ReadMarker("a-trigger", MyEnum.CharacterType.Mouse);
        yield return WaitForDoor(initialMarker, 10d);
        LifecycleAssert("B_existing_device_state_received", DeviceStatePreserved(initialMarker, true), "Late join must preserve the completed first hack and open door.");
        if (IsLateJoinRole)
        {
            var actor = LocalActor();
            var puzzle = PcsPuzzleDirector.Instance;
            bool hasPose = puzzle.TryGetSpawnPose(runner.LocalPlayer, options.Role, out var waiting, out _);
            LifecycleAssert("B_safe_waiting_position", hasPose && Vector2.Distance(actor.transform.position, waiting) < 2.5f,
                "Late join uses the authored waiting deck, allowing physics settling/separation.");
            LifecycleAssert("B_not_a_starting_role", !NetworkGameManager.Instance.IsStartingPlayer(runner.LocalPlayer) &&
                !PcsPlayerAbilities.CanParticipate(actor), "Late join is not assigned to a departed or missing starting role.");
            int beforeRoleEpoch = puzzle.GetResetEpoch(options.Role);
            int beforeGlobalEpoch = puzzle.ResetEpoch;
            // A negative authorization probe uses the real public RPC entry; it never writes network state directly.
            puzzle.RequestAction(actor, PcsPuzzleAction.Reset, -1, actor.transform.position);
            yield return Delay(1d);
            LifecycleAssert("B_late_reset_rpc_rejected", puzzle.GetResetEpoch(options.Role) == beforeRoleEpoch &&
                puzzle.ResetEpoch == beforeGlobalEpoch && DeviceStatePreserved(initialMarker, true),
                "A late participant's direct Reset request leaves room/global epochs and the existing door unchanged.");
        }
        AddObservation(4);
        WriteMarker("b-verified");
        foreach (var role in new[] { MyEnum.CharacterType.Rabbit, MyEnum.CharacterType.Mouse, MyEnum.CharacterType.Bear, MyEnum.CharacterType.Frog })
            yield return WaitForMarker("b-verified", role, Deadline(25d));

        Phase("C-preparing-normal-retry-and-master-departure");
        if (options.Role == MyEnum.CharacterType.Mouse)
        {
            var puzzle = PcsPuzzleDirector.Instance;
            var actor = LocalActor();
            int before = puzzle.GetResetEpoch(MyEnum.CharacterType.Mouse);
            puzzle.RequestAction(actor, PcsPuzzleAction.Reset, -1, actor.transform.position);
            double resetDeadline = Deadline(12d);
            while (puzzle.GetResetEpoch(MyEnum.CharacterType.Mouse) == before ||
                Vector2.Distance(actor.transform.position, puzzle.GetRespawnPosition(MyEnum.CharacterType.Mouse)) > 1f)
            {
                CheckDeadline(resetDeadline, "C_normal_tutorial_retry_timeout");
                yield return null;
            }
            LifecycleAssert("C_explicit_normal_retry", puzzle.GetResetEpoch(MyEnum.CharacterType.Mouse) != before,
                "The ordinary authorized Reset RPC restored the tutorial; no fixture pose or network-field write was used.");
            yield return Delay(0.6d);
            var console = puzzle.GetDevice(initialMarker.consoleIndex);
            yield return WalkAndHack(console, "C");
            var marker = MakeDeviceMarker(console);
            LifecycleAssert("C_door_is_still_moving", Vector2.Distance(marker.handoffPose, marker.open) > 0.1f,
                "Request departure while the newly opened door is still travelling.");
            WriteMarker("c-trigger", marker);
        }
        yield return WaitForMarker("c-trigger", MyEnum.CharacterType.Mouse, Deadline(45d));
        var handoff = ReadMarker("c-trigger", MyEnum.CharacterType.Mouse);
        if (options.Role == MyEnum.CharacterType.Rabbit)
        {
            var puzzle = PcsPuzzleDirector.Instance;
            LifecycleAssert("C_departure_from_original_master", runner.IsSharedModeMasterClient && puzzle.HasStateAuthority,
                "Only the original Shared master performs the planned departure.");
            LifecycleAssert("C_departure_requested_in_motion", DeviceStatePreserved(handoff, false) &&
                Vector2.Distance(puzzle.States[handoff.gateIndex].Position, handoff.open) > 0.03f,
                "This peer proves departure initiation, not the result observed by the remaining peers.");
            AddObservation(4);
            WriteMarker("c-departure-requested");
            Phase("C-original-master-leaving-normally");
            yield break;
        }

        yield return WaitForMarker("c-departure-requested", MyEnum.CharacterType.Rabbit, Deadline(15d));
        yield return WaitForObservation(3, 30d);
        var transferred = report.observations[report.observations.Count - 1];
        LifecycleAssert("C_original_master_absent_and_replaced", transferred.players.All(p => p.role != "Rabbit") &&
            (transferred.managerAuthorityRole == "Mouse" || transferred.managerAuthorityRole == "Bear" || transferred.managerAuthorityRole == "Frog"),
            "The original Rabbit actor is absent and an identified remaining role owns the sole manager; both scene authorities match the new Shared master.");
        var remainingPuzzle = PcsPuzzleDirector.Instance;
        LifecycleAssert("C_state_survives_new_authority", DeviceStatePreserved(handoff, false) &&
            remainingPuzzle.States[handoff.gateIndex].Position.y >= handoff.handoffPose.y - 0.05f,
            "Initialized state, hack counter, resource values, epochs and monotonic door pose survive departure.");
        yield return WaitForDoor(handoff, 15d);
        LifecycleAssert("C_door_finishes_under_new_authority", DeviceStatePreserved(handoff, true),
            "The same opened door reaches its authored endpoint after authority transfer without a reset.");
        Phase("C-observing-three-after-master-departure");
        yield return ObserveFor(3, options.Seconds);
        WriteMarker("c-verified");
        foreach (var role in new[] { MyEnum.CharacterType.Mouse, MyEnum.CharacterType.Bear, MyEnum.CharacterType.Frog })
            yield return WaitForMarker("c-verified", role, Deadline(20d));
        Phase("lifecycle-complete-shutdown-grace");
        yield return Delay(3d);
    }

    private IEnumerator WalkAndHack(PcsPuzzleDevice console, string step)
    {
        var actor = LocalActor();
        var input = actor.GetComponent<PlayerInput>();
        var puzzle = PcsPuzzleDirector.Instance;
        LifecycleAssert(step + "_normal_input_actor", options.Role == MyEnum.CharacterType.Mouse && input != null &&
            actor.HasStateAuthority && console != null, "Use the normal locally authoritative Mouse PlayerInput.");
        Vector2 start = actor.transform.position;
        double walkDeadline = Deadline(15d);
        float interactionDistance = Mathf.Min(1.1f, console.InteractionRange - 0.15f);
        Phase(step + "-Mouse-walking-to-first-console");
        while (Vector2.Distance(actor.transform.position, console.InteractionPoint) > interactionDistance)
        {
            CheckDeadline(walkDeadline, step + "_normal_Mouse_walk_timeout");
            float direction = Mathf.Sign(console.InteractionPoint.x - actor.transform.position.x);
            input.InjectDevelopmentInput(new Vector2(direction, 0f), false, aim: console.InteractionPoint);
            yield return null;
        }
        input.InjectDevelopmentInput(Vector2.zero, false, aim: console.InteractionPoint);
        LifecycleAssert(step + "_walk_displacement", Vector2.Distance(start, actor.transform.position) > 1f,
            "The Mouse reached the console through movement input; no transform or Rigidbody pose was assigned.");
        yield return Delay(0.2d);
        LifecycleAssert(step + "_fresh_hack_counter", puzzle.States[console.DeviceId].Counter == 0 && puzzle.States[console.DeviceId].Active == 0,
            "This attempt begins at the reset, inactive first console.");
        Phase(step + "-Mouse-normal-interaction-pulses");
        for (int pulse = 1; pulse <= console.RequiredInputs; pulse++)
        {
            input.InjectDevelopmentInput(Vector2.zero, false, interact: true, aim: console.InteractionPoint);
            double pulseDeadline = Deadline(5d);
            while (puzzle.States[console.DeviceId].Counter < pulse)
            {
                CheckDeadline(pulseDeadline, step + "_normal_hack_pulse_timeout");
                yield return null;
            }
            LifecycleAssert(step + "_hack_pulse_" + pulse, puzzle.States[console.DeviceId].Counter == pulse,
                "One normal input pulse produced one replicated progress increment.");
            // Do not delay the final pulse: C must observe a still-moving door.
            if (pulse < console.RequiredInputs) yield return Delay(0.45d);
        }
        input.ClearDevelopmentInput();
    }

    private LifecycleMarker MakeDeviceMarker(PcsPuzzleDevice console)
    {
        var puzzle = PcsPuzzleDirector.Instance;
        var gate = console.Links.FirstOrDefault(d => d != null && d.Kind == PcsDeviceKind.SlidingWall);
        LifecycleAssert("device_marker_gate", gate != null && gate.LowerStop != null && gate.UpperStop != null,
            "The test uses the console's authored gate references.");
        return new LifecycleMarker
        {
            consoleIndex = console.DeviceId, gateIndex = gate.DeviceId, counter = console.RequiredInputs,
            closed = gate.LowerStop.position, open = gate.UpperStop.position, handoffPose = puzzle.States[gate.DeviceId].Position,
            mouseEpoch = puzzle.GetResetEpoch(MyEnum.CharacterType.Mouse), resetEpoch = puzzle.ResetEpoch,
            energy = puzzle.Energy, batteries = puzzle.Batteries
        };
    }

    private bool DeviceStatePreserved(LifecycleMarker marker, bool requireOpen)
    {
        var puzzle = PcsPuzzleDirector.Instance;
        if (puzzle == null || !puzzle.CanSpawnPlayers || marker.consoleIndex < 0 || marker.gateIndex < 0 ||
            marker.consoleIndex >= puzzle.Devices.Length || marker.gateIndex >= puzzle.Devices.Length) return false;
        var console = puzzle.States[marker.consoleIndex];
        var gate = puzzle.States[marker.gateIndex];
        var gateDevice = puzzle.Devices[marker.gateIndex];
        bool actualOpen = gateDevice != null && gateDevice.Body != null &&
            Vector2.Distance(gateDevice.transform.position, marker.open) < 0.04f &&
            Vector2.Distance(gateDevice.Body.position, marker.open) < 0.04f;
        return console.Active == 1 && console.Counter == marker.counter && gate.Active == 1 &&
            puzzle.ResetEpoch == marker.resetEpoch && puzzle.GetResetEpoch(MyEnum.CharacterType.Mouse) == marker.mouseEpoch &&
            puzzle.ActiveSection == 0 && puzzle.Energy == marker.energy && puzzle.Batteries == marker.batteries &&
            gate.Position.y >= marker.closed.y - 0.05f && gate.Position.y <= marker.open.y + 0.05f &&
            (!requireOpen || (Vector2.Distance(gate.Position, marker.open) < 0.025f && actualOpen));
    }

    private IEnumerator WaitForDoor(LifecycleMarker marker, double seconds)
    {
        double deadline = Deadline(seconds);
        while (!DeviceStatePreserved(marker, true))
        {
            CheckDeadline(deadline, "replicated_door_state_or_open_pose_timeout");
            yield return null;
        }
    }

    private IEnumerator WaitForObservation(int count, double seconds)
    {
        double deadline = Deadline(seconds);
        double nextRecord = 0d;
        Observation observation;
        while (!TryObserve(out observation, count) || !observation.checksPassed)
        {
            if (observation != null && Time.realtimeSinceStartupAsDouble >= nextRecord)
            {
                report.lastPendingObservation = observation;
                Save();
                nextRecord = Time.realtimeSinceStartupAsDouble + 2d;
            }
            CheckDeadline(deadline, "lifecycle_player_state_or_authority_timeout");
            yield return null;
        }
        report.observations.Add(observation);
        Save();
    }

    private IEnumerator ObserveFor(int count, double seconds)
    {
        double deadline = Deadline(seconds);
        double next = 0d;
        while (Time.realtimeSinceStartupAsDouble < deadline)
        {
            if (Time.realtimeSinceStartupAsDouble >= next)
            {
                AddObservation(count);
                next = Time.realtimeSinceStartupAsDouble + 1d;
            }
            yield return null;
        }
    }

    private void AddObservation(int count)
    {
        if (!TryObserve(out var observation, count)) throw new SmokeFailure("lifecycle_normal_objects_missing");
        report.observations.Add(observation);
        Save();
        if (!observation.checksPassed) throw new SmokeFailure("lifecycle_observation_failed");
    }

    private NetworkObject LocalActor()
    {
        if (runner == null || !runner.TryGetPlayerObject(runner.LocalPlayer, out var actor) || actor == null || !actor.IsValid)
            throw new SmokeFailure("normal_local_player_missing");
        return actor;
    }
    private void LifecycleAssert(string id, bool passed, string detail)
    {
        report.lifecycleChecks.Add(new LifecycleCheck { id = id, passed = passed, detail = detail });
        Save();
        if (!passed) throw new SmokeFailure(id);
    }
    private static double Deadline(double seconds) => Time.realtimeSinceStartupAsDouble + seconds;
    private static IEnumerator Delay(double seconds)
    {
        double deadline = Deadline(seconds);
        while (Time.realtimeSinceStartupAsDouble < deadline) yield return null;
    }
    private string MarkerPath(string stage, MyEnum.CharacterType role) => Path.Combine(RunDirectory, "lifecycle-" + stage + "-" + role + ".json");
    private void WriteMarker(string stage, LifecycleMarker marker = null)
    {
        if (marker == null) marker = new LifecycleMarker();
        marker.utc = DateTime.UtcNow.ToString("O");
        marker.role = options.Role.ToString();
        marker.stage = stage;
        Directory.CreateDirectory(RunDirectory);
        string path = MarkerPath(stage, options.Role);
        if (File.Exists(path)) throw new SmokeFailure("lifecycle_marker_already_exists_use_fresh_run");
        File.WriteAllText(path + ".partial", JsonUtility.ToJson(marker, true));
        File.Move(path + ".partial", path);
    }
    private IEnumerator WaitForMarker(string stage, MyEnum.CharacterType role, double deadline)
    {
        while (!File.Exists(MarkerPath(stage, role)))
        {
            CheckDeadline(deadline, "lifecycle_peer_barrier_timeout_" + stage);
            yield return null;
        }
    }
    private LifecycleMarker ReadMarker(string stage, MyEnum.CharacterType role)
    {
        var marker = JsonUtility.FromJson<LifecycleMarker>(File.ReadAllText(MarkerPath(stage, role)));
        if (marker == null || marker.stage != stage || marker.role != role.ToString()) throw new SmokeFailure("invalid_lifecycle_marker");
        return marker;
    }
    private void CaptureGameView()
    {
        if (report.screenshotRequested) return;
        Directory.CreateDirectory(RunDirectory);
        ScreenCapture.CaptureScreenshot(Path.Combine(RunDirectory, options.Role + ".png"));
        report.screenshotRequested = true;
        Save();
    }

    private bool TryObserve(out Observation observation, int expectedPlayers = -1)
    {
        if (expectedPlayers < 0) expectedPlayers = options.Players;
        observation = null;
        var manager = NetworkGameManager.Instance;
        var puzzle = PcsPuzzleDirector.Instance;
        if (runner == null || !runner.IsRunning || manager == null || !manager.IsReady ||
            puzzle == null || !puzzle.CanSpawnPlayers || runner.SceneManager.IsBusy ||
            SceneManager.GetActiveScene().name != "Puzzle") return false;

        var players = new List<PlayerSample>();
        int connected = 0, localOwned = 0;
        NetworkObject local = null;
        bool rolesMatch = true;
        foreach (var player in runner.ActivePlayers)
        {
            connected++;
            if (!runner.TryGetPlayerObject(player, out var actor) || actor == null || !actor.IsValid) continue;
            var abilities = actor.GetComponent<PcsPlayerAbilities>();
            var input = actor.GetComponent<PlayerInput>();
            var body = actor.GetComponent<Rigidbody2D>();
            if (abilities == null || input == null || body == null) { rolesMatch = false; continue; }
            if (!manager.TryGetSelectedRole(player, out var selected) || selected != abilities.CharacterType) rolesMatch = false;
            if (actor.HasStateAuthority) localOwned++;
            if (player == runner.LocalPlayer) local = actor;
            players.Add(new PlayerSample { role = abilities.CharacterType.ToString(), position = actor.transform.position,
                localAuthority = actor.HasStateAuthority, inputEnabled = input.enabled, bodyType = body.bodyType.ToString(),
                bodySimulated = body.simulated, fullKinematicContacts = body.useFullKinematicContacts });
        }
        var ordered = players.OrderBy(p => p.role, StringComparer.Ordinal).ToArray();
        var camera = Camera.main;
        var follow = camera != null ? camera.GetComponent<PcsLocalCameraFollow>() : null;
        bool cameraBound = local != null && follow != null && CameraTarget != null &&
            (CameraTarget.GetValue(follow) as Transform) == local.transform;
        var samples = new DeviceSample[puzzle.Devices.Length];
        for (int index = 0; index < samples.Length; index++)
        {
            var state = puzzle.States[index];
            samples[index] = new DeviceSample { index = index, position = state.Position, active = state.Active,
                phase = state.Phase, counter = state.Counter, window = state.Window };
        }
        string digest;
        using (var sha = SHA256.Create())
            digest = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(JsonUtility.ToJson(new DeviceSet { devices = samples })))).Replace("-", "");
        bool localRole = local != null && local.GetComponent<PcsPlayerAbilities>().CharacterType == options.Role;
        bool inputsMatch = ordered.All(p => p.inputEnabled == p.localAuthority);
        bool physicsMatch = ordered.All(p => p.bodySimulated && (p.localAuthority ? p.bodyType == "Dynamic" :
            p.bodyType == "Kinematic" && p.fullKinematicContacts));
        bool oneSpawner = runner.GetComponents<PcsPlayerSpawner>().Length == 1 &&
            runner.GetComponents<MonoBehaviour>().All(c => c == null || c.GetType().Name != "PlayerSpawner");
        observation = new Observation
        {
            elapsedSeconds = Time.realtimeSinceStartupAsDouble - startedAt, networkTick = runner.Tick.Raw,
            connectedPlayers = connected, registeredPlayers = players.Count, localOwnedPlayers = localOwned,
            sharedMode = runner.GameMode == GameMode.Shared, teamLocked = manager.TeamLocked,
            isMaster = runner.IsSharedModeMasterClient, managerAuthority = manager.HasStateAuthority,
            directorAuthority = puzzle.HasStateAuthority, localRoleMatchesSelection = localRole,
            localStartingTeamMember = manager.IsStartingPlayer(runner.LocalPlayer), cameraBoundToLocalPlayer = cameraBound,
            localOverlayAvailable = puzzle.isActiveAndEnabled && local != null,
            physicsAuthorityMatches = physicsMatch,
            activeSection = puzzle.ActiveSection, resetEpoch = puzzle.ResetEpoch, completedTutorialMask = puzzle.CompletedTutorialMask,
            energy = puzzle.Energy, batteries = puzzle.Batteries, stageOneHacked = puzzle.StageOneHacked,
            shaftStarted = puzzle.ShaftStarted, shaftArrived = puzzle.ShaftArrived,
            deviceStateSha256 = digest, players = ordered, devices = samples
        };
        observation.managerInstances = FindObjectsByType<NetworkGameManager>(FindObjectsSortMode.None).Count(m => m.IsReady);
        observation.directorInstances = FindObjectsByType<PcsPuzzleDirector>(FindObjectsSortMode.None).Count(d => d.CanSpawnPlayers);
        if (manager.TryGetSelectedRole(manager.Object.StateAuthority, out var authorityRole))
            observation.managerAuthorityRole = authorityRole.ToString();
        observation.checksPassed = observation.sharedMode && observation.teamLocked && rolesMatch && inputsMatch && physicsMatch && oneSpawner &&
            connected == expectedPlayers && players.Count == expectedPlayers && localOwned == 1 && localRole && cameraBound &&
            observation.localStartingTeamMember == !IsLateJoinRole && observation.localOverlayAvailable &&
            observation.managerAuthority == observation.isMaster && observation.directorAuthority == observation.isMaster &&
            observation.managerInstances == 1 && observation.directorInstances == 1 &&
            FindObjectsByType<PcsPlayerAbilities>(FindObjectsSortMode.None).Count(p => p.Object != null && p.Object.IsValid) == expectedPlayers;
        return true;
    }

    private static Options ParseOptions(string[] arguments)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 0; index < arguments.Length; index++)
        {
            if (!arguments[index].StartsWith("--pcs-smoke-", StringComparison.Ordinal)) continue;
            string key = arguments[index];
            if (++index >= arguments.Length || arguments[index].StartsWith("--", StringComparison.Ordinal) || values.ContainsKey(key))
                throw new SmokeFailure("invalid_or_duplicate_smoke_argument");
            values.Add(key, arguments[index]);
        }
        string[] allowed = { "--pcs-smoke-role", "--pcs-smoke-room", "--pcs-smoke-count", "--pcs-smoke-seconds", "--pcs-smoke-run", "--pcs-smoke-scenario" };
        if (values.Keys.Any(key => !allowed.Contains(key))) throw new SmokeFailure("unknown_smoke_argument");
        if (!values.TryGetValue("--pcs-smoke-role", out var roleText) ||
            !Enum.TryParse(roleText, false, out MyEnum.CharacterType role) || roleText != role.ToString() ||
            (int)role < (int)MyEnum.CharacterType.Rabbit || (int)role > (int)MyEnum.CharacterType.Frog)
            throw new SmokeFailure("role_must_be_Rabbit_Bear_Mouse_or_Frog");
        if (!values.TryGetValue("--pcs-smoke-room", out var room) || string.IsNullOrWhiteSpace(room))
            throw new SmokeFailure("explicit_room_argument_required");
        var result = new Options { Role = role, Room = room };
        if (values.TryGetValue("--pcs-smoke-count", out var count) && (!int.TryParse(count, out result.Players) || (result.Players != 2 && result.Players != 4)))
            throw new SmokeFailure("participant_count_must_be_2_or_4");
        if (values.TryGetValue("--pcs-smoke-seconds", out var seconds) && (!int.TryParse(seconds, out result.Seconds) || result.Seconds < 10 || result.Seconds > 30))
            throw new SmokeFailure("observation_seconds_must_be_10_to_30");
        if (values.TryGetValue("--pcs-smoke-run", out var run)) result.Run = run;
        if (!Regex.IsMatch(result.Run, "^[A-Za-z0-9-]{1,48}$")) throw new SmokeFailure("run_name_must_be_1_to_48_ascii_letters_digits_or_hyphens");
        if (values.TryGetValue("--pcs-smoke-scenario", out var scenario)) result.Scenario = scenario;
        if (result.Scenario != "initial" && result.Scenario != "lifecycle" && result.Scenario != "carry-two" && result.Scenario != "pressure-two" && result.Scenario != "shaft-two" && result.Scenario != "layout-four")
            throw new SmokeFailure("scenario_must_be_initial_lifecycle_carry-two_pressure-two_shaft-two_or_layout-four");
        if (result.Scenario == "lifecycle" && (result.Players != 4 || result.Run == "default"))
            throw new SmokeFailure("lifecycle_requires_count_4_and_a_fresh_explicit_run_name");
        if (result.Scenario == "carry-two" && (result.Players != 2 || result.Run == "default" ||
            (result.Role != MyEnum.CharacterType.Rabbit && result.Role != MyEnum.CharacterType.Mouse)))
            throw new SmokeFailure("carry-two_requires_Rabbit_Mouse_count_2_and_a_fresh_explicit_run_name");
        if (result.Scenario == "pressure-two" && (result.Players != 2 || result.Run == "default" ||
            (result.Role != MyEnum.CharacterType.Mouse && result.Role != MyEnum.CharacterType.Bear)))
            throw new SmokeFailure("pressure-two_requires_Mouse_Bear_count_2_and_a_fresh_explicit_run_name");
        if (result.Scenario == "shaft-two" && (result.Players != 2 || result.Run == "default" || (result.Role != MyEnum.CharacterType.Mouse && result.Role != MyEnum.CharacterType.Bear)))
            throw new SmokeFailure("shaft-two_requires_Mouse_Bear_count_2_and_a_fresh_explicit_run_name");
        if (result.Scenario == "layout-four" && (result.Players != 4 || result.Run == "default"))
            throw new SmokeFailure("layout-four_requires_count_4_and_a_fresh_explicit_run_name");
        return result;
    }

    private void OnLobbyJoined() { lobbyReady = true; }
    private void OnSessionAccessFinished(bool success) { joined = success; joinFailed = !success; }
    private static void CheckDeadline(double deadline, string reason)
    {
        if (Time.realtimeSinceStartupAsDouble >= deadline) throw new SmokeFailure(reason);
    }
    private void Phase(string phase)
    {
        report.phase = phase;
        report.events.Add(phase);
        Save();
    }
    private void Save()
    {
        report.elapsedSeconds = Time.realtimeSinceStartupAsDouble - startedAt;
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
        File.WriteAllText(outputPath, JsonUtility.ToJson(report, true));
    }
    private void OnApplicationQuit()
    {
        if (finished || report == null) return;
        report.failures.Add("process_quit_before_normal_smoke_completion");
        report.passed = false;
        Phase("interrupted");
    }
}
#endif

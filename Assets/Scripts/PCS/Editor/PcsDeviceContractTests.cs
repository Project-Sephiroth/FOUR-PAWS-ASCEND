using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Fusion;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static partial class PcsSecondPassTools
{
    private const string DeviceTestFlag = "PCS.SecondPass.DeviceTests";
    private const string DeviceModeFlag = "PCS.SecondPass.DeviceMode";
    private const string DeviceSuiteFlag = "PCS.SecondPass.DeviceSuite";
    private const string DeviceTestScene = "Assets/Scenes/Puzzle.unity";

    [Serializable]
    private class DeviceCheck
    {
        public string id;
        public string status;
        public string detail;
    }

    [Serializable]
    private class DeviceReport
    {
        public string startedUtc;
        public string finishedUtc;
        public string heartbeatUtc;
        public string status;
        public string phase;
        public string unityVersion;
        public string mode;
        public string boundary = "Actual registered Puzzle director and Runner.Spawn player prefabs. Play-only fixtures select sections, position/freeze actors near devices, suppress active non-shaft enemies and drop tutorial dummies above supports. Shaft tests use the production authority Raise/Hold API, real ticks and physics, with unchanged authored speed and endpoints. Retired shaft requests must have no effect. This is device-contract coverage, not passenger traversal, multiplayer synchronization, lobby admission or four-player completion. No Scene is saved.";
        public List<DeviceCheck> checks = new List<DeviceCheck>();
        public List<string> errors = new List<string>();
        public List<string> diagnostics = new List<string>();
    }

    private static DeviceReport deviceReport;
    private static NetworkRunner deviceRunner;
    private static Task<StartGameResult> deviceStartTask;
    private static readonly Stack<IEnumerator> deviceRoutines = new Stack<IEnumerator>();
    private static readonly List<NetworkObject> deviceActors = new List<NetworkObject>();
    private static PcsPuzzleDirector testDirector;
    private static bool deviceFinishing;
    private static double deviceStartedRealtime;
    private static double deviceSceneDeadline;

    [InitializeOnLoadMethod]
    private static void InstallDeviceTestCallbacks()
    {
        EditorApplication.playModeStateChanged -= DeviceTestModeChanged;
        EditorApplication.playModeStateChanged += DeviceTestModeChanged;
        EditorApplication.update -= PollDeviceTests;
        EditorApplication.update += PollDeviceTests;
    }

    public static void BeginDeviceContractTests()
    {
        SessionState.SetString(DeviceSuiteFlag, "all");
        SessionState.SetString(DeviceModeFlag, "Single");
        BeginDeviceContractsCore();
    }

    public static void BeginSharedDeviceContractTests()
    {
        SessionState.SetString(DeviceSuiteFlag, "all");
        SessionState.SetString(DeviceModeFlag, "Shared");
        BeginDeviceContractsCore();
    }

    private sealed class PassiveVisualSnapshot
    {
        private readonly SpriteRenderer renderer;
        private readonly Color color;
        private readonly bool enabled, active;
        private readonly Vector3 position, scale;
        private readonly Quaternion rotation;
        public PassiveVisualSnapshot(SpriteRenderer value)
        {
            renderer = value; color = value.color; enabled = value.enabled; active = value.gameObject.activeSelf;
            position = value.transform.localPosition; scale = value.transform.localScale; rotation = value.transform.localRotation;
        }
        public bool Matches() => renderer != null && renderer.color == color && renderer.enabled == enabled &&
            renderer.gameObject.activeSelf == active && renderer.transform.localPosition == position &&
            renderer.transform.localScale == scale && renderer.transform.localRotation == rotation;
    }

    public static void BeginShaftLiftContractTests()
    {
        SessionState.SetString(DeviceSuiteFlag, "shaft");
        SessionState.SetString(DeviceModeFlag, "Shared");
        BeginDeviceContractsCore();
    }

    private static void BeginDeviceContractsCore()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop the current Play session before device tests.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Save all open scenes before device tests.");
        if (SceneUtility.GetBuildIndexByScenePath(DeviceTestScene) < 0)
            throw new InvalidOperationException("Puzzle must be registered in Build Settings.");
        Directory.CreateDirectory(EvidenceDirectory);
        string previous = Path.Combine(EvidenceDirectory, "device-tests.json");
        if (File.Exists(previous))
            File.Copy(previous, Path.Combine(EvidenceDirectory, "device-tests-previous-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + ".json"));
        SessionState.SetString(DeviceTestFlag, "requested");
        EditorSceneManager.OpenScene(DeviceTestScene);
        EditorApplication.EnterPlaymode();
    }

    private static void DeviceTestModeChanged(PlayModeStateChange state)
    {
        string flag = SessionState.GetString(DeviceTestFlag, "");
        if (state == PlayModeStateChange.EnteredPlayMode && flag == "requested")
        {
            SessionState.SetString(DeviceTestFlag, "running");
            bool shared = SessionState.GetString(DeviceModeFlag, "Single") == "Shared";
            deviceReport = new DeviceReport { startedUtc = DateTime.UtcNow.ToString("O"), status = "RUNNING",
                phase = "Starting isolated Runner", unityVersion = Application.unityVersion,
                mode = shared ? "Fusion GameMode.Shared, one process, private one-player session" : "Fusion GameMode.Single" };
            deviceFinishing = false;
            deviceStartedRealtime = EditorApplication.timeSinceStartup;
            deviceSceneDeadline = 0d;
            testDirector = null;
            deviceActors.Clear();
            deviceRoutines.Clear();
            Application.logMessageReceived += CaptureDeviceTestLog;
            GameObject runnerObject = new GameObject("[Development] Device contract Runner");
            UnityEngine.Object.DontDestroyOnLoad(runnerObject);
            deviceRunner = runnerObject.AddComponent<NetworkRunner>();
            NetworkSceneManagerDefault sceneManager = runnerObject.AddComponent<NetworkSceneManagerDefault>();
            deviceStartTask = deviceRunner.StartGame(new StartGameArgs { GameMode = shared ? GameMode.Shared : GameMode.Single,
                SessionName = shared ? "PCS_Device_" + Guid.NewGuid().ToString("N") : null,
                PlayerCount = 1, IsVisible = false, IsOpen = false,
                Scene = SceneRef.FromIndex(SceneUtility.GetBuildIndexByScenePath(DeviceTestScene)), SceneManager = sceneManager });
            SaveDeviceReport();
        }
        else if (state == PlayModeStateChange.ExitingPlayMode && flag == "running")
        {
            if (deviceReport != null)
            {
                deviceReport.status = "INTERRUPTED";
                deviceReport.finishedUtc = DateTime.UtcNow.ToString("O");
                SaveDeviceReport();
            }
            SessionState.SetString(DeviceTestFlag, "restore");
            Application.logMessageReceived -= CaptureDeviceTestLog;
        }
        else if (state == PlayModeStateChange.EnteredEditMode && !string.IsNullOrEmpty(flag))
        {
            SessionState.SetString(DeviceTestFlag, "");
            if (SceneManager.GetActiveScene().path != DeviceTestScene)
                EditorSceneManager.OpenScene(DeviceTestScene);
        }
    }

    private static void PollDeviceTests()
    {
        if (!EditorApplication.isPlaying || deviceFinishing || SessionState.GetString(DeviceTestFlag, "") != "running") return;
        if (deviceReport == null)
        {
            string file = Path.Combine(EvidenceDirectory, "device-tests.json");
            if (File.Exists(file)) deviceReport = JsonUtility.FromJson<DeviceReport>(File.ReadAllText(file));
            if (deviceReport != null)
            {
                deviceReport.status = "INTERRUPTED";
                deviceReport.phase = "Domain reload interrupted the running test; rerun after compilation finishes.";
                deviceReport.finishedUtc = DateTime.UtcNow.ToString("O");
                SaveDeviceReport();
            }
            SessionState.SetString(DeviceTestFlag, "restore");
            EditorApplication.ExitPlaymode();
            return;
        }
        try
        {
            if (EditorApplication.timeSinceStartup - deviceStartedRealtime > 210d)
                throw new TimeoutException("Device contract suite exceeded 210 seconds.");
            if (deviceStartTask != null)
            {
                if (!deviceStartTask.IsCompleted) return;
                if (deviceStartTask.IsFaulted) throw deviceStartTask.Exception;
                if (!deviceStartTask.Result.Ok) throw new InvalidOperationException("Test Runner start failed: " + deviceStartTask.Result.ShutdownReason);
                deviceStartTask = null;
                deviceSceneDeadline = EditorApplication.timeSinceStartup + 15d;
                deviceReport.phase = "Waiting for normal scene object registration";
                SaveDeviceReport();
            }
            if (testDirector == null)
            {
                PcsPuzzleDirector candidate = PcsPuzzleDirector.Instance;
                if (candidate == null || candidate.Object == null || !candidate.Object.IsValid || !candidate.CanSpawnPlayers)
                {
                    if (EditorApplication.timeSinceStartup > deviceSceneDeadline)
                        throw new TimeoutException("Puzzle director did not register in 15 seconds.");
                    return;
                }
                testDirector = candidate;
                DeviceAssert("scene-registration", testDirector.HasStateAuthority, "Actual NetworkSceneManagerDefault registration and initialized state authority.");
                deviceRoutines.Push(RunDeviceContracts());
            }
            if (deviceRoutines.Count == 0) { FinishDeviceTests(null); return; }
            IEnumerator current = deviceRoutines.Peek();
            if (!current.MoveNext()) deviceRoutines.Pop();
            else if (current.Current is IEnumerator nested) deviceRoutines.Push(nested);
        }
        catch (Exception ex) { FinishDeviceTests(ex); }
    }

    private static NetworkObject DeviceActor(MyEnum.CharacterType role)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player/Player_" + role + ".prefab");
        if (prefab == null) throw new InvalidOperationException("Missing actual player prefab: " + role);
        NetworkObject actor = deviceRunner.Spawn(prefab, new Vector3(-80f - (int)role * 3f, 0f, 0f), Quaternion.identity, deviceRunner.LocalPlayer);
        deviceActors.Add(actor);
        actor.GetComponent<Rigidbody2D>().constraints = RigidbodyConstraints2D.FreezeAll;
        actor.GetComponent<PlayerInput>().InjectDevelopmentInput(Vector2.zero, false);
        deviceRunner.SetPlayerObject(deviceRunner.LocalPlayer, actor);
        return actor;
    }

    private static PcsPuzzleDevice DeviceFind(PcsDeviceKind kind, int section, int channel = -1)
    {
        foreach (PcsPuzzleDevice device in testDirector.Devices)
            if (device != null && device.Kind == kind && device.Section == section && (channel < 0 || device.Channel == channel)) return device;
        throw new InvalidOperationException("Missing device: " + section + "/" + kind + "/" + channel);
    }

    private static void DeviceFixture(string property, object value)
    {
        PropertyInfo member = typeof(PcsPuzzleDirector).GetProperty(property, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (member == null || !member.CanWrite) throw new InvalidOperationException("Fixture property not found: " + property);
        member.SetValue(testDirector, value);
    }

    private static void PlaceDeviceActor(NetworkObject actor, Vector2 position)
    {
        actor.GetComponent<Mover>().ResetAt(position);
        actor.GetComponent<Rigidbody2D>().constraints = RigidbodyConstraints2D.FreezeAll;
        Physics2D.SyncTransforms();
    }

    private static void SuppressDeviceEnemies()
    {
        foreach (PcsPuzzleDevice device in testDirector.Devices)
        {
            if (device == null || device.IsPassiveShaftObject || device.Kind != PcsDeviceKind.Enemy) continue;
            PcsDeviceState state = testDirector.States[device.DeviceId];
            state.Phase = 4;
            state.Active = 0;
            testDirector.States.Set(device.DeviceId, state);
        }
    }

    private static IEnumerator DeviceWait(float seconds)
    {
        float end = Time.time + seconds;
        while (Time.time < end) yield return null;
    }

    private static IEnumerator DeviceWaitPosition(PcsPuzzleDevice device, Vector2 target, float timeout = 6f)
    {
        float end = Time.time + timeout;
        while (Vector2.Distance(testDirector.States[device.DeviceId].Position, target) > 0.025f && Time.time < end) yield return null;
    }

    private static IEnumerator DevicePulseHack(NetworkObject mouse, PcsPuzzleDevice console)
    {
        for (int i = 0; i < console.RequiredInputs; i++)
        {
            PlaceDeviceActor(mouse, console.InteractionPoint + Vector2.left * 0.35f);
            testDirector.RequestAction(mouse, PcsPuzzleAction.HackStep, console.DeviceId, console.InteractionPoint);
            yield return DeviceWait(testDirector.HackPulseInterval + 0.08f);
        }
    }

    private static void DeviceRequest(NetworkObject actor, PcsPuzzleAction action, PcsPuzzleDevice target, int channel = 0)
    {
        PlaceDeviceActor(actor, target.InteractionPoint + Vector2.left * 0.35f);
        CaptureDeviceActorDiagnostic(actor, action.ToString(), target);
        testDirector.RequestAction(actor, action, target.DeviceId, target.InteractionPoint, channel);
    }

    private static void CaptureDeviceActorDiagnostic(NetworkObject actor, string action, PcsPuzzleDevice target)
    {
        if (deviceReport.diagnostics.Count < 5)
        {
            RpcAttribute rpc = typeof(PcsPuzzleDirector).GetMethod("RPC_Action", BindingFlags.Instance | BindingFlags.NonPublic).GetCustomAttribute<RpcAttribute>();
            RpcInfo localInfo = RpcInfo.FromLocal(deviceRunner, RpcChannel.Reliable, rpc.HostMode);
            deviceReport.diagnostics.Add(action + ": role=" + actor.GetComponent<PcsPlayerAbilities>().CharacterType +
                ", HasStateAuthority=" + actor.HasStateAuthority + ", StateAuthority=" + actor.StateAuthority +
                ", LocalPlayer=" + deviceRunner.LocalPlayer + ", computedLocalRpcSource=" + localInfo.Source +
                ", SourceEqualsStateAuthority=" + (actor.StateAuthority == localInfo.Source) +
                ", ActorInputEqualsSource=" + (actor.InputAuthority == localInfo.Source) +
                ", IsServer=" + deviceRunner.IsServer + ", IsSharedModeMasterClient=" + deviceRunner.IsSharedModeMasterClient +
                ", actorTransform=" + actor.transform.position + ", body=" + actor.GetComponent<Rigidbody2D>().position +
                ", target=" + (target != null ? target.InteractionPoint.ToString() : "none") + ", section=" + testDirector.ActiveSection + ", epoch=" + testDirector.ResetEpoch);
            SaveDeviceReport();
        }
    }

    private static IEnumerator RunDeviceContracts()
    {
        NetworkObject mouse = DeviceActor(MyEnum.CharacterType.Mouse);
        NetworkObject frog = DeviceActor(MyEnum.CharacterType.Frog);
        NetworkObject bear = DeviceActor(MyEnum.CharacterType.Bear);
        yield return DeviceWait(0.25f);
        if (SessionState.GetString(DeviceSuiteFlag, "all") == "shaft")
        {
            yield return RunShaftLiftContracts(mouse, frog, bear);
            yield break;
        }
        DeviceFixture("ActiveSection", 1);
        int setupEpoch = testDirector.ResetEpoch;
        CaptureDeviceActorDiagnostic(mouse, "InitialReset", null);
        testDirector.RequestAction(mouse, PcsPuzzleAction.Reset, -1, Vector2.zero);
        yield return DeviceWait(testDirector.ResetDelay + 0.1f);
        DeviceAssert("rpc-setup-reset-accepted", testDirector.ResetEpoch == setupEpoch + 1,
            "Before negative assertions, an actual valid no-distance reset request must pass the production authority gate.");
        if (testDirector.ResetEpoch != setupEpoch + 1)
            throw new InvalidOperationException("No valid RPC was accepted. Read recorded Single/Shared authority diagnostics; do not interpret negative rejection tests as coverage.");
        SuppressDeviceEnemies();
        yield return RunPressureContracts(bear, mouse);
        yield return RunStageOnePlateShots(bear);
        PcsPuzzleDevice hack = DeviceFind(PcsDeviceKind.HackConsole, 1);
        PcsPuzzleDevice firstLift = DeviceFind(PcsDeviceKind.Elevator, 1);
        PcsPuzzleDevice lever = DeviceFind(PcsDeviceKind.Lever, 1);
        deviceReport.phase = "1-1 role, range, hack cancellation, latch, and three lift stops";
        SaveDeviceReport();
        DeviceAssert("lift-starts-upper", Vector2.Distance(testDirector.States[firstLift.DeviceId].Position, firstLift.UpperStop.position) < 0.03f,
            "Before hacking, three-stop lift is at its serialized upper stop.");
        DeviceRequest(frog, PcsPuzzleAction.HackStep, hack);
        yield return DeviceWait(0.1f);
        DeviceAssert("hack-rejects-wrong-role", testDirector.States[hack.DeviceId].Counter == 0, "Frog request cannot progress the Mouse console.");
        PlaceDeviceActor(mouse, hack.InteractionPoint + Vector2.left * (hack.InteractionRange + 3f));
        testDirector.RequestAction(mouse, PcsPuzzleAction.HackStep, hack.DeviceId, hack.InteractionPoint);
        yield return DeviceWait(0.1f);
        DeviceAssert("hack-rejects-distance", testDirector.States[hack.DeviceId].Counter == 0, "Authority validates actor distance, independent of local target selection.");
        DeviceRequest(mouse, PcsPuzzleAction.HackStep, hack);
        yield return DeviceWait(0.1f);
        DeviceAssert("hack-first-pulse", testDirector.States[hack.DeviceId].Counter == 1, "A valid authority RPC records the first pulse.");
        PlaceDeviceActor(mouse, hack.InteractionPoint + Vector2.left * (hack.InteractionRange + 3f));
        yield return DeviceWait(0.15f);
        DeviceAssert("hack-partial-cancels", testDirector.States[hack.DeviceId].Counter == 0 && !testDirector.StageOneHacked, "Leaving range cancels unfinished hacking.");
        yield return DevicePulseHack(mouse, hack);
        DeviceAssert("hack-completes", testDirector.StageOneHacked && testDirector.States[hack.DeviceId].Active == 1, "Three separated valid pulses latch StageOneHacked.");
        PlaceDeviceActor(mouse, hack.InteractionPoint + Vector2.left * 5f);
        yield return DeviceWait(0.2f);
        DeviceAssert("hack-completion-persists", testDirector.StageOneHacked && testDirector.States[hack.DeviceId].Active == 1, "Completed hack remains when the actor leaves and the pressure plate is not occupied.");
        yield return DeviceWaitPosition(firstLift, firstLift.MiddleStop.position);
        DeviceAssert("lift-middle-after-hack", Vector2.Distance(testDirector.States[firstLift.DeviceId].Position, firstLift.MiddleStop.position) < 0.03f, "Network ticks move to the serialized middle stop.");
        DeviceRequest(frog, PcsPuzzleAction.Interact, lever);
        yield return DeviceWaitPosition(firstLift, firstLift.LowerStop.position);
        DeviceAssert("lift-lower-after-lever", testDirector.LeverLower && Vector2.Distance(testDirector.States[firstLift.DeviceId].Position, firstLift.LowerStop.position) < 0.03f, "Frog lever calls the lift down for Bear recovery.");
        DeviceRequest(frog, PcsPuzzleAction.Interact, lever);
        yield return DeviceWaitPosition(firstLift, firstLift.MiddleStop.position);
        DeviceAssert("lift-return-middle", !testDirector.LeverLower && Vector2.Distance(testDirector.States[firstLift.DeviceId].Position, firstLift.MiddleStop.position) < 0.03f, "The second lever request returns to middle without undoing hacking.");
        int epoch = testDirector.ResetEpoch;
        testDirector.RequestAction(mouse, PcsPuzzleAction.Reset, -1, Vector2.zero);
        yield return DeviceWait(testDirector.ResetDelay + 0.1f);
        DeviceAssert("stage-one-reset", testDirector.ResetEpoch == epoch + 1 && !testDirector.StageOneHacked && !testDirector.LeverLower &&
            Vector2.Distance(testDirector.States[firstLift.DeviceId].Position, firstLift.UpperStop.position) < 0.03f, "Actual reset RPC advances epoch and restores pre-hack upper stop.");

        if (deviceRunner.GameMode == GameMode.Shared) yield return RunShaftLiftContracts(mouse, frog, bear);
        else deviceReport.checks.Add(new DeviceCheck { id = "shaft-requires-shared", status = "NOT_RUN",
            detail = "The production shaft command requires Shared; run BeginShaftLiftContractTests or the Shared suite." });
        DeviceFixture("ActiveSection", 0);
        int mouseEpoch = testDirector.GetResetEpoch(MyEnum.CharacterType.Mouse);
        int frogEpoch = testDirector.GetResetEpoch(MyEnum.CharacterType.Frog);
        testDirector.RequestAction(mouse, PcsPuzzleAction.Reset, -1, Vector2.zero);
        yield return DeviceWait(testDirector.ResetDelay + 0.1f);
        DeviceAssert("tutorial-reset-is-isolated", testDirector.GetResetEpoch(MyEnum.CharacterType.Mouse) != mouseEpoch && testDirector.GetResetEpoch(MyEnum.CharacterType.Frog) == frogEpoch,
            "A tutorial reset advances only the requesting role's room epoch.");
        yield return RunDummyContracts(bear);
        yield return RunTutorialCarryCompletion(frog);
        foreach (NetworkObject actor in deviceActors)
                if (actor != null && actor.IsValid) actor.GetComponent<PlayerInput>().ClearDevelopmentInput();
    }

    private static IEnumerator RunShaftLiftContracts(NetworkObject mouse, NetworkObject frog, NetworkObject bear)
    {
        deviceReport.phase = "Shaft Raise/Hold, passive objects and rejected retired actions";
        SaveDeviceReport();
        DeviceFixture("ActiveSection", 2);
        PcsPuzzleDevice lift = DeviceFind(PcsDeviceKind.Elevator, 2);
        if (!lift.IsShaftElevator || lift.UpperStop == null || lift.Body == null || lift.Solid == null || lift.Speed <= 0f)
            throw new InvalidOperationException("The authored main elevator requires its upper stop, body, solid and positive speed.");
        Vector2 initial = testDirector.States[lift.DeviceId].Position;
        Vector2 colliderOffset = (Vector2)lift.Solid.bounds.center - lift.Body.position;
        yield return DeviceWait(0.3f);
        DeviceAssert("shaft-default-holds-current-pose", testDirector.States[lift.DeviceId].Phase == (int)PcsShaftLiftCommand.Hold &&
            Vector2.Distance(initial, testDirector.States[lift.DeviceId].Position) < .005f,
            "No hack, boarding, reset or automatic descent is needed; normal ticks retain the initial current pose.");
        DeviceAssert("shaft-invalid-command-rejected", !testDirector.TrySetShaftLiftCommand(lift, (PcsShaftLiftCommand)2, out string invalidReason), invalidReason);
        DeviceAssert("shaft-null-target-rejected", !testDirector.TrySetShaftLiftCommand(null, PcsShaftLiftCommand.Raise, out string nullReason), nullReason);
        PcsPuzzleDevice firstLift = DeviceFind(PcsDeviceKind.Elevator, 1);
        PcsDeviceState firstLiftBefore = testDirector.States[firstLift.DeviceId];
        DeviceAssert("shaft-stage-one-target-rejected", !testDirector.TrySetShaftLiftCommand(firstLift, PcsShaftLiftCommand.Raise, out string wrongReason) &&
            firstLiftBefore.Equals(testDirector.States[firstLift.DeviceId]), wrongReason);

        int epoch = testDirector.ResetEpoch;
        bool hacked = testDirector.StageOneHacked;
        bool lever = testDirector.LeverLower;
        int energy = testDirector.Energy, batteries = testDirector.Batteries;
        int[] health = { testDirector.GetHealth(MyEnum.CharacterType.Mouse), testDirector.GetHealth(MyEnum.CharacterType.Frog), testDirector.GetHealth(MyEnum.CharacterType.Bear) };
        var passiveStates = new Dictionary<PcsPuzzleDevice, PcsDeviceState>();
        var passivePositions = new Dictionary<PcsPuzzleDevice, Vector3>();
        var passiveScales = new Dictionary<PcsPuzzleDevice, Vector3>();
        var passiveRotations = new Dictionary<PcsPuzzleDevice, Quaternion>();
        var passiveVisuals = new List<PassiveVisualSnapshot>();
        foreach (PcsPuzzleDevice device in testDirector.Devices)
        {
            if (device == null || !device.IsPassiveShaftObject) continue;
            passiveStates.Add(device, testDirector.States[device.DeviceId]);
            passivePositions.Add(device, device.transform.localPosition);
            passiveScales.Add(device, device.transform.localScale);
            passiveRotations.Add(device, device.transform.localRotation);
            foreach (SpriteRenderer visual in device.GetComponentsInChildren<SpriteRenderer>(true))
                passiveVisuals.Add(new PassiveVisualSnapshot(visual));
            bool colliderPolicy = device.Kind == PcsDeviceKind.TimedPlatform
                ? device.Solid != null && !device.Solid.isTrigger && device.Solid.enabled
                : device.Solid == null || !device.Solid.enabled;
            DeviceAssert("shaft-passive-collider-policy-" + device.DeviceId, colliderPolicy && (device.Trigger == null || !device.Trigger.enabled),
                "Former timed platforms keep a non-trigger walking surface; all other retired Solid/Trigger interactions are disabled.");
            NetworkObject actor = device.RequiredRole == MyEnum.CharacterType.Frog ? frog :
                device.RequiredRole == MyEnum.CharacterType.Bear ? bear : mouse;
            PlaceDeviceActor(actor, device.InteractionPoint);
            foreach (PcsPuzzleAction action in new[] { PcsPuzzleAction.Interact, PcsPuzzleAction.HackStep,
                PcsPuzzleAction.ActivateChannel, PcsPuzzleAction.Refill, PcsPuzzleAction.Remote, PcsPuzzleAction.Carry })
                testDirector.RequestAction(actor, action, device.DeviceId, device.InteractionPoint);
            yield return DeviceWait(.04f);
            device.Present(false, true);
            device.Present(true, true);
            device.ApplyPose(device.InitialPosition + new Vector2(11f, 7f));
            DeviceAssert("shaft-passive-unavailable-" + device.DeviceId, !device.Available && !device.IsRemoteTarget,
                "Retired shaft objects remain in the scene but expose no puzzle interaction.");
        }
        if (passiveStates.Count == 0) throw new InvalidOperationException("No passive shaft objects found; absence is not rejection coverage.");
        testDirector.RequestAction(mouse, PcsPuzzleAction.Reset, -1, Vector2.zero);
        yield return DeviceWait(testDirector.ResetDelay + .2f);
        foreach (var pair in passiveStates)
        {
            DeviceAssert("shaft-passive-state-unchanged-" + pair.Key.DeviceId, pair.Value.Equals(testDirector.States[pair.Key.DeviceId]),
                "Requests and normal section-two ticks cannot animate, unlock, consume, damage or advance this retired puzzle object.");
            PcsPuzzleDevice device = pair.Key;
            DeviceAssert("shaft-passive-layout-preserved-" + device.DeviceId,
                device.transform.localPosition == passivePositions[device] && device.transform.localScale == passiveScales[device] &&
                device.transform.localRotation == passiveRotations[device],
                "Normal ticks and stale Present/ApplyPose calls preserve local placement, rotation and scale.");
        }
        DeviceAssert("shaft-passive-visuals-preserved", passiveVisuals.TrueForAll(snapshot => snapshot.Matches()),
            "Stale hide/activate/move calls leave sprite color, visibility, active state and local transforms unchanged.");
        DeviceAssert("shaft-retired-progress-and-reset-inert", testDirector.ActiveSection == 2 && testDirector.ResetEpoch == epoch &&
            testDirector.StageOneHacked == hacked && testDirector.LeverLower == lever && testDirector.Energy == energy && testDirector.Batteries == batteries &&
            testDirector.GetHealth(MyEnum.CharacterType.Mouse) == health[0] && testDirector.GetHealth(MyEnum.CharacterType.Frog) == health[1] &&
            testDirector.GetHealth(MyEnum.CharacterType.Bear) == health[2],
            "Retired arrival/exit/enemy/channel/battery/reset paths preserve progress, health and compatibility fields.");

        Vector2 upper = lift.UpperStop.position;
        if (upper.y - initial.y < .3f) throw new InvalidOperationException("The initial pose must leave upward travel to observe Raise/Hold.");
        bool accepted = testDirector.TrySetShaftLiftCommand(lift, PcsShaftLiftCommand.Raise, out string reason);
        DeviceAssert("shaft-raise-accepted", accepted, reason);
        if (!accepted) throw new InvalidOperationException(reason);
        yield return DeviceWait(Mathf.Min(.25f / lift.Speed, 1f));
        Vector2 rising = testDirector.States[lift.DeviceId].Position;
        DeviceAssert("shaft-production-ascent", rising.y > initial.y + .03f && rising.y <= upper.y + .005f,
            "Production network ticks move the actual platform upward without actor formation or hacking.");
        Vector2 bodyBeforeHold = lift.Body.position;
        accepted = testDirector.TrySetShaftLiftCommand(lift, PcsShaftLiftCommand.Hold, out reason);
        DeviceAssert("shaft-hold-accepted", accepted, reason);
        Vector2 held = testDirector.States[lift.DeviceId].Position;
        DeviceAssert("shaft-hold-captures-actual-body", Vector2.Distance(held, bodyBeforeHold) < .0001f,
            "Hold captures the authority Rigidbody position rather than the next scheduled network movement pose.");
        yield return DeviceWait(.4f);
        DeviceAssert("shaft-holds-midair-with-physical-collider", testDirector.States[lift.DeviceId].Phase == 0 &&
            Vector2.Distance(held, testDirector.States[lift.DeviceId].Position) < .005f && Vector2.Distance(held, lift.Body.position) < .02f &&
            Vector2.Distance((Vector2)lift.Solid.bounds.center, held + colliderOffset) < .025f,
            "Hold retains the actual current position, including Rigidbody and solid; it does not return to LowerStop.");
        accepted = testDirector.TrySetShaftLiftCommand(lift, PcsShaftLiftCommand.Raise, out reason);
        DeviceAssert("shaft-resume-accepted", accepted, reason);
        float timeout = Vector2.Distance(held, upper) / lift.Speed + 5f;
        float deadline = Time.time + timeout;
        bool monotonic = true;
        float previousY = held.y;
        while (Time.time < deadline)
        {
            Vector2 position = testDirector.States[lift.DeviceId].Position;
            monotonic &= position.y >= previousY - .002f && position.y <= upper.y + .005f;
            previousY = position.y;
            if (Vector2.Distance(position, upper) < .01f && testDirector.States[lift.DeviceId].Phase == 0) break;
            yield return null;
        }
        yield return DeviceWait(.3f);
        DeviceAssert("shaft-upper-stop-auto-hold", monotonic && testDirector.States[lift.DeviceId].Phase == 0 &&
            Vector2.Distance(testDirector.States[lift.DeviceId].Position, upper) < .01f && Vector2.Distance(lift.Body.position, upper) < .02f,
            "At the unchanged authored endpoint the command changes to Hold; motion never descends or overshoots.");
    }

    private static void DropActorOnSupport(NetworkObject actor, Collider2D support, float xOffset = 0f)
    {
        CapsuleCollider2D capsule = actor.GetComponent<CapsuleCollider2D>();
        float foot = (capsule.offset.y - capsule.size.y * 0.5f) * actor.transform.lossyScale.y;
        PlaceDeviceActor(actor, new Vector2(support.bounds.center.x + xOffset, support.bounds.max.y - foot + 0.05f));
        actor.GetComponent<Rigidbody2D>().constraints = RigidbodyConstraints2D.FreezeRotation;
    }

    private static IEnumerator RunPressureContracts(NetworkObject bear, NetworkObject mouse)
    {
        deviceReport.phase = "V01/V02/V03: actual pressure contact, occupancy, and moving wall reversal";
        SaveDeviceReport();
        PcsPuzzleDevice plate = DeviceFind(PcsDeviceKind.PressurePlate, 1);
        PcsPuzzleDevice wall = plate.Links[0];
        DropActorOnSupport(bear, plate.Solid);
        yield return DeviceWait(0.65f);
        DeviceAssert("V01-bear-physical-top-contact", plate.PressureButton.IsPressed && testDirector.States[plate.DeviceId].Active == 1,
            "Actual dynamic Bear falls 0.05U onto serialized support; overlap plus enabled top contact must press.");
        yield return DeviceWaitPosition(wall, wall.UpperStop.position);
        DeviceAssert("V01-wall-opens-completely", Vector2.Distance(testDirector.States[wall.DeviceId].Position, wall.UpperStop.position) < 0.03f,
            "Continued physical occupancy moves the real wall to its open stop.");
        NetworkObject secondBear = DeviceActor(MyEnum.CharacterType.Bear);
        DropActorOnSupport(bear, plate.Solid, -0.35f);
        DropActorOnSupport(secondBear, plate.Solid, 0.35f);
        yield return DeviceWait(0.65f);
        DeviceAssert("V02-two-bears-occupy", plate.PressureButton.IsPressed, "Two real spawned Bear colliders stand on the support; this is a contact-count fixture, not two network peers.");
        PlaceDeviceActor(bear, new Vector2(-90f, 0f));
        yield return DeviceWait(0.25f);
        DeviceAssert("V02-one-remains-held", plate.PressureButton.IsPressed && testDirector.States[wall.DeviceId].Active == 1,
            "Removing the first actor must not release while the second actor still has top contact.");
        Vector2 openPosition = testDirector.States[wall.DeviceId].Position;
        secondBear.GetComponent<Collider2D>().enabled = false;
        yield return DeviceWait(0.25f);
        DeviceAssert("V02-disabled-last-collider-releases", !plate.PressureButton.IsPressed && testDirector.States[wall.DeviceId].Position.y < openPosition.y - 0.05f,
            "Disabling the final collider releases without relying on an exit callback; wall starts closing.");
        PlaceDeviceActor(secondBear, new Vector2(-95f, 0f));
        secondBear.GetComponent<Collider2D>().enabled = true;
        DropActorOnSupport(bear, plate.Solid);
        yield return DeviceWait(0.25f);
        Vector2 rising = testDirector.States[wall.DeviceId].Position;
        yield return DeviceWait(0.2f);
        DeviceAssert("V02-close-to-open-reversal", testDirector.States[wall.DeviceId].Position.y > rising.y + 0.02f,
            "Reoccupying mid-close reverses toward open from the current position.");
        PlaceDeviceActor(bear, new Vector2(-90f, 0f));
        yield return DeviceWaitPosition(wall, wall.InitialPosition);
        Bounds support = plate.Solid.bounds;
        PlaceDeviceActor(bear, new Vector2(support.max.x + 0.3f, support.max.y - 0.55f));
        yield return DeviceWait(0.2f);
        DeviceAssert("V03-side-contact-rejected", !plate.PressureButton.IsPressed, "Side setup puts Bear beside support below its top; side contact/sensor proximity must not hold the wall.");
        PlaceDeviceActor(bear, new Vector2(support.center.x, support.min.y - 1.02f));
        yield return DeviceWait(0.2f);
        DeviceAssert("V03-below-contact-rejected", !plate.PressureButton.IsPressed, "An actor below the support is not a standing top contact.");
        PlaceDeviceActor(bear, new Vector2(support.center.x, support.max.y + 0.25f));
        yield return DeviceWait(0.2f);
        DeviceAssert("V03-airborne-proximity-rejected", !plate.PressureButton.IsPressed, "A frozen airborne fixture above the plate cannot substitute for a responding top contact.");
        PlaceDeviceActor(bear, new Vector2(-90f, 0f));
        DropActorOnSupport(mouse, plate.Solid);
        yield return DeviceWait(0.5f);
        DeviceAssert("V03-wrong-role-on-support-rejected", !plate.PressureButton.IsPressed, "Real Mouse grounding on the support does not satisfy the Bear-only pressure rule.");
        PlaceDeviceActor(mouse, new Vector2(-85f, 0f));
        PlaceDeviceActor(secondBear, new Vector2(-95f, 0f));
        deviceRunner.SetPlayerObject(deviceRunner.LocalPlayer, bear);
    }

    private static void PlaceDummyFixture(PcsPuzzleDevice dummy, Vector2 position)
    {
        PcsDeviceState state = testDirector.States[dummy.DeviceId];
        state.Position = position;
        state.Velocity = Vector2.zero;
        state.Actor = default;
        state.Phase = 0;
        state.Active = 1;
        testDirector.States.Set(dummy.DeviceId, state);
        dummy.ApplyPose(position);
        Physics2D.SyncTransforms();
    }

    private static IEnumerator RunDummyContracts(NetworkObject bear)
    {
        deviceReport.phase = "Bear tutorial actual kinematic dummy contacts and two distinct ally throws";
        SaveDeviceReport();
        testDirector.RequestAction(bear, PcsPuzzleAction.Reset, -1, Vector2.zero);
        yield return DeviceWait(testDirector.ResetDelay + 0.1f);
        var dummies = new List<PcsPuzzleDevice>();
        var plates = new List<PcsPuzzleDevice>();
        var buttons = new List<PcsPuzzleDevice>();
        foreach (PcsPuzzleDevice device in testDirector.Devices)
        {
            if (device == null || device.Section != 0 || device.RequiredRole != MyEnum.CharacterType.Bear) continue;
            if (device.Kind == PcsDeviceKind.Dummy) dummies.Add(device);
            if (device.Kind == PcsDeviceKind.PressurePlate) plates.Add(device);
            if (device.Kind == PcsDeviceKind.RemoteButton && device.AcceptDummyOnly) buttons.Add(device);
        }
        if (dummies.Count != 2 || plates.Count != 2 || buttons.Count != 2)
            throw new InvalidOperationException("Bear tutorial needs exactly two configured allies, plates and receivers.");
        dummies.Sort((a, b) => a.InitialPosition.x.CompareTo(b.InitialPosition.x));
        plates.Sort((a, b) => a.InteractionPoint.x.CompareTo(b.InteractionPoint.x));
        buttons.Sort((a, b) => a.InteractionPoint.x.CompareTo(b.InteractionPoint.x));
        PcsPuzzleDevice bridge = plates[0].Links[0];
        PlaceDeviceActor(bear, new Vector2(-90f, 0f));
        PlaceDummyFixture(dummies[0], new Vector2(plates[0].Solid.bounds.center.x, plates[0].Solid.bounds.max.y + dummies[0].BodySize.y * 0.5f + 0.05f));
        yield return DeviceWait(0.7f);
        DeviceAssert("dummy-kinematic-top-contact", plates[0].PressureButton.IsPressed && testDirector.States[bridge.DeviceId].Active == 1 && bridge.Solid.enabled,
            "Existing dummy with full kinematic contacts falls through actual Director ticks onto the actual support; top contact must open bridge.");
        PlaceDummyFixture(dummies[1], new Vector2(plates[1].Solid.bounds.center.x, plates[1].Solid.bounds.max.y + dummies[1].BodySize.y * 0.5f + 0.05f));
        yield return DeviceWait(0.7f);
        PlaceDummyFixture(dummies[0], dummies[0].InitialPosition);
        yield return DeviceWait(0.3f);
        DeviceAssert("dummy-two-plates-or-handoff", !plates[0].PressureButton.IsPressed && plates[1].PressureButton.IsPressed && testDirector.States[bridge.DeviceId].Active == 1,
            "Removing the first ally preserves the bridge while the other ally stands on the second plate.");
        PlaceDummyFixture(dummies[1], new Vector2(-27f, dummies[1].InitialPosition.y));
        yield return DeviceWait(0.3f);
        DeviceAssert("dummy-last-plate-release", !plates[0].PressureButton.IsPressed && !plates[1].PressureButton.IsPressed && testDirector.States[bridge.DeviceId].Active == 0,
            "With both actual contacts gone the bridge folds; no stale plate assignment can hold it.");
        float floor = plates[1].Solid.bounds.max.y - 0.12f;
        PlaceDeviceActor(bear, new Vector2(-28.5f, floor + 0.03f));
        PlaceDummyFixture(dummies[0], new Vector2(-28.1f, floor + dummies[0].BodySize.y * 0.5f + 0.03f));
        yield return DeviceWait(0.1f);
        testDirector.RequestAction(bear, PcsPuzzleAction.Carry, dummies[0].DeviceId, dummies[0].InteractionPoint);
        yield return DeviceWait(0.2f);
        DeviceAssert("dummy-real-carry-request", testDirector.States[dummies[0].DeviceId].Phase == 1 && testDirector.States[dummies[0].DeviceId].Actor == bear.Id,
            "Normal G request attaches the configured first ally; fixture no longer drives its position.");
        TraceDummyLaunch("first-ally", bear, dummies[0], buttons[0]);
        testDirector.RequestAction(bear, PcsPuzzleAction.Throw, -1, buttons[0].InteractionPoint);
        yield return ObserveDummyFlight("first-ally", dummies[0], buttons[0]);
        DeviceAssert("dummy-first-ally-throw", testDirector.States[buttons[0].DeviceId].Active == 1 && testDirector.States[dummies[0].DeviceId].Phase == 4 &&
            testDirector.States[buttons[1].DeviceId].Active == 0, "A real ballistic ally throw activates and records only the first receiver, consuming that ally.");
        yield return DeviceWait(testDirector.ThrowCooldown + 0.1f);
        testDirector.RequestAction(bear, PcsPuzzleAction.Throw, -1, buttons[1].InteractionPoint);
        yield return DeviceWait(1f);
        DeviceAssert("dummy-receiver-rejects-rock", testDirector.States[buttons[1].DeviceId].Active == 0,
            "A normal stone after the first ally has been consumed cannot satisfy the second ally-only receiver.");
        PlaceDummyFixture(dummies[1], new Vector2(-28.1f, floor + dummies[1].BodySize.y * 0.5f + 0.03f));
        yield return DeviceWait(0.1f);
        testDirector.RequestAction(bear, PcsPuzzleAction.Carry, dummies[1].DeviceId, dummies[1].InteractionPoint);
        yield return DeviceWait(0.2f);
        DeviceAssert("dummy-second-carry-request", testDirector.States[dummies[1].DeviceId].Phase == 1 && testDirector.States[dummies[1].DeviceId].Actor == bear.Id,
            "The second distinct ally is actually held before its F input.");
        TraceDummyLaunch("second-ally", bear, dummies[1], buttons[1]);
        testDirector.RequestAction(bear, PcsPuzzleAction.Throw, -1, buttons[1].InteractionPoint);
        yield return ObserveDummyFlight("second-ally", dummies[1], buttons[1]);
        DeviceAssert("dummy-second-distinct-ally-throw", testDirector.States[buttons[0].DeviceId].Active == 1 && testDirector.States[buttons[1].DeviceId].Active == 1 &&
            testDirector.States[buttons[0].DeviceId].Counter != testDirector.States[buttons[1].DeviceId].Counter && testDirector.States[dummies[1].DeviceId].Phase == 4,
            "Second receiver records a different consumed ally ID after normal carry and throw requests.");
    }

    private static void TraceDummyLaunch(string label, NetworkObject carrier, PcsPuzzleDevice dummy, PcsPuzzleDevice receiver)
    {
        PcsDeviceState state = testDirector.States[dummy.DeviceId];
        MethodInfo throwVelocity = typeof(PcsPuzzleDirector).GetMethod("ThrowVelocity", BindingFlags.Instance | BindingFlags.NonPublic);
        Vector2 velocity = (Vector2)throwVelocity.Invoke(testDirector, new object[] { state.Position, receiver.InteractionPoint });
        Vector2 delta = (velocity + Physics2D.gravity * deviceRunner.DeltaTime) * deviceRunner.DeltaTime;
        RaycastHit2D hit = Physics2D.BoxCast(state.Position, dummy.BodySize, 0f, delta.normalized, delta.magnitude, testDirector.ObstructionMask);
        Lifter lifter = carrier.GetComponent<Lifter>();
        deviceReport.diagnostics.Add(label + " launch: dummyPhase=" + state.Phase + ", position=" + state.Position +
            ", size=" + dummy.BodySize + ", velocity=" + velocity + ", aim=" + receiver.InteractionPoint +
            ", receiverBounds=" + receiver.Bounds + ", carrierHeadTop=" + (lifter != null && lifter.Head != null ? lifter.Head.Top.ToString("F4") : "none") +
            ", dummyBottom=" + (state.Position.y - dummy.BodySize.y * 0.5f).ToString("F4") +
            ", nextBoxCast=" + (hit.collider != null ? hit.collider.name : "none") + ", distance=" + hit.distance.ToString("F4"));
        SaveDeviceReport();
    }

    private static IEnumerator ObserveDummyFlight(string label, PcsPuzzleDevice dummy, PcsPuzzleDevice receiver)
    {
        float end = Time.time + 2f;
        float sampleAt = -1f;
        int phase = -1;
        while (testDirector.States[receiver.DeviceId].Active == 0 && Time.time < end)
        {
            PcsDeviceState state = testDirector.States[dummy.DeviceId];
            if (state.Phase != phase || Time.time >= sampleAt)
            {
                phase = state.Phase;
                sampleAt = Time.time + 0.15f;
                deviceReport.diagnostics.Add(label + " flight: tick=" + deviceRunner.Tick.Raw + ", phase=" + state.Phase +
                    ", position=" + state.Position + ", velocity=" + state.Velocity + ", receiverActive=" + testDirector.States[receiver.DeviceId].Active);
                SaveDeviceReport();
            }
            yield return null;
        }
        PcsDeviceState final = testDirector.States[dummy.DeviceId];
        deviceReport.diagnostics.Add(label + " final: phase=" + final.Phase + ", position=" + final.Position +
            ", receiverActive=" + testDirector.States[receiver.DeviceId].Active + ", receiverAllyId=" + testDirector.States[receiver.DeviceId].Counter);
        SaveDeviceReport();
    }

    private static IEnumerator RunStageOnePlateShots(NetworkObject bear)
    {
        deviceReport.phase = "1-1 actual pressure plate to right enemy ballistic route";
        SaveDeviceReport();
        PcsPuzzleDevice plate = DeviceFind(PcsDeviceKind.PressurePlate, 1);
        PcsPuzzleDevice enemy = DeviceFind(PcsDeviceKind.Enemy, 1);
        // Keep the actual authored target pose; only patrol motion is frozen for repeatability.
        float originalSpeed = enemy.Speed;
        enemy.Speed = 0f;
        PcsDeviceState target = testDirector.States[enemy.DeviceId];
        target.Position = enemy.InitialPosition;
        target.Active = 1;
        target.Phase = 0;
        target.Counter = enemy.Health;
        testDirector.States.Set(enemy.DeviceId, target);
        DropActorOnSupport(bear, plate.Solid);
        yield return DeviceWait(0.5f);
        DeviceAssert("stage-one-shot-plate-held", plate.PressureButton.IsPressed, "Actual Bear stands on the authored pressure plate for both shots.");
        TraceStoneObstruction(bear, enemy.InteractionPoint);
        PlayerInput input = bear.GetComponent<PlayerInput>();
        input.InjectDevelopmentInput(Vector2.zero, false, ability: true, aim: enemy.InteractionPoint);
        yield return DeviceWait(1.8f);
        DeviceAssert("stage-one-plate-first-stone-hits", testDirector.States[enemy.DeviceId].Counter == enemy.Health - 1,
            "Actual F input from the pressure support aims at the authored right enemy. Health=" + testDirector.States[enemy.DeviceId].Counter + "; expected=" + (enemy.Health - 1) + ".");
        input.InjectDevelopmentInput(Vector2.zero, false, ability: true, aim: enemy.InteractionPoint);
        yield return DeviceWait(1.8f);
        DeviceAssert("stage-one-plate-second-stone-defeats", testDirector.States[enemy.DeviceId].Phase == 4 && plate.PressureButton.IsPressed,
            "Second actual F input defeats the same target while Bear continues holding the wall plate.");
        enemy.Speed = originalSpeed;
        SuppressDeviceEnemies();
        PlaceDeviceActor(bear, new Vector2(-90f, 0f));
    }

    private static void TraceStoneObstruction(NetworkObject actor, Vector2 aim)
    {
        Vector2 origin = (Vector2)actor.transform.position + Vector2.up * 0.6f;
        Vector2 position = origin + (aim - origin).normalized * 0.45f;
        Vector2 velocity = (Vector2)typeof(PcsPuzzleDirector).GetMethod("ThrowVelocity", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(testDirector, new object[] { position, aim });
        string first = "none";
        for (float elapsed = 0f; elapsed < testDirector.ProjectileLifetime; elapsed += deviceRunner.DeltaTime)
        {
            velocity += Physics2D.gravity * deviceRunner.DeltaTime;
            Vector2 next = position + velocity * deviceRunner.DeltaTime;
            RaycastHit2D hit = Physics2D.Linecast(position, next, testDirector.ObstructionMask);
            if (hit.collider != null)
            {
                first = hit.collider.name + " at " + hit.point + ", t=" + elapsed.ToString("F3");
                break;
            }
            position = next;
        }
        deviceReport.diagnostics.Add("StageOne actual-collider trajectory forecast: origin=" + origin + ", aim=" + aim + ", first Ground obstruction=" + first +
            ". This forecasts the production discrete trajectory against the live Scene; the following real input/damage assertions determine success.");
        SaveDeviceReport();
    }

    private static PcsPuzzleDevice TutorialDevice(PcsDeviceKind kind, MyEnum.CharacterType role)
    {
        foreach (PcsPuzzleDevice device in testDirector.Devices)
            if (device != null && device.Section == 0 && device.Kind == kind && device.RequiredRole == role) return device;
        throw new InvalidOperationException("Missing tutorial contract: " + role + "/" + kind);
    }

    private static IEnumerator RunTutorialCarryCompletion(NetworkObject frog)
    {
        deviceReport.phase = "Rabbit normal G drop, other-actor rejection, and Frog completion return recovery";
        SaveDeviceReport();
        NetworkObject rabbit = DeviceActor(MyEnum.CharacterType.Rabbit);
        NetworkObject anotherRabbit = DeviceActor(MyEnum.CharacterType.Rabbit);
        testDirector.RequestAction(rabbit, PcsPuzzleAction.Reset, -1, Vector2.zero);
        yield return DeviceWait(testDirector.ResetDelay + 0.15f);
        PcsPuzzleDevice rabbitAlly = TutorialDevice(PcsDeviceKind.Dummy, MyEnum.CharacterType.Rabbit);
        PlaceDeviceActor(rabbit, rabbitAlly.InitialPosition + new Vector2(-0.45f, -0.4f));
        PlayerInput rabbitInput = rabbit.GetComponent<PlayerInput>();
        rabbitInput.InjectDevelopmentInput(Vector2.zero, false, carry: true);
        yield return DeviceWait(0.25f);
        bool held = testDirector.States[rabbitAlly.DeviceId].Phase == 1 && testDirector.States[rabbitAlly.DeviceId].Actor == rabbit.Id;
        DeviceAssert("rabbit-dummy-G-carry", held, "Normal Rabbit G input carries the actual room ally, using local selection and authority validation.");
        float heldDistance = Vector2.Distance(rabbit.transform.position, rabbitAlly.InteractionPoint);
        PlaceDeviceActor(anotherRabbit, rabbitAlly.InteractionPoint + Vector2.right * 0.3f);
        anotherRabbit.GetComponent<PlayerInput>().InjectDevelopmentInput(Vector2.zero, false, carry: true);
        yield return DeviceWait(0.15f);
        DeviceAssert("other-actor-cannot-drop-held-dummy", held && testDirector.States[rabbitAlly.DeviceId].Phase == 1 && testDirector.States[rabbitAlly.DeviceId].Actor == rabbit.Id,
            "Another nearby Rabbit's G input cannot detach an ally owned by the first actor.");
        PlaceDeviceActor(anotherRabbit, new Vector2(-95f, 0f));
        rabbitInput.InjectDevelopmentInput(Vector2.zero, false, carry: true);
        yield return DeviceWait(0.15f);
        DeviceAssert("rabbit-owned-dummy-G-drop", held && testDirector.States[rabbitAlly.DeviceId].Phase == 0 && testDirector.States[rabbitAlly.DeviceId].Actor != rabbit.Id,
            "Normal holder G must release even when safe carry height exceeds pickup range. Held root distance=" + heldDistance.ToString("F3") + ", pickup range=" + rabbitAlly.InteractionRange.ToString("F3") + ".");
        PlaceDeviceActor(rabbit, new Vector2(-92f, 0f));

        deviceRunner.SetPlayerObject(deviceRunner.LocalPlayer, frog);
        testDirector.RequestAction(frog, PcsPuzzleAction.Reset, -1, Vector2.zero);
        yield return DeviceWait(testDirector.ResetDelay + 0.15f);
        PcsPuzzleDevice frogAlly = TutorialDevice(PcsDeviceKind.Dummy, MyEnum.CharacterType.Frog);
        PcsPuzzleDevice frogFinish = TutorialDevice(PcsDeviceKind.TutorialExit, MyEnum.CharacterType.Frog);
        PcsPuzzleDevice anchor = TutorialDevice(PcsDeviceKind.Anchor, MyEnum.CharacterType.Frog);
        PcsPuzzleDevice remote = TutorialDevice(PcsDeviceKind.RemoteButton, MyEnum.CharacterType.Frog);
        PlaceDeviceActor(frog, frogAlly.InitialPosition + new Vector2(-2.5f, -0.4f));
        Vector2 allyBefore = testDirector.States[frogAlly.DeviceId].Position;
        PlayerInput frogInput = frog.GetComponent<PlayerInput>();
        frogInput.InjectDevelopmentInput(Vector2.zero, false, ability: true, aim: frogAlly.InteractionPoint);
        yield return DeviceWait(0.8f);
        DeviceAssert("frog-tutorial-F-retrieves-ally", testDirector.States[frogAlly.DeviceId].Counter > 0 &&
            Vector2.Distance(testDirector.States[frogAlly.DeviceId].Position, frog.transform.position) < Vector2.Distance(allyBefore, frog.transform.position) - 0.5f,
            "Actual Frog F input records retrieval and brings the real practice ally closer.");
        if (testDirector.States[frogAlly.DeviceId].Phase != 1)
        {
            frogInput.InjectDevelopmentInput(Vector2.zero, false, carry: true);
            yield return DeviceWait(0.25f);
        }
        DeviceAssert("frog-retrieved-ally-can-be-carried", testDirector.States[frogAlly.DeviceId].Phase == 1 && testDirector.States[frogAlly.DeviceId].Actor == frog.Id,
            "After the required remote retrieval, normal G may finish attaching the nearby ally.");
        DeviceRequest(frog, PcsPuzzleAction.Remote, anchor);
        yield return DeviceWait(0.15f);
        DeviceRequest(frog, PcsPuzzleAction.Remote, remote);
        yield return DeviceWait(0.15f);
        PlaceDeviceActor(frog, new Vector2(frogFinish.Bounds.center.x, frogFinish.Bounds.min.y + 0.4f));
        yield return DeviceWait(0.25f);
        bool completed = (testDirector.CompletedTutorialMask & (1 << (int)MyEnum.CharacterType.Frog)) != 0;
        DeviceAssert("frog-tutorial-completion-and-ally-retirement", completed && testDirector.States[frogFinish.DeviceId].Active == 1 &&
            testDirector.States[frogAlly.DeviceId].Phase == 4 && testDirector.States[frogAlly.DeviceId].Actor != frog.Id,
            "Actual completion requires prior anchor/button/retrieval records and both actors inside the finish bounds; completed practice ally is retired.");
        int beforeReturnEpoch = testDirector.GetResetEpoch(MyEnum.CharacterType.Frog);
        // Shorten only return travel setup, then let the production fall/reset check run normally.
        PlaceDeviceActor(frog, new Vector2(-18.4f, -2.9f));
        yield return DeviceWait(0.6f);
        DeviceAssert("frog-completed-return-does-not-reset-room", completed && testDirector.GetResetEpoch(MyEnum.CharacterType.Frog) == beforeReturnEpoch &&
            (testDirector.CompletedTutorialMask & (1 << (int)MyEnum.CharacterType.Frog)) != 0,
            "At the real lower return elevation, the rescued ally cannot trigger the old InitialY-12 recovery and revoke completed T03.");
    }

    private static void DeviceAssert(string id, bool passed, string detail)
    {
        deviceReport.checks.Add(new DeviceCheck { id = id, status = passed ? "PASS" : "FAIL", detail = detail });
        SaveDeviceReport();
    }

    private static void CaptureDeviceTestLog(string message, string stack, UnityEngine.LogType type)
    {
        if (deviceReport == null || (type != UnityEngine.LogType.Error && type != UnityEngine.LogType.Exception && type != UnityEngine.LogType.Assert)) return;
        string safe = System.Text.RegularExpressions.Regex.Replace(message, "(?i)(appid|token|password|secret)\\s*[:=]\\s*\\S+", "$1=<redacted>");
        if (deviceReport.errors.Count < 30) deviceReport.errors.Add(safe);
    }

    private static async void FinishDeviceTests(Exception exception)
    {
        if (deviceFinishing) return;
        deviceFinishing = true;
        if (exception != null) deviceReport.errors.Add(exception.ToString());
        bool failed = exception != null || deviceReport.errors.Count > 0 || deviceReport.checks.Exists(x => x.status == "FAIL");
        deviceReport.status = failed ? "FAIL" : "PASS";
        deviceReport.finishedUtc = DateTime.UtcNow.ToString("O");
        deviceReport.phase = "Shutting down test Runner; Play-only fixtures will be discarded";
        SaveDeviceReport();
        SessionState.SetString(DeviceTestFlag, "restore");
        Application.logMessageReceived -= CaptureDeviceTestLog;
        try { if (deviceRunner != null) await deviceRunner.Shutdown(); }
        catch (Exception ex)
        {
            deviceReport.errors.Add("Runner shutdown failed: " + ex.Message);
            deviceReport.status = "FAIL";
            SaveDeviceReport();
        }
        EditorApplication.ExitPlaymode();
    }

    private static void SaveDeviceReport()
    {
        if (deviceReport == null) return;
        deviceReport.heartbeatUtc = DateTime.UtcNow.ToString("O");
        Directory.CreateDirectory(EvidenceDirectory);
        File.WriteAllText(Path.Combine(EvidenceDirectory, "device-tests.json"), JsonUtility.ToJson(deviceReport, true));
    }
}

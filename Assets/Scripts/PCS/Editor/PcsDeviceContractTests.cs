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
        public string boundary = "Actual registered Puzzle director and Runner.Spawn player prefabs; requests traverse RequestAction and the authority RPC. Play-only fixtures select a section, position actors near devices, freeze actors except measured pressure/lift passengers, suppress enemies except the measured live chase or stationary damage target, waive four-role formation only after checking rejection, seed power/batteries, drop existing dummies above pressure supports, and temporarily shorten final travel. Actual ticks, physics contacts, throws and damage run after setup. This tests device contracts, not route traversal, multiplayer synchronization, lobby admission, or four-player completion. No Scene is saved.";
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
        SessionState.SetString(DeviceModeFlag, "Single");
        BeginDeviceContractsCore();
    }

    public static void BeginSharedDeviceContractTests()
    {
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
            if (device == null || device.Kind != PcsDeviceKind.Enemy) continue;
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

        DeviceFixture("ActiveSection", 2);
        testDirector.RequestAction(mouse, PcsPuzzleAction.Reset, -1, Vector2.zero);
        yield return DeviceWait(testDirector.ResetDelay + 0.1f);
        SuppressDeviceEnemies();
        PcsPuzzleDevice shaft = DeviceFind(PcsDeviceKind.Elevator, 2);
        PcsPuzzleDevice start = DeviceFind(PcsDeviceKind.HackConsole, 2);
        PcsPuzzleDevice console = DeviceFind(PcsDeviceKind.ChannelConsole, 2);
        PcsPuzzleDevice orange = DeviceFind(PcsDeviceKind.RemoteButton, 2, 0);
        PcsPuzzleDevice green = DeviceFind(PcsDeviceKind.RemoteButton, 2, 1);
        PcsPuzzleDevice orangePlatform = orange.Links[0];
        Vector3 orangeRootScale = orangePlatform.transform.localScale;
        BoxCollider2D orangeShape = orangePlatform.Solid as BoxCollider2D;
        Vector2 orangeColliderSize = orangeShape != null ? orangeShape.size : Vector2.zero;
        Vector2 orangeColliderOffset = orangeShape != null ? orangeShape.offset : Vector2.zero;
        foreach (PcsPuzzleDevice barrier in testDirector.Devices)
        {
            if (barrier == null || !barrier.BlocksShaft) continue;
            PcsDeviceState state = testDirector.States[barrier.DeviceId];
            state.Active = 1;
            testDirector.States.Set(barrier.DeviceId, state);
        }
        deviceReport.phase = "1-2 start hack, D22 independent channels, energy, refill, endpoint, and reset";
        SaveDeviceReport();
        yield return DevicePulseHack(mouse, console);
        DeviceAssert("channel-console-cannot-start-shaft", !testDirector.ShaftStarted, "E pulses on ChannelConsole cannot bypass the separate start hack.");
        yield return DevicePulseHack(mouse, start);
        DeviceAssert("shaft-rejects-incomplete-formation", !testDirector.ShaftStarted, "The actual final hack pulse rejects a team without all three left roles and Rabbit on the right.");
        testDirector.RequireShaftBoarding = false;
        CapsuleCollider2D bearCapsule = bear.GetComponent<CapsuleCollider2D>();
        float bearFoot = (bearCapsule.offset.y - bearCapsule.size.y * 0.5f) * bear.transform.lossyScale.y;
        PlaceDeviceActor(bear, new Vector2(shaft.Solid.bounds.center.x - 1.5f, shaft.Solid.bounds.max.y - bearFoot + 0.03f));
        bear.GetComponent<Rigidbody2D>().constraints = RigidbodyConstraints2D.FreezeRotation;
        yield return DeviceWait(0.3f);
        float riderBefore = bear.GetComponent<Rigidbody2D>().position.y;
        float platformBefore = testDirector.States[shaft.DeviceId].Position.y;
        yield return DevicePulseHack(mouse, start);
        DeviceAssert("shaft-start-hack", testDirector.ShaftStarted, "Only the actual start HackConsole starts continuous ascent.");
        yield return DeviceWait(0.5f);
        float riderRise = bear.GetComponent<Rigidbody2D>().position.y - riderBefore;
        float platformRise = testDirector.States[shaft.DeviceId].Position.y - platformBefore;
        DeviceAssert("moving-lift-carries-normal-player", platformRise > 0.2f && Mathf.Abs(riderRise - platformRise) < 0.16f,
            "During measurement only actual Director/PcsElevator and normal Mover run; platform rise=" + platformRise.ToString("F3") + ", passenger rise=" + riderRise.ToString("F3") + ". Setup pose was placed on the lift; no test transform writes occur during measurement.");
        PlaceDeviceActor(bear, new Vector2(-90f, 0f));
        int initialPower = testDirector.Energy;
        DeviceRequest(frog, PcsPuzzleAction.Remote, orange);
        yield return DeviceWait(0.12f);
        DeviceAssert("channel-rejects-input-before-window", testDirector.States[orange.DeviceId].Active == 0, "Frog button input before activation does not persist into the next window.");
        DeviceRequest(mouse, PcsPuzzleAction.ActivateChannel, console, 0);
        yield return DeviceWait(0.12f);
        int window = testDirector.Channels[0].Window;
        DeviceAssert("channel-opens-without-prelatched-platform", testDirector.Channels[0].Active == 1 && testDirector.Energy == initialPower - testDirector.ChannelCost &&
            testDirector.States[orange.Links[0].DeviceId].Active == 0, "Opening a window costs energy once; a new physical ability input is still required.");
        DeviceRequest(frog, PcsPuzzleAction.Remote, orange);
        yield return DeviceWait(0.12f);
        DeviceAssert("channel-valid-input-enables-platform", testDirector.States[orange.DeviceId].Active == 1 && testDirector.States[orange.Links[0].DeviceId].Active == 1,
            "Actual Frog remote RPC enables the button's linked platform in this window.");
        SpriteRenderer orangeVisual = orangePlatform.Visuals.Length > 0 ? orangePlatform.Visuals[0] : null;
        bool childVisual = orangeVisual != null && orangeVisual.transform != orangePlatform.transform &&
            orangeVisual.transform.parent == orangePlatform.transform;
        DeviceAssert("D22-active-unfolds-child-and-enables-root-collider", childVisual &&
            Mathf.Abs(orangeVisual.transform.localScale.x - 1f) < 0.001f && orangeShape != null && orangeShape.enabled &&
            orangeShape.transform == orangePlatform.transform && orangePlatform.transform.localScale == orangeRootScale &&
            orangeShape.size == orangeColliderSize && orangeShape.offset == orangeColliderOffset,
            "Actual channel plus Frog input unfolds only the authored sprite child to x=1 and enables the existing root collider without changing its scale, size or offset.");
        int usedPower = testDirector.Energy;
        DeviceRequest(mouse, PcsPuzzleAction.ActivateChannel, console, 0);
        yield return DeviceWait(0.1f);
        DeviceAssert("channel-rearm-rejected-while-active", testDirector.Channels[0].Window == window && testDirector.Energy == usedPower, "Duplicate active-channel request neither spends energy nor extends the window.");
        yield return DeviceWait(0.6f);
        DeviceRequest(mouse, PcsPuzzleAction.ActivateChannel, console, 1);
        yield return DeviceWait(0.1f);
        DeviceRequest(frog, PcsPuzzleAction.Remote, green);
        yield return DeviceWait(0.1f);
        DeviceAssert("channels-have-independent-deadlines", testDirector.ChannelRemaining(1) > testDirector.ChannelRemaining(0) + 0.4f &&
            testDirector.States[green.Links[0].DeviceId].Active == 1, "Later green activation has its own deadline and linked platform.");
        float deadline = Time.time + testDirector.ChannelDuration + 0.5f;
        while (testDirector.Channels[0].Active != 0 && Time.time < deadline) yield return null;
        DeviceAssert("one-channel-expires-independently", testDirector.States[orange.Links[0].DeviceId].Active == 0 && testDirector.Channels[1].Active == 1 &&
            testDirector.States[green.Links[0].DeviceId].Active == 1, "Orange expiry folds only orange while green remains live.");
        DeviceAssert("D22-expiry-folds-only-child-and-disables-root-collider", childVisual &&
            Mathf.Abs(orangeVisual.transform.localScale.x - 0.12f) < 0.001f && orangeShape != null && !orangeShape.enabled &&
            orangeShape.transform == orangePlatform.transform && orangePlatform.transform.localScale == orangeRootScale &&
            orangeShape.size == orangeColliderSize && orangeShape.offset == orangeColliderOffset && green.Links[0].Solid.enabled,
            "Natural Orange expiry folds its sprite child to x=0.12 and disables collision while preserving the root transform/collider geometry; the later Green collider remains enabled.");
        DeviceRequest(mouse, PcsPuzzleAction.ActivateChannel, console, 0);
        yield return DeviceWait(0.12f);
        DeviceAssert("new-window-requires-new-input", testDirector.Channels[0].Window == window + 1 && testDirector.States[orange.Links[0].DeviceId].Active == 0,
            "Reactivation increments Window and does not reuse the previous button hit.");
        DeviceRequest(frog, PcsPuzzleAction.Remote, orange);
        yield return DeviceWait(0.12f);
        DeviceAssert("new-input-accepted-in-new-window", testDirector.States[orange.Links[0].DeviceId].Active == 1, "A new Frog request activates the new window.");
        DeviceFixture("Energy", 0);
        Vector2 liftBefore = testDirector.States[shaft.DeviceId].Position;
        epoch = testDirector.ResetEpoch;
        DeviceRequest(mouse, PcsPuzzleAction.ActivateChannel, console, 2);
        yield return DeviceWait(0.6f);
        DeviceAssert("zero-power-blocks-only-new-channel", testDirector.Energy == 0 && testDirector.Channels[2].Active == 0 && testDirector.ShaftStarted &&
            testDirector.ResetEpoch == epoch && testDirector.States[shaft.DeviceId].Position.y > liftBefore.y + 0.1f, "With a zero-energy fixture, new purple activation is rejected while the actual lift continues rising.");
        DeviceFixture("Batteries", 2);
        DeviceRequest(mouse, PcsPuzzleAction.Refill, console);
        yield return DeviceWait(0.12f);
        DeviceAssert("battery-refill", testDirector.Batteries == 1 && testDirector.Energy == console.BatteryValue, "R consumes one carried battery charge and restores the configured power amount.");
        usedPower = testDirector.Energy;
        DeviceRequest(mouse, PcsPuzzleAction.Refill, console);
        yield return DeviceWait(0.12f);
        DeviceAssert("refill-cooldown", testDirector.Batteries == 1 && testDirector.Energy == usedPower, "A repeated immediate R request is rejected by the shared cooldown.");
        Vector3 realEnd = shaft.UpperStop.position;
        shaft.UpperStop.position = (Vector3)testDirector.States[shaft.DeviceId].Position + Vector3.up * 0.25f;
        yield return DeviceWait(0.7f);
        Vector2 arrived = testDirector.States[shaft.DeviceId].Position;
        yield return DeviceWait(0.25f);
        DeviceAssert("shaft-stops-only-at-end", testDirector.ShaftArrived && Vector2.Distance(arrived, testDirector.States[shaft.DeviceId].Position) < 0.01f,
            "A temporarily shortened endpoint fixture reaches ShaftArrived and stops; this does not prove full-route traversal.");
        shaft.UpperStop.position = realEnd;
        epoch = testDirector.ResetEpoch;
        testDirector.RequestAction(mouse, PcsPuzzleAction.Reset, -1, Vector2.zero);
        yield return DeviceWait(testDirector.ResetDelay + 0.1f);
        bool channelsClear = true;
        for (int i = 0; i < 4; i++) channelsClear &= testDirector.Channels[i].Active == 0;
        DeviceAssert("shaft-reset-coherent", testDirector.ResetEpoch == epoch + 1 && !testDirector.ShaftStarted && !testDirector.ShaftArrived &&
            testDirector.Energy == testDirector.InitialEnergy && testDirector.Batteries == 0 && channelsClear &&
            Vector2.Distance(testDirector.States[shaft.DeviceId].Position, shaft.LowerStop.position) < 0.03f, "Reset restores lift, energy, batteries, all channel windows, and the new actor reset epoch together.");
        yield return RunEnemyBodyContracts(mouse, bear, frog);
        yield return RunEnemyContracts(bear, frog);
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

    private static IEnumerator RunEnemyContracts(NetworkObject bear, NetworkObject frog)
    {
        deviceReport.phase = "Actual Bear projectiles, enemy health, and battery drop";
        SaveDeviceReport();
        SuppressDeviceEnemies();
        PcsPuzzleDevice enemy = DeviceFind(PcsDeviceKind.Enemy, 2);
        PcsPuzzleDevice battery = enemy.Links[0];
        float speed = enemy.Speed;
        enemy.Speed = 0f;
        PcsDeviceState enemyState = testDirector.States[enemy.DeviceId];
        enemyState.Phase = 0;
        enemyState.Active = 1;
        enemyState.Counter = enemy.Health;
        enemyState.Position = enemy.InitialPosition;
        testDirector.States.Set(enemy.DeviceId, enemyState);
        PlaceDeviceActor(bear, enemy.InitialPosition + new Vector2(-3f, -0.5f));
        yield return DeviceWait(0.15f);
        testDirector.RequestAction(bear, PcsPuzzleAction.Throw, -1, enemy.InitialPosition);
        float deadline = Time.time + 2f;
        while (testDirector.States[enemy.DeviceId].Counter == enemy.Health && Time.time < deadline) yield return null;
        DeviceAssert("enemy-first-projectile-damages", testDirector.States[enemy.DeviceId].Counter == enemy.Health - 1 && testDirector.States[enemy.DeviceId].Phase != 4 &&
            testDirector.States[battery.DeviceId].Active == 0, "A real ballistic stone hits the stationary fixture enemy once; battery remains absent before death.");
        yield return DeviceWait(testDirector.ThrowCooldown + 0.1f);
        testDirector.RequestAction(bear, PcsPuzzleAction.Throw, -1, enemy.InitialPosition);
        deadline = Time.time + 2f;
        while (testDirector.States[enemy.DeviceId].Phase != 4 && Time.time < deadline) yield return null;
        DeviceAssert("enemy-second-projectile-spawns-battery", testDirector.States[enemy.DeviceId].Phase == 4 && testDirector.States[battery.DeviceId].Active == 1 &&
            testDirector.States[battery.DeviceId].Phase != 4, "Second normal Bear throw defeats the configured two-hit enemy and activates its actual linked battery.");
        int charges = testDirector.Batteries;
        DeviceRequest(frog, PcsPuzzleAction.Carry, battery);
        yield return DeviceWait(0.15f);
        DeviceAssert("spawned-battery-collects-once", testDirector.States[battery.DeviceId].Phase == 4 && testDirector.Batteries == charges + battery.BatteryValue,
            "A valid G request collects the newly dropped battery through normal authority logic.");
        testDirector.RequestAction(frog, PcsPuzzleAction.Carry, battery.DeviceId, battery.InteractionPoint);
        yield return DeviceWait(0.1f);
        DeviceAssert("consumed-battery-cannot-duplicate", testDirector.Batteries == charges + battery.BatteryValue, "Repeating collection cannot duplicate the consumed battery.");
        enemy.Speed = speed;
        PlaceDeviceActor(bear, new Vector2(-90f, 0f));
        PlaceDeviceActor(frog, new Vector2(-95f, 0f));
    }

    private static IEnumerator RunEnemyBodyContracts(NetworkObject mouse, NetworkObject bear, NetworkObject frog)
    {
        deviceReport.phase = "Authored shaft enemy body collision, ground chase, two stones, and Frog F battery retrieval";
        SaveDeviceReport();
        SuppressDeviceEnemies();
        PcsPuzzleDevice enemy = DeviceFind(PcsDeviceKind.Enemy, 2);
        PcsPuzzleDevice battery = enemy.Links[0];
        PcsDeviceState enemyState = testDirector.States[enemy.DeviceId];
        enemyState.Position = enemy.InitialPosition;
        enemyState.Velocity = Vector2.zero;
        enemyState.Phase = 0;
        enemyState.Active = 1;
        enemyState.Counter = enemy.Health;
        enemyState.Timer = default;
        testDirector.States.Set(enemy.DeviceId, enemyState);
        enemy.ApplyPose(enemyState.Position);
        PcsDeviceState batteryState = testDirector.States[battery.DeviceId];
        batteryState.Active = 0;
        batteryState.Phase = 0;
        batteryState.Actor = default;
        batteryState.Velocity = Vector2.zero;
        testDirector.States.Set(battery.DeviceId, batteryState);
        PlaceDeviceActor(bear, new Vector2(-90f, 0f));
        PlaceDeviceActor(frog, new Vector2(-95f, 0f));
        PlaceDeviceActor(mouse, enemy.InitialPosition + new Vector2(1f, -2f));
        deviceRunner.SetPlayerObject(deviceRunner.LocalPlayer, mouse);
        float enabledDeadline = Time.time + 0.5f;
        while (!enemy.Solid.enabled && Time.time < enabledDeadline) yield return null;
        Physics2D.SyncTransforms();
        float maximumPenetration = 0f;
        string deepestGround = "none";
        int sampleCount = 0;
        int startTick = deviceRunner.Tick.Raw;
        float chaseEnd = Time.time + 0.5f;
        while (Time.time < chaseEnd)
        {
            Physics2D.SyncTransforms();
            float penetration = DeviceGroundPenetration(enemy, out string ground);
            if (penetration > maximumPenetration) { maximumPenetration = penetration; deepestGround = ground; }
            sampleCount++;
            yield return null;
        }
        Physics2D.SyncTransforms();
        float finalPenetration = DeviceGroundPenetration(enemy, out string finalGround);
        if (finalPenetration > maximumPenetration) { maximumPenetration = finalPenetration; deepestGround = finalGround; }
        Vector2 afterChase = testDirector.States[enemy.DeviceId].Position;
        bool simulated = deviceRunner.Tick.Raw > startTick && sampleCount > 1;
        DeviceAssert("enemy-authored-ledge-body-does-not-penetrate", simulated && maximumPenetration <= 0.003f,
            "Actual authored enemy, ledge and full collider chase a registered Mouse 1U to the side and 2U below for 0.5s. " +
            "Enemy speed and ground are unchanged. Maximum penetration=" + maximumPenetration.ToString("F4") +
            "U, obstacle=" + deepestGround + ", position=" + afterChase + ", samples=" + sampleCount + ".");
        DeviceAssert("enemy-floor-contact-preserves-horizontal-chase", simulated && afterChase.x > enemy.InitialPosition.x + 0.05f,
            "The body cannot sink into the ledge, but still advances horizontally toward the lower/side Mouse. Delta=" + (afterChase - enemy.InitialPosition) + ".");
        deviceReport.diagnostics.Add("Enemy body chase: initial=" + enemy.InitialPosition + ", target=" + mouse.transform.position +
            ", after=" + afterChase + ", colliderSize=" + enemy.BodySize + ", speed=" + enemy.Speed + ", maxGroundPenetration=" + maximumPenetration.ToString("F4"));
        SaveDeviceReport();
        float originalSpeed = enemy.Speed;
        enemy.Speed = 0f;
        PlaceDeviceActor(mouse, new Vector2(-85f, 0f));
        PlaceDeviceActor(bear, afterChase + new Vector2(-3f, -0.5f));
        deviceRunner.SetPlayerObject(deviceRunner.LocalPlayer, bear);
        yield return DeviceWait(testDirector.ThrowCooldown + 0.1f);
        bear.GetComponent<PlayerInput>().InjectDevelopmentInput(Vector2.zero, false, ability: true, aim: afterChase);
        float deadline = Time.time + 2f;
        while (testDirector.States[enemy.DeviceId].Counter == enemy.Health && Time.time < deadline) yield return null;
        bool firstDamage = testDirector.States[enemy.DeviceId].Counter == enemy.Health - 1 && testDirector.States[enemy.DeviceId].Phase != 4;
        DeviceAssert("enemy-after-body-chase-first-F-stone", firstDamage && testDirector.States[battery.DeviceId].Active == 0,
            "Actual Bear F damages the enemy at its resulting chase pose; only speed is frozen for the damage phase, not enemy position or the ledge.");
        yield return DeviceWait(testDirector.ThrowCooldown + 0.1f);
        bear.GetComponent<PlayerInput>().InjectDevelopmentInput(Vector2.zero, false, ability: true, aim: afterChase);
        deadline = Time.time + 2f;
        while (testDirector.States[enemy.DeviceId].Phase != 4 && Time.time < deadline) yield return null;
        bool dropped = firstDamage && testDirector.States[enemy.DeviceId].Phase == 4 && testDirector.States[battery.DeviceId].Active == 1;
        DeviceAssert("enemy-after-body-chase-second-F-stone-drops-battery", dropped,
            "Second actual Bear F defeats the same body-corrected enemy and activates its linked battery at the death pose.");
        PlaceDeviceActor(bear, new Vector2(-90f, 0f));
        yield return DeviceWait(0.15f);
        Vector2 cellPosition = testDirector.States[battery.DeviceId].Position;
        PlaceDeviceActor(frog, cellPosition + new Vector2(2f, 0.1f));
        deviceRunner.SetPlayerObject(deviceRunner.LocalPlayer, frog);
        int charges = testDirector.Batteries;
        bool sawRemotePull = false;
        frog.GetComponent<PlayerInput>().InjectDevelopmentInput(Vector2.zero, false, ability: true, aim: cellPosition);
        deadline = Time.time + 1.5f;
        while (Time.time < deadline && testDirector.States[battery.DeviceId].Phase != 4)
        {
            sawRemotePull |= testDirector.States[battery.DeviceId].Phase == 2;
            yield return null;
        }
        DeviceAssert("spawned-battery-F-starts-remote-pull", dropped && sawRemotePull,
            "Actual Frog F selection and authority RPC put the real dropped battery into pull phase; no Carry/G request is substituted.");
        DeviceAssert("spawned-battery-F-collects-once", dropped && sawRemotePull && testDirector.States[battery.DeviceId].Phase == 4 &&
            testDirector.Batteries == charges + battery.BatteryValue,
            "The same battery travels through normal TickBattery body movement and is collected once. Start=" + cellPosition +
            ", final=" + testDirector.States[battery.DeviceId].Position + ", Frog=" + frog.transform.position + ", feedback=" + frog.GetComponent<PcsPlayerAbilities>().Feedback + ".");
        yield return DeviceWait(0.7f);
        frog.GetComponent<PlayerInput>().InjectDevelopmentInput(Vector2.zero, false, ability: true, aim: cellPosition);
        yield return DeviceWait(0.2f);
        DeviceAssert("spawned-battery-F-cannot-duplicate", dropped && sawRemotePull && testDirector.States[battery.DeviceId].Phase == 4 &&
            testDirector.Batteries == charges + battery.BatteryValue, "A repeated actual F cannot collect the retired battery twice.");
        enemy.Speed = originalSpeed;
        PlaceDeviceActor(frog, new Vector2(-95f, 0f));
        SuppressDeviceEnemies();
    }

    private static float DeviceGroundPenetration(PcsPuzzleDevice device, out string groundName)
    {
        groundName = "none";
        if (device.Solid == null || !device.Solid.enabled) return float.PositiveInfinity;
        Bounds bounds = device.Solid.bounds;
        Collider2D[] overlaps = Physics2D.OverlapBoxAll(bounds.center, bounds.size, 0f, testDirector.ObstructionMask);
        float deepest = 0f;
        foreach (Collider2D ground in overlaps)
        {
            if (ground == null || ground == device.Solid || ground.isTrigger || !ground.enabled) continue;
            ColliderDistance2D separation = device.Solid.Distance(ground);
            if (!separation.isValid || !separation.isOverlapped || separation.distance >= -deepest) continue;
            deepest = -separation.distance;
            Vector2 networkPosition = testDirector.States[device.DeviceId].Position;
            RaycastHit2D cast = Physics2D.BoxCast(networkPosition + Vector2.up * 0.1f, device.BodySize, 0f,
                Vector2.down, 0.3f, testDirector.ObstructionMask);
            groundName = ground.name + "; state=" + networkPosition.ToString("F6") +
                "; body=" + (device.Body != null ? device.Body.position.ToString("F6") : "none") +
                "; transform=" + device.transform.position.ToString("F6") + "; bodyBottom=" + bounds.min.y.ToString("F6") +
                "; groundTop=" + ground.bounds.max.y.ToString("F6") + "; separation=" + separation.distance.ToString("F6") +
                "; pointA=" + separation.pointA.ToString("F6") +
                "; pointB=" + separation.pointB.ToString("F6") + "; castCollider=" + (cast.collider != null ? cast.collider.name : "none") +
                "; castCentroid=" + cast.centroid.ToString("F6") + "; castNormal=" + cast.normal.ToString("F6");
        }
        return deepest;
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

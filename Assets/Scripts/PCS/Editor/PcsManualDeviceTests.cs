using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Fusion;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static partial class PcsSecondPassTools
{
    public static void RunManualDeviceTests() { LayoutBeginPlay("manual"); }

    private const string ManualSessionFlag = "PCS.Manual.NormalSession";
    private const string ManualRestoreSceneFlag = "PCS.Manual.RestoreScene";
    private static IEnumerator manualSessionRoutine;
    private static ManualSessionReport manualSessionReport;
    private static NetworkLauncher manualSessionLauncher;
    private static bool manualJoinFinished;
    private static bool manualJoinSucceeded;
    private static double manualSessionStarted;
    private static string manualSessionWrittenPhase;

    [Serializable] private sealed class ManualSessionReport
    {
        public string mode;
        public string status;
        public string phase;
        public string startedUtc;
        public string updatedUtc;
        public string finishedUtc;
        public string room;
        public string error;
        public double elapsedSeconds;
        public bool canStartGameObserved;
        public bool sceneLoadRequested;
        public bool directEnableRejected;
        public string directEnableReason;
        public ManualSessionObservation observation;
    }

    [Serializable] private sealed class ManualSessionObservation
    {
        public string id;
        public string utc;
        public string scene;
        public bool play;
        public bool sceneDirty;
        public int runnerComponents;
        public int runningRunners;
        public string gameMode;
        public int activePlayers;
        public int managerRoster;
        public int startingRoster;
        public bool managerReady;
        public bool teamLocked;
        public bool canStartGame;
        public int playerActors;
        public int locallyOwnedActors;
        public bool localPlayerObject;
        public string localRole;
        public bool directorFound;
        public bool directorSpawned;
        public bool directorAuthority;
        public bool testReady;
        public string reason;
        public bool testsEnabled;
        public bool hasForcedInputs;
        public int forcedPressureCount;
        public string leverInput;
        public int section = -1;
        public int resetEpoch = -1;
        public bool hacked;
        public bool leverLower;
        public List<ManualDeviceObservation> devices = new List<ManualDeviceObservation>();
    }

    [Serializable] private sealed class ManualDeviceObservation
    {
        public int id;
        public string name;
        public string kind;
        public int active;
        public float stateY;
        public bool hasBody;
        public float bodyY;
        public string pressureInput;
    }

    [InitializeOnLoadMethod]
    private static void InstallManualSessionCallbacks()
    {
        EditorApplication.playModeStateChanged -= ManualSessionModeChanged;
        EditorApplication.playModeStateChanged += ManualSessionModeChanged;
        EditorApplication.update -= PollManualSession;
        EditorApplication.update += PollManualSession;
    }

    public static void RunManualNormalSession() { BeginManualSession("normal", "Assets/Scenes/LobbyScene.unity"); }
    public static void RunManualDirectPlay() { BeginManualSession("direct", LayoutPlayScene); }

    private static void BeginManualSession(string mode, string scene)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop the current Play session before starting this observation.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Save the user scene before starting this observation.");
        if (SceneUtility.GetBuildIndexByScenePath(scene) < 0)
            throw new InvalidOperationException("The observation scene must already be in Build Settings: " + scene);
        SessionState.SetString(ManualRestoreSceneFlag, SceneManager.GetActiveScene().path);
        SessionState.SetString(ManualSessionFlag, "requested-" + mode);
        Selection.activeObject = null;
        EditorSceneManager.OpenScene(scene);
        EditorApplication.EnterPlaymode();
    }

    private static void ManualSessionModeChanged(PlayModeStateChange state)
    {
        string flag = SessionState.GetString(ManualSessionFlag, "");
        if (state == PlayModeStateChange.EnteredPlayMode && flag.StartsWith("requested-", StringComparison.Ordinal))
        {
            string mode = flag.Substring("requested-".Length);
            manualSessionReport = new ManualSessionReport { mode = mode, status = "RUNNING", phase = "Entered Play",
                startedUtc = DateTime.UtcNow.ToString("O"), room = mode == "normal" ? "PCS_Manual_" + Guid.NewGuid().ToString("N") : "" };
            manualSessionStarted = EditorApplication.timeSinceStartup;
            manualSessionWrittenPhase = null;
            manualJoinFinished = manualJoinSucceeded = false;
            SessionState.SetString(ManualSessionFlag, "running-" + mode);
            manualSessionRoutine = mode == "normal" ? RunNormalManualSession() : ObserveDirectManualPlay();
            SaveManualSessionReport();
        }
        else if (state == PlayModeStateChange.ExitingPlayMode && !string.IsNullOrEmpty(flag))
        {
            UnsubscribeManualSession();
            manualSessionRoutine = null;
            if (manualSessionReport != null && manualSessionReport.status == "RUNNING")
            {
                manualSessionReport.status = "INTERRUPTED";
                manualSessionReport.error = "Play ended before normal session observation completed.";
                manualSessionReport.finishedUtc = DateTime.UtcNow.ToString("O");
                SaveManualSessionReport();
            }
        }
        else if (state == PlayModeStateChange.EnteredEditMode && !string.IsNullOrEmpty(flag))
        {
            SessionState.SetString(ManualSessionFlag, "");
            string original = SessionState.GetString(ManualRestoreSceneFlag, "");
            SessionState.SetString(ManualRestoreSceneFlag, "");
            bool dirty = false;
            for (int i = 0; i < SceneManager.sceneCount; i++) dirty |= SceneManager.GetSceneAt(i).isDirty;
            if (!dirty && !string.IsNullOrEmpty(original) && SceneManager.GetActiveScene().path != original)
                EditorSceneManager.OpenScene(original);
        }
    }

    private static void PollManualSession()
    {
        if (!EditorApplication.isPlaying || manualSessionRoutine == null) return;
        try
        {
            if (EditorApplication.timeSinceStartup - manualSessionStarted > 150d)
                throw new TimeoutException("Normal session observation timed out during: " + manualSessionReport.phase);
            if (!manualSessionRoutine.MoveNext())
            {
                manualSessionReport.status = "PASS";
                manualSessionReport.phase = "Ready for manual EditorWindow inspection; Play intentionally remains running";
                manualSessionReport.finishedUtc = DateTime.UtcNow.ToString("O");
                manualSessionReport.observation = CaptureManualSession("ready");
                SaveManualSessionReport();
                manualSessionRoutine = null;
                UnsubscribeManualSession();
            }
            else if (manualSessionWrittenPhase != manualSessionReport.phase) SaveManualSessionReport();
        }
        catch (Exception exception)
        {
            manualSessionReport.status = "FAIL";
            manualSessionReport.error = exception.ToString();
            manualSessionReport.finishedUtc = DateTime.UtcNow.ToString("O");
            manualSessionReport.observation = CaptureManualSession("failure");
            SaveManualSessionReport();
            manualSessionRoutine = null;
            UnsubscribeManualSession();
        }
    }

    private static IEnumerator RunNormalManualSession()
    {
        if (SceneManager.GetActiveScene().name != "LobbyScene") throw new InvalidOperationException("Expected the saved LobbyScene.");
        manualSessionReport.phase = "Waiting for normal lobby components";
        while (GameManager.Instance == null || (manualSessionLauncher = UnityEngine.Object.FindFirstObjectByType<NetworkLauncher>()) == null)
            yield return null;
        manualSessionLauncher.OnSessionAccessFinished += ManualSessionJoined;
        GameManager.Instance.SetMyCharacter(MyEnum.CharacterType.Bear);
        manualSessionReport.phase = "Waiting for normal Shared lobby connection";
        NetworkRunner runner = null;
        while (runner == null || !runner.LobbyInfo.IsValid)
        {
            runner = UnityEngine.Object.FindFirstObjectByType<NetworkRunner>();
            yield return null;
        }
        manualSessionReport.phase = "Joining through NetworkLauncher.TryAccessSession";
        manualSessionLauncher.TryAccessSession(manualSessionReport.room);
        while (!manualJoinFinished) yield return null;
        if (!manualJoinSucceeded) throw new InvalidOperationException("Normal launcher reported that session access failed.");
        manualSessionReport.phase = "Waiting for one real roster member and CanStartGame";
        while (true)
        {
            NetworkGameManager manager = NetworkGameManager.Instance;
            if (manager != null && manager.IsReady && manager.Runner == runner)
            {
                if (runner.GameMode != GameMode.Shared) throw new InvalidOperationException("Normal launcher did not start Shared mode.");
                int active = 0, reserved = 0;
                foreach (PlayerRef player in runner.ActivePlayers)
                {
                    active++;
                    if (manager.TryGetSelectedRole(player, out _)) reserved++;
                }
                if (active > 1) throw new InvalidOperationException("Unexpected additional participant in the unique manual test room.");
                if (active == 1 && reserved == 1 && manager.CanStartGame)
                {
                    manualSessionReport.canStartGameObserved = true;
                    manager.LoadScene("Puzzle");
                    manualSessionReport.sceneLoadRequested = true;
                    break;
                }
            }
            yield return null;
        }
        manualSessionReport.phase = "Waiting for normal Puzzle registration and selected Bear spawn";
        while (true)
        {
            ManualSessionObservation observation = CaptureManualSession("normal-start-check");
            if (observation.scene == LayoutPlayScene && observation.runningRunners == 1 && observation.activePlayers == 1 &&
                observation.managerRoster == 1 && observation.startingRoster == 1 && observation.localPlayerObject &&
                observation.playerActors == 1 && observation.locallyOwnedActors == 1 && observation.localRole == "Bear" &&
                observation.directorAuthority && observation.testReady)
            {
                if (observation.testsEnabled || observation.hasForcedInputs)
                    throw new InvalidOperationException("Manual forcing was unexpectedly enabled on fresh normal session entry.");
                manualSessionReport.observation = observation;
                break;
            }
            yield return null;
        }
    }

    private static IEnumerator ObserveDirectManualPlay()
    {
        manualSessionReport.phase = "Observing direct Puzzle Play without network bootstrap";
        double deadline = EditorApplication.timeSinceStartup + 1.5d;
        while (EditorApplication.timeSinceStartup < deadline) yield return null;
        PcsPuzzleDirector director = UnityEngine.Object.FindFirstObjectByType<PcsPuzzleDirector>();
        if (director == null) throw new InvalidOperationException("Direct Play scene has no Director to test rejection against.");
        manualSessionReport.directEnableRejected = !director.EditorSetTestsEnabled(true, out string reason);
        manualSessionReport.directEnableReason = reason;
        ManualSessionObservation observation = CaptureManualSession("direct-play");
        if (!manualSessionReport.directEnableRejected || string.IsNullOrEmpty(reason) || observation.runningRunners != 0 ||
            observation.directorSpawned || observation.testReady || observation.testsEnabled)
            throw new InvalidOperationException("Direct Play negative test unexpectedly has an active network session or editor controls.");
        manualSessionReport.observation = observation;
    }

    private static void ManualSessionJoined(bool success)
    {
        manualJoinFinished = true;
        manualJoinSucceeded = success;
    }

    private static void UnsubscribeManualSession()
    {
        if (manualSessionLauncher != null) manualSessionLauncher.OnSessionAccessFinished -= ManualSessionJoined;
        manualSessionLauncher = null;
    }

    public static void ObserveManualDeviceSession(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || id.Contains(".."))
            throw new ArgumentException("Observation id must be a simple filename.", nameof(id));
        Directory.CreateDirectory(LayoutPlayEvidence);
        File.WriteAllText(Path.Combine(LayoutPlayEvidence, id + "-manual-observation.json"), JsonUtility.ToJson(CaptureManualSession(id), true));
    }

    private static ManualSessionObservation CaptureManualSession(string id)
    {
        var observation = new ManualSessionObservation { id = id, utc = DateTime.UtcNow.ToString("O"),
            scene = SceneManager.GetActiveScene().path, sceneDirty = SceneManager.GetActiveScene().isDirty,
            play = EditorApplication.isPlaying, reason = "퍼즐 Director가 없습니다." };
        NetworkRunner[] runners = UnityEngine.Object.FindObjectsByType<NetworkRunner>(FindObjectsSortMode.None);
        observation.runnerComponents = runners.Length;
        NetworkRunner runner = null;
        foreach (NetworkRunner candidate in runners)
            if (candidate.IsRunning) { runner = candidate; observation.runningRunners++; }
        PcsPuzzleDirector director = PcsPuzzleDirector.Instance;
        if (director == null) director = UnityEngine.Object.FindFirstObjectByType<PcsPuzzleDirector>();
        observation.directorFound = director != null;
        if (director != null)
        {
            observation.directorSpawned = director.Object != null && director.Object.IsValid;
            observation.testReady = director.EditorTryGetTestStatus(out observation.reason);
            observation.testsEnabled = director.EditorTestsEnabled;
            observation.hasForcedInputs = director.EditorHasForcedInputs;
            observation.forcedPressureCount = director.EditorForcedPressureCount;
            observation.leverInput = director.EditorLeverInputLabel;
            if (observation.directorSpawned)
            {
                observation.directorAuthority = director.HasStateAuthority;
                observation.section = director.ActiveSection;
                observation.resetEpoch = director.ResetEpoch;
                observation.hacked = director.StageOneHacked;
                observation.leverLower = director.LeverLower;
                foreach (PcsPuzzleDevice device in director.Devices)
                {
                    if (device == null || device.Section != 1 || device.DeviceId < 0 || device.DeviceId >= director.Devices.Length ||
                        director.Devices[device.DeviceId] != device) continue;
                    PcsDeviceState state = director.States[device.DeviceId];
                    observation.devices.Add(new ManualDeviceObservation { id = device.DeviceId, name = device.name,
                        kind = device.Kind.ToString(), active = state.Active, stateY = state.Position.y,
                        hasBody = device.Body != null, bodyY = device.Body != null ? device.Body.position.y : 0f,
                        pressureInput = device.Kind == PcsDeviceKind.PressurePlate ? director.EditorGetPressureInput(device).ToString() : "" });
                }
            }
        }
        NetworkGameManager manager = NetworkGameManager.Instance;
        observation.managerReady = manager != null && manager.IsReady;
        if (observation.managerReady)
        {
            observation.teamLocked = manager.TeamLocked;
            observation.canStartGame = manager.CanStartGame;
        }
        if (runner != null)
        {
            observation.gameMode = runner.GameMode.ToString();
            foreach (PlayerRef player in runner.ActivePlayers)
            {
                observation.activePlayers++;
                if (observation.managerReady && manager.Runner == runner)
                {
                    if (manager.TryGetSelectedRole(player, out _)) observation.managerRoster++;
                    if (manager.IsStartingPlayer(player)) observation.startingRoster++;
                }
            }
            if (runner.TryGetPlayerObject(runner.LocalPlayer, out NetworkObject actor) && actor != null && actor.IsValid)
            {
                observation.localPlayerObject = true;
                PcsPlayerAbilities ability = actor.GetComponent<PcsPlayerAbilities>();
                observation.localRole = ability != null ? ability.CharacterType.ToString() : "missing ability";
            }
            foreach (PcsPlayerAbilities ability in UnityEngine.Object.FindObjectsByType<PcsPlayerAbilities>(FindObjectsSortMode.None))
                if (ability.Object != null && ability.Object.IsValid && ability.Runner == runner)
                {
                    observation.playerActors++;
                    if (ability.HasStateAuthority) observation.locallyOwnedActors++;
                }
        }
        return observation;
    }

    private static void SaveManualSessionReport()
    {
        manualSessionReport.updatedUtc = DateTime.UtcNow.ToString("O");
        manualSessionReport.elapsedSeconds = EditorApplication.timeSinceStartup - manualSessionStarted;
        manualSessionWrittenPhase = manualSessionReport.phase;
        Directory.CreateDirectory(LayoutPlayEvidence);
        File.WriteAllText(Path.Combine(LayoutPlayEvidence, "manual-" + manualSessionReport.mode + "-session.json"), JsonUtility.ToJson(manualSessionReport, true));
    }

    private sealed class ManualDeviceProbe
    {
        public PcsPuzzleDevice plate;
        public PcsPuzzleDevice wall;
        public PcsPuzzleDevice lift;
        public readonly Dictionary<PcsPuzzleDevice, int> otherPlates = new Dictionary<PcsPuzzleDevice, int>();
        public bool isolated = true;
        public bool continuous = true;
        public bool finite = true;
        public int samples;
        public float maximumStepExcess;
        public float maximumColumnError;
        public SpriteRenderer column;
        public Vector3 fixedColumnPoint;
        public Vector3 movingColumnPoint;
        public Vector3 initialLiftPosition;
        public float localBottom;
        public float localTop;
        public bool fixedLocalTop;
        private Vector2 previousWall;
        private Vector2 previousLift;
        private int previousTick;

        public void Start()
        {
            foreach (PcsPuzzleDevice d in layoutPlayDirector.Devices)
                if (d != null && d.Kind == PcsDeviceKind.PressurePlate && d != plate)
                    otherPlates.Add(d, layoutPlayDirector.States[d.DeviceId].Active);
            column = (SpriteRenderer)LayoutElevatorField("column").GetValue(lift.Elevator);
            if (column == null || column.sprite == null) throw new InvalidOperationException("Central lift column is missing.");
            float bottom = (float)LayoutElevatorField("columnBottom").GetValue(lift.Elevator);
            float top = (float)LayoutElevatorField("columnTop").GetValue(lift.Elevator);
            localBottom = (column.sprite.rect.height * bottom - column.sprite.pivot.y) / column.sprite.pixelsPerUnit;
            localTop = (column.sprite.rect.height * top - column.sprite.pivot.y) / column.sprite.pixelsPerUnit;
            Vector3 b = column.transform.TransformPoint(new Vector3(0f, localBottom, 0f));
            Vector3 t = column.transform.TransformPoint(new Vector3(0f, localTop, 0f));
            fixedLocalTop = t.y > b.y;
            fixedColumnPoint = fixedLocalTop ? t : b;
            movingColumnPoint = fixedLocalTop ? b : t;
            initialLiftPosition = lift.transform.position;
            RebaseMotion();
        }

        public void RebaseMotion()
        {
            previousWall = layoutPlayDirector.States[wall.DeviceId].Position;
            previousLift = layoutPlayDirector.States[lift.DeviceId].Position;
            previousTick = layoutPlayRunner.Tick.Raw;
        }

        public void Sample()
        {
            samples++;
            int tick = layoutPlayRunner.Tick.Raw;
            int elapsedTicks = Mathf.Max(0, tick - previousTick);
            Vector2 wallNow = layoutPlayDirector.States[wall.DeviceId].Position;
            Vector2 liftNow = layoutPlayDirector.States[lift.DeviceId].Position;
            float wallExcess = Vector2.Distance(previousWall, wallNow) - wall.Speed * layoutPlayRunner.DeltaTime * elapsedTicks;
            float liftExcess = Vector2.Distance(previousLift, liftNow) - lift.Speed * layoutPlayRunner.DeltaTime * elapsedTicks;
            maximumStepExcess = Mathf.Max(maximumStepExcess, Mathf.Max(wallExcess, liftExcess));
            // Editor updates can observe a completed simulation tick before Runner.Tick advances.
            continuous &= wallExcess <= wall.Speed * layoutPlayRunner.DeltaTime + .015f &&
                liftExcess <= lift.Speed * layoutPlayRunner.DeltaTime + .015f;
            finite &= float.IsFinite(wallNow.x) && float.IsFinite(wallNow.y) && float.IsFinite(liftNow.x) && float.IsFinite(liftNow.y);
            previousWall = wallNow;
            previousLift = liftNow;
            previousTick = tick;
            foreach (KeyValuePair<PcsPuzzleDevice, int> other in otherPlates)
                isolated &= layoutPlayDirector.EditorGetPressureInput(other.Key) == PcsPuzzleDirector.EditorPressureInput.Normal &&
                    layoutPlayDirector.States[other.Key.DeviceId].Active == other.Value;
            maximumColumnError = Mathf.Max(maximumColumnError, ColumnError());
        }

        public float ColumnError()
        {
            Vector3 b = column.transform.TransformPoint(new Vector3(0f, localBottom, 0f));
            Vector3 t = column.transform.TransformPoint(new Vector3(0f, localTop, 0f));
            return Mathf.Max(Vector3.Distance(fixedLocalTop ? t : b, fixedColumnPoint),
                Vector3.Distance(fixedLocalTop ? b : t, movingColumnPoint + lift.transform.position - initialLiftPosition));
        }
    }

    private static IEnumerator LayoutRunManualDeviceTests()
    {
        layoutPlayReport.boundary = "One real GameMode.Shared client and the current saved Puzzle scene. " +
            "The enabled authority director runs its normal FixedUpdateNetwork, device decisions, MoveAuthority and MovePosition. " +
            "Commands use exactly the public Editor manual-test API; no player actor, pressure contact, E pulse or F aiming success is simulated. " +
            "No device Transform is moved by this test. The explicit section-reset command uses the existing production reset path. " +
            "No Scene save or production completion flag fixture. Actual no-StateAuthority/proxy rejection is not exercised in this one-client run.";
        PcsPuzzleDirector director = layoutPlayDirector;
        PcsPuzzleDevice lift = ManualFindDevice(d => d.Section == 1 && d.Kind == PcsDeviceKind.Elevator &&
            d.LiftPolicy == PcsLiftPolicy.StageOneThreeStop, "central lift");
        PcsPuzzleDevice console = ManualFindDevice(d => d.Section == 1 && d.Kind == PcsDeviceKind.HackConsole &&
            Array.IndexOf(d.Links, lift) >= 0, "central lift console");
        PcsPuzzleDevice lever = ManualFindDevice(d => d.Section == 1 && d.Kind == PcsDeviceKind.Lever && d.RequiresRemoteHold &&
            Array.IndexOf(d.Links, lift) >= 0, "central lift hold lever");
        PcsPuzzleDevice plate = ManualFindDevice(d => d.Section == 1 && d.Kind == PcsDeviceKind.PressurePlate &&
            Array.Exists(d.Links, target => target != null && target.Kind == PcsDeviceKind.SlidingWall), "stage-one pressure plate");
        PcsPuzzleDevice wall = Array.Find(plate.Links, target => target != null && target.Kind == PcsDeviceKind.SlidingWall);
        if (wall == null || wall.Body == null || lift.Body == null || lift.Elevator == null ||
            lift.UpperStop == null || lift.MiddleStop == null || lift.LowerStop == null || wall.UpperStop == null)
            throw new InvalidOperationException("Manual test requires the actual registered wall, central lift, bodies and three stops.");

        yield return LayoutWait(.15f);
        LayoutCheck("manual-default-disabled", "Editor", !director.EditorTestsEnabled && !director.EditorHasForcedInputs,
            "Fresh scene registration: editor forcing is off and no forced input exists.");
        bool denied = !director.EditorSetPressureInput(plate, PcsPuzzleDirector.EditorPressureInput.Pressed, out string reason);
        LayoutCheck("manual-disabled-rejects-input", "Editor", denied && !string.IsNullOrEmpty(reason), reason);
        bool enabled = director.EditorSetTestsEnabled(true, out reason);
        LayoutCheck("manual-authority-enabled", "Editor", enabled && director.EditorTestsEnabled && director.HasStateAuthority, reason);
        if (!enabled) throw new InvalidOperationException(reason);

        GameObject unregistered = new GameObject("[Test only] Unregistered pressure identity");
        try
        {
            PcsPuzzleDevice impostor = unregistered.AddComponent<PcsPuzzleDevice>();
            impostor.Kind = PcsDeviceKind.PressurePlate;
            impostor.DeviceId = plate.DeviceId;
            impostor.Section = plate.Section;
            denied = !director.EditorSetPressureInput(impostor, PcsPuzzleDirector.EditorPressureInput.Pressed, out reason);
            LayoutCheck("manual-unregistered-rejected", "Editor", denied && !director.EditorHasForcedInputs, reason);
        }
        finally { UnityEngine.Object.Destroy(unregistered); }

        denied = !director.EditorSetPressureInput(plate, (PcsPuzzleDirector.EditorPressureInput)1234, out reason);
        LayoutCheck("manual-invalid-input-rejected", "Editor", denied, reason);
        int tutorialMask = director.CompletedTutorialMask;
        yield return ManualCommand(PcsPuzzleDirector.EditorStageOneCommand.PrepareSection);
        LayoutCheck("manual-prepare-only-runtime-section", "Editor", director.ActiveSection == 1 &&
            director.CompletedTutorialMask == tutorialMask && director.enabled,
            "Runtime section=" + director.ActiveSection + "; tutorial completion mask unchanged=" + tutorialMask + "; director remains enabled.");

        ManualDeviceProbe probe = new ManualDeviceProbe { plate = plate, wall = wall, lift = lift };
        probe.Start();
        PcsPuzzleDevice otherSection = Array.Find(director.Devices,
            d => d != null && d.isActiveAndEnabled && d.Kind == PcsDeviceKind.PressurePlate && d.Section == 0);
        if (otherSection != null)
        {
            denied = !director.EditorSetPressureInput(otherSection, PcsPuzzleDirector.EditorPressureInput.Pressed, out reason);
            LayoutCheck("manual-other-section-rejected", "Editor", denied &&
                director.EditorGetPressureInput(otherSection) == PcsPuzzleDirector.EditorPressureInput.Normal, reason);
        }
        else throw new InvalidOperationException("No other-section pressure device exists to test isolation.");

        Vector2 closed = wall.InitialPosition;
        Vector2 open = wall.UpperStop.position;
        yield return ManualPressure(probe, PcsPuzzleDirector.EditorPressureInput.Pressed);
        yield return ManualObserve(probe, .75f);
        float risingY = director.States[wall.DeviceId].Position.y;
        LayoutCheck("manual-wall-rising", wall.name, risingY > closed.y + .1f && risingY < open.y - .05f,
            "Partial opening through actual ticks: y=" + risingY.ToString("F4"));
        yield return ManualPressure(probe, PcsPuzzleDirector.EditorPressureInput.Released);
        yield return ManualObserve(probe, .25f);
        float fallingY = director.States[wall.DeviceId].Position.y;
        LayoutCheck("manual-wall-reverse-down", wall.name, fallingY < risingY - .02f && fallingY > closed.y,
            "Release during travel: " + risingY.ToString("F4") + " -> " + fallingY.ToString("F4"));
        yield return ManualPressure(probe, PcsPuzzleDirector.EditorPressureInput.Pressed);
        yield return ManualObserve(probe, .25f);
        LayoutCheck("manual-wall-reverse-up", wall.name, director.States[wall.DeviceId].Position.y > fallingY + .02f,
            "Second direction change starts at the current wall pose.");
        yield return ManualWaitForTarget(probe, wall, open, "manual-wall-open");
        yield return ManualObserve(probe, 1.2f);
        LayoutCheck("manual-pressure-survives-real-ticks", plate.name,
            director.States[plate.DeviceId].Active != 0 && plate.PressureButton.IsPressed &&
            Vector2.Distance(wall.Body.position, open) < .02f && director.EditorForcedPressureCount == 1,
            "Held at the upper endpoint with zero player actors, one selected override, normal pressure ticks continuing.");
        yield return ManualPressure(probe, PcsPuzzleDirector.EditorPressureInput.Released);
        yield return ManualWaitForTarget(probe, wall, closed, "manual-wall-closed");
        yield return ManualPressure(probe, PcsPuzzleDirector.EditorPressureInput.Pressed);
        yield return ManualObserve(probe, .35f);
        yield return ManualPressure(probe, PcsPuzzleDirector.EditorPressureInput.Normal);
        yield return ManualWaitForTarget(probe, wall, closed, "manual-pressure-normal-return");
        LayoutCheck("manual-pressure-normal-query-restored", plate.name,
            !plate.PressureButton.EvaluatePressure(plate.AcceptDummyOnly, plate.RequiredRole) &&
            director.States[plate.DeviceId].Active == 0 && !plate.PressureButton.IsPressed && director.EditorForcedPressureCount == 0,
            "No actual participant contact; normal EvaluatePressure and authority state both false after override removal.");

        yield return ManualCommand(PcsPuzzleDirector.EditorStageOneCommand.HoldLever);
        LayoutCheck("manual-unhacked-lever-denied", lever.name, !director.LeverLower && !director.StageOneHacked,
            director.EditorTestLastMessage);
        yield return ManualCommand(PcsPuzzleDirector.EditorStageOneCommand.HackComplete);
        yield return ManualPressure(probe, PcsPuzzleDirector.EditorPressureInput.Released);
        yield return ManualWaitForTarget(probe, lift, lift.MiddleStop.position, "manual-lift-upper-to-middle");
        LayoutCheck("manual-hack-persists-after-pressure-release", console.name, director.StageOneHacked &&
            director.States[console.DeviceId].Active != 0 && director.States[console.DeviceId].Counter == console.RequiredInputs &&
            director.States[plate.DeviceId].Active == 0, "Hack complete state remains latched while the pressure plate is released.");

        yield return ManualCommand(PcsPuzzleDirector.EditorStageOneCommand.HoldLever);
        yield return ManualObserve(probe, .8f);
        float descendingY = director.States[lift.DeviceId].Position.y;
        LayoutCheck("manual-lift-descending", lift.name, descendingY < lift.MiddleStop.position.y - .1f &&
            descendingY > lift.LowerStop.position.y + .05f && director.LeverLower, "Partial descent=" + descendingY.ToString("F4"));
        yield return ManualCommand(PcsPuzzleDirector.EditorStageOneCommand.ReleaseLever);
        yield return ManualObserve(probe, .25f);
        float returningY = director.States[lift.DeviceId].Position.y;
        LayoutCheck("manual-lift-reverse-middle", lift.name, returningY > descendingY + .02f,
            "Release during descent: " + descendingY.ToString("F4") + " -> " + returningY.ToString("F4"));
        yield return ManualCommand(PcsPuzzleDirector.EditorStageOneCommand.HoldLever);
        yield return ManualWaitForTarget(probe, lift, lift.LowerStop.position, "manual-lift-lower");
        yield return ManualObserve(probe, 1.25f);
        LayoutCheck("manual-forced-hold-exceeds-normal-lease", lever.name,
            director.LeverLower && director.States[lever.DeviceId].Active != 0 && Vector2.Distance(lift.Body.position, lift.LowerStop.position) < .02f,
            "Forced hold remains active at lower endpoint for 1.25 seconds, exceeding the normal 0.65-second actor lease.");
        yield return ManualCommand(PcsPuzzleDirector.EditorStageOneCommand.ReleaseLever);
        yield return ManualWaitForTarget(probe, lift, lift.MiddleStop.position, "manual-lift-lower-to-middle");

        yield return ManualCommand(PcsPuzzleDirector.EditorStageOneCommand.HoldLever);
        yield return ManualObserve(probe, .6f);
        yield return ManualCommand(PcsPuzzleDirector.EditorStageOneCommand.HackIncomplete);
        yield return ManualWaitForTarget(probe, lift, lift.UpperStop.position, "manual-hack-incomplete-upper");
        LayoutCheck("manual-hack-incomplete-clears-progress", console.name, !director.StageOneHacked && !director.LeverLower &&
            director.States[console.DeviceId].Active == 0 && director.States[console.DeviceId].Counter == 0 &&
            director.States[lever.DeviceId].Active == 0, "Console progress and lever are cleared; normal movement returned to upper, no direct pose mutation by the tool.");

        yield return ManualCommand(PcsPuzzleDirector.EditorStageOneCommand.HackComplete);
        yield return ManualWaitForTarget(probe, lift, lift.MiddleStop.position, "manual-second-hack-middle");
        yield return ManualPressure(probe, PcsPuzzleDirector.EditorPressureInput.Pressed);
        yield return ManualCommand(PcsPuzzleDirector.EditorStageOneCommand.HoldLever);
        yield return ManualObserve(probe, .55f);
        int beforeClearEpoch = director.ResetEpoch;
        director.EditorReleaseForcedInputs();
        yield return ManualWaitForTarget(probe, lift, lift.MiddleStop.position, "manual-clear-inputs-middle");
        yield return ManualWaitForTarget(probe, wall, closed, "manual-clear-inputs-wall");
        LayoutCheck("manual-clear-inputs-preserves-progress", "Editor", !director.EditorHasForcedInputs &&
            director.StageOneHacked && !director.LeverLower && director.ResetEpoch == beforeClearEpoch &&
            director.States[console.DeviceId].Active != 0 && director.States[plate.DeviceId].Active == 0,
            "All forced input removed; actual empty pressure/hold behavior returns, hack remains complete, reset epoch unchanged.");

        LayoutCheck("manual-other-plates-isolated", "Editor", probe.isolated,
            "Observed " + probe.otherPlates.Count + " unselected pressure devices throughout forced-input transitions.");
        LayoutCheck("manual-motion-continuity", "Editor", probe.finite && probe.continuous && probe.samples > 40,
            "Actual authority-state motion sampled " + probe.samples + " times; maximum excess above elapsed tick speed=" +
            probe.maximumStepExcess.ToString("F6") + ". One tick plus .015 tolerance accommodates Editor polling order.");
        LayoutCheck("manual-column-has-no-physics", lift.name, lift.Elevator.enabled &&
            probe.column.GetComponentInChildren<Collider2D>(true) == null && probe.column.GetComponentInChildren<Rigidbody2D>(true) == null,
            "The animated column is visual only; no Collider2D or Rigidbody2D on the column or its children.");
        LayoutCheck("manual-column-attachment", lift.name, probe.maximumColumnError < .12f && probe.ColumnError() < .008f,
            "Maximum sampled endpoint error=" + probe.maximumColumnError.ToString("F6") +
            "; settled error=" + probe.ColumnError().ToString("F6") + "; .12 moving tolerance accounts for Editor update versus LateUpdate.");

        yield return ManualPressure(probe, PcsPuzzleDirector.EditorPressureInput.Pressed);
        yield return ManualCommand(PcsPuzzleDirector.EditorStageOneCommand.HoldLever);
        yield return ManualObserve(probe, .3f);
        int beforeResetEpoch = director.ResetEpoch;
        yield return ManualCommand(PcsPuzzleDirector.EditorStageOneCommand.ResetSection);
        yield return LayoutWait(director.ResetDelay + .2f);
        // Production ResetSection deliberately resets poses. It is not an input-transition continuity check.
        probe.RebaseMotion();
        LayoutCheck("manual-section-reset-progress", "Editor", !director.EditorHasForcedInputs && !director.StageOneHacked &&
            !director.StageOneComplete && !director.LeverLower && director.ResetEpoch > beforeResetEpoch &&
            director.States[console.DeviceId].Active == 0 && director.States[console.DeviceId].Counter == 0,
            "Explicit existing section reset clears inputs AND progress; reset epoch " + beforeResetEpoch + " -> " + director.ResetEpoch);
        yield return ManualWaitForTarget(probe, lift, lift.UpperStop.position, "manual-section-reset-upper");
        yield return ManualWaitForTarget(probe, wall, closed, "manual-section-reset-closed");

        yield return ManualCommand(PcsPuzzleDirector.EditorStageOneCommand.HackComplete);
        yield return ManualCommand(PcsPuzzleDirector.EditorStageOneCommand.HoldLever);
        yield return ManualPressure(probe, PcsPuzzleDirector.EditorPressureInput.Pressed);
        yield return ManualObserve(probe, .4f);
        director.EditorSetTestsEnabled(false, out reason);
        yield return ManualWaitForTarget(probe, lift, lift.MiddleStop.position, "manual-disable-returns-middle");
        yield return ManualWaitForTarget(probe, wall, closed, "manual-disable-returns-closed");
        LayoutCheck("manual-disable-clears-forcing-not-hack", "Editor", !director.EditorTestsEnabled && !director.EditorHasForcedInputs &&
            director.StageOneHacked && !director.LeverLower && director.States[plate.DeviceId].Active == 0, reason);
        LayoutCheck("manual-still-real-shared-authority", "Editor", director.enabled && director.HasStateAuthority &&
            layoutPlayRunner.IsRunning && layoutPlayRunner.GameMode == Fusion.GameMode.Shared && layoutPlayActors.Count == 0,
            "Director remained enabled; one running Shared client; no fake player actors or direct transform movement.");
    }

    private static PcsPuzzleDevice ManualFindDevice(Predicate<PcsPuzzleDevice> predicate, string label)
    {
        PcsPuzzleDevice found = null;
        foreach (PcsPuzzleDevice device in layoutPlayDirector.Devices)
        {
            if (device == null || !device.isActiveAndEnabled || !predicate(device)) continue;
            if (found != null) throw new InvalidOperationException("Ambiguous " + label + " registry.");
            found = device;
        }
        return found != null ? found : throw new InvalidOperationException("Missing " + label + " registry.");
    }

    private static IEnumerator ManualCommand(PcsPuzzleDirector.EditorStageOneCommand command)
    {
        layoutPlayReport.phase = "Manual API: " + command;
        if (!layoutPlayDirector.EditorQueueStageOneCommand(command, out string reason))
            throw new InvalidOperationException(command + " rejected: " + reason);
        float deadline = Time.time + 3f;
        while (layoutPlayDirector.EditorTestHasPendingCommand && Time.time < deadline) yield return null;
        if (layoutPlayDirector.EditorTestHasPendingCommand) throw new TimeoutException("Manual command was not consumed: " + command);
        yield return LayoutWait(.08f);
    }

    private static IEnumerator ManualPressure(ManualDeviceProbe probe, PcsPuzzleDirector.EditorPressureInput mode)
    {
        if (!layoutPlayDirector.EditorSetPressureInput(probe.plate, mode, out string reason))
            throw new InvalidOperationException("Pressure " + mode + " rejected: " + reason);
        yield return ManualObserve(probe, .08f);
    }

    private static IEnumerator ManualObserve(ManualDeviceProbe probe, float seconds)
    {
        float deadline = Time.time + seconds;
        while (Time.time < deadline) { probe.Sample(); yield return null; }
        probe.Sample();
    }

    private static IEnumerator ManualWaitForTarget(ManualDeviceProbe probe, PcsPuzzleDevice device, Vector2 target, string id)
    {
        layoutPlayReport.phase = id;
        float remaining = Vector2.Distance(layoutPlayDirector.States[device.DeviceId].Position, target);
        float deadline = Time.time + remaining / Mathf.Max(.01f, device.Speed) + 4f;
        while (Time.time < deadline && (Vector2.Distance(layoutPlayDirector.States[device.DeviceId].Position, target) > .008f ||
            Vector2.Distance(device.Body.position, target) > .015f))
        { probe.Sample(); yield return null; }
        yield return ManualObserve(probe, .12f);
        float stateError = Vector2.Distance(layoutPlayDirector.States[device.DeviceId].Position, target);
        float bodyError = Vector2.Distance(device.Body.position, target);
        LayoutCheck(id, device.name, stateError < .008f && bodyError < .015f,
            "Authority state target error=" + stateError.ToString("F6") + "; real Rigidbody target error=" + bodyError.ToString("F6") +
            "; target=" + target.ToString("F3"));
    }
}

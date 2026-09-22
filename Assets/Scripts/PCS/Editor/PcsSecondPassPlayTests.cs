using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Fusion;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using LogType = UnityEngine.LogType;

public static partial class PcsSecondPassTools
{
    private const string PlayTestFlag = "PCS.SecondPass.PlayTests";
    private const string PlayTestModeFlag = "PCS.SecondPass.PlayTests.Mode";
    private const string PlayTestScene = "Assets/Scenes/Puzzle.unity";
    private const float ArenaTop = -0.5f;

    [Serializable]
    private class PcsPlayResult
    {
        public string id;
        public string role;
        public string status;
        public float measured;
        public float expected;
        public string detail;
    }

    [Serializable]
    private class PcsPlayReport
    {
        public string startedUtc;
        public string finishedUtc;
        public string status;
        public string unityVersion;
        public string phase;
        public string heartbeatUtc;
        public string requestedMode;
        public string mode = "Not started";
        public string boundary = "Movement sandbox near (-80,0) plus actual Puzzle geometry routes and Frog ability checks, using existing player prefabs, normal Runner.Spawn and PlayerInput/Mover network ticks. Setup poses and disabled puzzle director isolate movement; this is not puzzle completion or four-client validation. In the private Shared mode, every test actor is owned by this one client; remote authority and replication are not tested.";
        public List<PcsPlayResult> results = new List<PcsPlayResult>();
        public List<string> errors = new List<string>();
        public List<string> diagnostics = new List<string>();
    }

    private static PcsPlayReport playReport;
    private static NetworkRunner playRunner;
    private static Task<StartGameResult> playStartTask;
    private static IEnumerator playRoutine;
    private static double playStartedRealtime;
    private static bool playFinishing;
    private static bool playDirectorWasEnabled;
    private static bool playAwaitingScene;
    private static double playSceneDeadline;
    private static double playNextHeartbeat;

    [InitializeOnLoadMethod]
    private static void InstallPlayTestCallbacks()
    {
        EditorApplication.playModeStateChanged -= OnPlayTestModeChanged;
        EditorApplication.playModeStateChanged += OnPlayTestModeChanged;
        EditorApplication.update -= PollPlayTests;
        EditorApplication.update += PollPlayTests;
        EditorApplication.delayCall += RecoverReloadedPlayTest;
    }

    private static void RecoverReloadedPlayTest()
    {
        if (SessionState.GetString(PlayTestFlag, "") != "running" || playReport != null)
            return;
        string path = Path.Combine(EvidenceDirectory, "play-tests.json");
        playReport = File.Exists(path) ? JsonUtility.FromJson<PcsPlayReport>(File.ReadAllText(path)) : null;
        if (playReport == null)
            playReport = new PcsPlayReport { startedUtc = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion };
        playReport.status = "INTERRUPTED";
        playReport.phase = "Domain reload interrupted the in-memory test state.";
        playReport.errors.Add("A script/domain reload interrupted the test. No unrecorded checks are treated as passed.");
        playReport.finishedUtc = DateTime.UtcNow.ToString("O");
        SavePlayReport();
        playFinishing = true;
        SessionState.SetString(PlayTestFlag, "restore");
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            EditorApplication.ExitPlaymode();
        else
            SessionState.SetString(PlayTestFlag, "");
    }

    public static void BeginPlayTests()
    {
        BeginPlayerTests(GameMode.Single);
    }

    public static void BeginSharedPlayTests()
    {
        BeginPlayerTests(GameMode.Shared);
    }

    private static void BeginPlayerTests(GameMode mode)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop the existing Play session before starting isolated tests.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Save all open scenes before Play tests.");
        int index = SceneUtility.GetBuildIndexByScenePath(PlayTestScene);
        if (index < 0)
            throw new InvalidOperationException("BuildPuzzle must register Puzzle in Build Settings first.");
        Directory.CreateDirectory(EvidenceDirectory);
        string previous = Path.Combine(EvidenceDirectory, "play-tests.json");
        if (File.Exists(previous))
            File.Copy(previous, Path.Combine(EvidenceDirectory,
                "play-tests-attempt-" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + ".json"));
        SessionState.SetInt(PlayTestModeFlag, (int)mode);
        SessionState.SetString(PlayTestFlag, "requested");
        EditorSceneManager.OpenScene(PlayTestScene);
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayTestModeChanged(PlayModeStateChange state)
    {
        string flag = SessionState.GetString(PlayTestFlag, "");
        if (state == PlayModeStateChange.EnteredPlayMode && flag == "requested")
        {
            SessionState.SetString(PlayTestFlag, "running");
            GameMode requestedMode = (GameMode)SessionState.GetInt(PlayTestModeFlag, (int)GameMode.Single);
            playReport = new PcsPlayReport
            {
                startedUtc = DateTime.UtcNow.ToString("O"),
                status = "RUNNING",
                phase = "Starting " + requestedMode + " Runner",
                requestedMode = "Fusion GameMode." + requestedMode,
                unityVersion = Application.unityVersion
            };
            playFinishing = false;
            playAwaitingScene = false;
            playStartedRealtime = EditorApplication.timeSinceStartup;
            playNextHeartbeat = 0d;
            Application.logMessageReceived += CapturePlayTestLog;
            GameObject testRunner = new GameObject("[Development] PCS " + requestedMode + " test Runner");
            UnityEngine.Object.DontDestroyOnLoad(testRunner);
            playRunner = testRunner.AddComponent<NetworkRunner>();
            NetworkSceneManagerDefault sceneManager = testRunner.AddComponent<NetworkSceneManagerDefault>();
            StartGameArgs gameArgs = new StartGameArgs
            {
                GameMode = requestedMode,
                PlayerCount = 1,
                Scene = SceneRef.FromIndex(SceneUtility.GetBuildIndexByScenePath(PlayTestScene)),
                SceneManager = sceneManager
            };
            if (requestedMode == GameMode.Shared)
            {
                gameArgs.SessionName = "PCS-PlayerTests-" + Guid.NewGuid().ToString("N");
                gameArgs.IsVisible = false;
                gameArgs.IsOpen = false;
            }
            playStartTask = playRunner.StartGame(gameArgs);
            SavePlayReport();
        }
        else if (state == PlayModeStateChange.ExitingPlayMode && flag == "running" && !playFinishing)
        {
            if (playReport != null)
            {
                playReport.status = "INTERRUPTED";
                playReport.finishedUtc = DateTime.UtcNow.ToString("O");
                SavePlayReport();
            }
            SessionState.SetString(PlayTestFlag, "restore");
            Application.logMessageReceived -= CapturePlayTestLog;
        }
        else if (state == PlayModeStateChange.EnteredEditMode && !string.IsNullOrEmpty(flag))
        {
            SessionState.SetString(PlayTestFlag, "");
            // Play-only objects are discarded by Unity. No test ever saves the scene.
            if (SceneManager.GetActiveScene().path != PlayTestScene)
                EditorSceneManager.OpenScene(PlayTestScene);
        }
    }

    private static void PollPlayTests()
    {
        if (!EditorApplication.isPlaying || playFinishing || SessionState.GetString(PlayTestFlag, "") != "running")
            return;
        try
        {
            if (playReport == null)
            {
                RecoverReloadedPlayTest();
                return;
            }
            if (EditorApplication.timeSinceStartup >= playNextHeartbeat)
            {
                SavePlayReport();
                playNextHeartbeat = EditorApplication.timeSinceStartup + 1d;
            }
            if (EditorApplication.timeSinceStartup - playStartedRealtime > 120d)
                throw new TimeoutException("Play test suite exceeded 120 seconds.");
            if (playStartTask != null)
            {
                if (!playStartTask.IsCompleted)
                    return;
                if (playStartTask.IsFaulted)
                    throw playStartTask.Exception;
                if (!playStartTask.Result.Ok)
                    throw new InvalidOperationException(playReport.requestedMode + " Runner failed: " + playStartTask.Result.ShutdownReason);
                playReport.mode = "Fusion GameMode." + playRunner.GameMode;
                if (playRunner.GameMode != (GameMode)SessionState.GetInt(PlayTestModeFlag, (int)GameMode.Single))
                    throw new InvalidOperationException("The actual Runner mode differs from the requested test mode.");
                playReport.diagnostics.Add("Runner mode confirmed: " + playRunner.GameMode + "; one local client, all test actors locally owned.");
                playStartTask = null;
                playAwaitingScene = true;
                playSceneDeadline = EditorApplication.timeSinceStartup + 15d;
                playReport.phase = "Waiting for normal Puzzle scene NetworkObject registration";
            }
            if (playAwaitingScene)
            {
                PcsPuzzleDirector director = PcsPuzzleDirector.Instance;
                if (director == null || director.Object == null || !director.Object.IsValid || !director.CanSpawnPlayers)
                {
                    if (EditorApplication.timeSinceStartup > playSceneDeadline)
                        throw new TimeoutException("Normal Fusion scene registration did not complete within 15 seconds.");
                    return;
                }
                playAwaitingScene = false;
                SetupPlayArena();
                playRoutine = RunPlayerPlayTests();
            }
            if (playRoutine != null && !playRoutine.MoveNext())
                FinishPlayerPlayTests(null);
        }
        catch (Exception ex)
        {
            FinishPlayerPlayTests(ex);
        }
    }

    private static void SetupPlayArena()
    {
        PcsPuzzleDirector director = PcsPuzzleDirector.Instance;
        bool registered = director != null && director.Object != null && director.Object.IsValid;
        RecordPlay("scene-network-registration", "Scene", registered, registered ? 1f : 0f, 1f,
            "Puzzle loaded by NetworkSceneManagerDefault; scene director must be a registered NetworkObject.");
        if (!registered)
            throw new InvalidOperationException("Puzzle scene NetworkObject registration did not complete.");
        playDirectorWasEnabled = director.enabled;
        director.enabled = false;
        GameObject floor = new GameObject("[Development] Player test floor");
        floor.layer = LayerMask.NameToLayer("Ground");
        floor.transform.position = new Vector3(-80f, -1f, 0f);
        BoxCollider2D floorCollider = floor.AddComponent<BoxCollider2D>();
        floorCollider.size = new Vector2(24f, 1f);
        GameObject ladder = new GameObject("[Development] Player test ladder");
        ladder.transform.position = new Vector3(-74f, 1.5f, 0f);
        BoxCollider2D trigger = ladder.AddComponent<BoxCollider2D>();
        trigger.isTrigger = true;
        trigger.size = new Vector2(1.2f, 5f);
        PcsPuzzleDevice device = ladder.AddComponent<PcsPuzzleDevice>();
        device.Kind = PcsDeviceKind.Ladder;
        device.RequiredRole = MyEnum.CharacterType.Mouse;
        device.Trigger = trigger;
        Physics2D.SyncTransforms();
    }

    private static NetworkObject SpawnTestPlayer(MyEnum.CharacterType role, float x, float surfaceTop = ArenaTop)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player/Player_" + role + ".prefab");
        if (prefab == null)
            throw new InvalidOperationException("Missing player prefab: " + role);
        CapsuleCollider2D capsule = prefab.GetComponent<CapsuleCollider2D>();
        float footOffset = (capsule.offset.y - capsule.size.y * 0.5f) * prefab.transform.localScale.y;
        NetworkObject actor = playRunner.Spawn(prefab, new Vector3(x, surfaceTop - footOffset + 0.04f, 0f),
            Quaternion.identity, playRunner.LocalPlayer);
        playRunner.SetPlayerObject(playRunner.LocalPlayer, actor);
        if (actor.GetComponent<PcsPlayerAbilities>() == null || actor.GetComponent<PlayerInput>() == null)
            throw new InvalidOperationException("Player prefab has not received the second-pass components: " + role);
        return actor;
    }

    private static IEnumerator RunPlayerPlayTests()
    {
        MyEnum.CharacterType[] roles = { MyEnum.CharacterType.Bear, MyEnum.CharacterType.Rabbit,
            MyEnum.CharacterType.Mouse, MyEnum.CharacterType.Frog };
        foreach (MyEnum.CharacterType role in roles)
        {
            playReport.phase = role + " spawn/grounding";
            NetworkObject actor = SpawnTestPlayer(role, -84f);
            Mover mover = actor.GetComponent<Mover>();
            PlayerInput input = actor.GetComponent<PlayerInput>();
            input.InjectDevelopmentInput(Vector2.zero, false);
            float deadline = Time.time + 3f;
            while (!mover.Grounded && Time.time < deadline) yield return null;
            RecordPlay("spawn-grounded", role.ToString(), mover.Grounded, mover.Body.position.y, 0f,
                "Normal Runner.Spawn; actual Mover grounding excludes the actor's own colliders.");

            playReport.phase = role + " run speed";
            input.InjectDevelopmentInput(Vector2.right, false);
            // Ignore only input delivery startup. The assertion still includes normal ground contact/friction.
            float warmup = Time.fixedTime + 0.15f;
            while (Time.fixedTime < warmup) yield return null;
            float start = Time.time;
            float fixedStart = Time.fixedTime;
            int startTick = playRunner.Tick.Raw;
            float tickDuration = playRunner.DeltaTime;
            float startX = mover.Body.position.x;
            float lastObservedFixedTime = fixedStart;
            float velocitySum = 0f;
            float minimumVelocity = float.PositiveInfinity;
            float maximumVelocity = float.NegativeInfinity;
            int velocitySamples = 0;
            while (Time.fixedTime - fixedStart < 1f)
            {
                yield return null;
                if (Time.fixedTime <= lastObservedFixedTime) continue;
                lastObservedFixedTime = Time.fixedTime;
                float observedVelocity = mover.Body.linearVelocity.x;
                velocitySum += observedVelocity;
                minimumVelocity = Mathf.Min(minimumVelocity, observedVelocity);
                maximumVelocity = Mathf.Max(maximumVelocity, observedVelocity);
                velocitySamples++;
            }
            float displacement = mover.Body.position.x - startX;
            float physicsElapsed = Time.fixedTime - fixedStart;
            float frameElapsed = Time.time - start;
            float networkElapsed = (playRunner.Tick.Raw - startTick) * tickDuration;
            float speed = displacement / physicsElapsed;
            RecordPlay("run-speed", role.ToString(), velocitySamples > 0 && physicsElapsed >= 1f &&
                Mathf.Abs(speed - mover.MoveSpeed) <= Mathf.Max(0.35f, mover.MoveSpeed * 0.2f),
                speed, mover.MoveSpeed, "Rigidbody2D displacement / elapsed Unity fixed time after 0.15s input-delivery warmup. " +
                "Normal materials and ground contacts remain; the existing 20% bound is unchanged. dx=" + displacement.ToString("F4") +
                ", fixedSeconds=" + physicsElapsed.ToString("F4") + ", frameSeconds=" + frameElapsed.ToString("F4") +
                ", networkSeconds=" + networkElapsed.ToString("F4") + ", frameSpeed=" + (displacement / frameElapsed).ToString("F4") +
                ", networkSpeed=" + (networkElapsed > 0f ? displacement / networkElapsed : 0f).ToString("F4") +
                ", observedVxMin/Mean/Max=" + minimumVelocity.ToString("F4") + "/" +
                (velocitySamples > 0 ? velocitySum / velocitySamples : 0f).ToString("F4") + "/" + maximumVelocity.ToString("F4") +
                ", observedFixedFrames=" + velocitySamples + ". Velocity samples are frame observations, not every physics substep.");
            input.InjectDevelopmentInput(Vector2.zero, false);
            deadline = Time.time + 0.25f;
            while (Time.time < deadline) yield return null;

            float baseY = mover.Body.position.y;
            playReport.phase = role + " single jump";
            float peak = baseY;
            float theoretical = mover.JumpSpeed * mover.JumpSpeed / (2f * Mathf.Abs(Physics2D.gravity.y * mover.Body.gravityScale));
            input.InjectDevelopmentInput(Vector2.zero, true);
            start = Time.time;
            while (Time.time - start < 3f)
            {
                peak = Mathf.Max(peak, mover.Body.position.y);
                if (Time.time - start > 0.2f && mover.Grounded) break;
                yield return null;
            }
            float singleHeight = peak - baseY;
            bool jumpPass = singleHeight > theoretical * 0.65f && singleHeight < theoretical * 1.25f + 0.05f && mover.Grounded;
            RecordPlay("single-jump", role.ToString(), jumpPass, singleHeight, theoretical,
                "One latched jump press; theoretical value is only a comparison bound, measured peak and landing determine this result.");

            if (role == MyEnum.CharacterType.Rabbit)
            {
                playReport.phase = "Rabbit double jump";
                deadline = Time.time + 0.2f;
                while (Time.time < deadline) yield return null;
                baseY = mover.Body.position.y;
                peak = baseY;
                input.InjectDevelopmentInput(Vector2.zero, true);
                bool second = false;
                start = Time.time;
                while (Time.time - start < 4f)
                {
                    peak = Mathf.Max(peak, mover.Body.position.y);
                    if (!second && Time.time - start > 0.15f && mover.Body.linearVelocity.y <= 0f)
                    {
                        input.InjectDevelopmentInput(Vector2.zero, true);
                        second = true;
                    }
                    if (second && Time.time - start > 0.5f && mover.Grounded) break;
                    yield return null;
                }
                float doubledHeight = peak - baseY;
                RecordPlay("double-jump", "Rabbit", second && doubledHeight > singleHeight * 1.55f &&
                    doubledHeight < singleHeight * 2.4f && mover.Grounded, doubledHeight, singleHeight * 2f,
                    "Second press at the measured first apex; no wall climbing or position change used.");
            }
            input.ClearDevelopmentInput();
            playRunner.Despawn(actor);
            deadline = Time.time + 0.2f;
            while (Time.time < deadline) yield return null;
        }

        playReport.phase = "Mouse ladder climb/hold/release";
        NetworkObject climber = SpawnTestPlayer(MyEnum.CharacterType.Mouse, -74f);
        Mover climbMover = climber.GetComponent<Mover>();
        PlayerInput climbInput = climber.GetComponent<PlayerInput>();
        climbInput.InjectDevelopmentInput(Vector2.zero, false);
        float wait = Time.time + 0.5f;
        while (Time.time < wait) yield return null;
        float climbY = climbMover.Body.position.y;
        climbInput.InjectDevelopmentInput(Vector2.zero, false, ladder: 1f);
        wait = Time.time + 0.8f;
        while (Time.time < wait) yield return null;
        float climbed = climbMover.Body.position.y - climbY;
        RecordPlay("ladder-climb", "Mouse", climbed > 1.4f && climbed < 2.5f, climbed, 2f,
            "W-axis input uses the actual PcsPlayerAbilities ladder mode and Mover physics writer.");
        climbInput.InjectDevelopmentInput(Vector2.zero, false);
        float heldY = climbMover.Body.position.y;
        wait = Time.time + 0.25f;
        while (Time.time < wait) yield return null;
        RecordPlay("ladder-hold", "Mouse", Mathf.Abs(climbMover.Body.position.y - heldY) < 0.12f,
            climbMover.Body.position.y - heldY, 0f, "Releasing W holds only while the actor remains within the ladder trigger.");
        climbInput.InjectDevelopmentInput(Vector2.zero, true);
        wait = Time.time + 0.12f;
        while (Time.time < wait) yield return null;
        RecordPlay("ladder-jump-off", "Mouse", climbMover.Body.gravityScale > 0f && climbMover.Body.position.y > heldY + 0.1f,
            climbMover.Body.position.y - heldY, 0f, "Jump releases ladder mode and restores the prefab gravity.");
        climbInput.ClearDevelopmentInput();
        playRunner.Despawn(climber);

        playReport.phase = "Rabbit and Mouse carry/drop/reboard";
        NetworkObject carrier = SpawnTestPlayer(MyEnum.CharacterType.Rabbit, -82f);
        NetworkObject passenger = SpawnTestPlayer(MyEnum.CharacterType.Mouse, -81.2f);
        PlayerInput carrierInput = carrier.GetComponent<PlayerInput>();
        PlayerInput passengerInput = passenger.GetComponent<PlayerInput>();
        Lifter lifter = carrier.GetComponent<Lifter>();
        var rider = passenger.GetComponent<Mover>().Rider;
        carrierInput.InjectDevelopmentInput(Vector2.zero, false);
        passengerInput.InjectDevelopmentInput(Vector2.zero, false);
        wait = Time.time + 0.6f;
        while (Time.time < wait) yield return null;
        RpcInfo localRpc = RpcInfo.FromLocal(playRunner, RpcChannel.Reliable, RpcHostMode.SourceIsServer);
        playReport.diagnostics.Add("carry preflight: sourceMatchesCarrierAuthority=" + (localRpc.Source == carrier.StateAuthority) +
            ", sourceMatchesPassengerAuthority=" + (localRpc.Source == passenger.StateAuthority) +
            ", invokeLocal=" + localRpc.IsInvokeLocal + ", carrierHasAuthority=" + carrier.HasStateAuthority +
            ", passengerHasAuthority=" + passenger.HasStateAuthority + ", carrierValid=" + carrier.IsValid +
            ", passengerValid=" + passenger.IsValid + ", carrierParticipates=" + PcsPlayerAbilities.CanParticipate(carrier) +
            ", passengerParticipates=" + PcsPlayerAbilities.CanParticipate(passenger) + ", canBePickedUp=" + rider.CanBePickedUp +
            ", headPresent=" + (lifter.Head != null) + ", massAllowed=" + (passenger.GetComponent<Rigidbody2D>().mass <= lifter.Rb.mass) +
            ", headToPassengerDistance=" + (lifter.Head != null ? Vector2.Distance(lifter.Head.transform.position,
                passenger.GetComponent<Mover>().Body.position).ToString("F3") : "missing"));
        SavePlayReport();
        carrierInput.InjectDevelopmentInput(Vector2.zero, false, carry: true);
        wait = Time.time + 0.8f;
        while (Time.time < wait) yield return null;
        playReport.diagnostics.Add("carry after request: carrierSlotOccupied=" + (lifter.CarriedObject != null) +
            ", riderHasCarrier=" + (rider.CarrierObject != null) + ", canBePickedUp=" + rider.CanBePickedUp);
        RecordPlay("carry-attach", "Rabbit+Mouse", rider.IsRiding && rider.CarrierObject == carrier && lifter.CurrentRider == rider,
            rider.IsRiding ? 1f : 0f, 1f, "G input traverses actual Lifter reservation and Rider authority RPC.");
        carrierInput.InjectDevelopmentInput(Vector2.right, false);
        wait = Time.time + 0.6f;
        while (Time.time < wait) yield return null;
        float horizontalError = Mathf.Abs(passenger.GetComponent<Mover>().Body.position.x - carrier.GetComponent<Mover>().Body.position.x);
        RecordPlay("carry-follow", "Rabbit+Mouse", rider.IsRiding && horizontalError < 0.3f,
            horizontalError, 0f, "Passenger pose is written by passenger Mover; no extra test controller or Joint drives it.");
        carrierInput.InjectDevelopmentInput(Vector2.zero, false);
        bool carriedBeforeDrop = rider.IsRiding && rider.CarrierObject == carrier && lifter.CurrentRider == rider;
        passengerInput.InjectDevelopmentInput(Vector2.zero, false, carry: true);
        wait = Time.time + 0.2f;
        while (Time.time < wait) yield return null;
        RecordPlay("carry-drop", "Rabbit+Mouse", carriedBeforeDrop && !rider.IsRiding && lifter.CurrentRider == null && passengerInput.CanMoveInput,
            rider.IsRiding ? 1f : 0f, 0f, "G releases both carried state and carrier reservation; movement input is restored.");
        wait = Time.time + 0.5f;
        while (Time.time < wait) yield return null;
        carrierInput.InjectDevelopmentInput(Vector2.zero, false, carry: true);
        wait = Time.time + 0.7f;
        while (Time.time < wait) yield return null;
        RecordPlay("carry-reboard", "Rabbit+Mouse", carriedBeforeDrop && rider.IsRiding && rider.CarrierObject == carrier && lifter.CurrentRider == rider,
            rider.IsRiding ? 1f : 0f, 1f, "Reboarding after the release cooldown exercises the former Lifter stale-slot bug.");
        passengerInput.ClearDevelopmentInput();
        carrierInput.ClearDevelopmentInput();
        rider.Drop();
        playRunner.Despawn(passenger);
        playRunner.Despawn(carrier);

        IEnumerator geometry = RunActualSceneGeometryTests();
        while (geometry.MoveNext()) yield return null;
        IEnumerator carryRoutes = RunActualCarryRoutes();
        while (carryRoutes.MoveNext()) yield return null;
        IEnumerator followup = RunFrogFollowupTests();
        while (followup.MoveNext()) yield return null;
    }

    private static Collider2D RequireGeometrySurface(string objectName)
    {
        GameObject target = GameObject.Find(objectName);
        Collider2D surface = target != null ? target.GetComponent<Collider2D>() : null;
        if (surface == null || !surface.enabled || surface.isTrigger)
            throw new InvalidOperationException("The actual Puzzle geometry is missing an enabled solid: " + objectName);
        return surface;
    }

    private static bool StandingOnGeometry(Mover mover, Collider2D surface)
    {
        Bounds body = mover.BodyCollider.bounds;
        Bounds floor = surface.bounds;
        return mover.Grounded && Mathf.Abs(body.min.y - floor.max.y) < 0.12f &&
            mover.Body.position.x > floor.min.x && mover.Body.position.x < floor.max.x;
    }

    private static IEnumerator RunActualSceneGeometryTests()
    {
        Collider2D middleSurface = RequireGeometrySurface("Lift_StageOne_ThreeStops");
        PcsPuzzleDevice lift = middleSurface.GetComponent<PcsPuzzleDevice>();
        if (lift == null || lift.MiddleStop == null)
            throw new InvalidOperationException("The actual stage-one lift needs its configured MiddleStop.");
        // Fixture setup only: select the post-hack middle pose without claiming the hack was completed.
        lift.ApplyPose(lift.MiddleStop.position);
        if (lift.Body != null) lift.Body.linearVelocity = Vector2.zero;
        Physics2D.SyncTransforms();
        float wait = Time.time + 0.15f;
        while (Time.time < wait) yield return null;

        Collider2D rightFloor = RequireGeometrySurface("Floor_Landing_Right");
        playReport.phase = "Actual 1-1 geometry: Rabbit floor to middle lift";
        NetworkObject rabbit = SpawnTestPlayer(MyEnum.CharacterType.Rabbit, middleSurface.bounds.max.x + 0.55f,
            rightFloor.bounds.max.y);
        Mover rabbitMover = rabbit.GetComponent<Mover>();
        PlayerInput rabbitInput = rabbit.GetComponent<PlayerInput>();
        rabbitInput.InjectDevelopmentInput(Vector2.zero, false);
        wait = Time.time + 2f;
        while (!rabbitMover.Grounded && Time.time < wait) yield return null;
        bool startedOnFloor = StandingOnGeometry(rabbitMover, rightFloor);
        float start = Time.time;
        float baseFeet = rabbitMover.BodyCollider.bounds.min.y;
        float peakFeet = baseFeet;
        bool secondJump = false;
        bool enteringLift = false;
        rabbitInput.InjectDevelopmentInput(Vector2.zero, true);
        while (Time.time - start < 3f)
        {
            float feet = rabbitMover.BodyCollider.bounds.min.y;
            peakFeet = Mathf.Max(peakFeet, feet);
            if (!secondJump && Time.time - start > 0.15f && rabbitMover.Body.linearVelocity.y <= 0f)
            {
                rabbitInput.InjectDevelopmentInput(Vector2.zero, true);
                secondJump = true;
            }
            if (secondJump && feet > middleSurface.bounds.max.y + 0.08f)
                enteringLift = true;
            rabbitInput.InjectDevelopmentInput(enteringLift && rabbitMover.Body.position.x > middleSurface.bounds.max.x - 0.5f
                ? Vector2.left : Vector2.zero, false);
            if (Time.time - start > 0.5f && StandingOnGeometry(rabbitMover, middleSurface)) break;
            yield return null;
        }
        RecordPlay("scene-rabbit-floor-to-middle", "Rabbit", startedOnFloor && secondJump &&
            StandingOnGeometry(rabbitMover, middleSurface), rabbitMover.BodyCollider.bounds.min.y,
            middleSurface.bounds.max.y, "Actual Floor_Landing_Right and Lift_StageOne_ThreeStops; fixture sets only the middle lift pose and spawn. " +
            "Normal vertical double jump then left input. Required rise=" + (middleSurface.bounds.max.y - baseFeet).ToString("F3") +
            ", measured peak rise=" + (peakFeet - baseFeet).ToString("F3") + ", final x=" + rabbitMover.Body.position.x.ToString("F3"));
        rabbitInput.ClearDevelopmentInput();
        playRunner.Despawn(rabbit);

        Collider2D leftUpper = RequireGeometrySurface("StageOne_LeftUpperExit");
        playReport.phase = "Actual 1-1 geometry: Bear middle lift to left upper exit";
        NetworkObject bear = SpawnTestPlayer(MyEnum.CharacterType.Bear, middleSurface.bounds.min.x + 0.55f,
            middleSurface.bounds.max.y);
        Mover bearMover = bear.GetComponent<Mover>();
        PlayerInput bearInput = bear.GetComponent<PlayerInput>();
        bearInput.InjectDevelopmentInput(Vector2.zero, false);
        wait = Time.time + 2f;
        while (!bearMover.Grounded && Time.time < wait) yield return null;
        bool startedOnLift = StandingOnGeometry(bearMover, middleSurface);
        baseFeet = bearMover.BodyCollider.bounds.min.y;
        peakFeet = baseFeet;
        bool enteringLeft = false;
        start = Time.time;
        bearInput.InjectDevelopmentInput(Vector2.zero, true);
        while (Time.time - start < 2.5f)
        {
            float feet = bearMover.BodyCollider.bounds.min.y;
            peakFeet = Mathf.Max(peakFeet, feet);
            if (feet > leftUpper.bounds.max.y + 0.03f) enteringLeft = true;
            bearInput.InjectDevelopmentInput(enteringLeft && bearMover.Body.position.x > leftUpper.bounds.max.x - 0.7f
                ? Vector2.left : Vector2.zero, false);
            if (Time.time - start > 0.25f && StandingOnGeometry(bearMover, leftUpper)) break;
            yield return null;
        }
        RecordPlay("scene-bear-middle-to-left-upper", "Bear", startedOnLift && StandingOnGeometry(bearMover, leftUpper),
            bearMover.BodyCollider.bounds.min.y, leftUpper.bounds.max.y,
            "Actual middle lift and StageOne_LeftUpperExit. Spawn is setup; one vertical jump then left input drives the route. " +
            "Required rise=" + (leftUpper.bounds.max.y - baseFeet).ToString("F3") + ", measured peak rise=" +
            (peakFeet - baseFeet).ToString("F3") + ", final x=" + bearMover.Body.position.x.ToString("F3"));
        bearInput.ClearDevelopmentInput();
        playRunner.Despawn(bear);

        playReport.phase = "Actual 1-1 geometry: Frog middle lift to left upper exit";
        NetworkObject stageFrog = SpawnTestPlayer(MyEnum.CharacterType.Frog, middleSurface.bounds.min.x + 0.55f,
            middleSurface.bounds.max.y);
        Mover stageFrogMover = stageFrog.GetComponent<Mover>();
        PlayerInput stageFrogInput = stageFrog.GetComponent<PlayerInput>();
        stageFrogInput.InjectDevelopmentInput(Vector2.zero, false);
        wait = Time.time + 2f;
        while (!stageFrogMover.Grounded && Time.time < wait) yield return null;
        // Approach the real edge using normal movement, leaving body clearance from the raised destination's side.
        float frogTakeoffX = Mathf.Max(middleSurface.bounds.min.x + stageFrogMover.BodyCollider.bounds.extents.x + 0.04f,
            leftUpper.bounds.max.x + stageFrogMover.BodyCollider.bounds.extents.x + 0.04f);
        float frogReadySince = -1f;
        wait = Time.time + 2f;
        while (Time.time < wait)
        {
            float offset = frogTakeoffX - stageFrogMover.Body.position.x;
            bool ready = Mathf.Abs(offset) <= 0.04f && StandingOnGeometry(stageFrogMover, middleSurface);
            float control = Mathf.Sign(offset) * Mathf.Clamp(Mathf.Abs(offset) / (stageFrogMover.MoveSpeed * 0.12f), 0.35f, 1f);
            stageFrogInput.InjectDevelopmentInput(ready ? Vector2.zero : Vector2.right * control, false);
            if (!ready) frogReadySince = -1f;
            else if (frogReadySince < 0f) frogReadySince = Time.time;
            else if (Time.time - frogReadySince >= 0.1f) break;
            yield return null;
        }
        bool frogStartedOnLift = StandingOnGeometry(stageFrogMover, middleSurface) && frogReadySince >= 0f;
        baseFeet = stageFrogMover.BodyCollider.bounds.min.y;
        peakFeet = baseFeet;
        bool frogLaunchObserved = false;
        bool frogClearedTop = false;
        bool frogLanded = false;
        float frogLandedSince = -1f;
        float frogLandingX = leftUpper.bounds.max.x - stageFrogMover.BodyCollider.bounds.extents.x - 0.2f;
        var frogTrace = new System.Text.StringBuilder();
        int frogTick = -1;
        start = Time.time;
        stageFrogInput.InjectDevelopmentInput(Vector2.zero, true);
        while (Time.time - start < 2.5f)
        {
            float feet = stageFrogMover.BodyCollider.bounds.min.y;
            peakFeet = Mathf.Max(peakFeet, feet);
            if (stageFrogMover.Body.linearVelocity.y > stageFrogMover.JumpSpeed * 0.5f) frogLaunchObserved = true;
            if (feet >= leftUpper.bounds.max.y + 0.01f) frogClearedTop = true;
            stageFrogInput.InjectDevelopmentInput(frogClearedTop && stageFrogMover.Body.position.x > frogLandingX ? Vector2.left : Vector2.zero, false);
            bool settled = frogClearedTop && StandingOnGeometry(stageFrogMover, leftUpper) &&
                stageFrogMover.BodyCollider.bounds.max.x <= leftUpper.bounds.max.x - 0.02f &&
                Mathf.Abs(feet - leftUpper.bounds.max.y) <= 0.05f &&
                Mathf.Abs(stageFrogMover.Body.linearVelocity.x) < 0.1f && Mathf.Abs(stageFrogMover.Body.linearVelocity.y) < 0.1f;
            if (!settled) frogLandedSince = -1f;
            else if (frogLandedSince < 0f) frogLandedSince = Time.time;
            else if (Time.time - frogLandedSince >= 0.15f) { frogLanded = true; break; }
            if (frogTick != playRunner.Tick.Raw)
            {
                frogTick = playRunner.Tick.Raw;
                frogTrace.Append("t=").Append((Time.time-start).ToString("F3")).Append(",x=").Append(stageFrogMover.Body.position.x.ToString("F3"))
                    .Append(",foot=").Append(feet.ToString("F3")).Append(",vy=").Append(stageFrogMover.Body.linearVelocity.y.ToString("F3")).Append(';');
            }
            yield return null;
        }
        playReport.diagnostics.Add("scene-frog-middle-to-left-upper trace: " + frogTrace);
        RecordPlay("scene-frog-middle-to-left-upper", "Frog", frogStartedOnLift && frogLaunchObserved && frogClearedTop && frogLanded,
            stageFrogMover.BodyCollider.bounds.min.y, leftUpper.bounds.max.y,
            "Actual middle lift and StageOne_LeftUpperExit. Spawn is setup, then normal walking approaches an edge with body clearance. " +
            "One vertical jump clears the step before left input, avoiding side friction during ascent. Required rise=" +
            (leftUpper.bounds.max.y - baseFeet).ToString("F3") + ", measured peak rise=" + (peakFeet - baseFeet).ToString("F3") +
            ", takeoffTargetX=" + frogTakeoffX.ToString("F3") + ", started=" + frogStartedOnLift + ", launched=" + frogLaunchObserved +
            ", cleared=" + frogClearedTop + ", settledLanding=" + frogLanded + ", final=" + stageFrogMover.Body.position);
        stageFrogInput.ClearDevelopmentInput();
        playRunner.Despawn(stageFrog);

        IEnumerator corridor = RunActualMouseLadder("Corridor_Ladder", "Corridor_Entrance", "Corridor_UpperWalkway",
            "scene-mouse-corridor-ladder-exit");
        while (corridor.MoveNext()) yield return null;
        IEnumerator tutorial = RunActualMouseLadder("Tutorial_Mouse_Ladder", "Tutorial_Mouse_Floor", "Tutorial_Mouse_RaisedPassage",
            "scene-mouse-tutorial-ladder-exit");
        while (tutorial.MoveNext()) yield return null;
    }

    private static IEnumerator RunActualMouseLadder(string ladderName, string floorName, string exitName, string resultId)
    {
        playReport.phase = "Actual Mouse ladder and exit: " + ladderName;
        GameObject ladderObject = GameObject.Find(ladderName);
        PcsPuzzleDevice ladderDevice = ladderObject != null ? ladderObject.GetComponent<PcsPuzzleDevice>() : null;
        if (ladderDevice == null || !ladderDevice.IsLadder || ladderDevice.Trigger == null)
            throw new InvalidOperationException("The actual Puzzle ladder is missing: " + ladderName);
        Collider2D startFloor = RequireGeometrySurface(floorName);
        Collider2D upperFloor = RequireGeometrySurface(exitName);
        NetworkObject mouse = SpawnTestPlayer(MyEnum.CharacterType.Mouse, ladderDevice.InteractionPoint.x, startFloor.bounds.max.y);
        Mover mouseMover = mouse.GetComponent<Mover>();
        PlayerInput mouseInput = mouse.GetComponent<PlayerInput>();
        mouseInput.InjectDevelopmentInput(Vector2.zero, false);
        float wait = Time.time + 2f;
        while (!mouseMover.Grounded && Time.time < wait) yield return null;
        bool startedOnFloor = StandingOnGeometry(mouseMover, startFloor);
        float initialFeet = mouseMover.BodyCollider.bounds.min.y;
        mouseInput.InjectDevelopmentInput(Vector2.zero, false, ladder: 1f);
        float start = Time.time;
        while (mouseMover.BodyCollider.bounds.min.y < upperFloor.bounds.max.y + 0.45f && Time.time - start < 9f)
            yield return null;
        bool reachedExitHeight = mouseMover.BodyCollider.bounds.min.y >= upperFloor.bounds.max.y + 0.45f;
        float climbed = mouseMover.BodyCollider.bounds.min.y - initialFeet;
        float exitDirection = upperFloor.bounds.center.x > mouseMover.Body.position.x ? 1f : -1f;
        float exitX = exitDirection > 0f ? upperFloor.bounds.min.x + 0.7f : upperFloor.bounds.max.x - 0.7f;
        mouseInput.InjectDevelopmentInput(Vector2.right * exitDirection, true);
        start = Time.time;
        while (Time.time - start < 2f)
        {
            bool movingToExit = exitDirection > 0f ? mouseMover.Body.position.x < exitX : mouseMover.Body.position.x > exitX;
            mouseInput.InjectDevelopmentInput(movingToExit ? Vector2.right * exitDirection : Vector2.zero, false);
            if (Time.time - start > 0.2f && StandingOnGeometry(mouseMover, upperFloor)) break;
            yield return null;
        }
        RecordPlay(resultId, "Mouse", startedOnFloor && reachedExitHeight && StandingOnGeometry(mouseMover, upperFloor),
            mouseMover.BodyCollider.bounds.min.y, upperFloor.bounds.max.y,
            "Actual " + ladderName + " to " + exitName + "; spawn is fixture setup, W climbs and jump/right input exits. " +
            "Measured ladder rise=" + climbed.ToString("F3") + ", final x=" + mouseMover.Body.position.x.ToString("F3") +
            ". No collider, movement setting, or scene asset is changed by this route test.");
        mouseInput.ClearDevelopmentInput();
        playRunner.Despawn(mouse);
    }


    private static IEnumerator RunActualCarryRoutes()
    {
        SetMovementFixtureSection(1);
        PcsPuzzleDevice gate = GameObject.Find("Wall_ConsoleAccess").GetComponent<PcsPuzzleDevice>();
        if (gate == null || gate.UpperStop == null) throw new InvalidOperationException("Console gate fixture lacks its open stop.");
        Vector2 gateInitial = gate.transform.position;
        // The pressure-plate/open gate contract is tested elsewhere; this fixture isolates carrying through the route.
        gate.ApplyPose(gate.UpperStop.position);
        if (gate.Body != null) gate.Body.linearVelocity = Vector2.zero;
        Physics2D.SyncTransforms();
        Collider2D floor = RequireGeometrySurface("Floor_Landing_Right");
        Collider2D console = RequireGeometrySurface("Landing_Right_Console");
        IEnumerator consoleRoute = RunRabbitMouseCarryHop(floor, console, 1f,
            console.bounds.min.x - 0.45f, "scene-carry-rabbit-mouse-floor-to-console");
        while (consoleRoute.MoveNext()) yield return null;
        Collider2D middle = RequireGeometrySurface("Lift_StageOne_ThreeStops");
        PcsPuzzleDevice lift = middle.GetComponent<PcsPuzzleDevice>();
        lift.ApplyPose(lift.MiddleStop.position);
        if (lift.Body != null) lift.Body.linearVelocity = Vector2.zero;
        Physics2D.SyncTransforms();
        Collider2D upper = RequireGeometrySurface("StageOne_LeftUpperExit");
        IEnumerator upperRoute = RunRabbitMouseCarryHop(middle, upper, -1f,
            middle.bounds.min.x + 0.55f, "scene-carry-rabbit-mouse-middle-to-left-upper");
        while (upperRoute.MoveNext()) yield return null;
        gate.ApplyPose(gateInitial);
        Physics2D.SyncTransforms();
        SetMovementFixtureSection(0);
    }

    private static IEnumerator RunRabbitMouseCarryHop(Collider2D source, Collider2D destination, float direction,
        float spawnX, string resultId)
    {
        playReport.phase = "Actual Rabbit/Mouse carry route: " + resultId;
        NetworkObject rabbit = SpawnTestPlayer(MyEnum.CharacterType.Rabbit, spawnX, source.bounds.max.y);
        NetworkObject mouse = SpawnTestPlayer(MyEnum.CharacterType.Mouse, spawnX - direction * 0.8f, source.bounds.max.y);
        Mover rabbitMover = rabbit.GetComponent<Mover>();
        Mover mouseMover = mouse.GetComponent<Mover>();
        PlayerInput rabbitInput = rabbit.GetComponent<PlayerInput>();
        PlayerInput mouseInput = mouse.GetComponent<PlayerInput>();
        Lifter lifter = rabbit.GetComponent<Lifter>();
        var rider = mouseMover.Rider;
        rabbitInput.InjectDevelopmentInput(Vector2.zero, false);
        mouseInput.InjectDevelopmentInput(Vector2.zero, false);
        float wait = Time.time + 2f;
        while ((!rabbitMover.Grounded || !mouseMover.Grounded) && Time.time < wait) yield return null;
        bool bothStartedOnSource = StandingOnGeometry(rabbitMover, source) && StandingOnGeometry(mouseMover, source);
        rabbitInput.InjectDevelopmentInput(Vector2.zero, false, carry: true);
        wait = Time.time + 0.8f;
        while (Time.time < wait) yield return null;
        bool attached = rider != null && rider.IsRiding && rider.CarrierObject == rabbit &&
            lifter.CarriedObject == mouse && lifter.CurrentRider == rider;
        bool relationshipMaintained = attached;
        float initialFeet = rabbitMover.BodyCollider.bounds.min.y;
        float peakFeet = initialFeet;
        float maximumFollowError = 0f;
        float halfWidth = rabbitMover.BodyCollider.bounds.extents.x;
        float approachX = direction > 0f ? destination.bounds.min.x - halfWidth - 0.05f
            : destination.bounds.max.x + halfWidth + 0.05f;
        float landingX = direction > 0f ? destination.bounds.min.x + 0.65f : destination.bounds.max.x - 0.65f;
        bool second = false;
        bool firstLaunch = false;
        bool secondLaunch = false;
        bool above = false;
        bool landed = false;
        float stableSince = -1f;
        float began = Time.time;
        var trace = new System.Text.StringBuilder();
        int observedTick = -1;
        rabbitInput.InjectDevelopmentInput(Vector2.zero, true);
        while (Time.time - began < 4f)
        {
            float feet = rabbitMover.BodyCollider.bounds.min.y;
            peakFeet = Mathf.Max(peakFeet, feet);
            if (!second && rabbitMover.Body.linearVelocity.y > rabbitMover.JumpSpeed * 0.5f) firstLaunch = true;
            if (second && rabbitMover.Body.linearVelocity.y > rabbitMover.JumpSpeed * 0.5f) secondLaunch = true;
            if (!second && Time.time - began > 0.15f && rabbitMover.Body.linearVelocity.y <= 0f)
            {
                rabbitInput.InjectDevelopmentInput(Vector2.zero, true);
                second = true;
            }
            if (second && feet > destination.bounds.max.y + 0.02f) above = true;
            float targetX = above ? landingX : approachX;
            float offsetX = targetX - rabbitMover.Body.position.x;
            bool moving = direction > 0f ? offsetX > 0f : offsetX < 0f;
            float control = above ? direction : Mathf.Clamp(offsetX / (rabbitMover.MoveSpeed * 0.08f), -1f, 1f);
            rabbitInput.InjectDevelopmentInput(moving ? Vector2.right * control : Vector2.zero, false);
            relationshipMaintained &= rider != null && rider.IsRiding && rider.CarrierObject == rabbit &&
                lifter.CarriedObject == mouse && lifter.CurrentRider == rider;
            float xError = Mathf.Abs(mouseMover.Body.position.x - lifter.Head.transform.position.x);
            float yError = Mathf.Abs(mouseMover.BodyCollider.bounds.min.y - (lifter.Head.Top + 0.015f));
            maximumFollowError = Mathf.Max(maximumFollowError, xError, yError);
            Bounds rb = rabbitMover.BodyCollider.bounds;
            Bounds platform = destination.bounds;
            bool stable = above && StandingOnGeometry(rabbitMover, destination) && rb.min.x >= platform.min.x + 0.02f &&
                rb.max.x <= platform.max.x - 0.02f && Mathf.Abs(feet - platform.max.y) <= 0.05f &&
                Mathf.Abs(rabbitMover.Body.linearVelocity.x) < 0.1f && Mathf.Abs(rabbitMover.Body.linearVelocity.y) < 0.1f;
            if (!stable) stableSince = -1f;
            else if (stableSince < 0f) stableSince = Time.time;
            else if (Time.time - stableSince >= 0.15f) { landed = true; break; }
            int tick = playRunner.Tick.Raw;
            if (tick != observedTick)
            {
                observedTick = tick;
                trace.Append("t=").Append((Time.time-began).ToString("F3")).Append(",x=").Append(rabbitMover.Body.position.x.ToString("F3"))
                    .Append(",foot=").Append(feet.ToString("F3")).Append(",vy=").Append(rabbitMover.Body.linearVelocity.y.ToString("F3"))
                    .Append(",carry=").Append(relationshipMaintained ? 1 : 0).Append(';');
            }
            yield return null;
        }
        float finalFollowX = Mathf.Abs(mouseMover.Body.position.x - lifter.Head.transform.position.x);
        float finalFollowY = Mathf.Abs(mouseMover.BodyCollider.bounds.min.y - (lifter.Head.Top + 0.015f));
        bool carriedRoute = bothStartedOnSource && attached && relationshipMaintained && firstLaunch && secondLaunch && landed &&
            finalFollowX < 0.15f && finalFollowY < 0.15f;
        playReport.diagnostics.Add(resultId + " trace: " + trace);
        RecordPlay(resultId, "Rabbit+Mouse", carriedRoute, rabbitMover.BodyCollider.bounds.min.y, destination.bounds.max.y,
            "Actual scene solids; section=1, open console gate and middle lift pose are explicit fixture conditions. Only initial spawns set player poses. " +
            "Rabbit G carries Mouse, normal double jump and movement reach the target. Both replicated carry endpoints must remain consistent. " +
            "Single private Shared peer owns both actors; this does not prove cross-client timing. started=" + bothStartedOnSource +
            ", attached=" + attached + ", maintained=" + relationshipMaintained + ", firstLaunch=" + firstLaunch + ", secondLaunch=" + secondLaunch +
            ", landed=" + landed + ", requiredRise=" + (destination.bounds.max.y - initialFeet).ToString("F3") +
            ", peakRise=" + (peakFeet-initialFeet).ToString("F3") + ", maxFollowError=" + maximumFollowError.ToString("F3") +
            ", finalFollowXY=" + finalFollowX.ToString("F3") + "/" + finalFollowY.ToString("F3"));

        playReport.phase = "Mouse owner G drop after route: " + resultId;
        rabbitInput.InjectDevelopmentInput(Vector2.zero, false);
        float mouseStartX = mouseMover.Body.position.x;
        mouseInput.InjectDevelopmentInput(Vector2.zero, false, carry: true);
        wait = Time.time + 0.15f;
        while (Time.time < wait) yield return null;
        bool releasedByOwner = attached && !rider.IsRiding && lifter.CarriedObject == null && mouseInput.CanMoveInput;
        wait = Time.time + 0.65f;
        while (Time.time < wait)
        {
            mouseInput.InjectDevelopmentInput(Vector2.right * direction, false);
            yield return null;
        }
        mouseInput.InjectDevelopmentInput(Vector2.zero, false);
        wait = Time.time + 0.2f;
        while (Time.time < wait) yield return null;
        RecordPlay(resultId + "-mouse-owner-drop", "Mouse", carriedRoute && releasedByOwner && !rider.IsRiding &&
            lifter.CarriedObject == null && StandingOnGeometry(mouseMover, destination) &&
            (mouseMover.Body.position.x - mouseStartX) * direction > 0.3f,
            (mouseMover.Body.position.x - mouseStartX) * direction, 0.3f,
            "Requires the preceding carry route to pass. Mouse owner's G releases both endpoints, then ordinary horizontal input walks off Rabbit and lands on the actual target platform.");
        mouseInput.ClearDevelopmentInput();
        rabbitInput.ClearDevelopmentInput();
        if (rider != null && rider.IsRiding) rider.Drop();
        playRunner.Despawn(mouse);
        playRunner.Despawn(rabbit);
    }

    private static void SetMovementFixtureSection(int section)
    {
        PcsPuzzleDirector director = PcsPuzzleDirector.Instance;
        if (director == null || director.enabled || !director.HasStateAuthority)
            throw new InvalidOperationException("Movement fixture needs the valid, disabled, locally authoritative director.");
        var property = typeof(PcsPuzzleDirector).GetProperty("ActiveSection");
        if (property == null) throw new InvalidOperationException("Missing section fixture property.");
        // Test setup selects device visibility/filtering only; it does not claim progression completion.
        property.SetValue(director, section);
    }

    private static IEnumerator RunFrogFollowupTests()
    {
        playReport.phase = "Frog normal input: pull eligible player";
        SetMovementFixtureSection(0);
        NetworkObject frog = SpawnTestPlayer(MyEnum.CharacterType.Frog, -84f);
        NetworkObject mouse = SpawnTestPlayer(MyEnum.CharacterType.Mouse, -80f);
        Mover frogMover = frog.GetComponent<Mover>();
        Mover mouseMover = mouse.GetComponent<Mover>();
        PlayerInput frogInput = frog.GetComponent<PlayerInput>();
        PlayerInput mouseInput = mouse.GetComponent<PlayerInput>();
        frogInput.InjectDevelopmentInput(Vector2.zero, false);
        mouseInput.InjectDevelopmentInput(Vector2.zero, false);
        float wait = Time.time + 2f;
        while ((!frogMover.Grounded || !mouseMover.Grounded) && Time.time < wait) yield return null;
        bool grounded = frogMover.Grounded && mouseMover.Grounded;
        float initialMouseX = mouseMover.Body.position.x;
        frogInput.InjectDevelopmentInput(Vector2.zero, false, ability: true, aim: (Vector2)mouseMover.BodyCollider.bounds.center);
        wait = Time.time + 1.5f;
        while (Time.time < wait) yield return null;
        float finalGap = mouseMover.Body.position.x - frogMover.Body.position.x;
        RecordPlay("frog-pull-eligible-player", "Frog+Mouse", grounded && initialMouseX - mouseMover.Body.position.x > 2.5f &&
            Mathf.Abs(finalGap - 0.75f) < 0.3f && mouseMover.Grounded, finalGap, 0.75f,
            "Normal F/aim input invokes player-owner pull RPC and Mover motion. No position writes after spawn. " +
            "Mouse displacement=" + (initialMouseX - mouseMover.Body.position.x).ToString("F3") +
            ", feedback=" + frog.GetComponent<PcsPlayerAbilities>().Feedback + ". One Shared peer owns both actors.");
        mouseInput.ClearDevelopmentInput();
        playRunner.Despawn(mouse);

        playReport.phase = "Frog normal input: Bear mass rejection";
        NetworkObject bear = SpawnTestPlayer(MyEnum.CharacterType.Bear, -80f);
        Mover bearMover = bear.GetComponent<Mover>();
        PlayerInput bearInput = bear.GetComponent<PlayerInput>();
        bearInput.InjectDevelopmentInput(Vector2.zero, false);
        wait = Time.time + 2f;
        while (!bearMover.Grounded && Time.time < wait) yield return null;
        bool bearStartedGrounded = bearMover.Grounded;
        float bearStartX = bearMover.Body.position.x;
        frogInput.InjectDevelopmentInput(Vector2.zero, false, ability: true, aim: (Vector2)bearMover.BodyCollider.bounds.center);
        wait = Time.time + 0.6f;
        while (Time.time < wait) yield return null;
        float bearDelta = Mathf.Abs(bearMover.Body.position.x - bearStartX);
        RecordPlay("frog-pull-overweight-rejected", "Frog+Bear", bearStartedGrounded && bearMover.Body.mass > frogMover.Body.mass &&
            bearDelta < 0.1f && bearMover.Grounded, bearDelta, 0f,
            "Normal F aimed at an otherwise nearby unobstructed Bear; actual mass eligibility must reject it.");
        bearInput.ClearDevelopmentInput();
        frogInput.ClearDevelopmentInput();
        playRunner.Despawn(bear);
        playRunner.Despawn(frog);

        playReport.phase = "Actual 1-1 Frog floor/anchor/middle route";
        SetMovementFixtureSection(1);
        Collider2D middle = RequireGeometrySurface("Lift_StageOne_ThreeStops");
        Collider2D floor = RequireGeometrySurface("Floor_Landing_Right");
        PcsPuzzleDevice lift = middle.GetComponent<PcsPuzzleDevice>();
        lift.ApplyPose(lift.MiddleStop.position);
        if (lift.Body != null) lift.Body.linearVelocity = Vector2.zero;
        GameObject anchorObject = GameObject.Find("StageOne_FrogAnchor");
        PcsPuzzleDevice anchor = anchorObject != null ? anchorObject.GetComponent<PcsPuzzleDevice>() : null;
        if (anchor == null || anchor.Kind != PcsDeviceKind.Anchor)
            throw new InvalidOperationException("Actual StageOne_FrogAnchor is missing.");
        Physics2D.SyncTransforms();
        frog = SpawnTestPlayer(MyEnum.CharacterType.Frog, middle.bounds.max.x + 0.8f, floor.bounds.max.y);
        frogMover = frog.GetComponent<Mover>();
        frogInput = frog.GetComponent<PlayerInput>();
        frogInput.InjectDevelopmentInput(Vector2.zero, false);
        wait = Time.time + 2f;
        while (!frogMover.Grounded && Time.time < wait) yield return null;
        bool startedOnRight = StandingOnGeometry(frogMover, floor);
        Vector2 anchorStart = frogMover.Body.position;
        float peakFeet = frogMover.BodyCollider.bounds.min.y;
        bool enteredMiddle = false;
        frogInput.InjectDevelopmentInput(Vector2.zero, false, ability: true, aim: anchor.InteractionPoint);
        float began = Time.time;
        while (Time.time - began < 3.5f)
        {
            peakFeet = Mathf.Max(peakFeet, frogMover.BodyCollider.bounds.min.y);
            if (frogMover.BodyCollider.bounds.min.y > middle.bounds.max.y + 0.06f) enteredMiddle = true;
            frogInput.InjectDevelopmentInput(enteredMiddle && frogMover.Body.position.x > middle.bounds.max.x - 0.55f
                ? Vector2.left : Vector2.zero, false, aim: anchor.InteractionPoint);
            if (Time.time - began > 0.5f && StandingOnGeometry(frogMover, middle)) break;
            yield return null;
        }
        RecordPlay("scene-frog-anchor-floor-to-middle", "Frog", startedOnRight && enteredMiddle && StandingOnGeometry(frogMover, middle),
            frogMover.BodyCollider.bounds.min.y, middle.bounds.max.y,
            "Actual anchor/lift/floor positions and colliders remain. Section=1 and lift middle pose are explicit setup, not hack completion. " +
            "F/aim then left input only; start=" + anchorStart + ", anchor=" + anchor.InteractionPoint +
            ", peakFeet=" + peakFeet.ToString("F3") + ", final=" + frogMover.Body.position +
            ", feedback=" + frog.GetComponent<PcsPlayerAbilities>().Feedback);
        frogInput.ClearDevelopmentInput();
        playRunner.Despawn(frog);

        SetMovementFixtureSection(0);
        IEnumerator returnRoute = RunTutorialReturnDescent();
        while (returnRoute.MoveNext()) yield return null;
    }

    private static IEnumerator RunTutorialReturnDescent()
    {
        playReport.phase = "Actual tutorial Frog return ladder descent";
        Collider2D bridge = RequireGeometrySurface("Tutorial_Frog_ReturnBridge");
        Collider2D returnFloor = RequireGeometrySurface("Tutorial_ReturnFloor");
        PcsPuzzleDevice ladder = GameObject.Find("Tutorial_ReturnLadder").GetComponent<PcsPuzzleDevice>();
        if (ladder == null || !ladder.IsLadder || ladder.Trigger == null)
            throw new InvalidOperationException("Actual tutorial return ladder is missing.");
        NetworkObject frog = SpawnTestPlayer(MyEnum.CharacterType.Frog, bridge.bounds.max.x - 0.4f, bridge.bounds.max.y);
        Mover mover = frog.GetComponent<Mover>();
        PlayerInput input = frog.GetComponent<PlayerInput>();
        input.InjectDevelopmentInput(Vector2.zero, false);
        float wait = Time.time + 2f;
        while (!mover.Grounded && Time.time < wait) yield return null;
        bool startedOnBridge = StandingOnGeometry(mover, bridge);
        float initialFeet = mover.BodyCollider.bounds.min.y;
        // Walk off the real bridge before requesting downward ladder motion; the bridge remains solid.
        wait = Time.time + 2f;
        while (mover.Body.position.x < ladder.InteractionPoint.x - 0.05f && Time.time < wait)
        {
            input.InjectDevelopmentInput(Vector2.right, false);
            yield return null;
        }
        bool enteredLadder = ladder.Trigger.bounds.Intersects(mover.BodyCollider.bounds);
        input.InjectDevelopmentInput(Vector2.zero, false, ladder: -1f);
        wait = Time.time + 9f;
        while (mover.BodyCollider.bounds.min.y > returnFloor.bounds.max.y + 0.08f && Time.time < wait)
            yield return null;
        bool reachedReturnFloor = mover.BodyCollider.bounds.min.y <= returnFloor.bounds.max.y + 0.08f;
        float descentFeet = mover.BodyCollider.bounds.min.y;
        float rendezvousX = GameObject.Find("Tutorial_FourRoleRendezvous").transform.position.x;
        wait = Time.time + 2f;
        while (mover.Body.position.x < rendezvousX - 0.1f && Time.time < wait)
        {
            input.InjectDevelopmentInput(Vector2.right, false);
            yield return null;
        }
        input.InjectDevelopmentInput(Vector2.zero, false);
        wait = Time.time + 0.2f;
        while (Time.time < wait) yield return null;
        RecordPlay("scene-frog-tutorial-return-ladder", "Frog", startedOnBridge && enteredLadder && reachedReturnFloor &&
            StandingOnGeometry(mover, returnFloor) && Mathf.Abs(mover.Body.position.x - rendezvousX) < 0.3f,
            initialFeet - descentFeet, bridge.bounds.max.y - returnFloor.bounds.max.y,
            "Actual Frog return bridge, continuous return ladder, divider and ground remain. Initial spawn only; right to leave bridge, S to descend, " +
            "right to rendezvous use normal inputs. This is route reachability, not tutorial completion latch. Final=" + mover.Body.position);
        input.ClearDevelopmentInput();
        playRunner.Despawn(frog);
    }

    private static void RecordPlay(string id, string role, bool passed, float value, float expected, string detail)
    {
        playReport.results.Add(new PcsPlayResult
        {
            id = id, role = role, status = passed ? "PASS" : "FAIL", measured = value, expected = expected, detail = detail
        });
        SavePlayReport();
    }

    private static void CapturePlayTestLog(string message, string stack, LogType type)
    {
        if (playReport == null || (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)) return;
        string safe = System.Text.RegularExpressions.Regex.Replace(message, "(?i)(appid|token|password|secret)\\s*[:=]\\s*\\S+", "$1=<redacted>");
        if (playReport.errors.Count < 40) playReport.errors.Add(safe);
    }

    private static async void FinishPlayerPlayTests(Exception exception)
    {
        if (playFinishing) return;
        if (playReport == null)
        {
            RecoverReloadedPlayTest();
            return;
        }
        playFinishing = true;
        playRoutine = null;
        if (exception != null) playReport.errors.Add(exception.ToString());
        bool failed = exception != null || playReport.errors.Count > 0 || playReport.results.Count != 36 ||
            playReport.results.Exists(x => x.status == "FAIL");
        playReport.status = failed ? "FAIL" : "PASS";
        playReport.phase = "Finishing and restoring edit mode";
        playReport.finishedUtc = DateTime.UtcNow.ToString("O");
        SavePlayReport();
        SessionState.SetString(PlayTestFlag, "restore");
        Application.logMessageReceived -= CapturePlayTestLog;
        PcsPuzzleDirector director = PcsPuzzleDirector.Instance;
        if (director != null) director.enabled = playDirectorWasEnabled;
        try
        {
            if (playRunner != null) await playRunner.Shutdown();
        }
        catch (Exception ex)
        {
            playReport.errors.Add("Test Runner cleanup failed: " + ex.Message);
            playReport.status = "FAIL";
            SavePlayReport();
        }
        EditorApplication.ExitPlaymode();
    }

    private static void SavePlayReport()
    {
        if (playReport == null) return;
        playReport.heartbeatUtc = DateTime.UtcNow.ToString("O");
        Directory.CreateDirectory(EvidenceDirectory);
        File.WriteAllText(Path.Combine(EvidenceDirectory, "play-tests.json"), JsonUtility.ToJson(playReport, true));
    }
}

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
    private const string LayoutPlayFlag = "PCS.Layout.PlayTests";
    private const string LayoutPlayModeFlag = "PCS.Layout.PlayMode";
    private const string LayoutPlayScene = "Assets/Scenes/Puzzle.unity";
    private static readonly string LayoutPlayEvidence = Path.Combine(Path.GetTempPath(), "FourPawsLayoutRefinement");

    [Serializable] private class LayoutPlayCheck
    {
        public string id;
        public string role;
        public string status;
        public string detail;
    }

    [Serializable] private class LayoutPlayReport
    {
        public string startedUtc;
        public string finishedUtc;
        public string heartbeatUtc;
        public string status;
        public string phase;
        public string mode;
        public string unityVersion;
        public string boundary = "Actual GameMode.Shared Runner and normally spawned player prefabs in the saved Puzzle scene. " +
            "One private process owns test actors sequentially; this does not prove remote synchronization or four-human completion. " +
            "Fixtures select the section and initial actor pose only; ordinary PlayerInput/Mover ticks move actors thereafter. " +
            "Column-only checks pause the director and exercise the existing MoveAuthority/MovePosition API, so those checks prove visible attachment, not puzzle input transitions. " +
            "No Scene is saved, no completion flag is forced, and no Bear tutorial task is completed by a fixture.";
        public List<LayoutPlayCheck> checks = new List<LayoutPlayCheck>();
        public List<string> diagnostics = new List<string>();
        public List<string> errors = new List<string>();
    }

    private sealed class LayoutRamp
    {
        public BoxCollider2D collider;
        public Vector2 low;
        public Vector2 high;
    }

    private static LayoutPlayReport layoutPlayReport;
    private static NetworkRunner layoutPlayRunner;
    private static Task<StartGameResult> layoutPlayStart;
    private static PcsPuzzleDirector layoutPlayDirector;
    private static readonly Stack<IEnumerator> layoutPlayRoutines = new Stack<IEnumerator>();
    private static readonly List<NetworkObject> layoutPlayActors = new List<NetworkObject>();
    private static bool layoutPlayFinishing;
    private static double layoutPlayStarted;
    private static double layoutPlaySceneDeadline;
    private static double layoutPlayHeartbeat;

    [InitializeOnLoadMethod]
    private static void LayoutInstallPlayCallbacks()
    {
        EditorApplication.playModeStateChanged -= LayoutPlayModeChanged;
        EditorApplication.playModeStateChanged += LayoutPlayModeChanged;
        EditorApplication.update -= LayoutPollPlay;
        EditorApplication.update += LayoutPollPlay;
    }

    public static void RunLayoutPlayTests() { LayoutBeginPlay("all"); }
    public static void RunLayoutMovementTests() { LayoutBeginPlay("movement"); }
    public static void RunLayoutColumnTests() { LayoutBeginPlay("columns"); }
    public static void RunLayoutTutorialTests() { LayoutBeginPlay("tutorials"); }
    public static void RunLayoutAccessTests() { LayoutBeginPlay("access"); }
    public static void RunLayoutDoorTests() { LayoutBeginPlay("doors"); }

    private static void LayoutBeginPlay(string mode)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop the existing Play session before layout checks.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Save the current user scene before layout checks.");
        if (SceneUtility.GetBuildIndexByScenePath(LayoutPlayScene) < 0)
            throw new InvalidOperationException("Puzzle must already be in Build Settings.");
        // The bundled Fusion custom Inspector has a missing editor-resource reference; keep
        // runtime verification independent of repainting a selected NetworkBehaviour.
        Selection.activeObject = null;
        Directory.CreateDirectory(LayoutPlayEvidence);
        string path = Path.Combine(LayoutPlayEvidence, "layout-play-" + mode + ".json");
        if (File.Exists(path))
            File.Copy(path, Path.Combine(LayoutPlayEvidence, "layout-play-" + mode + "-" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + ".json"));
        SessionState.SetString(LayoutPlayModeFlag, mode);
        SessionState.SetString(LayoutPlayFlag, "requested");
        EditorSceneManager.OpenScene(LayoutPlayScene);
        EditorApplication.EnterPlaymode();
    }

    private static void LayoutPlayModeChanged(PlayModeStateChange state)
    {
        string flag = SessionState.GetString(LayoutPlayFlag, "");
        if (state == PlayModeStateChange.EnteredPlayMode && flag == "requested")
        {
            SessionState.SetString(LayoutPlayFlag, "running");
            layoutPlayReport = new LayoutPlayReport { startedUtc = DateTime.UtcNow.ToString("O"), status = "RUNNING",
                phase = "Starting private Shared Runner", unityVersion = Application.unityVersion,
                mode = SessionState.GetString(LayoutPlayModeFlag, "all") };
            layoutPlayFinishing = false;
            layoutPlayStarted = EditorApplication.timeSinceStartup;
            layoutPlayHeartbeat = 0;
            layoutPlayDirector = null;
            layoutPlayActors.Clear();
            layoutPlayRoutines.Clear();
            Application.logMessageReceived += LayoutCapturePlayLog;
            GameObject host = new GameObject("[Development] Layout validation Runner");
            UnityEngine.Object.DontDestroyOnLoad(host);
            layoutPlayRunner = host.AddComponent<NetworkRunner>();
            NetworkSceneManagerDefault scenes = host.AddComponent<NetworkSceneManagerDefault>();
            layoutPlayStart = layoutPlayRunner.StartGame(new StartGameArgs { GameMode = GameMode.Shared,
                SessionName = "PCS_Layout_" + Guid.NewGuid().ToString("N"), PlayerCount = 1, IsOpen = false, IsVisible = false,
                Scene = SceneRef.FromIndex(SceneUtility.GetBuildIndexByScenePath(LayoutPlayScene)), SceneManager = scenes });
            LayoutSavePlayReport();
        }
        else if (state == PlayModeStateChange.ExitingPlayMode && flag == "running")
        {
            if (layoutPlayReport != null)
            {
                layoutPlayReport.status = "INTERRUPTED";
                layoutPlayReport.finishedUtc = DateTime.UtcNow.ToString("O");
                LayoutSavePlayReport();
            }
            SessionState.SetString(LayoutPlayFlag, "restore");
            Application.logMessageReceived -= LayoutCapturePlayLog;
        }
        else if (state == PlayModeStateChange.EnteredEditMode && !string.IsNullOrEmpty(flag))
        {
            SessionState.SetString(LayoutPlayFlag, "");
            if (SceneManager.GetActiveScene().path != LayoutPlayScene) EditorSceneManager.OpenScene(LayoutPlayScene);
        }
    }

    private static void LayoutPollPlay()
    {
        if (!EditorApplication.isPlaying || layoutPlayFinishing || SessionState.GetString(LayoutPlayFlag, "") != "running") return;
        if (layoutPlayReport == null)
        {
            string path = Path.Combine(LayoutPlayEvidence, "layout-play-" + SessionState.GetString(LayoutPlayModeFlag, "all") + ".json");
            if (File.Exists(path)) layoutPlayReport = JsonUtility.FromJson<LayoutPlayReport>(File.ReadAllText(path));
            if (layoutPlayReport != null)
            {
                layoutPlayReport.status = "INTERRUPTED";
                layoutPlayReport.errors.Add("Domain reload interrupted this attempt; no unrecorded checks passed.");
                layoutPlayReport.finishedUtc = DateTime.UtcNow.ToString("O");
                LayoutSavePlayReport();
            }
            SessionState.SetString(LayoutPlayFlag, "restore");
            EditorApplication.ExitPlaymode();
            return;
        }
        try
        {
            if (EditorApplication.timeSinceStartup - layoutPlayStarted > 600d)
                throw new TimeoutException("Layout Play checks exceeded 600 seconds.");
            if (EditorApplication.timeSinceStartup >= layoutPlayHeartbeat)
            {
                LayoutSavePlayReport();
                layoutPlayHeartbeat = EditorApplication.timeSinceStartup + 1d;
            }
            if (layoutPlayStart != null)
            {
                if (!layoutPlayStart.IsCompleted) return;
                if (layoutPlayStart.IsFaulted) throw layoutPlayStart.Exception;
                if (!layoutPlayStart.Result.Ok) throw new InvalidOperationException("Shared Runner failed: " + layoutPlayStart.Result.ShutdownReason);
                layoutPlayStart = null;
                layoutPlaySceneDeadline = EditorApplication.timeSinceStartup + 20d;
                layoutPlayReport.phase = "Waiting for saved Puzzle NetworkObject registration";
            }
            if (layoutPlayDirector == null)
            {
                PcsPuzzleDirector candidate = PcsPuzzleDirector.Instance;
                if (candidate == null || !candidate.CanSpawnPlayers)
                {
                    if (EditorApplication.timeSinceStartup > layoutPlaySceneDeadline)
                        throw new TimeoutException("Saved Puzzle director did not register within 20 seconds.");
                    return;
                }
                layoutPlayDirector = candidate;
                LayoutCheck("shared-registration", "Scene", candidate.HasStateAuthority && layoutPlayRunner.GameMode == GameMode.Shared,
                    "Actual scene registration; one private client. No substitute single mode.");
                layoutPlayRoutines.Push(LayoutRunSelectedTests());
            }
            if (layoutPlayRoutines.Count == 0) { LayoutFinishPlay(null); return; }
            IEnumerator routine = layoutPlayRoutines.Peek();
            if (!routine.MoveNext()) layoutPlayRoutines.Pop();
            else if (routine.Current is IEnumerator child) layoutPlayRoutines.Push(child);
        }
        catch (Exception ex) { LayoutFinishPlay(ex); }
    }

    private static IEnumerator LayoutRunSelectedTests()
    {
        string mode = layoutPlayReport.mode;
        if (mode == "manual") yield return LayoutRunManualDeviceTests();
        if (mode == "all" || mode == "movement") yield return LayoutRunZigzag();
        if (mode == "all" || mode == "tutorials")
        {
            yield return LayoutRunRabbitTutorial();
            yield return LayoutRunFrogTutorial();
            yield return LayoutRunMouseTutorial();
        }
        if (mode == "all" || mode == "access") yield return LayoutRunCarryAccess();
        if (mode == "all" || mode == "doors") yield return LayoutRunDoorTests();
        if (mode == "all" || mode == "columns") yield return LayoutRunColumns();
    }

    private static void LayoutSelectSection(int section)
    {
        PropertyInfo property = typeof(PcsPuzzleDirector).GetProperty("ActiveSection", BindingFlags.Public | BindingFlags.Instance);
        property.SetValue(layoutPlayDirector, section);
        layoutPlayReport.diagnostics.Add("Fixture: section=" + section + ". Completion/hack flags were not set.");
    }

    private static NetworkObject LayoutSpawn(MyEnum.CharacterType role, Vector2 feet)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player/Player_" + role + ".prefab");
        if (prefab == null) throw new InvalidOperationException("Missing normal player prefab " + role);
        CapsuleCollider2D capsule = prefab.GetComponent<CapsuleCollider2D>();
        if (capsule == null) throw new InvalidOperationException("Expected the existing capsule on " + role);
        float offset = (capsule.offset.y - capsule.size.y * .5f) * prefab.transform.localScale.y;
        NetworkObject actor = layoutPlayRunner.Spawn(prefab, new Vector3(feet.x, feet.y - offset + .06f, 0f),
            Quaternion.identity, layoutPlayRunner.LocalPlayer);
        layoutPlayRunner.SetPlayerObject(layoutPlayRunner.LocalPlayer, actor);
        layoutPlayActors.Add(actor);
        actor.GetComponent<PlayerInput>().InjectDevelopmentInput(Vector2.zero, false);
        layoutPlayReport.diagnostics.Add("Fixture: " + role + " initial feet=" + feet + "; standard prefab/normal motion thereafter.");
        return actor;
    }

    private static void LayoutDespawn(NetworkObject actor)
    {
        if (actor == null) return;
        actor.GetComponent<PlayerInput>().ClearDevelopmentInput();
        layoutPlayRunner.Despawn(actor);
        layoutPlayActors.Remove(actor);
    }

    private static List<LayoutRamp> LayoutFindRamps()
    {
        List<LayoutRamp> ramps = new List<LayoutRamp>();
        foreach (BoxCollider2D box in UnityEngine.Object.FindObjectsByType<BoxCollider2D>(FindObjectsSortMode.None))
        {
            if (!box.enabled || box.isTrigger || box.transform.position.x < -1f || box.transform.position.x > 18f ||
                box.transform.position.y < 3f || box.transform.position.y > 17f ||
                (!box.name.Contains("Landing_Right_Console") && !box.name.Contains("Zigzag"))) continue;
            Vector2 left = box.transform.TransformPoint(box.offset + new Vector2(-box.size.x * .5f, box.size.y * .5f));
            Vector2 right = box.transform.TransformPoint(box.offset + box.size * .5f);
            float angle = Mathf.Abs(Mathf.Atan2(right.y - left.y, right.x - left.x) * Mathf.Rad2Deg);
            if (angle < 15f || angle > 70f) continue;
            ramps.Add(new LayoutRamp { collider = box, low = left.y < right.y ? left : right, high = left.y < right.y ? right : left });
        }
        ramps.Sort((a, b) => a.low.y.CompareTo(b.low.y));
        if (ramps.Count != 3) throw new InvalidOperationException("Expected the latest three zigzag ramps, found " + ramps.Count);
        foreach (LayoutRamp ramp in ramps)
            layoutPlayReport.diagnostics.Add("Actual ramp " + LayoutTestPath(ramp.collider.transform) + " low=" + ramp.low + " high=" + ramp.high +
                " collider=" + ramp.collider.size + "/" + ramp.collider.offset + "; effector=" + ramp.collider.usedByEffector);
        return ramps;
    }

    private static IEnumerator LayoutRunZigzag()
    {
        LayoutSelectSection(1);
        List<LayoutRamp> ramps = LayoutFindRamps();
        MyEnum.CharacterType[] roles = { MyEnum.CharacterType.Mouse, MyEnum.CharacterType.Bear, MyEnum.CharacterType.Frog, MyEnum.CharacterType.Rabbit };
        foreach (MyEnum.CharacterType role in roles)
        {
            LayoutRamp first = ramps[0];
            Vector2 firstPoint = LayoutFindFloor(new Vector2(6.3f, 3.2f));
            NetworkObject actor = LayoutSpawn(role, firstPoint);
            Mover mover = actor.GetComponent<Mover>();
            PlayerInput input = actor.GetComponent<PlayerInput>();
            yield return LayoutWaitGrounded(mover, 3f);
            yield return LayoutWait(.3f);
            LayoutCheck("zigzag-initial-ground", role.ToString(), mover.Grounded, LayoutActorState(mover));
            var entryStep = LayoutNamedSurface("Zigzag_EntryStep");
            yield return LayoutSingleHop(actor, new Vector2(7.05f, entryStep.bounds.max.y), entryStep, "zigzag-entry-step");
            yield return LayoutSingleHop(actor, first.low + Vector2.left * .1f, first.collider, "zigzag-first-ramp-entry");
            for (int i = 0; i < ramps.Count; i++)
            {
                LayoutRamp ramp = ramps[i];
                if (i == 1)
                {
                    input.InjectDevelopmentInput(Vector2.left, true);
                    yield return null;
                }
                if (i == 2)
                {
                    // Cross the actual turn landing before reversing toward the last ramp.
                    yield return LayoutWalkTo(actor, Vector2.Lerp(ramps[i - 1].low, ramps[i - 1].high, .99f), 8f, "zigzag-second-tip", false);
                    var turn = LayoutNamedSurface("Zigzag_MiddleTurnStep");
                    yield return LayoutSingleHop(actor, new Vector2(5.75f, turn.bounds.max.y), turn, "zigzag-middle-step");
                    yield return LayoutSingleHop(actor, new Vector2(5.1f, 11.62f), null, "zigzag-middle-turn-landing");
                    yield return LayoutWalkTo(actor, new Vector2(.5f, 11.69f), 8f, "zigzag-last-entry-approach", true);
                    var lastEntry = LayoutNamedSurface("Zigzag_LastEntryStep");
                    yield return LayoutSingleHop(actor, new Vector2(.65f, lastEntry.bounds.max.y), lastEntry, "zigzag-last-entry-step");
                    yield return LayoutSingleHop(actor, ramp.low + Vector2.left * .1f, ramp.collider, "zigzag-last-ramp-entry");
                }
                if (i > 0)
                    yield return LayoutWalkTo(actor, Vector2.Lerp(ramp.low, ramp.high, .10f), 12f, "zigzag-turn-" + i, true, ramp.collider);
                yield return LayoutWalkTo(actor, Vector2.Lerp(ramp.low, ramp.high, .48f), 15f, "zigzag-up-mid-" + i, true, ramp.collider);
                input.InjectDevelopmentInput(Vector2.zero, false);
                yield return LayoutWait(.25f);
                Vector2 stopped = mover.Body.position;
                yield return LayoutWait(.4f);
                var contacts = new List<ContactPoint2D>();
                mover.Body.GetContacts(contacts);
                bool onThisRamp = contacts.Exists(c => c.enabled &&
                    (c.collider == ramp.collider || c.otherCollider == ramp.collider));
                LayoutCheck("zigzag-stop-" + i, role.ToString(), mover.Grounded && onThisRamp && Vector2.Distance(stopped, mover.Body.position) < .18f,
                    "Stopped on actual slope: contact=" + onThisRamp + "; delta=" + Vector2.Distance(stopped, mover.Body.position).ToString("F4") + "; " + LayoutActorState(mover));
                if (i == 0)
                {
                    yield return LayoutWalkTo(actor, Vector2.Lerp(ramp.low, ramp.high, .30f), 6f, "zigzag-descend", false);
                    float y = mover.Body.position.y;
                    float peak = y;
                    input.InjectDevelopmentInput(Vector2.zero, true);
                    float deadline = Time.time + 3f;
                    bool airborne = false;
                    while (Time.time < deadline)
                    {
                        peak = Mathf.Max(peak, mover.Body.position.y);
                        airborne |= !mover.Grounded;
                        contacts.Clear(); mover.Body.GetContacts(contacts);
                        if (airborne && mover.Grounded && Time.time > deadline - 2.7f &&
                            contacts.Exists(c => c.enabled && (c.collider == ramp.collider || c.otherCollider == ramp.collider))) break;
                        yield return null;
                    }
                    contacts.Clear(); mover.Body.GetContacts(contacts);
                    bool landedOnRamp = contacts.Exists(c => c.enabled && (c.collider == ramp.collider || c.otherCollider == ramp.collider));
                    LayoutCheck("zigzag-jump-and-land", role.ToString(), airborne && peak - y > .15f && mover.Grounded && landedOnRamp,
                        "One normal jump press, height=" + (peak - y).ToString("F3") + "; " + LayoutActorState(mover));
                }
                // At the shared tip either adjacent deck may support the body; each ramp's midpoint
                // has its own strict contact check, and the following turn requires the next ramp.
                yield return LayoutWalkTo(actor, Vector2.Lerp(ramp.low, ramp.high, .99f), 15f, "zigzag-up-end-" + i, true);
            }
            PcsPuzzleDevice checkpoint = null;
            foreach (PcsPuzzleDevice device in layoutPlayDirector.Devices)
                if (device != null && device.Section == 1 && device.Kind == PcsDeviceKind.Checkpoint) { checkpoint = device; break; }
            if (checkpoint != null)
            {
                Collider2D dock = LayoutNamedSurface("Connection_ElevatorDock_Right");
                yield return LayoutWalkTo(actor, new Vector2(dock.bounds.center.x, dock.bounds.max.y), 15f,
                    "zigzag-checkpoint-approach", true);
                LayoutCheck("zigzag-checkpoint-overlap", role.ToString(), checkpoint.Trigger != null &&
                    checkpoint.Trigger.bounds.Intersects(mover.BodyCollider.bounds), "Actual checkpoint overlap only; one actor cannot satisfy four-role progression. " + LayoutActorState(mover));
            }
            else LayoutCheck("zigzag-checkpoint-present", role.ToString(), false, "No current section-one checkpoint found.");
            ScreenCapture.CaptureScreenshot(Path.Combine(LayoutPlayEvidence, "layout-zigzag-" + role + ".png"));
            yield return LayoutWait(.15f);
            LayoutDespawn(actor);
        }
    }

    private static IEnumerator LayoutSingleHop(NetworkObject actor, Vector2 feetTarget, Collider2D support, string id)
    {
        Mover mover = actor.GetComponent<Mover>();
        PlayerInput input = actor.GetComponent<PlayerInput>();
        yield return LayoutWaitGrounded(mover, 2f);
        input.InjectDevelopmentInput(Vector2.zero, true);
        float deadline = Time.time + 4f, startFeet = mover.BodyCollider.bounds.min.y, peak = startFeet;
        bool airborne = false, reached = false;
        string actualSupport = "none";
        var contacts = new List<ContactPoint2D>();
        layoutPlayReport.phase = actor.name + " " + id;
        while (Time.time < deadline)
        {
            float feet = mover.BodyCollider.bounds.min.y;
            peak = Mathf.Max(peak, feet);
            airborne |= !mover.Grounded;
            float dx = feetTarget.x - mover.Body.position.x;
            // These surfaces are one-way: ordinary simultaneous jump/steering preserves the short Mouse arc.
            float move = Mathf.Clamp(dx / (mover.MoveSpeed * .12f), -1f, 1f);
            input.InjectDevelopmentInput(Vector2.right * move, false);
            contacts.Clear(); mover.Body.GetContacts(contacts);
            bool supported = false;
            foreach (var contact in contacts)
            {
                var other = contact.collider != null && contact.collider.attachedRigidbody == mover.Body ? contact.otherCollider : contact.collider;
                if (contact.enabled && other != null && other.attachedRigidbody != mover.Body && contact.point.y < mover.BodyCollider.bounds.center.y && Mathf.Abs(contact.normal.y) >= .55f)
                { supported = true; actualSupport = other.name; }
            }
            // Larger characters may land on the next deck instead of the optional small step.
            // Require real support and bounded feet height, then separately test contact on every ramp.
            if (airborne && supported && mover.Grounded && Mathf.Abs(dx) < .22f && feet >= feetTarget.y - .16f && feet < feetTarget.y + 1.3f)
            { reached = true; break; }
            yield return null;
        }
        input.InjectDevelopmentInput(Vector2.zero, false);
        LayoutCheck(id, actor.GetComponent<PcsPlayerAbilities>().CharacterType.ToString(), reached,
            "One normal jump and horizontal steering; requested support=" + (support != null ? support.name : "turn deck") +
            "; actual support=" + actualSupport + "; rise=" + (peak - startFeet).ToString("F3") + "; " + LayoutActorState(mover));
    }

    private static IEnumerator LayoutWalkTo(NetworkObject actor, Vector2 feetTarget, float timeout, string id, bool allowJump, Collider2D expectedSupport = null)
    {
        Mover mover = actor.GetComponent<Mover>();
        PlayerInput input = actor.GetComponent<PlayerInput>();
        layoutPlayReport.phase = actor.name + " " + id + " -> " + feetTarget;
        float deadline = Time.time + timeout;
        float progressAt = Time.time;
        float lastX = mover.Body.position.x;
        float lastJump = -100f;
        bool reached = false;
        bool cameraVisible = true;
        int jumpCount = 0;
        var supportContacts = new List<ContactPoint2D>();
        while (Time.time < deadline)
        {
            Vector2 position = mover.Body.position;
            float errorX = feetTarget.x - position.x;
            float feetY = mover.BodyCollider.bounds.min.y;
            bool supported = true;
            if (expectedSupport != null)
            {
                supportContacts.Clear();
                mover.Body.GetContacts(supportContacts);
                supported = supportContacts.Exists(c => c.enabled && (c.collider == expectedSupport || c.otherCollider == expectedSupport));
            }
            if (supported && Mathf.Abs(errorX) < .18f && feetY >= feetTarget.y - .48f && feetY < feetTarget.y + 1.8f && mover.Grounded)
            { reached = true; break; }
            if (Mathf.Abs(position.x - lastX) > .06f) { lastX = position.x; progressAt = Time.time; }
            bool jump = allowJump && mover.Grounded && Time.time - lastJump > .9f &&
                (Time.time - progressAt > .65f || (Mathf.Abs(errorX) < .5f && feetY < feetTarget.y - .5f));
            if (jump) { lastJump = Time.time; jumpCount++; progressAt = Time.time; }
            input.InjectDevelopmentInput(Mathf.Abs(errorX) < .08f ? Vector2.zero : Vector2.right * Mathf.Sign(errorX), jump);
            Camera camera = Camera.main;
            if (camera != null)
            {
                Vector3 view = camera.WorldToViewportPoint(mover.BodyCollider.bounds.center);
                cameraVisible &= view.z > 0f && view.x >= -.02f && view.x <= 1.02f && view.y >= -.02f && view.y <= 1.02f;
            }
            yield return null;
        }
        input.InjectDevelopmentInput(Vector2.zero, false);
        LayoutCheck(id, actor.GetComponent<PcsPlayerAbilities>().CharacterType.ToString(), reached,
            "Target feet=" + feetTarget + "; regular jump presses=" + jumpCount + "; " + LayoutActorState(mover));
        LayoutCheck(id + "-camera", actor.GetComponent<PcsPlayerAbilities>().CharacterType.ToString(), cameraVisible,
            "Actual main-camera viewport sampled during normal movement; next landing visibility is inspected separately.");
    }

    private static IEnumerator LayoutWaitGrounded(Mover mover, float seconds)
    {
        float deadline = Time.time + seconds;
        while (!mover.Grounded && Time.time < deadline) yield return null;
    }

    private static IEnumerator LayoutWait(float seconds)
    {
        float deadline = Time.time + seconds;
        while (Time.time < deadline) yield return null;
    }

    private static string LayoutActorState(Mover mover)
    {
        return "position=" + mover.Body.position.ToString("F3") + "; feet=" + mover.BodyCollider.bounds.min.y.ToString("F3") +
            "; velocity=" + mover.Body.linearVelocity.ToString("F3") + "; grounded=" + mover.Grounded +
            "; section=" + layoutPlayDirector.ActiveSection + "; reset=" + layoutPlayDirector.ResetEpoch;
    }

    private static IEnumerator LayoutRunColumns()
    {
        layoutPlayDirector.enabled = false;
        try
        {
            int count = 0;
            foreach (PcsPuzzleDevice device in layoutPlayDirector.Devices)
            {
                if (device == null || device.Kind != PcsDeviceKind.Elevator || device.Elevator == null ||
                    (device.LiftPolicy != PcsLiftPolicy.StageOneThreeStop && device.LiftPolicy != PcsLiftPolicy.MainContinuous)) continue;
                count++;
                PcsElevator elevator = device.Elevator;
                SpriteRenderer sprite = (SpriteRenderer)LayoutElevatorField("column").GetValue(elevator);
                if (sprite == null || sprite.sprite == null) throw new InvalidOperationException("Missing current elevator column: " + device.name);
                float bottom = (float)LayoutElevatorField("columnBottom").GetValue(elevator);
                float top = (float)LayoutElevatorField("columnTop").GetValue(elevator);
                bool fixedTop = (bool)LayoutElevatorField("columnFixedAtTop").GetValue(elevator);
                float localBottom = (sprite.sprite.rect.height * bottom - sprite.sprite.pivot.y) / sprite.sprite.pixelsPerUnit;
                float localTop = (sprite.sprite.rect.height * top - sprite.sprite.pivot.y) / sprite.sprite.pixelsPerUnit;
                Vector3 b = sprite.transform.TransformPoint(new Vector3(0, localBottom, 0));
                Vector3 t = sprite.transform.TransformPoint(new Vector3(0, localTop, 0));
                bool fixedLocalTop = (t.y > b.y) == fixedTop;
                Vector3 fixedPoint = fixedLocalTop ? t : b;
                Vector3 movingPoint = fixedLocalTop ? b : t;
                Vector3 initial = device.transform.position;
                Vector3 initialScale = sprite.transform.localScale;
                bool noPhysics = sprite.GetComponentInChildren<Collider2D>(true) == null && sprite.GetComponentInChildren<Rigidbody2D>(true) == null;
                LayoutCheck("column-configuration", device.name, elevator.enabled && noPhysics &&
                    fixedTop == (device.LiftPolicy == PcsLiftPolicy.StageOneThreeStop),
                    "fixedTop=" + fixedTop + "; enabled=" + elevator.enabled + "; noColumnPhysics=" + noPhysics + "; root rotation=" + device.transform.eulerAngles);
                float maxFixedError = 0f, maxMovingError = 0f, settledError = 0f;
                bool targetsReached = true;
                float[] distances = { 1.2f, .45f, 2.2f, 0f };
                foreach (float distance in distances)
                {
                    Vector2 target = (Vector2)initial + Vector2.up * (fixedTop ? -distance : distance);
                    layoutPlayReport.phase = device.name + " column attachment -> " + target;
                    float deadline = Time.time + 8f;
                    float lastFixed = -1f;
                    while (Vector2.Distance(device.Body.position, target) > .005f && Time.time < deadline)
                    {
                        if (Time.fixedTime > lastFixed)
                        {
                            elevator.MoveAuthority(device.Body.position, target, 1.5f, Time.fixedDeltaTime);
                            lastFixed = Time.fixedTime;
                        }
                        yield return null;
                        Vector3 currentB = sprite.transform.TransformPoint(new Vector3(0, localBottom, 0));
                        Vector3 currentT = sprite.transform.TransformPoint(new Vector3(0, localTop, 0));
                        Vector3 fixedNow = fixedLocalTop ? currentT : currentB;
                        Vector3 movingNow = fixedLocalTop ? currentB : currentT;
                        maxFixedError = Mathf.Max(maxFixedError, Vector3.Distance(fixedNow, fixedPoint));
                        maxMovingError = Mathf.Max(maxMovingError, Vector3.Distance(movingNow, movingPoint + device.transform.position - initial));
                    }
                    targetsReached &= Vector2.Distance(device.Body.position, target) <= .005f;
                    yield return LayoutWait(.1f);
                    Vector3 settledBottom = sprite.transform.TransformPoint(new Vector3(0, localBottom, 0));
                    Vector3 settledTop = sprite.transform.TransformPoint(new Vector3(0, localTop, 0));
                    settledError = Mathf.Max(settledError, Vector3.Distance(fixedLocalTop ? settledTop : settledBottom, fixedPoint));
                    settledError = Mathf.Max(settledError, Vector3.Distance(fixedLocalTop ? settledBottom : settledTop,
                        movingPoint + device.transform.position - initial));
                }
                LayoutCheck("column-fixed-end-and-attached-end", device.name,
                    elevator.enabled && targetsReached && maxFixedError < .08f && maxMovingError < .08f && settledError < .008f &&
                    Vector3.Distance(sprite.transform.localScale, initialScale) < .01f,
                    "Real Rigidbody MovePosition/real LateUpdate; targetsReached=" + targetsReached + "; fixed error=" + maxFixedError.ToString("F6") +
                    "; attachment error=" + maxMovingError.ToString("F6") + "; settled error=" + settledError.ToString("F6") +
                    "; restored scale error=" + Vector3.Distance(sprite.transform.localScale, initialScale).ToString("F6") +
                    ". Moving observations allow .08 world units for Editor update versus LateUpdate timing; settled end tolerance is .008.");
            }
            LayoutCheck("two-column-regressions", "Scene", count == 2, "Expected hanging stage-one and bottom-fixed main shaft; found " + count);
        }
        finally { layoutPlayDirector.enabled = true; }
    }

    private static FieldInfo LayoutElevatorField(string name)
    {
        FieldInfo field = typeof(PcsElevator).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null) throw new MissingFieldException("PcsElevator", name);
        return field;
    }

    private static IEnumerator LayoutRunFrogTutorial()
    {
        LayoutSelectSection(0);
        List<PcsPuzzleDevice> anchors = new List<PcsPuzzleDevice>();
        PcsPuzzleDevice lever = null, finish = null;
        foreach (PcsPuzzleDevice device in layoutPlayDirector.Devices)
        {
            if (device == null || device.Section != 0 || device.RequiredRole != MyEnum.CharacterType.Frog) continue;
            if (device.Kind == PcsDeviceKind.Anchor) anchors.Add(device);
            if (device.Kind == PcsDeviceKind.Lever && device.LatchOnActivate) lever = device;
            if (device.Kind == PcsDeviceKind.TutorialExit) finish = device;
        }
        anchors.Sort((a, b) => a.InteractionPoint.x.CompareTo(b.InteractionPoint.x));
        if (anchors.Count != 2 || lever == null || finish == null)
            throw new InvalidOperationException("Current Frog tutorial requires exactly two anchors, a latched lever and an exit.");
        Transform spawn = layoutPlayDirector.TutorialSpawns[(int)MyEnum.CharacterType.Frog];
        NetworkObject actor = LayoutSpawn(MyEnum.CharacterType.Frog, LayoutFindFloor(spawn.position));
        Mover mover = actor.GetComponent<Mover>();
        PlayerInput input = actor.GetComponent<PlayerInput>();
        PcsPlayerAbilities ability = actor.GetComponent<PcsPlayerAbilities>();
        yield return LayoutWaitGrounded(mover, 3f);
        foreach (PcsPuzzleDevice anchor in anchors)
        {
            float deadline = Time.time + 10f;
            while (Vector2.Distance(mover.BodyCollider.bounds.center, anchor.InteractionPoint) > ability.PullRange - .35f && Time.time < deadline)
            {
                input.InjectDevelopmentInput(Vector2.right * Mathf.Sign(anchor.InteractionPoint.x - mover.Body.position.x), false);
                yield return null;
            }
            bool blockedCenter, blockedRoot;
            string beforeCenter = LayoutLineDiagnostic(actor, anchor, true, out blockedCenter);
            string beforeRoot = LayoutLineDiagnostic(actor, anchor, false, out blockedRoot);
            bool usedJump = (blockedCenter || blockedRoot) && mover.Grounded;
            if (usedJump)
            {
                input.InjectDevelopmentInput(Vector2.zero, true);
                yield return LayoutWait(.18f);
            }
            string fireCenter = LayoutLineDiagnostic(actor, anchor, true, out blockedCenter);
            string fireRoot = LayoutLineDiagnostic(actor, anchor, false, out blockedRoot);
            input.InjectDevelopmentInput(Vector2.zero, false, ability: true, aim: anchor.InteractionPoint);
            float startY = mover.Body.position.y, peak = startY, nearest = float.PositiveInfinity;
            deadline = Time.time + 4f;
            while (Time.time < deadline)
            {
                peak = Mathf.Max(peak, mover.Body.position.y);
                nearest = Mathf.Min(nearest, Vector2.Distance(mover.BodyCollider.bounds.center, anchor.InteractionPoint));
                yield return null;
            }
            LayoutCheck("frog-anchor-" + anchor.DeviceId, "Frog", nearest < 1f && peak > startY + .15f && layoutPlayDirector.States[anchor.DeviceId].Active != 0,
                "Aim+F once; conditional normal jump=" + usedJump + "; closest=" + nearest.ToString("F3") + "; rise=" + (peak - startY).ToString("F3") +
                "; target active=" + layoutPlayDirector.States[anchor.DeviceId].Active + "; feedback=" + ability.Feedback +
                "; before center=" + beforeCenter + "; before root=" + beforeRoot + "; firing center=" + fireCenter + "; firing root=" + fireRoot + "; " + LayoutActorState(mover));
        }
        input.InjectDevelopmentInput(Vector2.zero, false, ability: true, aim: lever.InteractionPoint);
        yield return LayoutWait(1.5f);
        LayoutCheck("frog-latched-lever", "Frog", layoutPlayDirector.States[lever.DeviceId].Active != 0,
            "Normal F press then release; held input=false; feedback=" + ability.Feedback + "; " + LayoutActorState(mover));
        yield return LayoutWalkTo(actor, new Vector2(finish.Bounds.center.x, finish.Bounds.min.y + .2f), 12f, "frog-exit-walk", true);
        yield return LayoutWait(.3f);
        LayoutCheck("frog-complete-without-dummy", "Frog", finish.RequiredDummyCount == 0 &&
            (layoutPlayDirector.CompletedTutorialMask & (1 << (int)MyEnum.CharacterType.Frog)) != 0,
            "No dummy pickup or fixture completion; actual exit occupancy and lever condition. mask=" + layoutPlayDirector.CompletedTutorialMask);
        ScreenCapture.CaptureScreenshot(Path.Combine(LayoutPlayEvidence, "layout-frog-tutorial.png"));
        yield return LayoutWait(.15f);
        LayoutDespawn(actor);
    }

    private static IEnumerator LayoutRunMouseTutorial()
    {
        LayoutSelectSection(0);
        List<PcsPuzzleDevice> consoles = new List<PcsPuzzleDevice>();
        PcsPuzzleDevice ladder = null, finish = null;
        foreach (PcsPuzzleDevice device in layoutPlayDirector.Devices)
        {
            if (device == null || device.Section != 0 || device.RequiredRole != MyEnum.CharacterType.Mouse) continue;
            if (device.Kind == PcsDeviceKind.HackConsole) consoles.Add(device);
            if (device.IsLadder && device.DeployableLadder) ladder = device;
            if (device.Kind == PcsDeviceKind.TutorialExit) finish = device;
        }
        consoles.Sort((a, b) => a.InteractionPoint.x.CompareTo(b.InteractionPoint.x));
        if (consoles.Count != 3 || ladder == null || finish == null)
            throw new InvalidOperationException("Current Mouse tutorial requires three consoles, deployable ladder and exit.");
        Vector2 start = LayoutFindFloor(consoles[0].InteractionPoint + Vector2.left * .65f);
        NetworkObject actor = LayoutSpawn(MyEnum.CharacterType.Mouse, start);
        Mover mover = actor.GetComponent<Mover>();
        PlayerInput input = actor.GetComponent<PlayerInput>();
        yield return LayoutWaitGrounded(mover, 3f);
        Vector2 stowed = ladder.transform.position;
        LayoutCheck("mouse-ladder-stowed", "Mouse", !ladder.CanClimb && !ladder.Trigger.enabled, "Before any hack; ladder=" + stowed);
        for (int i = 0; i < consoles.Count; i++)
        {
            PcsPuzzleDevice console = consoles[i];
            if (i > 0)
                yield return LayoutWalkTo(actor, LayoutFindFloor(console.InteractionPoint + Vector2.left * .35f), 14f, "mouse-console-approach-" + i, true);
            for (int pulse = 0; pulse < console.RequiredInputs; pulse++)
            {
                input.InjectDevelopmentInput(Vector2.zero, false, interact: true, aim: console.InteractionPoint);
                yield return LayoutWait(layoutPlayDirector.HackPulseInterval + .15f);
            }
            LayoutCheck("mouse-hack-" + (i + 1), "Mouse", layoutPlayDirector.States[console.DeviceId].Active != 0,
                "Normal E pulses; count=" + layoutPlayDirector.States[console.DeviceId].Counter + "; " + LayoutActorState(mover));
            if (i == 1)
            {
                bool premature = false;
                float deadline = Time.time + 8f;
                while (!ladder.CanClimb && Time.time < deadline)
                {
                    premature |= ladder.Trigger.enabled && Vector2.Distance(ladder.transform.position, ladder.LowerStop.position) > .026f;
                    yield return null;
                }
                LayoutCheck("mouse-ladder-deploy", "Mouse", !premature && ladder.CanClimb && ladder.transform.position.y < stowed.y - .2f,
                    "No trigger before complete; actual descent=" + (stowed.y - ladder.transform.position.y).ToString("F3") + "; canClimb=" + ladder.CanClimb);
                yield return LayoutWalkTo(actor, new Vector2(ladder.InteractionPoint.x, mover.BodyCollider.bounds.min.y), 8f, "mouse-ladder-approach", true);
                float low = mover.Body.position.y;
                input.InjectDevelopmentInput(Vector2.zero, false, ladder: 1f);
                deadline = Time.time + 9f;
                while (mover.BodyCollider.bounds.min.y < ladder.Trigger.bounds.max.y - .15f && Time.time < deadline) yield return null;
                input.InjectDevelopmentInput(Vector2.right, true);
                yield return LayoutWait(.55f);
                input.InjectDevelopmentInput(Vector2.zero, false);
                LayoutCheck("mouse-ladder-climb-and-dismount", "Mouse", mover.Body.position.y > low + 1f &&
                    mover.BodyCollider.bounds.min.y > ladder.Trigger.bounds.max.y - .6f,
                    "Normal W, then right+jump at upper end; climb=" + (mover.Body.position.y - low).ToString("F3") + "; " + LayoutActorState(mover));
            }
        }
        yield return LayoutWalkTo(actor, new Vector2(finish.Bounds.center.x, finish.Bounds.min.y + .2f), 12f, "mouse-exit-walk", true);
        yield return LayoutWait(.3f);
        LayoutCheck("mouse-complete-three-hacks", "Mouse", (layoutPlayDirector.CompletedTutorialMask & (1 << (int)MyEnum.CharacterType.Mouse)) != 0,
            "Actual three E-hack conditions and exit occupancy; mask=" + layoutPlayDirector.CompletedTutorialMask);
        ScreenCapture.CaptureScreenshot(Path.Combine(LayoutPlayEvidence, "layout-mouse-tutorial.png"));
        yield return LayoutWait(.15f);
        LayoutDespawn(actor);
    }

    private static string LayoutLineDiagnostic(NetworkObject actor, PcsPuzzleDevice target, bool useCenter, out bool blocked)
    {
        Mover mover = actor.GetComponent<Mover>();
        Vector2 from = useCenter ? (Vector2)mover.BodyCollider.bounds.center : (Vector2)actor.transform.position;
        Vector2 offset = target.InteractionPoint - from;
        foreach (RaycastHit2D hit in Physics2D.RaycastAll(from, offset.normalized, offset.magnitude))
        {
            if (hit.collider == null || hit.collider.isTrigger || hit.collider.attachedRigidbody == mover.Body ||
                hit.collider.transform.IsChildOf(actor.transform) || hit.collider.transform.IsChildOf(target.transform) || hit.distance <= .025f) continue;
            blocked = true;
            return LayoutTestPath(hit.collider.transform) + " point=" + hit.point.ToString("F3") + " distance=" + hit.distance.ToString("F3") +
                " from=" + from.ToString("F3") + " to=" + target.InteractionPoint.ToString("F3");
        }
        blocked = false;
        return "clear from=" + from.ToString("F3") + " to=" + target.InteractionPoint.ToString("F3");
    }

    private static BoxCollider2D LayoutNamedSurface(string name)
    {
        BoxCollider2D found = null;
        foreach (BoxCollider2D box in UnityEngine.Object.FindObjectsByType<BoxCollider2D>(FindObjectsSortMode.None))
        {
            if (box.name != name || !box.enabled || box.isTrigger) continue;
            if (found != null) throw new InvalidOperationException("Ambiguous actual surface: " + name);
            found = box;
        }
        if (found == null) throw new InvalidOperationException("Missing actual surface: " + name);
        return found;
    }

    private static IEnumerator LayoutRunRabbitTutorial()
    {
        LayoutSelectSection(0);
        PcsPuzzleDevice ally = null, finish = null;
        foreach (PcsPuzzleDevice device in layoutPlayDirector.Devices)
        {
            if (device == null || device.Section != 0 || device.RequiredRole != MyEnum.CharacterType.Rabbit) continue;
            if (device.Kind == PcsDeviceKind.Dummy) ally = device;
            if (device.Kind == PcsDeviceKind.TutorialExit) finish = device;
        }
        if (ally == null || finish == null) throw new InvalidOperationException("Rabbit original ally and finish are required.");
        Collider2D floor = LayoutNamedSurface("Tutorial_Rabbit_Floor");
        Transform spawn = layoutPlayDirector.TutorialSpawns[(int)MyEnum.CharacterType.Rabbit];
        NetworkObject actor = LayoutSpawn(MyEnum.CharacterType.Rabbit, LayoutFindFloor(spawn.position));
        Mover mover = actor.GetComponent<Mover>();
        PlayerInput input = actor.GetComponent<PlayerInput>();
        yield return LayoutWaitGrounded(mover, 3f);
        yield return LayoutWalkTo(actor, new Vector2(ally.transform.position.x - .6f, floor.bounds.max.y), 8f, "rabbit-ally-approach", false);
        input.InjectDevelopmentInput(Vector2.zero, false, carry: true, aim: ally.InteractionPoint);
        yield return LayoutWait(.35f);
        LayoutCheck("rabbit-normal-G-pickup", "Rabbit", layoutPlayDirector.States[ally.DeviceId].Phase == 1 &&
            layoutPlayDirector.States[ally.DeviceId].Actor == actor.Id, "Original tutorial ally; G input via PlayerInput, no state fixture.");
        string[] decks = { "Tutorial_Rabbit_FirstJump", "Tutorial_Rabbit_Jump_1", "Tutorial_Rabbit_Jump_2", "Tutorial_Rabbit_UpperExit" };
        Collider2D source = floor;
        for (int i = 0; i < decks.Length; i++)
        {
            Collider2D destination = LayoutNamedSurface(decks[i]);
            yield return LayoutRabbitHop(actor, source, destination, "rabbit-tutorial-hop-" + i);
            bool stillCarried = layoutPlayDirector.States[ally.DeviceId].Phase == 1 && layoutPlayDirector.States[ally.DeviceId].Actor == actor.Id;
            bool rescuedAtFinish = i == decks.Length - 1 && layoutPlayDirector.States[ally.DeviceId].Phase == 4 &&
                layoutPlayDirector.States[finish.DeviceId].Active != 0 &&
                (layoutPlayDirector.CompletedTutorialMask & (1 << (int)MyEnum.CharacterType.Rabbit)) != 0 &&
                finish.Trigger.bounds.Intersects(mover.BodyCollider.bounds);
            LayoutCheck("rabbit-ally-retained-" + i, "Rabbit", stillCarried || rescuedAtFinish,
                "Carry preserved, or final hop entered the real finish and retired its rescued ally. stillCarried=" + stillCarried +
                "; actualRescue=" + rescuedAtFinish + "; ally phase=" + layoutPlayDirector.States[ally.DeviceId].Phase + "; " + LayoutActorState(mover));
            source = destination;
        }
        yield return LayoutWalkTo(actor, new Vector2(finish.Bounds.center.x, source.bounds.max.y), 8f, "rabbit-exit-with-ally", false);
        yield return LayoutWait(.4f);
        LayoutCheck("rabbit-original-tutorial-complete", "Rabbit", finish.RequiredDummyCount == 1 &&
            (layoutPlayDirector.CompletedTutorialMask & (1 << (int)MyEnum.CharacterType.Rabbit)) != 0,
            "Original carry/jump/ally occupancy completion, no completion flags seeded; mask=" + layoutPlayDirector.CompletedTutorialMask);
        ScreenCapture.CaptureScreenshot(Path.Combine(LayoutPlayEvidence, "layout-rabbit-tutorial.png"));
        yield return LayoutWait(.15f);
        LayoutDespawn(actor);
    }

    private static IEnumerator LayoutRabbitHop(NetworkObject actor, Collider2D source, Collider2D destination, string id, float? desiredX = null, float? takeoffOverride = null)
    {
        Mover mover = actor.GetComponent<Mover>();
        PlayerInput input = actor.GetComponent<PlayerInput>();
        float landingX = desiredX ?? destination.bounds.center.x;
        float direction = landingX >= mover.Body.position.x ? 1f : -1f;
        float takeoffX = direction > 0f ? destination.bounds.min.x - .75f : destination.bounds.max.x + .75f;
        takeoffX = Mathf.Clamp(takeoffX, source.bounds.min.x + .38f, source.bounds.max.x - .38f);
        if (takeoffOverride.HasValue) takeoffX = takeoffOverride.Value;
        yield return LayoutWalkTo(actor, new Vector2(takeoffX, source.bounds.max.y), 10f, id + "-takeoff", takeoffOverride.HasValue);
        input.InjectDevelopmentInput(Vector2.zero, false);
        yield return LayoutWait(.15f);
        bool startedGrounded = mover.Grounded && Mathf.Abs(mover.BodyCollider.bounds.min.y - source.bounds.max.y) < .12f;
        float startFeet = mover.BodyCollider.bounds.min.y, peakFeet = startFeet;
        float began = Time.time;
        bool second = false, firstLaunch = false, secondLaunch = false, above = false, landed = false;
        float halfWidth = mover.BodyCollider.bounds.extents.x;
        foreach (Collider2D child in mover.GetComponentsInChildren<Collider2D>())
            if (child.enabled && !child.isTrigger && child.attachedRigidbody == mover.Body)
                halfWidth = Mathf.Max(halfWidth, Mathf.Abs(child.bounds.min.x - mover.Body.position.x), Mathf.Abs(child.bounds.max.x - mover.Body.position.x));
        float approachX = direction > 0f ? destination.bounds.min.x - halfWidth - .18f : destination.bounds.max.x + halfWidth + .18f;
        bool horizontallyUnder = mover.Body.position.x > destination.bounds.min.x && mover.Body.position.x < destination.bounds.max.x;
        if (horizontallyUnder) approachX = mover.Body.position.x;
        var trace = new System.Text.StringBuilder();
        float nextTrace = 0f;
        input.InjectDevelopmentInput(Vector2.zero, true);
        while (Time.time - began < 4f)
        {
            float feet = mover.BodyCollider.bounds.min.y;
            peakFeet = Mathf.Max(peakFeet, feet);
            if (!second && mover.Body.linearVelocity.y > mover.JumpSpeed * .5f) firstLaunch = true;
            if (second && mover.Body.linearVelocity.y > mover.JumpSpeed * .5f) secondLaunch = true;
            if (!second && firstLaunch && Time.time - began > .15f && mover.Body.linearVelocity.y <= .1f)
            {
                input.InjectDevelopmentInput(Vector2.zero, true);
                second = true;
            }
            if (feet > destination.bounds.max.y + .07f) above = true;
            float targetX = above ? landingX : approachX;
            float dx = targetX - mover.Body.position.x;
            float control = Mathf.Abs(dx) < .045f ? 0f : Mathf.Clamp(dx / (mover.MoveSpeed * .12f), -1f, 1f);
            input.InjectDevelopmentInput(Vector2.right * control, false);
            if (Time.time >= nextTrace)
            {
                nextTrace = Time.time + .1f;
                trace.Append("t=").Append((Time.time - began).ToString("F2")).Append(" x=").Append(mover.Body.position.x.ToString("F3"))
                    .Append(" feet=").Append(feet.ToString("F3")).Append(" vy=").Append(mover.Body.linearVelocity.y.ToString("F3"))
                    .Append(" g=").Append(mover.Grounded).Append(';');
            }
            if (above && mover.Grounded && Mathf.Abs(feet - destination.bounds.max.y) < .10f &&
                mover.BodyCollider.bounds.min.x >= destination.bounds.min.x && mover.BodyCollider.bounds.max.x <= destination.bounds.max.x)
            { landed = true; break; }
            yield return null;
        }
        input.InjectDevelopmentInput(Vector2.zero, false);
        yield return LayoutWait(.18f);
        LayoutCheck(id, "Rabbit", startedGrounded && firstLaunch && second && secondLaunch && landed && mover.Grounded,
            "Normal two jump presses and horizontal steering. source=" + source.name + " destination=" + destination.name +
            "; startGround=" + startedGrounded + "; first/second=" + firstLaunch + "/" + secondLaunch + "; peakRise=" + (peakFeet - startFeet).ToString("F3") +
            "; landed=" + landed + "; " + LayoutActorState(mover) + "; trace=" + trace);
    }

    private static IEnumerator LayoutRunCarryAccess()
    {
        LayoutSelectSection(1);
        Collider2D step1 = LayoutNamedSurface("StageOne_CarryStep_1");
        Collider2D step2 = LayoutNamedSurface("StageOne_CarryStep_2");
        Collider2D floor = null, upper = null;
        foreach (BoxCollider2D box in UnityEngine.Object.FindObjectsByType<BoxCollider2D>(FindObjectsSortMode.None))
        {
            if (!box.enabled || box.isTrigger || box.bounds.min.x > -4.2f || box.bounds.max.x < -4.2f) continue;
            if (Mathf.Abs(box.bounds.max.y + 3f) < .15f) floor = box;
            if (Mathf.Abs(box.bounds.max.y - 2.769f) < .18f) upper = box;
        }
        if (floor == null || upper == null) throw new InvalidOperationException("Actual left floor and upper access deck not found at x=-4.2.");
        foreach (MyEnum.CharacterType role in new[] { MyEnum.CharacterType.Frog, MyEnum.CharacterType.Mouse })
        {
            // Independent access attempts start with the actual section reset, including enemies.
            // Otherwise the second pair inherits enemies already chasing the previous pair.
            typeof(PcsPuzzleDirector).GetMethod("ResetSection", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(layoutPlayDirector, new object[] { role });
            layoutPlayReport.diagnostics.Add("Fixture: fresh production section reset before the independent " + role + " carry attempt; enemies remain active.");
            yield return LayoutWait(layoutPlayDirector.ResetDelay + .2f);
            NetworkObject rabbit = LayoutSpawn(MyEnum.CharacterType.Rabbit, new Vector2(-7.5f, floor.bounds.max.y));
            NetworkObject rider = LayoutSpawn(role, new Vector2(-6.7f, floor.bounds.max.y));
            Mover rm = rabbit.GetComponent<Mover>();
            PlayerInput input = rabbit.GetComponent<PlayerInput>();
            Camera.main.GetComponent<PcsLocalCameraFollow>().Bind(rabbit.transform);
            yield return LayoutWaitGrounded(rm, 3f);
            yield return LayoutWaitGrounded(rider.GetComponent<Mover>(), 3f);
            input.InjectDevelopmentInput(Vector2.zero, false, carry: true);
            yield return LayoutWait(.5f);
            LayoutCheck("access-pickup-" + role, "Rabbit", rider.GetComponent<Mover>().Rider.IsRiding,
                "Two actual normal player prefabs; normal G pickup. Both locally owned in this geometry-only session.");
            Collider2D buttonTop = layoutPlayDirector.Devices[1].Solid;
            yield return LayoutRabbitHop(rabbit, buttonTop, step1, "access-first-step-" + role, takeoffOverride: -4.25f);
            yield return LayoutRabbitHop(rabbit, step1, step2, "access-second-step-" + role);
            yield return LayoutRabbitHop(rabbit, step2, upper, "access-upper-deck-" + role, -4.2f);
            LayoutCheck("access-passenger-retained-" + role, "Rabbit", rider.GetComponent<Mover>().Rider.IsRiding &&
                rm.Grounded && Mathf.Abs(rm.BodyCollider.bounds.min.y - upper.bounds.max.y) < .15f,
                "Actual passenger remains carried at left upper gate's right side. " + LayoutActorState(rm));
            input.InjectDevelopmentInput(Vector2.zero, false, carry: true);
            yield return LayoutWait(.8f);
            LayoutCheck("access-passenger-drop-" + role, role.ToString(), !rider.GetComponent<Mover>().Rider.IsRiding &&
                rider.GetComponent<Mover>().Body.position.x > -6.6f,
                "Normal G release; no teleport through the closed console gate. " + LayoutActorState(rider.GetComponent<Mover>()));
            LayoutDespawn(rider);
            LayoutDespawn(rabbit);
        }
    }

    private static Vector2 LayoutFindFloor(Vector2 near)
    {
        RaycastHit2D[] hits = Physics2D.RaycastAll(near + Vector2.up * .4f, Vector2.down, 8f, LayerMask.GetMask("Ground"));
        foreach (RaycastHit2D hit in hits)
            if (hit.collider != null && !hit.collider.isTrigger && hit.distance > 0f && hit.normal.y > .55f)
                return hit.point;
        throw new InvalidOperationException("No actual Ground surface below fixture/waypoint " + near);
    }

    private static string LayoutTestPath(Transform item)
    {
        string path = item.name;
        while (item.parent != null) { item = item.parent; path = item.name + "/" + path; }
        return path;
    }

    private static void LayoutCheck(string id, string role, bool passed, string detail)
    {
        layoutPlayReport.checks.Add(new LayoutPlayCheck { id = id, role = role, status = passed ? "PASS" : "FAIL", detail = detail });
        LayoutSavePlayReport();
    }

    private static void LayoutCapturePlayLog(string message, string stack, UnityEngine.LogType type)
    {
        if (layoutPlayReport == null || (type != UnityEngine.LogType.Error && type != UnityEngine.LogType.Exception && type != UnityEngine.LogType.Assert)) return;
        string safe = System.Text.RegularExpressions.Regex.Replace(message, "(?i)(appid|token|password|secret)\\s*[:=]\\s*\\S+", "$1=<redacted>");
        if (layoutPlayReport.errors.Count < 40) layoutPlayReport.errors.Add(safe);
    }

    private static void LayoutSavePlayReport()
    {
        if (layoutPlayReport == null) return;
        layoutPlayReport.heartbeatUtc = DateTime.UtcNow.ToString("O");
        Directory.CreateDirectory(LayoutPlayEvidence);
        File.WriteAllText(Path.Combine(LayoutPlayEvidence, "layout-play-" + layoutPlayReport.mode + ".json"), JsonUtility.ToJson(layoutPlayReport, true));
    }

    private static async void LayoutFinishPlay(Exception exception)
    {
        if (layoutPlayFinishing) return;
        layoutPlayFinishing = true;
        layoutPlayRoutines.Clear();
        if (exception != null) layoutPlayReport.errors.Add(exception.ToString());
        layoutPlayReport.status = exception != null || layoutPlayReport.errors.Count > 0 || layoutPlayReport.checks.Count < 2 ||
            layoutPlayReport.checks.Exists(x => x.status == "FAIL") ? "FAIL" : "PASS";
        layoutPlayReport.phase = "Finishing and restoring edit mode";
        layoutPlayReport.finishedUtc = DateTime.UtcNow.ToString("O");
        LayoutSavePlayReport();
        SessionState.SetString(LayoutPlayFlag, "restore");
        Application.logMessageReceived -= LayoutCapturePlayLog;
        if (layoutPlayDirector != null) layoutPlayDirector.enabled = true;
        try { if (layoutPlayRunner != null) await layoutPlayRunner.Shutdown(); }
        catch (Exception ex)
        {
            layoutPlayReport.errors.Add("Runner cleanup failed: " + ex.Message);
            layoutPlayReport.status = "FAIL";
            LayoutSavePlayReport();
        }
        EditorApplication.ExitPlaymode();
    }
}

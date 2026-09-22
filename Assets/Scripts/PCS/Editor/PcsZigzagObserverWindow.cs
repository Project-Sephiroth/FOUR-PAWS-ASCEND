#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using Fusion;
using UnityEditor;
using UnityEngine;

/// <summary>Read-only observation of a real player's movement. All route data belongs to this window.</summary>
public sealed class PcsZigzagObserverWindow : EditorWindow
{
    private sealed class Point
    {
        public string name;
        public Vector2 position;
        public float radius = 0.65f;
        public Point(string label) { name = label; }
        public Point Copy() => new Point(name) { position = position, radius = radius };
    }

    private struct Sample
    {
        public double time;
        public Vector2 foot, velocity, input;
        public bool grounded, inputKnown, jump;
        public int contacts;
    }

    // No SerializeField, EditorPrefs, scene objects or runtime hooks. Reload always stops observation.
    private Mover player;
    private PlayerInput playerInput;
    private readonly List<Point> points = new List<Point>();
    private readonly List<Point> recordedRoute = new List<Point>();
    private readonly List<Sample> samples = new List<Sample>();
    private readonly List<string> events = new List<string>();
    private readonly List<ContactPoint2D> contacts = new List<ContactPoint2D>(16);
    private Vector2 scroll;
    private bool running, showHandles, normalInputConfirmed, sawJump, sawAirborne;
    private int nextPoint, jumpPolicy, recordedJumpPolicy;
    private double started, previousTime, nextRepaint, stationarySince, reverseSince;
    private float routeWidth = 2f, fallMargin = 3f, stationarySeconds = 2f;
    private Vector2 stationaryAnchor;
    private bool stationaryReported, reverseReported;
    private string status = "미실행", playerDescription = "", recordedStartUtc = "", report = "아직 관찰 기록이 없습니다.";
    private string selfCheckResult = "미실행";
    private const int MaxSamples = 6000;
    private const int MaxEvents = 160;
    private static readonly string[] JumpPolicies = { "점프 허용 여부 미정", "걷기만 시험 (사용자 지정)", "점프 허용 시험 (사용자 지정)" };

    [MenuItem("Tools/FOUR PAWS/지그재그 이동 관찰")]
    public static void OpenWindow()
    {
        var window = GetWindow<PcsZigzagObserverWindow>("지그재그 이동 관찰");
        window.minSize = new Vector2(460f, 620f);
        window.Show();
    }

    private void OnEnable()
    {
        running = false;
        normalInputConfirmed = false;
        if (points.Count == 0)
        {
            points.Add(new Point("시작"));
            points.Add(new Point("중간 지점"));
            points.Add(new Point("도착"));
        }
        EditorApplication.update += Observe;
        EditorApplication.playModeStateChanged += PlayModeChanged;
        AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
        SceneView.duringSceneGui += DrawRoute;
    }

    private void OnDisable()
    {
        Stop("창 닫힘: 관찰 중지");
        EditorApplication.update -= Observe;
        EditorApplication.playModeStateChanged -= PlayModeChanged;
        AssemblyReloadEvents.beforeAssemblyReload -= BeforeReload;
        SceneView.duringSceneGui -= DrawRoute;
        SceneView.RepaintAll();
    }

    private void BeforeReload() { Stop("스크립트 다시 로드: 관찰 중지"); }
    private void PlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.ExitingEditMode)
            Stop("Play 전환: 관찰 중지");
        normalInputConfirmed = false;
        Repaint();
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.LabelField("실제 캐릭터 이동 관찰 · Editor 전용", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("이 창은 게임 상태를 변경하지 않습니다. 정상 Shared 세션에서 로컬 플레이어를 직접 조작하세요.\n" +
            "좌표는 몸통 Collider 하단 중앙(발 위치)의 월드 XY입니다. 시작 → 중간 지점들 → 도착 순서로 기록합니다.", MessageType.Info);
        using (new EditorGUI.DisabledScope(running))
        {
            player = (Mover)EditorGUILayout.ObjectField("실제 플레이어 (Mover)", player, typeof(Mover), true);
            if (GUILayout.Button("Hierarchy 선택에서 플레이어 가져오기"))
                player = Selection.activeGameObject == null ? null : Selection.activeGameObject.GetComponentInParent<Mover>();
            jumpPolicy = EditorGUILayout.Popup("이번 관찰 조건", jumpPolicy, JumpPolicies);
            normalInputConfirmed = EditorGUILayout.ToggleLeft("기존 강제/개발 입력을 해제하고 직접 조작함을 확인", normalInputConfirmed);
            EditorGUILayout.HelpBox("PlayerInput 공개 값만 읽습니다. 사람 키와 기존 개발 입력의 출처는 관측 불가입니다.\n" +
                "JumpInput은 물리 틱에서 소비되어 일부 누락될 수 있습니다. 점프 미관측을 걷기 통과 증명으로 쓰지 마세요.", MessageType.None);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("경로 지정 (씬에 저장하지 않음)", EditorStyles.boldLabel);
            for (int i = 0; i < points.Count; i++)
            {
                Point point = points[i];
                EditorGUILayout.LabelField(i == 0 ? "0 · 시작" : i == points.Count - 1 ? i + " · 도착 영역" : i + " · 순서 지점");
                point.name = EditorGUILayout.TextField("설명", point.name);
                point.position = EditorGUILayout.Vector2Field("발 위치 / 월드 XY", point.position);
                point.radius = EditorGUILayout.FloatField("통과 반경", point.radius);
                EditorGUILayout.BeginHorizontal();
                using (new EditorGUI.DisabledScope(player == null || player.BodyCollider == null))
                    if (GUILayout.Button("현재 플레이어 발 위치 읽기")) point.position = FootPosition(player);
                using (new EditorGUI.DisabledScope(Selection.activeTransform == null))
                    if (GUILayout.Button("선택한 Transform 위치 읽기")) point.position = Selection.activeTransform.position;
                if (i > 0 && i < points.Count - 1 && GUILayout.Button("지점 제외", GUILayout.Width(76f)))
                { points.RemoveAt(i); GUIUtility.ExitGUI(); }
                EditorGUILayout.EndHorizontal();
            }
            if (GUILayout.Button("도착 앞에 중간 지점 추가") && points.Count < 20)
                points.Insert(points.Count - 1, new Point("중간 지점 " + (points.Count - 1)));
            showHandles = EditorGUILayout.ToggleLeft("Scene View 경로 표시 / 좌표 핸들 (창 데이터만 변경)", showHandles);
            routeWidth = EditorGUILayout.FloatField("경로 중심선 허용 거리", routeWidth);
            fallMargin = EditorGUILayout.FloatField("전체 경로 아래 낙하 여유", fallMargin);
            stationarySeconds = EditorGUILayout.FloatField("정지 관찰 시간 (초)", stationarySeconds);
        }

        string reason = GetEnvironmentIssue();
        if (reason == null) reason = ValidateRoute(points, routeWidth, fallMargin, stationarySeconds);
        EditorGUILayout.HelpBox(reason ?? "로컬 권한과 경로를 확인했습니다. 시작 영역에 선 뒤 관찰을 시작하세요.",
            reason == null ? MessageType.Info : MessageType.Warning);
        EditorGUILayout.BeginHorizontal();
        using (new EditorGUI.DisabledScope(running))
            if (GUILayout.Button("관찰 시작")) StartObservation();
        using (new EditorGUI.DisabledScope(!running))
            if (GUILayout.Button("관찰 중지")) Stop("사용자가 중지: 미완주 / 미확인");
        if (GUILayout.Button("결과 초기화 (기록만)")) ClearRecords();
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.LabelField("상태", status);
        EditorGUILayout.LabelField("기록", samples.Count + "개 / 최대 " + MaxSamples + "개 (약 10분)");
        if (recordedRoute.Count > 0)
            EditorGUILayout.LabelField("다음 지점", nextPoint < recordedRoute.Count ? recordedRoute[nextPoint].name : "순서 지점 모두 도달");
        if (samples.Count > 0)
        {
            Sample s = samples[samples.Count - 1];
            EditorGUILayout.LabelField("최근 관측", $"{s.time:F1}초  발 {s.foot:F2}  속도 {s.velocity:F2}");
            EditorGUILayout.LabelField("공개 접지 / 접촉 수", s.grounded + " / " + s.contacts + " (서로 별도 값)");
        }
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("결과 텍스트 갱신")) report = CreateReport();
        if (GUILayout.Button("결과 복사")) { report = CreateReport(); EditorGUIUtility.systemCopyBuffer = report; }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.TextArea(report, GUILayout.MinHeight(140f), GUILayout.MaxHeight(250f));
        EditorGUILayout.Space();
        if (GUILayout.Button("도구 판정 자체 점검 (합성 좌표, 실제 플레이 아님)")) RunSelfCheck();
        EditorGUILayout.LabelField("판정 자체 점검", selfCheckResult);
        EditorGUILayout.EndScrollView();
    }

    private string GetEnvironmentIssue()
    {
        if (!EditorApplication.isPlaying) return "실행 불가: Play 중이 아닙니다. LobbyScene에서 정상 세션으로 진입하세요.";
        if (EditorApplication.isPaused) return "일시정지 상태입니다. 관찰은 연속된 실행 중에만 가능합니다.";
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return "컴파일 / 임포트 중입니다.";
        if (player == null || EditorUtility.IsPersistent(player)) return "실행 불가: 실제 씬의 플레이어 Mover를 지정하세요. Prefab 에셋은 대상이 아닙니다.";
        if (!player.isActiveAndEnabled) return "실행 불가: 플레이어 Mover가 비활성 상태입니다.";
        if (player.Object == null || !player.Object.IsValid) return "실행 불가: 플레이어 NetworkObject가 스폰되지 않았습니다.";
        if (player.Runner == null || !player.Runner.IsRunning || player.Runner.GameMode != GameMode.Shared)
            return "실행 불가: 실행 중인 정상 Shared Runner가 필요합니다.";
        if (!player.HasStateAuthority) return "실행 불가: 로컬 StateAuthority가 없는 플레이어입니다. 원격 입력은 관측하지 않습니다.";
        if (player.Body == null || !player.Body.simulated || player.BodyCollider == null || !player.BodyCollider.enabled)
            return "실행 불가: 활성 몸통 Collider와 시뮬레이션 중인 Rigidbody2D가 필요합니다.";
        if (PcsPuzzleDirector.Instance != null &&
            (PcsPuzzleDirector.Instance.EditorHasForcedInputs || PcsPuzzleDirector.Instance.EditorTestHasPendingCommand))
            return "기존 장치 테스트의 강제 입력 / 대기 명령이 남아 있습니다. 먼저 해당 창에서 해제하세요.";
        return null;
    }

    private void StartObservation()
    {
        string issue = GetEnvironmentIssue() ?? ValidateRoute(points, routeWidth, fallMargin, stationarySeconds);
        if (issue == null && !normalInputConfirmed) issue = "기존 강제/개발 입력 해제 여부를 먼저 확인하세요. 이 도구는 해제 명령도 보내지 않습니다.";
        if (issue == null && !Contains(points[0], FootPosition(player))) issue = "시작 영역 밖입니다. 직접 이동해 시작 지점에 선 뒤 시작하세요.";
        if (issue == null && !player.Grounded) issue = "출발 조건 미충족: 시작 영역에서 접지한 상태로 관찰을 시작하세요.";
        if (issue != null) { status = "테스트 환경 / 시작 조건 때문에 실행 불가"; report = issue; return; }

        ClearRecords();
        foreach (Point point in points) recordedRoute.Add(point.Copy());
        playerInput = player.GetComponent<PlayerInput>();
        var abilities = player.GetComponent<PcsPlayerAbilities>();
        playerDescription = PathOf(player.transform) + " | " + (abilities == null ? "역할 관측 불가" : abilities.CharacterType.ToString()) +
            " | Unity " + Application.unityVersion + " | Shared / 로컬 StateAuthority";
        recordedJumpPolicy = jumpPolicy;
        recordedStartUtc = DateTime.UtcNow.ToString("O");
        started = previousTime = Time.timeAsDouble;
        nextPoint = 1;
        running = true;
        status = "관찰 중 (자동 이동 없음)";
        Sample first = ReadSample(0d);
        samples.Add(first);
        sawJump = first.jump;
        sawAirborne = !first.grounded;
        stationaryAnchor = first.foot;
        stationarySince = started;
        reverseSince = started;
        AddEvent(0d, "시작 영역 확인. 출발: " + recordedRoute[0].name);
        report = "관찰을 시작했습니다. Game View에 포커스를 두고 기존 키로 조작하세요.";
    }

    private void Observe()
    {
        if (EditorApplication.timeSinceStartup >= nextRepaint)
        {
            nextRepaint = EditorApplication.timeSinceStartup + 0.2;
            Repaint();
            if (showHandles) SceneView.RepaintAll();
        }
        if (!running) return;
        string issue = GetEnvironmentIssue();
        if (issue != null) { Stop("관측 중단 / 미확인: " + issue); return; }
        if (playerInput != null && playerInput.isActiveAndEnabled && playerInput.JumpInput) sawJump = true;
        double now = Time.timeAsDouble;
        double delta = now - previousTime;
        if (delta < 0d) { Stop("문제 관찰 / 재현 필요: 실행 시간 역행"); return; }
        if (delta < 0.1d) return;
        if (samples.Count >= MaxSamples || events.Count >= MaxEvents)
        { Stop("기록 상한 도달: 관찰 중지 / 미확인"); return; }

        Sample sample = ReadSample(now - started);
        Sample previous = samples[samples.Count - 1];
        samples.Add(sample);
        previousTime = now;
        sawAirborne |= !sample.grounded;
        sawJump |= sample.jump;
        // A read-only observer cannot prove the absence of every external position write.
        // Large discontinuities and missed observation intervals must never produce a pass.
        float plausibleStep = Mathf.Max(0.8f,
            (Mathf.Max(previous.velocity.magnitude, sample.velocity.magnitude) + player.MoveSpeed + player.JumpSpeed) * (float)delta * 2f);
        if (delta > 0.5d || !Finite(sample.foot) || !Finite(sample.velocity) || !Finite(plausibleStep) ||
            Vector2.Distance(previous.foot, sample.foot) > plausibleStep)
        { Stop("문제 관찰 / 재현 필요: 관측 공백 또는 위치 불연속. 순간이동 여부 확인 필요"); return; }

        if (player.Rider != null && player.Rider.IsRiding)
        { Stop("문제 관찰 / 조건 확인 필요: 운반 중 이동은 독립 경사 등반 통과로 세지 않습니다."); return; }
        if (recordedJumpPolicy == 1 && (sawJump || sawAirborne))
        { Stop("문제 관찰 / 조건 확인 필요: 걷기 시험 중 점프 입력 또는 비접지 관측. 경로 불가능을 뜻하지 않습니다."); return; }
        float minimumY = recordedRoute[0].position.y;
        foreach (Point point in recordedRoute) minimumY = Mathf.Min(minimumY, point.position.y);
        if (sample.foot.y < minimumY - fallMargin)
        { Stop("문제 관찰 / 재현 필요: 경로 아래 낙하"); return; }
        float distance = float.PositiveInfinity;
        for (int i = 1; i < recordedRoute.Count; i++)
            distance = Mathf.Min(distance, DistanceToSegment(sample.foot, recordedRoute[i - 1].position, recordedRoute[i].position));
        if (distance > routeWidth)
        { Stop("문제 관찰 / 경로 지정 확인 필요: 관찰 경로 이탈"); return; }

        int reached = Advance(recordedRoute, nextPoint, previous.foot, sample.foot);
        if (reached < 0) { Stop("문제 관찰 / 재현 필요: 순서 지점을 건너뛴 이동"); return; }
        if (reached > nextPoint)
        {
            AddEvent(sample.time, "도달: " + nextPoint + " · " + recordedRoute[nextPoint].name + " / 발 " + sample.foot.ToString("F2"));
            nextPoint = reached;
            reverseReported = false;
        }
        if (nextPoint == recordedRoute.Count)
        { Stop("관측된 조건에서 통과 (순서 경로 도달, 조작감·4인·입력 출처는 별도 확인)"); return; }

        if (Vector2.Distance(stationaryAnchor, sample.foot) > 0.15f)
        { stationaryAnchor = sample.foot; stationarySince = now; stationaryReported = false; }
        else if (!stationaryReported && now - stationarySince >= stationarySeconds)
        {
            string intent = !sample.inputKnown ? "입력 의도 관측 불가" : Mathf.Abs(sample.input.x) > 0.1f
                ? "공개 이동 입력 있음 · 막힘 여부 재현 필요" : "공개 이동 입력 없음";
            AddEvent(sample.time, "정지 관찰: " + sample.foot.ToString("F2") + " / " + intent + " / 다음 " + recordedRoute[nextPoint].name);
            stationaryReported = true;
        }
        Vector2 direction = (recordedRoute[nextPoint].position - recordedRoute[nextPoint - 1].position).normalized;
        bool backwards = Vector2.Dot(sample.velocity, direction) < -0.3f;
        if (!backwards) { reverseSince = now; reverseReported = false; }
        else if (!reverseReported && now - reverseSince >= 0.5d)
        {
            AddEvent(sample.time, "경로 역방향 속도 관찰 (미끄러짐/의도 이동 구분 필요): " + sample.foot.ToString("F2") +
                " / 속도 " + sample.velocity.ToString("F2") + " / 입력 " + (sample.inputKnown ? sample.input.ToString("F2") : "관측 불가"));
            reverseReported = true;
        }
    }

    private Sample ReadSample(double time)
    {
        contacts.Clear();
        player.Body.GetContacts(contacts);
        bool known = playerInput != null && playerInput.isActiveAndEnabled && playerInput.CanMoveInput;
        return new Sample { time = time, foot = FootPosition(player), velocity = player.Body.linearVelocity,
            grounded = player.Grounded, contacts = contacts.Count, inputKnown = known,
            input = known ? playerInput.MoveInput : Vector2.zero,
            jump = playerInput != null && playerInput.isActiveAndEnabled && playerInput.JumpInput };
    }

    private void Stop(string reason)
    {
        if (!running) return;
        running = false;
        status = reason;
        AddEvent(samples.Count == 0 ? 0d : samples[samples.Count - 1].time, reason);
        report = CreateReport();
        Repaint();
    }

    private void ClearRecords()
    {
        running = false;
        samples.Clear(); events.Clear(); recordedRoute.Clear();
        sawJump = sawAirborne = stationaryReported = reverseReported = false;
        playerDescription = recordedStartUtc = ""; nextPoint = 0;
        status = "미실행";
        report = "관찰 기록만 초기화했습니다. 플레이어·퍼즐·씬 상태는 변경하지 않았습니다.";
        SceneView.RepaintAll();
    }

    private void AddEvent(double time, string message)
    {
        if (events.Count < MaxEvents) events.Add(time.ToString("F2") + "초 | " + message);
    }

    private string CreateReport()
    {
        var text = new StringBuilder();
        text.AppendLine("지그재그 수동 이동 관찰: " + status).AppendLine(playerDescription);
        text.AppendLine("시작 UTC: " + recordedStartUtc);
        text.AppendLine("관찰 조건: " + JumpPolicies[recordedJumpPolicy]);
        text.AppendLine("점프 입력 관측=" + sawJump + ", 비접지 관측=" + sawAirborne);
        text.AppendLine("입력 출처·소비 사이 점프·미세한 외부 위치 변경은 관측 불가. 점프 미관측 != 걷기 통과 증명.");
        text.AppendLine("접지는 Mover.Grounded 공개 판정, 접촉 수는 Rigidbody2D.GetContacts 조회. 접지 내부 contact/cast 구분은 관측 불가.");
        text.AppendLine("이 도구는 입력·위치·물리·퍼즐을 변경하지 않음. 한 캐릭터 관찰은 네 역할/4인 동기화/조작감 검증이 아님.");
        for (int i = 0; i < recordedRoute.Count; i++)
            text.AppendLine($"경로 {i}: {recordedRoute[i].name}, 발 {recordedRoute[i].position:F3}, 반경 {recordedRoute[i].radius:F2}");
        foreach (string entry in events) text.AppendLine(entry);
        text.AppendLine("궤적: 초 | 발X,Y | 속도X,Y | Grounded | 접촉수 | 공개이동입력 | JumpInput");
        foreach (Sample sample in samples)
            text.AppendLine($"{sample.time:F2} | {sample.foot:F3} | {sample.velocity:F3} | {sample.grounded} | {sample.contacts} | " +
                (sample.inputKnown ? sample.input.ToString("F2") : "관측 불가") + " | " + sample.jump);
        return text.ToString();
    }

    private void DrawRoute(SceneView view)
    {
        if (!showHandles) return;
        List<Point> route = running ? recordedRoute : points;
        Color previousColor = Handles.color;
        try
        {
            for (int i = 0; i < route.Count; i++)
            {
                Point point = route[i];
                if (!Finite(point.position) || !Finite(point.radius) || point.radius <= 0f) continue;
                Vector3 position = point.position;
                Handles.color = i == 0 ? Color.green : i == route.Count - 1 ? Color.cyan : Color.yellow;
                Handles.DrawWireDisc(position, Vector3.forward, point.radius);
                Handles.Label(position + Vector3.up * point.radius, i + " · " + point.name);
                if (i > 0 && Finite(route[i - 1].position)) Handles.DrawLine(route[i - 1].position, position);
                if (!running)
                {
                    EditorGUI.BeginChangeCheck();
                    Vector3 moved = Handles.PositionHandle(position, Quaternion.identity);
                    if (EditorGUI.EndChangeCheck()) { point.position = moved; Repaint(); }
                }
            }
            Handles.color = Color.magenta;
            for (int i = 1; i < samples.Count; i++) Handles.DrawLine(samples[i - 1].foot, samples[i].foot);
        }
        finally { Handles.color = previousColor; }
    }

    private static Vector2 FootPosition(Mover mover)
    { Bounds bounds = mover.BodyCollider.bounds; return new Vector2(bounds.center.x, bounds.min.y); }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool Finite(Vector2 value) => Finite(value.x) && Finite(value.y);
    private static bool Contains(Point point, Vector2 position) => Vector2.Distance(point.position, position) <= point.radius;
    private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 line = b - a;
        float t = line.sqrMagnitude < 0.000001f ? 0f : Mathf.Clamp01(Vector2.Dot(point - a, line) / line.sqrMagnitude);
        return Vector2.Distance(point, a + line * t);
    }

    private static int Advance(List<Point> route, int next, Vector2 previous, Vector2 current)
    {
        // No sweeping through several checkpoints in a single observation sample.
        for (int i = next + 1; i < route.Count; i++)
            if (DistanceToSegment(route[i].position, previous, current) <= route[i].radius) return -1;
        return DistanceToSegment(route[next].position, previous, current) <= route[next].radius ? next + 1 : next;
    }

    private static string ValidateRoute(List<Point> route, float width, float fall, float stationary)
    {
        if (route.Count < 3) return "경로에 시작·중간 지점·도착이 필요합니다.";
        if (!Finite(width) || width <= 0f || !Finite(fall) || fall <= 0f || !Finite(stationary) || stationary < 0.5f)
            return "경로 거리·낙하 여유는 양수, 정지 시간은 0.5초 이상이어야 합니다.";
        for (int i = 0; i < route.Count; i++)
        {
            if (!Finite(route[i].position) || !Finite(route[i].radius) || route[i].radius <= 0f || route[i].radius > width)
                return i + "번 지점의 유한 좌표와 반경(0 초과, 경로 허용 거리 이하)을 확인하세요.";
            for (int j = 0; j < i; j++)
                if (Vector2.Distance(route[i].position, route[j].position) <= route[i].radius + route[j].radius)
                    return j + "번과 " + i + "번 통과 영역이 겹칩니다. 좌표 또는 반경을 조정하세요.";
        }
        return null;
    }

    private static string PathOf(Transform target)
    {
        string path = target.name;
        while (target.parent != null) { target = target.parent; path = target.name + "/" + path; }
        return path;
    }

    private void RunSelfCheck()
    {
        // Synthetic coordinates exercise only this window's guards, never game objects or physics.
        var route = new List<Point> { new Point("시작") { position = Vector2.zero, radius = 0.2f },
            new Point("중간") { position = new Vector2(2f, 0f), radius = 0.2f },
            new Point("도착") { position = new Vector2(4f, 0f), radius = 0.2f } };
        int passed = 0;
        if (ValidateRoute(route, 1f, 2f, 2f) == null) passed++;
        if (!Contains(route[0], route[2].position)) passed++;
        if (Advance(route, 1, Vector2.zero, new Vector2(2f, 0f)) == 2) passed++;
        if (Advance(route, 1, Vector2.zero, new Vector2(4f, 0f)) == -1) passed++;
        if (Advance(route, 1, Vector2.zero, new Vector2(0.5f, 0f)) == 1) passed++;
        if (Advance(route, 2, new Vector2(2f, 0f), new Vector2(4f, 0f)) == 3) passed++;
        route[1].position = Vector2.zero;
        if (ValidateRoute(route, 1f, 2f, 2f) != null) passed++;
        route[1].position = new Vector2(float.NaN, 0f);
        if (ValidateRoute(route, 1f, 2f, 2f) != null) passed++;
        selfCheckResult = passed + "/8 " + (passed == 8 ? "통과" : "실패") + " · 합성 판정만, 캐릭터 이동 미검증";
    }
}
#endif

using UnityEditor;
using UnityEngine;

/// <summary>Editor-only input fixtures; the director remains responsible for simulation and replication.</summary>
public sealed class PcsDeviceTestWindow : EditorWindow
{
    private PcsPuzzleDirector director;
    private PcsPuzzleDirector ownedDirector;
    private PcsPuzzleDevice selectedDevice;
    private PcsPuzzleDevice pressurePlate;
    private Vector2 scroll;
    private double nextRefresh;
    private string lastResult;
    private bool lastResultFailed;
    private bool showHelp;

    [MenuItem("Tools/FOUR PAWS/장치 수동 테스트")]
    public static void OpenWindow()
    {
        var window = GetWindow<PcsDeviceTestWindow>("장치 수동 테스트");
        window.minSize = new Vector2(440f, 580f);
        window.Show();
    }

    private void OnEnable()
    {
        // No serialized fields or EditorPrefs: a reopened window never restores forced inputs.
        director = null;
        ownedDirector = null;
        showHelp = false;
        EditorApplication.update += RefreshWindow;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        AssemblyReloadEvents.beforeAssemblyReload += ReleaseOwnedInputs;
        Selection.selectionChanged += OnSelectionChanged;
        OnSelectionChanged();
    }

    private void OnDisable()
    {
        ReleaseOwnedInputs();
        EditorApplication.update -= RefreshWindow;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        AssemblyReloadEvents.beforeAssemblyReload -= ReleaseOwnedInputs;
        Selection.selectionChanged -= OnSelectionChanged;
    }

    private void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode || state == PlayModeStateChange.ExitingEditMode)
            ReleaseOwnedInputs();
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            director = null;
            lastResult = "Play가 끝나 테스트 입력을 해제했습니다. 저장된 씬에는 테스트 상태를 기록하지 않습니다.";
            lastResultFailed = false;
        }
        Repaint();
    }

    private void ReleaseOwnedInputs()
    {
        if (ownedDirector != null)
        {
            ownedDirector.EditorReleaseForcedInputs();
            ownedDirector.EditorSetTestsEnabled(false, out _);
        }
        ownedDirector = null;
    }

    private void RefreshWindow()
    {
        if (EditorApplication.timeSinceStartup < nextRefresh) return;
        nextRefresh = EditorApplication.timeSinceStartup + 0.2;
        PcsPuzzleDirector current = PcsPuzzleDirector.Instance;
        if (ownedDirector != null && ownedDirector != current) ReleaseOwnedInputs();
        director = current;
        if (director != null && director.EditorTestsEnabled) ownedDirector = director;
        Repaint();
    }

    private void OnSelectionChanged()
    {
        GameObject selected = Selection.activeGameObject;
        selectedDevice = selected != null ? selected.GetComponentInParent<PcsPuzzleDevice>() : null;
        if (selectedDevice == null && selected != null)
        {
            PcsPuzzleDevice[] children = selected.GetComponentsInChildren<PcsPuzzleDevice>(true);
            if (children.Length == 1) selectedDevice = children[0];
        }
        if (selectedDevice != null && selectedDevice.Kind == PcsDeviceKind.PressurePlate)
            pressurePlate = selectedDevice;
        Repaint();
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.LabelField("장치 수동 테스트", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("정상 Shared 세션과 장치 StateAuthority가 필요합니다.\n" +
            "입력 조건을 강제하는 도구입니다. 실제 캐릭터 입력 성공 검증과는 다릅니다.", MessageType.Info);

        bool ready = false;
        string reason;
        if (!EditorApplication.isPlaying)
            reason = "Play 중에만 사용할 수 있습니다. Shared 세션으로 진입한 뒤 테스트를 켜세요.";
        else if (director == null)
            reason = "스폰된 퍼즐 제어기(PcsPuzzleDirector)가 없습니다. Runner와 Puzzle 씬의 네트워크 등록을 확인하세요.";
        else ready = director.EditorTryGetTestStatus(out reason);
        if (ready) EditorGUILayout.LabelField(reason, EditorStyles.wordWrappedMiniLabel);
        else EditorGUILayout.HelpBox(reason, MessageType.Warning);

        bool enabled = director != null && director.EditorTestsEnabled;
        using (new EditorGUI.DisabledScope(!ready && !enabled))
        {
            bool requested = EditorGUILayout.ToggleLeft("이번 Play에서 수동 테스트 활성화", enabled);
            if (requested != enabled && director != null)
            {
                bool ok = director.EditorSetTestsEnabled(requested, out string result);
                SetResult(ok, result);
                if (ok) ownedDirector = requested ? director : null;
                enabled = director.EditorTestsEnabled;
            }
        }
        bool canControl = ready && enabled;
        bool pending = director != null && director.EditorTestHasPendingCommand;
        if (ready)
        {
            EditorGUILayout.LabelField("구간 " + SectionLabel(director.ActiveSection) + " · 강제 압력판 " +
                director.EditorForcedPressureCount + "개 · 레버 " + director.EditorLeverInputLabel, EditorStyles.wordWrappedMiniLabel);
        }
        if (pending) EditorGUILayout.HelpBox("다음 네트워크 틱에서 명령을 처리하고 있습니다.", MessageType.Info);
        DrawShaftLiftControls(canControl);
        using (new EditorGUI.DisabledScope(!canControl || pending))
            if (GUILayout.Button("1-1 테스트 구간으로 전환"))
                Queue(PcsPuzzleDirector.EditorStageOneCommand.PrepareSection);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("지정한 압력판만 시험", EditorStyles.boldLabel);
        pressurePlate = (PcsPuzzleDevice)EditorGUILayout.ObjectField("시험할 압력판", pressurePlate, typeof(PcsPuzzleDevice), true);
        PcsPuzzleDevice stagePlate = FindStageOnePlate();
        EditorGUILayout.BeginHorizontal();
        using (new EditorGUI.DisabledScope(stagePlate == null))
            if (GUILayout.Button("1-1 압력판 선택")) pressurePlate = selectedDevice = stagePlate;
        using (new EditorGUI.DisabledScope(selectedDevice == null || selectedDevice.Kind != PcsDeviceKind.PressurePlate))
            if (GUILayout.Button("현재 선택한 압력판 사용")) pressurePlate = selectedDevice;
        EditorGUILayout.EndHorizontal();
        bool validPlate = pressurePlate != null && pressurePlate.Kind == PcsDeviceKind.PressurePlate;
        if (pressurePlate != null && !validPlate)
            EditorGUILayout.HelpBox("PressurePlate 종류의 PcsPuzzleDevice를 지정해야 합니다.", MessageType.Warning);
        if (validPlate)
        {
            EditorGUILayout.LabelField("대상", pressurePlate.name + " / ID " + pressurePlate.DeviceId + " / " + SectionLabel(pressurePlate.Section));
            DrawLinks(pressurePlate);
        }
        var mode = director != null && validPlate ? director.EditorGetPressureInput(pressurePlate) : PcsPuzzleDirector.EditorPressureInput.Normal;
        using (new EditorGUI.DisabledScope(!canControl || pending || !validPlate))
        {
            int next = GUILayout.Toolbar((int)mode, new[] { "정상 입력", "강제로 누름", "강제로 해제" });
            if (next != (int)mode)
                SetResult(director.EditorSetPressureInput(pressurePlate, (PcsPuzzleDirector.EditorPressureInput)next, out string result), result);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("1-1 중앙 발판", EditorStyles.boldLabel);
        PcsPuzzleDevice lift = FindStageOneLift();
        if (lift != null) DrawReference("연결된 중앙 발판", lift);
        else if (ready) EditorGUILayout.HelpBox("등록된 1-1 삼단계 중앙 발판이 없거나 둘 이상입니다. 장치 연결을 확인하세요.", MessageType.Warning);
        if (ready)
        {
            string hack = director.StageOneHacked ? "완료" : "미완료";
            string target = !director.StageOneHacked ? "상단" : director.LeverLower ? "하단" : "중단";
            EditorGUILayout.LabelField("해킹 " + hack + " · 목표 " + target, EditorStyles.wordWrappedMiniLabel);
        }
        using (new EditorGUI.DisabledScope(!canControl || pending || lift == null))
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("해킹 미완료")) Queue(PcsPuzzleDirector.EditorStageOneCommand.HackIncomplete);
            if (GUILayout.Button("해킹 완료")) Queue(PcsPuzzleDirector.EditorStageOneCommand.HackComplete);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("레버 유지")) Queue(PcsPuzzleDirector.EditorStageOneCommand.HoldLever);
            if (GUILayout.Button("레버 해제")) Queue(PcsPuzzleDirector.EditorStageOneCommand.ReleaseLever);
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.LabelField("미완료: 상단 / 완료: 중단 / 유지: 하단 / 해제: 중단", EditorStyles.wordWrappedMiniLabel);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("강제 입력 해제와 구간 초기화", EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(director == null || !enabled))
        {
            if (GUILayout.Button("모든 강제 입력 해제 · 정상 판정으로 복귀"))
            {
                director.EditorReleaseForcedInputs();
                SetResult(true, "강제 압력판·레버 입력과 대기 명령을 해제하고 시험하던 1-2 엘리베이터를 현재 위치에 정지했습니다. 1-1 해킹 완료 등 진행 상태는 유지합니다.");
            }
        }
        EditorGUILayout.LabelField("강제 입력 해제는 해킹 완료 상태를 유지합니다.", EditorStyles.wordWrappedMiniLabel);
        using (new EditorGUI.DisabledScope(!canControl || pending))
            if (GUILayout.Button("1-1 구간 초기화 (테스트 초기화)"))
                Queue(PcsPuzzleDirector.EditorStageOneCommand.ResetSection);
        EditorGUILayout.LabelField("구간 초기화는 1-1 진행·장치 위치·체력을 초기화합니다.", EditorStyles.wordWrappedMiniLabel);
        if (!string.IsNullOrEmpty(lastResult))
            EditorGUILayout.HelpBox(lastResult, lastResultFailed ? MessageType.Warning : MessageType.Info);
        if (director != null && !string.IsNullOrEmpty(director.EditorTestLastMessage))
            EditorGUILayout.LabelField("제어기 처리 결과: " + director.EditorTestLastMessage, EditorStyles.wordWrappedLabel);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("현재 선택한 장치와 연결", EditorStyles.boldLabel);
        selectedDevice = (PcsPuzzleDevice)EditorGUILayout.ObjectField("확인할 장치", selectedDevice, typeof(PcsPuzzleDevice), true);
        if (selectedDevice == null)
            EditorGUILayout.LabelField("Hierarchy에서 장치를 선택하거나 위 칸에 넣으세요.", EditorStyles.wordWrappedMiniLabel);
        else DrawDevice(selectedDevice);

        EditorGUILayout.Space();
        showHelp = EditorGUILayout.Foldout(showHelp, "사용법·주의사항", true);
        if (showHelp)
        {
            EditorGUILayout.HelpBox("시작: LobbyScene에서 캐릭터 선택 → 혼자 Shared 방 입장 → 게임 시작 → 이 창의 테스트 활성화.\n" +
                "1-2 엘리베이터: 구간 전환 없이 '1-2 상승 시작'을 누르세요. 1-1 장치 시험만 '1-1 테스트 구간으로 전환'이 필요합니다.\n" +
                "Puzzle 직접 Play만으로는 네트워크가 초기화되지 않습니다. 구간 전환은 실행 중에만 적용하며 튜토리얼 완료 처리나 씬 저장을 하지 않습니다.\n\n" +
                "압력판 정상 입력: 실제 캐릭터 판정으로 복귀합니다. 강제로 해제: 대상이 올라서도 지정한 판만 눌리지 않게 합니다. " +
                "강제 입력을 모두 해제해도 곰이 실제로 밟고 있으면 압력판은 계속 눌릴 수 있습니다.\n\n" +
                "해킹 완료 후 압력판을 놓아도 해킹 상태는 유지됩니다. 이동 중에도 해킹·레버 조건을 바꿀 수 있습니다.\n\n" +
                "구간 초기화는 실제 게임의 리셋을 사용하므로 장치가 초기 위치로 즉시 돌아옵니다. " +
                "창 닫기·코드 리로드·Play 종료 시 강제 입력을 해제하고 테스트를 끕니다. 시험하던 1-2 엘리베이터에는 현재 위치 정지를 요청합니다.\n\n" +
                "강제 입력 해제·창 닫기는 현재 시험 구간과 해킹 완료를 유지합니다. 저장된 씬의 시작 상태로 돌아가려면 Play를 종료하세요.\n\n" +
                "실제 곰 접촉·쥐 E 해킹·개구리 조준/F 홀드 성공은 별도로 검증해야 합니다.", MessageType.Info);
        }
        EditorGUILayout.EndScrollView();
    }

    private void Queue(PcsPuzzleDirector.EditorStageOneCommand command)
    {
        SetResult(director.EditorQueueStageOneCommand(command, out string reason), reason);
    }

    private void DrawShaftLiftControls(bool canControl)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("1-2 메인 엘리베이터 · 이동만 시험", EditorStyles.boldLabel);
        PcsPuzzleDevice lift = FindShaftLift();
        if (lift != null) DrawReference("시험할 엘리베이터", lift);
        else EditorGUILayout.HelpBox("1-2 메인 엘리베이터가 하나로 확인되지 않습니다. 정상 Shared 세션에서 등록 상태를 확인하세요.", MessageType.Info);
        EditorGUILayout.HelpBox("1-2 구간 전환 없이 기존 엘리베이터의 상승·현재 위치 정지만 제어합니다.\n" +
            "이동 명령은 네트워크 상태에 반영됩니다. 탑승 안전이나 통로 통과 여부는 별도로 확인하세요.", MessageType.Info);
        bool valid = false;
        string reason = "";
        if (canControl) valid = director.EditorTryGetShaftLiftStatus(lift, out reason);
        if (canControl && !valid) EditorGUILayout.HelpBox(reason, MessageType.Warning);
        if (valid)
        {
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("상단 목표", lift.UpperStop, typeof(Transform), true);
            }
            EditorGUILayout.LabelField("현재 Y / 상단", director.States[lift.DeviceId].Position.y.ToString("F3") + " / " +
                (lift.UpperStop != null ? lift.UpperStop.position.y.ToString("F3") : "목표 없음"));
            EditorGUILayout.LabelField("이동 속도 (Device.Speed)", lift.Speed.ToString("F2") + " 유닛/초");
            PcsPuzzleDirector.EditorShaftLiftInput mode = director.EditorGetShaftLiftInput(lift);
            EditorGUILayout.LabelField("네트워크 이동 명령", director.States[lift.DeviceId].Phase == (int)PcsShaftLiftCommand.Raise ? "상승" : "현재 위치 정지");
            EditorGUILayout.LabelField("이 창의 시험 입력", mode == PcsPuzzleDirector.EditorShaftLiftInput.Normal ? "해제됨" :
                mode == PcsPuzzleDirector.EditorShaftLiftInput.Raise ? "상승" : "현재 위치 정지");
        }
        using (new EditorGUI.DisabledScope(!valid))
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("1-2 상승 시작")) SetShaftLiftInput(lift, PcsPuzzleDirector.EditorShaftLiftInput.Raise);
            if (GUILayout.Button("현재 위치 정지")) SetShaftLiftInput(lift, PcsPuzzleDirector.EditorShaftLiftInput.Hold);
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.LabelField("테스트 끄기·강제 입력 해제·창 닫기: 이 창에서 시험하던 엘리베이터를 현재 위치에 정지합니다.", EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("1-1 장치 시험", EditorStyles.boldLabel);
    }

    private void SetShaftLiftInput(PcsPuzzleDevice lift, PcsPuzzleDirector.EditorShaftLiftInput mode)
    {
        SetResult(director.EditorSetShaftLiftInput(lift, mode, out string reason), reason);
    }

    private PcsPuzzleDevice FindShaftLift()
    {
        if (director == null || director.Devices == null) return null;
        PcsPuzzleDevice found = null;
        foreach (PcsPuzzleDevice device in director.Devices)
        {
            if (device == null || !device.isActiveAndEnabled || !device.IsShaftElevator) continue;
            if (found != null) return null;
            found = device;
        }
        return found;
    }

    private void SetResult(bool success, string message)
    {
        lastResult = message;
        lastResultFailed = !success;
        Repaint();
    }

    private PcsPuzzleDevice FindStageOneLift()
    {
        if (director == null) return null;
        PcsPuzzleDevice found = null;
        foreach (PcsPuzzleDevice device in director.Devices)
        {
            if (device == null || device.Section != 1 || device.Kind != PcsDeviceKind.Elevator ||
                device.LiftPolicy != PcsLiftPolicy.StageOneThreeStop) continue;
            if (found != null) return null;
            found = device;
        }
        return found;
    }

    private PcsPuzzleDevice FindStageOnePlate()
    {
        if (director == null) return null;
        PcsPuzzleDevice found = null;
        foreach (PcsPuzzleDevice device in director.Devices)
        {
            if (device == null || !device.isActiveAndEnabled || device.Section != 1 ||
                device.Kind != PcsDeviceKind.PressurePlate) continue;
            if (found != null) return null;
            found = device;
        }
        return found;
    }

    private static void DrawDevice(PcsPuzzleDevice device)
    {
        EditorGUILayout.LabelField("장치 번호 / 종류", device.DeviceId + " / " + KindLabel(device.Kind));
        EditorGUILayout.LabelField("소속 구간", SectionLabel(device.Section));
        EditorGUILayout.LabelField("오브젝트 활성", device.isActiveAndEnabled ? "활성" : "비활성");
        if (device.IsPassiveShaftObject)
            EditorGUILayout.LabelField("1-2 퍼즐 기능 제거 · 저장된 배치만 유지", EditorStyles.wordWrappedMiniLabel);
        DrawReference("현재 장치", device);
        DrawLinks(device);
    }

    private static void DrawLinks(PcsPuzzleDevice device)
    {
        if (device.Links == null || device.Links.Length == 0)
        {
            EditorGUILayout.LabelField("연결 대상", "없음");
            return;
        }
        for (int i = 0; i < device.Links.Length; i++)
            DrawReference("연결 대상 " + (i + 1), device.Links[i]);
    }

    private static void DrawReference(string label, PcsPuzzleDevice device)
    {
        EditorGUILayout.BeginHorizontal();
        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.ObjectField(label, device, typeof(PcsPuzzleDevice), true);
        using (new EditorGUI.DisabledScope(device == null))
            if (GUILayout.Button("위치 보기", GUILayout.Width(70f)))
            {
                EditorGUIUtility.PingObject(device.gameObject);
                Bounds bounds = device.Bounds;
                if (device.Kind == PcsDeviceKind.Elevator)
                {
                    Vector3 size = bounds.size;
                    Vector3 offset = bounds.center - device.transform.position;
                    if (device.LowerStop != null) bounds.Encapsulate(new Bounds(device.LowerStop.position + offset, size));
                    if (device.MiddleStop != null) bounds.Encapsulate(new Bounds(device.MiddleStop.position + offset, size));
                    if (device.UpperStop != null) bounds.Encapsulate(new Bounds(device.UpperStop.position + offset, size));
                }
                if (bounds.size.magnitude < 3f) bounds.size = Vector3.one * 3f;
                if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.Frame(bounds, false);
            }
        EditorGUILayout.EndHorizontal();
    }

    private static string SectionLabel(int section)
    {
        switch (section)
        {
            case -1: return "공통";
            case 0: return "튜토리얼";
            case 1: return "1-1";
            case 2: return "1-2";
            case 3: return "완료";
            default: return "구간 " + section;
        }
    }

    private static string KindLabel(PcsDeviceKind kind)
    {
        switch (kind)
        {
            case PcsDeviceKind.PressurePlate: return "압력판";
            case PcsDeviceKind.SlidingWall: return "이동 문";
            case PcsDeviceKind.Elevator: return "발판";
            case PcsDeviceKind.HackConsole: return "해킹 콘솔";
            case PcsDeviceKind.Lever: return "레버";
            case PcsDeviceKind.RemoteButton: return "원격 버튼";
            case PcsDeviceKind.ChannelConsole: return "채널 콘솔";
            case PcsDeviceKind.TimedPlatform: return "시간제 발판";
            case PcsDeviceKind.BarrierControl: return "장벽 제어";
            case PcsDeviceKind.Arrival: return "도착 구역";
            case PcsDeviceKind.Exit: return "출구";
            case PcsDeviceKind.TutorialExit: return "튜토리얼 출구";
            case PcsDeviceKind.Dummy: return "더미";
            case PcsDeviceKind.Anchor: return "고리";
            case PcsDeviceKind.Ladder: return "사다리";
            case PcsDeviceKind.Enemy: return "적";
            case PcsDeviceKind.Battery: return "배터리";
            case PcsDeviceKind.Checkpoint: return "체크포인트";
            case PcsDeviceKind.KillZone: return "위험 구역";
            case PcsDeviceKind.Instruction: return "안내";
            default: return kind.ToString();
        }
    }
}

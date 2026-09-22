#if UNITY_EDITOR
using System.Collections.Generic;
using Fusion;
using UnityEngine;

// No fields or methods in this file exist in a player build. Requests are local,
// non-serialized inputs; only the existing state authority consumes them in its tick.
public partial class PcsPuzzleDirector
{
    public enum EditorPressureInput { Normal, Pressed, Released }
    public enum EditorStageOneCommand { PrepareSection, HackIncomplete, HackComplete, HoldLever, ReleaseLever, ResetSection }
    public enum EditorShaftLiftInput { Normal, Raise, Hold, Lower }

    private bool editorTestsEnabled;
    private readonly Dictionary<PcsPuzzleDevice, EditorPressureInput> editorPressureInputs = new Dictionary<PcsPuzzleDevice, EditorPressureInput>();
    private readonly Queue<EditorStageOneCommand> editorCommands = new Queue<EditorStageOneCommand>();
    private PcsPuzzleDevice editorHoldDevice;
    private bool editorHoldPressed;
    private PcsPuzzleDevice editorShaftLift;
    private EditorShaftLiftInput editorShaftLiftInput;
    private Vector2 editorShaftHoldPosition;
    private int editorTestSection = -1;
    private string editorTestLastMessage = "수동 테스트가 꺼져 있습니다.";

    public bool EditorTestsEnabled => editorTestsEnabled;
    public bool EditorHasForcedInputs => editorPressureInputs.Count != 0 || editorHoldDevice != null || editorShaftLift != null;
    public int EditorForcedPressureCount => editorPressureInputs.Count;
    public bool EditorTestHasPendingCommand => editorCommands.Count != 0;
    public string EditorTestLastMessage => editorTestLastMessage;
    public string EditorLeverInputLabel => editorHoldDevice == null ? "정상 입력" : editorHoldPressed ? "강제로 유지" : "강제로 해제";

    public bool EditorTryGetTestStatus(out string reason)
    {
        if (!Application.isPlaying) reason = "Play 중에만 사용할 수 있습니다.";
        else if (!isActiveAndEnabled) reason = "퍼즐 Director가 비활성 상태입니다.";
        else if (Object == null || !Object.IsValid)
            reason = "Puzzle이 실행 중인 Runner에 스폰·등록되지 않았습니다. LobbyScene에서 정상 Shared 세션을 시작하세요. Puzzle 직접 Play만으로는 사용할 수 없습니다.";
        else if (Runner == null || !Runner.IsRunning) reason = "실행 중인 NetworkRunner가 없습니다.";
        else if (Runner.GameMode != GameMode.Shared) reason = "Shared 세션에서만 사용할 수 있습니다.";
        else if (!HasStateAuthority) reason = "이 클라이언트에는 퍼즐 StateAuthority가 없습니다. 권한을 가진 Editor에서 조작하세요.";
        else if (!Initialized) reason = "퍼즐 네트워크 초기화를 기다리는 중입니다.";
        else { reason = "정상 Shared 세션 · 퍼즐 StateAuthority 확인됨"; return true; }
        return false;
    }

    public bool EditorSetTestsEnabled(bool enabled, out string reason)
    {
        if (!enabled)
        {
            EditorForgetDeviceTests();
            reason = editorTestLastMessage = "테스트 꺼짐: 다음 물리 틱부터 정상 입력을 사용합니다. 해킹 진행은 유지됩니다.";
            return true;
        }
        if (!EditorTryGetTestStatus(out reason)) return false;
        editorTestsEnabled = true;
        editorTestSection = ActiveSection;
        reason = editorTestLastMessage = "수동 테스트 켜짐. 장치를 지정해 입력을 선택하세요.";
        return true;
    }

    public EditorPressureInput EditorGetPressureInput(PcsPuzzleDevice plate)
    {
        return plate != null && editorPressureInputs.TryGetValue(plate, out EditorPressureInput mode) ? mode : EditorPressureInput.Normal;
    }

    public bool EditorSetPressureInput(PcsPuzzleDevice plate, EditorPressureInput mode, out string reason)
    {
        if (!EditorCanRequest(out reason)) return false;
        if (!EditorRegistered(plate) || plate.Kind != PcsDeviceKind.PressurePlate)
        { reason = "현재 Director에 등록된 활성 압력판을 지정하세요."; return false; }
        if (mode != EditorPressureInput.Normal && mode != EditorPressureInput.Pressed && mode != EditorPressureInput.Released)
        { reason = "알 수 없는 압력판 입력입니다."; return false; }
        if (mode == EditorPressureInput.Normal) editorPressureInputs.Remove(plate);
        else
        {
            if (plate.Section >= 0 && plate.Section != ActiveSection)
            { reason = "압력판과 현재 구간이 다릅니다. 1-1 장치는 먼저 명시적으로 1-1 시험 구간으로 전환하세요."; return false; }
            editorPressureInputs[plate] = mode;
        }
        reason = editorTestLastMessage = plate.name + ": " + (mode == EditorPressureInput.Normal ? "정상 판정으로 복귀" : mode == EditorPressureInput.Pressed ? "강제로 누름" : "강제로 해제");
        return true;
    }

    public bool EditorQueueStageOneCommand(EditorStageOneCommand command, out string reason)
    {
        if (!EditorCanRequest(out reason) || !EditorFindStageOne(out _, out _, out _, out reason)) return false;
        if (command < EditorStageOneCommand.PrepareSection || command > EditorStageOneCommand.ResetSection)
        { reason = "알 수 없는 1-1 명령입니다."; return false; }
        if (command != EditorStageOneCommand.PrepareSection && ActiveSection != 1)
        { reason = "먼저 '1-1 시험 구간으로 전환'을 누르세요. 튜토리얼 완료로 기록되지는 않습니다."; return false; }
        if (editorCommands.Count >= 8) { reason = "이전 명령이 처리될 때까지 기다려 주세요."; return false; }
        editorCommands.Enqueue(command);
        reason = editorTestLastMessage = "명령 대기: 다음 권한자 물리 틱에서 처리합니다.";
        return true;
    }

    public EditorShaftLiftInput EditorGetShaftLiftInput(PcsPuzzleDevice lift) =>
        lift != null && editorShaftLift == lift ? editorShaftLiftInput : EditorShaftLiftInput.Normal;

    public bool EditorTryGetShaftLiftStatus(PcsPuzzleDevice lift, out string reason)
    {
        if (!EditorTryGetTestStatus(out reason)) return false;
        if (!EditorRegistered(lift) || lift.Section != 2 || lift.Kind != PcsDeviceKind.Elevator ||
            lift.LiftPolicy != PcsLiftPolicy.MainContinuous)
        { reason = "등록된 1-2 메인 엘리베이터가 필요합니다."; return false; }
        if (lift.Elevator == null || !lift.Elevator.isActiveAndEnabled || lift.Body == null ||
            !lift.Body.simulated || lift.Body.bodyType != RigidbodyType2D.Kinematic ||
            (lift.Body.constraints & RigidbodyConstraints2D.FreezePositionY) != 0)
        { reason = "활성 PcsElevator와 Y 이동이 가능한 Kinematic Rigidbody2D가 필요합니다."; return false; }
        if (!float.IsFinite(lift.Speed) || lift.Speed <= 0f)
        { reason = "PcsPuzzleDevice.Speed는 유한한 양수여야 합니다."; return false; }
        if (lift.LowerStop == null || lift.UpperStop == null || lift.LowerStop.IsChildOf(lift.transform) || lift.UpperStop.IsChildOf(lift.transform))
        { reason = "움직이는 발판 밖에 독립된 하단·상단 목표를 연결하세요."; return false; }
        Vector3 lower = lift.LowerStop.position;
        Vector3 upper = lift.UpperStop.position;
        Vector2 position = States[lift.DeviceId].Position;
        if (!float.IsFinite(lower.x) || !float.IsFinite(lower.y) || !float.IsFinite(lower.z) ||
            !float.IsFinite(upper.x) || !float.IsFinite(upper.y) || !float.IsFinite(upper.z) ||
            !float.IsFinite(position.x) || !float.IsFinite(position.y) ||
            Mathf.Abs(lower.x - upper.x) > 0.001f || Mathf.Abs(position.x - lower.x) > 0.001f ||
            Mathf.Abs(lower.z - upper.z) > 0.001f || Mathf.Abs(lower.z - lift.transform.position.z) > 0.001f ||
            !float.IsFinite(upper.y - lower.y) || upper.y <= lower.y + 0.001f)
        { reason = "목표 좌표를 확인하세요. 동일한 월드 X/Z이며 상단 Y가 하단보다 높아야 합니다."; return false; }
        reason = "1-2 메인 엘리베이터 이동 연결 확인됨";
        return true;
    }

    public bool EditorSetShaftLiftInput(PcsPuzzleDevice lift, EditorShaftLiftInput mode, out string reason)
    {
        if (!EditorCanRequest(out reason)) return false;
        if (mode < EditorShaftLiftInput.Normal || mode > EditorShaftLiftInput.Lower)
        { reason = "알 수 없는 엘리베이터 시험 입력입니다."; return false; }
        if (mode == EditorShaftLiftInput.Normal)
        {
            if (editorShaftLift == lift)
            {
                editorShaftLift = null;
                editorShaftLiftInput = EditorShaftLiftInput.Normal;
                editorShaftHoldPosition = default;
            }
            reason = editorTestLastMessage = "1-2 엘리베이터 강제 입력 해제: 기존 해킹 상태에 따른 목표로 현재 위치에서 이동합니다.";
            return true;
        }
        if (!EditorTryGetShaftLiftStatus(lift, out reason)) return false;
        editorShaftLift = lift;
        editorShaftLiftInput = mode;
        editorShaftHoldPosition = States[lift.DeviceId].Position;
        reason = editorTestLastMessage = mode == EditorShaftLiftInput.Raise ? "1-2 상승 요청: 기존 속도로 상단 목표까지 이동합니다." :
            mode == EditorShaftLiftInput.Lower ? "1-2 하단 복귀 요청: 순간이동 없이 기존 속도로 내려갑니다." : "1-2 엘리베이터를 현재 위치에 유지합니다.";
        return true;
    }

    private bool EditorTryShaftLiftTarget(PcsPuzzleDevice lift, out Vector2 target)
    {
        target = default;
        if (!editorTestsEnabled || editorShaftLift != lift) return false;
        if (!EditorTryGetShaftLiftStatus(lift, out string reason))
        {
            editorShaftLift = null;
            editorShaftLiftInput = EditorShaftLiftInput.Normal;
            editorShaftHoldPosition = default;
            editorTestLastMessage = "엘리베이터 시험 해제: " + reason;
            return false;
        }
        target = editorShaftLiftInput == EditorShaftLiftInput.Raise ? (Vector2)lift.UpperStop.position :
            editorShaftLiftInput == EditorShaftLiftInput.Lower ? (Vector2)lift.LowerStop.position : editorShaftHoldPosition;
        return true;
    }

    public void EditorReleaseForcedInputs()
    {
        editorPressureInputs.Clear();
        editorHoldDevice = null;
        editorShaftLift = null;
        editorShaftLiftInput = EditorShaftLiftInput.Normal;
        editorShaftHoldPosition = default;
        editorCommands.Clear();
        // A forced hold has no actor/lease. The normal TickRemoteHolds clears it,
        // or a fresh legitimate owner request replaces it, on the next tick.
        editorTestLastMessage = "강제 입력 해제: 실제 판정으로 복귀합니다. 해킹 완료 상태는 유지됩니다.";
    }

    private void EditorForgetDeviceTests()
    {
        EditorReleaseForcedInputs();
        editorTestsEnabled = false;
        editorTestSection = -1;
    }

    private void OnDisable() { EditorForgetDeviceTests(); }

    private bool EditorCanRequest(out string reason)
    {
        if (!EditorTryGetTestStatus(out reason)) return false;
        if (!editorTestsEnabled) { reason = "먼저 수동 테스트 사용을 켜세요."; return false; }
        return true;
    }

    private bool EditorRegistered(PcsPuzzleDevice device)
    {
        return device != null && device.isActiveAndEnabled && device.DeviceId >= 0 &&
            device.DeviceId < Devices.Length && Devices[device.DeviceId] == device;
    }

    private bool EditorFindStageOne(out PcsPuzzleDevice lift, out PcsPuzzleDevice console, out PcsPuzzleDevice lever, out string reason)
    {
        lift = console = lever = null;
        int lifts = 0, consoles = 0, levers = 0;
        foreach (PcsPuzzleDevice d in Devices)
            if (EditorRegistered(d) && d.Section == 1 && d.Kind == PcsDeviceKind.Elevator && d.LiftPolicy == PcsLiftPolicy.StageOneThreeStop)
            { lift = d; lifts++; }
        if (lifts == 1)
            foreach (PcsPuzzleDevice d in Devices)
            {
                if (!EditorRegistered(d) || d.Section != 1 || d.Links == null || System.Array.IndexOf(d.Links, lift) < 0) continue;
                if (d.Kind == PcsDeviceKind.HackConsole) { console = d; consoles++; }
                if (d.Kind == PcsDeviceKind.Lever && d.RequiresRemoteHold) { lever = d; levers++; }
            }
        if (lifts != 1 || consoles != 1 || levers != 1)
        { reason = "1-1 중앙 발판과 연결된 해킹 콘솔·유지 레버가 각각 하나씩 등록되어 있어야 합니다."; return false; }
        reason = "1-1 연결 확인됨";
        return true;
    }

    private void EditorTickDeviceTests()
    {
        if (!editorTestsEnabled) return;
        if (!EditorTryGetTestStatus(out string reason)) { EditorForgetDeviceTests(); editorTestLastMessage = reason; return; }
        if (editorTestSection != ActiveSection)
        {
            EditorReleaseForcedInputs();
            editorTestSection = ActiveSection;
            editorTestLastMessage = "진행 구간이 바뀌어 이전 구간의 강제 입력을 해제했습니다.";
        }
        if (editorShaftLiftInput != EditorShaftLiftInput.Normal && !EditorRegistered(editorShaftLift))
        {
            editorShaftLift = null;
            editorShaftLiftInput = EditorShaftLiftInput.Normal;
            editorShaftHoldPosition = default;
            editorTestLastMessage = "엘리베이터가 비활성화·파괴되거나 등록이 바뀌어 해당 시험 입력을 해제했습니다.";
        }
        if (editorCommands.Count == 0 || !ResetTimer.ExpiredOrNotRunning(Runner)) return;
        EditorStageOneCommand command = editorCommands.Dequeue();
        if (!EditorFindStageOne(out _, out PcsPuzzleDevice console, out PcsPuzzleDevice lever, out reason))
        { editorTestLastMessage = reason; return; }
        if (command == EditorStageOneCommand.PrepareSection)
        {
            EditorReleaseForcedInputs();
            ActiveSection = editorTestSection = 1;
            editorTestLastMessage = "실행 중 구간을 1-1로 전환했습니다. 캐릭터 이동·튜토리얼 완료·Scene 저장은 하지 않습니다.";
            return;
        }
        if (ActiveSection != 1) { editorTestLastMessage = "현재 구간이 1-1이 아니므로 명령을 취소했습니다."; return; }
        if (command == EditorStageOneCommand.ResetSection)
        {
            ResetSection(MyEnum.CharacterType.None);
            editorTestLastMessage = "1-1 구간 초기화: 강제 입력, 해킹·레버·완료·체력 상태와 장치 위치를 기존 초기화 경로로 복구했습니다.";
        }
        else if (command == EditorStageOneCommand.HackComplete || command == EditorStageOneCommand.HackIncomplete)
        {
            PcsDeviceState state = States[console.DeviceId];
            state.Actor = default;
            state.Timer = default;
            if (command == EditorStageOneCommand.HackComplete)
            {
                state.Counter = console.RequiredInputs;
                CompleteHack(console, ref state);
                editorTestLastMessage = "해킹 완료 조건을 적용했습니다. 압력판을 놓아도 완료 상태는 유지됩니다.";
            }
            else
            {
                state.Active = state.Counter = 0;
                StageOneHacked = false;
                StageOneComplete = false;
                editorHoldDevice = null;
                ClearRemoteHold(lever);
                SetLinks(console, false);
                editorTestLastMessage = "해킹 미완료: 콘솔 진행과 레버 유지를 지우고 기존 이동 경로로 상단에 복귀합니다.";
            }
            States.Set(console.DeviceId, state);
        }
        else
        {
            if (command == EditorStageOneCommand.HoldLever && !StageOneHacked)
            { editorTestLastMessage = "해킹 미완료 상태에서는 레버로 발판을 내릴 수 없습니다."; return; }
            editorHoldDevice = lever;
            editorHoldPressed = command == EditorStageOneCommand.HoldLever;
            editorTestLastMessage = editorHoldPressed ? "레버를 강제로 유지합니다. 정상 입력 복귀 전까지 하단 요청이 유지됩니다." : "레버를 강제로 해제합니다. 해킹 완료 상태면 중단으로 복귀합니다.";
        }
    }

    private bool EditorTryPressureInput(PcsPuzzleDevice plate, out bool pressed)
    {
        pressed = false;
        if (!editorTestsEnabled || !EditorRegistered(plate) || !HasStateAuthority ||
            (plate.Section >= 0 && plate.Section != ActiveSection) || !editorPressureInputs.TryGetValue(plate, out EditorPressureInput mode)) return false;
        pressed = mode == EditorPressureInput.Pressed;
        return true;
    }

    private bool EditorOverridesRemoteHold(PcsPuzzleDevice device)
    {
        return editorTestsEnabled && HasStateAuthority && EditorRegistered(device) && editorHoldDevice == device && ActiveSection == 1;
    }

    private bool EditorApplyRemoteHoldInput(PcsPuzzleDevice device)
    {
        if (!EditorOverridesRemoteHold(device)) return false;
        if (!editorHoldPressed || !StageOneHacked) { ClearRemoteHold(device); return true; }
        PcsDeviceState state = States[device.DeviceId];
        state.Active = 1;
        state.Actor = default;
        state.HoldOwner = default;
        state.Timer = default;
        state.Velocity = Vector2.zero;
        States.Set(device.DeviceId, state);
        LeverLower = true;
        SetLinks(device, true);
        return true;
    }
}
#endif

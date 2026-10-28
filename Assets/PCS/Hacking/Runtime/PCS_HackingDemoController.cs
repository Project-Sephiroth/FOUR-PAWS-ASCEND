using UnityEngine;
using UnityEngine.UI;

namespace FourPaws.PCS.Hacking
{
    // 시작·취소·재시작과 시도 ID를 소유한다. 패널만 숨기며 자기 액터의 잠금을 복구한다.
    public sealed class PCS_HackingDemoController : MonoBehaviour
    {
        public PCS_TimingPuzzle timing = new PCS_TimingPuzzle();
        [Tooltip("자기 데모 구성체의 조작체입니다. 팀 플레이어를 연결하지 않습니다.")] public PCS_HackingDemoActor actor;
        [Tooltip("자기 타이밍 데모 단말입니다.")] public Transform timingTerminal;
        [Tooltip("꺼도 제어기가 살아 있는 자기 패널입니다.")] public GameObject panel;
        [Tooltip("자기 패널의 퍼즐 화면입니다.")] public PCS_HackingView view;
        public Button beginTiming;
        public Button restart;
        public Button close;
        public Button cancel;
        public Text result;
        public Text approach;
        public PCS_RotationPuzzle rotation = new PCS_RotationPuzzle();
        [Tooltip("자기 회전 데모 단말입니다.")] public Transform rotationTerminal;
        public Button beginRotation;
        public Text rotationResult;
        [Tooltip("초기 회전 배치를 재현하는 시드입니다.")] public int rotationSeed = 2026;
        [Tooltip("E로 단말을 열 수 있는 최대 거리입니다.")] public float entryDistance = 1.25f;
        [Tooltip("첫 모의 경보까지의 시간(초)입니다.")] public float firstAlarmDelay = 5f;
        [Tooltip("후속 모의 경보 간격(초)입니다.")] public float alarmInterval = 5f;

        public PCS_HackingSession session;public PCS_HackingSessionPresenter presenter;
        public PCS_TimingStageData[] timingStages;public PCS_RotationStageData[] rotationStages;
        public Button[] timingSingleButtons,rotationSingleButtons;
        public long AttemptId=>session?session.AttemptId:0;public int Deliveries=>session?session.Deliveries:0;public int AlarmCount=>presenter?presenter.AlarmCount:0;
        public bool IsOpen=>session&&session.State!=PCS_HackingSession.SessionState.Closed;public bool IsRotation=>session&&session.IsRotation;
        long shownAttempt=-1;PCS_HackingSession.SessionState shownState;bool released;bool ending;int commandFrame=-1;
        readonly System.Collections.Generic.List<UnityEngine.Events.UnityAction> actions=new System.Collections.Generic.List<UnityEngine.Events.UnityAction>();
        void Awake()
        {
            if(!session||!presenter){Debug.LogError("PCS 설정 오류: 세션과 표시 어댑터 연결을 확인하세요.",this);enabled=false;return;}
            session.timing=timing;session.rotation=rotation;presenter.session=session;presenter.view=view;presenter.panel=panel;presenter.firstAlarmDelay=firstAlarmDelay;presenter.alarmInterval=alarmInterval;
            if(panel)panel.SetActive(false);if(actor)actor.SetLocked(false);if(result)result.text="타이밍 결과: 대기";if(rotationResult)rotationResult.text="회전 연결 결과: 대기";
        }
        void OnEnable()
        {
            if(beginTiming)beginTiming.onClick.AddListener(BeginTimingSequence);if(beginRotation)beginRotation.onClick.AddListener(BeginRotationSequence);
            AddButtons(timingSingleButtons,false);AddButtons(rotationSingleButtons,true);
        }
        void AddButtons(Button[] buttons,bool useRotation){if(buttons==null)return;for(int i=0;i<buttons.Length;i++){int index=i;UnityEngine.Events.UnityAction action=()=>{if(useRotation)StartRotationStage(index);else StartTimingStage(index);};actions.Add(action);if(buttons[i])buttons[i].onClick.AddListener(action);}}
        void OnDisable()
        {
            if(beginTiming)beginTiming.onClick.RemoveListener(BeginTimingSequence);if(beginRotation)beginRotation.onClick.RemoveListener(BeginRotationSequence);
            int index=0;foreach(var group in new[]{timingSingleButtons,rotationSingleButtons})if(group!=null)foreach(var button in group){if(button&&index<actions.Count)button.onClick.RemoveListener(actions[index]);index++;}actions.Clear();
            ending=true;if(actor)actor.SetLocked(false);if(panel)panel.SetActive(false);if(session)session.Cancel("데모 호스트 비활성화");ending=false;
        }
        bool Ready()=>isActiveAndEnabled&&!ending&&!IsOpen&&session;
        void Opened(bool started){if(!started){if(result&&session)result.text=session.Error;return;}commandFrame=Time.frameCount;released=false;if(actor)actor.SetLocked(true);if(presenter)presenter.Refresh();Show();}
        public void BeginTiming(){if(Ready())Opened(session.StartLegacyTiming());}
        public void BeginRotation(){if(Ready())Opened(session.StartLegacyRotation(rotationSeed));}
        public void BeginTimingSequence(){if(!Ready())return;if(timingStages==null||timingStages.Length==0){BeginTiming();return;}Opened(timingStages.Length==1?session.StartSingleStage(timingStages[0]):session.StartSequence(timingStages));}
        public void BeginRotationSequence(){if(!Ready())return;if(rotationStages==null||rotationStages.Length==0){BeginRotation();return;}Opened(rotationStages.Length==1?session.StartSingleStage(rotationStages[0],rotationSeed):session.StartSequence(rotationStages,rotationSeed));}
        public void StartTimingStage(int index){if(Ready()&&timingStages!=null&&index>=0&&index<timingStages.Length)Opened(session.StartSingleStage(timingStages[index]));}
        public void StartRotationStage(int index){if(Ready()&&rotationStages!=null&&index>=0&&index<rotationStages.Length)Opened(session.StartSingleStage(rotationStages[index],rotationSeed));}
        public void Restart(){if(IsOpen&&presenter){presenter.Restart();Show();}}
        public void Close(){if(!session)return;commandFrame=Time.frameCount;released=false;if(actor)actor.SetLocked(false);if(panel)panel.SetActive(false);session.CloseHack();Show();}
        public bool DeliverSuccess(long attempt){bool accepted=session&&session.ObserveOutcome(attempt);if(presenter)presenter.Refresh();Show();return accepted;}
        public void RotateTile(int index,long attempt){if(presenter)presenter.RotateTile(index,attempt);}
        static void SetButtons(Button[] group,bool ready){if(group!=null)foreach(var button in group)if(button)button.interactable=ready;}
        void Show()
        {
            if(!session)return;bool open=IsOpen;if(actor)actor.SetLocked(open);if(beginTiming)beginTiming.interactable=!open;if(beginRotation)beginRotation.interactable=!open;
            SetButtons(timingSingleButtons,!open);SetButtons(rotationSingleButtons,!open);
            if(shownAttempt==AttemptId&&shownState==session.State)return;shownAttempt=AttemptId;shownState=session.State;
            if(result)result.gameObject.SetActive(!IsRotation);if(rotationResult)rotationResult.gameObject.SetActive(IsRotation);var output=IsRotation?rotationResult:result;
            if(output)output.text=(IsRotation?"회전 연결":"타이밍")+" 결과: "+(session.State==PCS_HackingSession.SessionState.Completed?"성공":session.State==PCS_HackingSession.SessionState.StageFailed?"단계 실패":session.State==PCS_HackingSession.SessionState.StageSucceeded?"단계 성공":open?"진행 중":"닫힘")+" | 시도 "+AttemptId+" | 전체 전달 "+Deliveries+"회";
        }
        void LateUpdate()
        {
            Show();if(!session||IsOpen||commandFrame==Time.frameCount)return;if(!released)released=!PCS_HackingDemoActor.AnyDemoKeyHeld()&&!Input.GetMouseButton(0);if(!released||!actor)return;
            actor.Move(Time.deltaTime);float a=timingTerminal?Vector3.Distance(actor.transform.position,timingTerminal.position):float.PositiveInfinity,b=rotationTerminal?Vector3.Distance(actor.transform.position,rotationTerminal.position):float.PositiveInfinity;
            int near=a<=Mathf.Max(0,entryDistance)&&a<=b?0:b<=Mathf.Max(0,entryDistance)?1:-1;
            if(approach)approach.text=near==0?"타이밍 단말: E로 단계 순차 실행":near==1?"회전 연결 단말: E로 단계 순차 실행":"WASD / 방향키: 민트색 조작체 이동. 단말 가까이에서 E";
            if(Input.GetKeyDown(KeyCode.E)){if(near==0)BeginTimingSequence();else if(near==1)BeginRotationSequence();}
        }
    }
}

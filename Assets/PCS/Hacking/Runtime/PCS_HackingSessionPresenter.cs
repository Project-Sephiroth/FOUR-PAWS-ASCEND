using UnityEngine;
using UnityEngine.UI;
namespace FourPaws.PCS.Hacking
{
    // 선택적 표시/입력 어댑터. 세션은 이 컴포넌트 없이도 public API로 실행된다.
    public sealed class PCS_HackingSessionPresenter : MonoBehaviour
    {
        public PCS_HackingSession session;public PCS_HackingView view;public GameObject panel;
        public Button restart,close,cancel,next,newConfiguration;
        [Header("데모에만 사용하는 모의 경보")]public bool useDemoAlarm=true;public float firstAlarmDelay=5,alarmInterval=5;
        public int AlarmCount{get;private set;}long shownAttempt=-1;PCS_HackingSession.SessionState shownState;float elapsed;bool released;int commandFrame=-1;int shownAlarm=-1;bool rotationDirty;long shownRotationRevision=-1;
        void Awake(){if(panel)panel.SetActive(false);}
        void OnEnable(){if(restart)restart.onClick.AddListener(Restart);if(close)close.onClick.AddListener(Close);if(cancel)cancel.onClick.AddListener(Close);if(next)next.onClick.AddListener(Next);if(newConfiguration)newConfiguration.onClick.AddListener(NewConfiguration);}
        void OnDisable(){if(restart)restart.onClick.RemoveListener(Restart);if(close)close.onClick.RemoveListener(Close);if(cancel)cancel.onClick.RemoveListener(Close);if(next)next.onClick.RemoveListener(Next);if(newConfiguration)newConfiguration.onClick.RemoveListener(NewConfiguration);if(panel)panel.SetActive(false);if(session)session.Cancel("표시 호스트 비활성화");}
        public void Restart(){if(!session)return;if(session.State==PCS_HackingSession.SessionState.Completed)session.RestartSession();else session.RetryCurrentStage();Command();}
        public void Close(){if(session)session.CloseHack();Command();}
        public void Next(){if(session)session.NextStage();Command();}
        public void NewConfiguration(){if(session)session.NewRotationConfiguration();Command();}
        void Command(){commandFrame=Time.frameCount;released=false;Refresh();}
        public void Refresh()
        {
            if(!session||!view||!panel)return;bool open=session.State!=PCS_HackingSession.SessionState.Closed;panel.SetActive(open);if(!open)return;
            if(session.Error.Length>0){view.ShowConfigurationError(session.Error);return;}
            if(shownAttempt!=session.AttemptId){shownAttempt=session.AttemptId;rotationDirty=true;elapsed=0;AlarmCount=0;released=false;commandFrame=Time.frameCount;
                if(session.IsRotation){long attempt=session.AttemptId;view.BindRotation(session.rotation,i=>RotateTile(i,attempt));}else view.Bind(session.timing);
                shownState=(PCS_HackingSession.SessionState)(-1);
            }
            if(shownState!=session.State){rotationDirty=true;shownState=session.State;view.feedback.text=session.Error.Length>0?session.Error:session.State==PCS_HackingSession.SessionState.Completed?"성공! 전체 완료를 한 번 전달했습니다. 닫기 또는 새 세션 재시작을 선택하세요.":session.State==PCS_HackingSession.SessionState.StageSucceeded?"단계 성공! 다음 단계 버튼을 누르세요.":session.State==PCS_HackingSession.SessionState.StageFailed?"오입력 한도에 도달했습니다. 같은 단계 다시 도전 / R":"금색 포인터가 핀과 겹칠 때 Space를 누르세요.";if(session.IsRotation&&session.State==PCS_HackingSession.SessionState.Running)view.feedback.text="회전 가능한 타일을 눌러 핀을 연결하세요. 같은 판과 새 구성을 구분합니다.";}
            if(session.IsRotation&&shownRotationRevision!=session.rotation.Revision){rotationDirty=true;shownRotationRevision=session.rotation.Revision;}
            if(rotationDirty)view.title.text=(session.IsRotation?"회전 연결":"타이밍 해킹")+" / "+session.DisplayNumber+"단계 · "+(session.IsSingle?"단독 실행":"순차 "+(session.StageIndex+1)+" / "+session.StageCount);
            if(session.IsRotation){if(rotationDirty||shownAlarm!=AlarmCount)view.ShowRotation(session.rotation,AlarmCount);}else view.Show(AlarmCount);shownAlarm=AlarmCount;rotationDirty=false;
            if(next)next.gameObject.SetActive(session.State==PCS_HackingSession.SessionState.StageSucceeded);
            if(newConfiguration)newConfiguration.gameObject.SetActive(session.IsRotation&&session.State!=PCS_HackingSession.SessionState.Completed);
        }
        public void RotateTile(int index,long attempt)
        {if(!session||commandFrame==Time.frameCount||!released||Input.GetKeyDown(KeyCode.R)||Input.GetKeyDown(KeyCode.Escape))return;rotationDirty=session.Rotate(index,attempt)||rotationDirty;Refresh();}
        void LateUpdate()
        {
            if(!session)return;Refresh();if(session.State==PCS_HackingSession.SessionState.Closed||commandFrame==Time.frameCount)return;
            if(Input.GetKeyDown(KeyCode.Escape)){Close();return;}if(Input.GetKeyDown(KeyCode.R)){Restart();return;}
            if(!released)released=!PCS_HackingDemoActor.AnyDemoKeyHeld()&&!Input.GetMouseButton(0);
            if(session.State!=PCS_HackingSession.SessionState.Running)return;
            session.Advance(Time.deltaTime);
            if(!session.IsRotation&&released&&Input.GetKeyDown(KeyCode.Space)){var hit=session.Press(session.AttemptId);if(view&&session.State==PCS_HackingSession.SessionState.Running)view.feedback.text=hit==PCS_TimingPuzzle.Hit.Miss?"오입력입니다. 한도를 확인하세요.":hit==PCS_TimingPuzzle.Hit.AlreadyConnected?"이미 연결한 핀입니다. 불이익은 없습니다.":"핀을 연결했습니다.";}
            if(useDemoAlarm&&session.State==PCS_HackingSession.SessionState.Running&&firstAlarmDelay>0&&alarmInterval>0&&!float.IsInfinity(firstAlarmDelay)&&!float.IsInfinity(alarmInterval)){elapsed+=Time.deltaTime;AlarmCount=elapsed<firstAlarmDelay?0:1+Mathf.FloorToInt((elapsed-firstAlarmDelay)/alarmInterval);}
            Refresh();
        }
    }
}

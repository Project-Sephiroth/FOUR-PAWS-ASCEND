using System;
using UnityEngine;
namespace FourPaws.PCS.CircuitHack
{
    // 실행 세션은 데모 버튼·액터와 독립적이다. 선택한 단계만 읽고 시도 스냅샷을 재사용한다.
    public sealed class CircuitHackManager : MonoBehaviour
    {
        public enum HackState { Closed,Running,FailFeedback,StageFeedback,Completed }
        [Header("직접 편집 단계 / 이전 SO 호환")]
        public CircuitStageLayout[] layouts;
        [Tooltip("이전 호출 호환용입니다. layouts가 연결되면 벽을 병합하지 않습니다.")] public CircuitMazeData[] stages;
        public CircuitInputAdapter input; public CircuitHackUI view; public GameObject overlay; public AudioSource failureAudio;
        [Header("선택적 방향 전환음") ]
        [Tooltip("미연결이면 무음입니다. 충돌음과 다른 전용 Source만 사용합니다.") ] public CircuitTurnSfx turnSfx;
        [Min(0)] public float failDelay=.75f,stageDelay=.6f;
        [Tooltip("기본 꺼짐. 3회 실패마다 0.9배, 최소 0.6배입니다.")] public bool assistSlowdown;
        public event Action<int> OnHackCompleted;
        public event Action<int> OnHackCancelled;
        public event Action<int,int,string> OnStageFailed;
        public event Action<int,int,string> OnStageCompleted;
        public HackState State{get;private set;}=HackState.Closed;
        public int RunId{get;private set;} public int AttemptId{get;private set;} public int StageIndex{get;private set;} public int Failures{get;private set;}
        public int StageCount=>sources!=null?sources.Length:legacy?.Length??0;
        public string StageId=>CurrentData?.stageId??"";
        public int DisplayStage=>CurrentData?.displayNumber??1;
        public bool IsSingle{get;private set;}
        public string Error{get;private set;}
        public string LastCancelReason{get;private set;}
        public CircuitSignalController Signal{get;}=new CircuitSignalController();
        public CircuitMazeData.Snapshot CurrentData{get;private set;}
        private CircuitStageLayout[] sources; private CircuitMazeData.Snapshot[] legacy;
        private float elapsed;private bool focusPaused,focusReleaseWait,closing,dispatchingFinalStage;
        private string DelaysError()=>!CircuitCollisionChecker.Finite(failDelay)||!CircuitCollisionChecker.Finite(stageDelay)||failDelay<0||stageDelay<0?"대기 시간은 0 이상의 유한 값이어야 합니다.":null;
        public string ValidateSetup()
        {
            string error=DelaysError();if(error!=null)return error;
            if(layouts!=null&&layouts.Length>0){foreach(var stage in layouts)if(!stage)return "선택한 배치 참조가 없습니다.";else if(!stage.TryCapture(out _,out error))return error;return null;}
            if(stages==null||stages.Length==0)return "선택할 단계 데이터가 없습니다.";
            foreach(var stage in stages){if(!stage)return "선택한 단계 데이터가 없습니다.";error=CircuitMazeData.Validate(stage.Capture(),false);if(error!=null)return error;}return null;
        }
        public void StartHack(){if(layouts!=null&&layouts.Length>0)StartSequence(layouts);else StartSequence(stages);}
        public bool StartSingleStage(CircuitStageLayout stage)=>StartLayouts(new[]{stage},true);
        public bool StartSequence(CircuitStageLayout[] selected)=>StartLayouts(selected,false);
        public bool StartSingleStage(CircuitMazeData stage)=>StartLegacy(new[]{stage},true);
        public bool StartSequence(CircuitMazeData[] selected)=>StartLegacy(selected,false);
        private bool CanStart()=>!closing&&!dispatchingFinalStage&&isActiveAndEnabled&&(State==HackState.Closed||State==HackState.Completed);
        private bool Reject(string error){Error="설정 오류: "+error;if(overlay)overlay.SetActive(true);if(view)view.Show(this);return false;}
        private bool StartLayouts(CircuitStageLayout[] selected,bool single)
        {
            if(!CanStart())return false;if(DelaysError()!=null)return Reject(DelaysError());
            if(selected==null||selected.Length==0||selected.Length>16)return Reject("선택 목록은 1~16개 단계여야 합니다.");
            foreach(var stage in selected){if(!stage)return Reject("선택한 배치가 없습니다.");if(!stage.TryCapture(out _,out string error))return Reject(error);}
            sources=(CircuitStageLayout[])selected.Clone();legacy=null;return BeginSession(single);
        }
        private bool StartLegacy(CircuitMazeData[] selected,bool single)
        {
            if(!CanStart())return false;if(DelaysError()!=null)return Reject(DelaysError());
            if(selected==null||selected.Length==0||selected.Length>16)return Reject("선택 목록은 1~16개 단계여야 합니다.");
            var snapshots=new CircuitMazeData.Snapshot[selected.Length];
            for(int i=0;i<selected.Length;i++){if(!selected[i])return Reject("선택한 단계 데이터가 없습니다.");var d=selected[i].Capture();d.stageId=selected[i].name;d.displayNumber=NumberInName(selected[i].name,i+1);string e=CircuitMazeData.Validate(d,false);if(e!=null)return Reject(e);d.routeError=CircuitMazeData.ValidateRoute(d);snapshots[i]=d;}
            legacy=snapshots;sources=null;return BeginSession(single);
        }
        private static int NumberInName(string n,int fallback){for(int i=1;i<=99;i++)if(n.EndsWith(i.ToString("00"),StringComparison.Ordinal))return i;return fallback;}
        private bool BeginSession(bool single){IsSingle=single;RunId++;StageIndex=0;Failures=0;Error=null;LastCancelReason=null;focusPaused=focusReleaseWait=false;if(overlay)overlay.SetActive(true);return EnterStage();}
        private bool EnterStage()
        {
            if(sources!=null){string error=null;CircuitMazeData.Snapshot data=null;if(!sources[StageIndex]||!sources[StageIndex].TryCapture(out data,out error)){string message=sources[StageIndex]?error:"선택 배치가 제거되었습니다.";int oldRun=RunId;CancelHack("단계 배치 오류: "+message);if(RunId==oldRun&&State==HackState.Closed)Reject(message);return false;}CurrentData=data;}
            else CurrentData=legacy[StageIndex];
            BeginAttempt();return true;
        }
        private void BeginAttempt(){if(turnSfx)turnSfx.ClearPlayback(this);elapsed=0;AttemptId++;State=HackState.Running;Signal.Reset(CurrentData.start,CurrentData.initialDirection,SpeedFor(CurrentData.baseSpeed,Failures,assistSlowdown));if(input)input.RequireRelease();if(failureAudio)failureAudio.Stop();if(view){view.Bind(CurrentData);view.Show(this);}}
        public static float SpeedFor(float original,int failures,bool assist)=>original*(assist?Mathf.Max(.6f,Mathf.Pow(.9f,failures/3)):1f);
        public void RestartStage(){if(State==HackState.Running||State==HackState.FailFeedback||State==HackState.StageFeedback)BeginAttempt();}
        public void ReloadLayoutAndStart(){if(CurrentData==null||sources==null)return;var selected=sources[StageIndex];CloseHack();StartSingleStage(selected);}
        public void EndHost(){if(closing)return;closing=true;try{CancelHack("호스트 비활성화/종료");}finally{closing=false;}}
        public void CloseHack()=>CancelHack("사용자 닫기");
        public void CancelHack(string reason)
        {
            if(dispatchingFinalStage)return;
            if(turnSfx)turnSfx.ClearPlayback(this);
            if(State==HackState.Closed){if(overlay)overlay.SetActive(false);return;}
            bool cancel=State!=HackState.Completed;int run=RunId;State=HackState.Closed;elapsed=0;sources=null;legacy=null;CurrentData=null;Error=null;LastCancelReason=reason;
            if(input)input.RequireRelease();if(failureAudio)failureAudio.Stop();if(view&&view.trail)view.trail.ClearTrail();if(overlay)overlay.SetActive(false);if(cancel)OnHackCancelled?.Invoke(run);
        }
        private void Update()
        {
            if(input&&input.ClosePressed){CloseHack();return;}if(State==HackState.Closed)return;
            if(overlay&&!overlay.activeInHierarchy){EndHost();return;}if(State==HackState.Completed)return;
            bool visible=Application.isFocused&&Screen.width>0&&Screen.height>0&&(!view||(view.board&&view.board.rect.width>0&&view.board.rect.height>0));
            if(!visible){focusPaused=true;if(input)input.RequireRelease();if(view)view.Show(this,true);return;}
            if(focusPaused){focusPaused=false;focusReleaseWait=true;if(input)input.RequireRelease();if(view)view.Show(this,true);return;}
            Vector2Int direction=Vector2Int.zero;bool ready=!input||input.Read(out direction);
            if(focusReleaseWait){if(ready)focusReleaseWait=false;if(view)view.Show(this,true);return;}
            Advance(Time.unscaledDeltaTime,direction);
        }
        public void Advance(float dt,Vector2Int direction)
        {
            if(!CircuitCollisionChecker.Finite(dt)||dt<0||State==HackState.Closed||State==HackState.Completed)return;
            if(State==HackState.FailFeedback||State==HackState.StageFeedback){elapsed+=dt;float delay=State==HackState.FailFeedback?failDelay:stageDelay;if(elapsed>=delay){if(State==HackState.StageFeedback){StageIndex++;Failures=0;EnterStage();}else BeginAttempt();}return;}
            bool turned=Signal.Turn(direction);if(turned&&turnSfx)turnSfx.HandleAcceptedTurn(this);var hit=CircuitCollisionChecker.Check(CurrentData,Signal.Position,Signal.Propose(dt));Signal.Commit(hit.position);if(view&&view.trail)view.trail.UpdateEnd(hit.position,turned);
            int run=RunId,attempt=AttemptId;string stage=StageId;
            if(hit.kind==CircuitCollisionChecker.Kind.Wall){Failures++;State=HackState.FailFeedback;elapsed=0;if(input)input.RequireRelease();if(failureAudio&&failureAudio.clip)failureAudio.Play();if(view)view.Show(this);OnStageFailed?.Invoke(run,attempt,stage);return;}
            if(hit.kind==CircuitCollisionChecker.Kind.Goal){elapsed=0;if(input)input.RequireRelease();bool final=StageIndex==StageCount-1;State=final?HackState.Completed:HackState.StageFeedback;if(view)view.Show(this);dispatchingFinalStage=final;try{OnStageCompleted?.Invoke(run,attempt,stage);}finally{dispatchingFinalStage=false;}if(final)OnHackCompleted?.Invoke(run);return;}
            if(view)view.Show(this);
        }
        private void OnDisable()=>EndHost();
        private void OnDestroy(){closing=true;CloseHack();}
    }
}

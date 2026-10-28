using System;
using UnityEngine;
namespace FourPaws.PCS.Hacking
{
    // 액터·UI·경보 없이 선택한 단계만 실행하는 퍼즐 세션. 외부 효과는 종결 이벤트 구독자가 맡는다.
    public sealed class PCS_HackingSession : MonoBehaviour
    {
        public enum SessionState{Closed,Running,StageSucceeded,StageFailed,Completed}
        public PCS_TimingPuzzle timing=new PCS_TimingPuzzle();public PCS_RotationPuzzle rotation=new PCS_RotationPuzzle();
        public SessionState State{get;private set;}public long SessionId{get;private set;}public long AttemptId{get;private set;}
        public bool IsRotation{get;private set;}public bool IsSingle{get;private set;}public int StageIndex{get;private set;}public int Deliveries{get;private set;}
        public string Error{get;private set;}="";public string LastCancelReason{get;private set;}="";
        public int StageCount=>IsRotation?rotationStages?.Length??0:timingStages?.Length??0;
        public string StageId=>IsRotation?rotationStages?[StageIndex].stageId:timingStages?[StageIndex].stageId;
        public int DisplayNumber=>IsRotation?rotationStages?[StageIndex].displayNumber??1:timingStages?[StageIndex].displayNumber??1;
        public event Action<long> OnHackCompleted,OnHackCancelled;
        public event Action<long,long,string> OnStageCompleted,OnStageFailed;
        PCS_TimingStageData.Snapshot[] timingStages;PCS_RotationStageData.Snapshot[] rotationStages;int seed,variant;bool closing,dispatchingFinalStage;
        bool CanStart=>isActiveAndEnabled&&!closing&&!dispatchingFinalStage&&(State==SessionState.Closed||State==SessionState.Completed);
        bool Reject(string error){Error="설정 오류: "+error;return false;}
        public bool StartSingleStage(PCS_TimingStageData stage)=>StartTiming(stage?new[]{stage.Capture()}:null,true);
        public bool StartSequence(PCS_TimingStageData[] stages)=>StartTiming(stages==null?null:Array.ConvertAll(stages,s=>s?s.Capture():null),false);
        public bool StartSingleStage(PCS_RotationStageData stage,int seed=2026)=>StartRotation(stage?new[]{stage.Capture()}:null,seed,true);
        public bool StartSequence(PCS_RotationStageData[] stages,int seed=2026)=>StartRotation(stages==null?null:Array.ConvertAll(stages,s=>s?s.Capture():null),seed,false);
        public bool StartLegacyTiming()=>StartTiming(new[]{new PCS_TimingStageData.Snapshot{stageId="timing-legacy",displayNumber=1,count=timing.targetCount,speed=timing.degreesPerSecond,tolerance=timing.toleranceDegrees,limit=timing.missLimit}},true);
        public bool StartLegacyRotation(int initialSeed)=>StartRotation(new[]{PCS_RotationPuzzle.Legacy()},initialSeed,true);
        bool StartTiming(PCS_TimingStageData.Snapshot[] selected,bool single)
        {
            if(!CanStart)return false;if(selected==null||selected.Length==0||selected.Length>16)return Reject("선택한 단계 1~16개가 필요합니다.");
            foreach(var item in selected){string e=item?.Validate()??(item==null?"단계 참조가 없습니다.":null);if(e!=null)return Reject(e);}
            timingStages=selected;rotationStages=null;IsRotation=false;return BeginSession(single);
        }
        bool StartRotation(PCS_RotationStageData.Snapshot[] selected,int initialSeed,bool single)
        {
            if(!CanStart)return false;if(selected==null||selected.Length==0||selected.Length>16)return Reject("선택한 단계 1~16개가 필요합니다.");
            foreach(var item in selected){string e=item?.Validate()??(item==null?"단계 참조가 없습니다.":null);if(e!=null)return Reject(e);}
            rotationStages=selected;timingStages=null;IsRotation=true;seed=initialSeed;variant=0;return BeginSession(single);
        }
        bool BeginSession(bool single){timing.Cancel();rotation.Cancel();SessionId++;StageIndex=Deliveries=0;IsSingle=single;Error="";LastCancelReason="";return EnterStage();}
        bool EnterStage(){if(IsRotation){variant=0;return BeginAttempt(true);}if(!timingStages[StageIndex].Apply(timing,out string error))return Reject(error);return BeginAttempt(false);}
        bool BeginAttempt(bool shuffle)
        {
            AttemptId++;Error="";bool ok=IsRotation?(shuffle?rotation.Begin(rotationStages[StageIndex],unchecked(seed+StageIndex*7919),variant):rotation.Restart()):timing.Begin();
            State=ok?SessionState.Running:SessionState.StageFailed;if(!ok)Error=IsRotation?rotation.Error:timing.Error;return ok;
        }
        public bool RetryCurrentStage(){if(State==SessionState.Closed||State==SessionState.Completed)return false;return BeginAttempt(false);}
        public bool RestartSession(){if(dispatchingFinalStage||State!=SessionState.Completed)return false;return BeginSession(IsSingle);}
        public bool NewRotationConfiguration(){if(!IsRotation||State==SessionState.Closed||State==SessionState.Completed)return false;seed=unchecked(seed+1);variant=(variant+1)%rotationStages[StageIndex].graphs.Length;return BeginAttempt(true);}
        public bool NextStage(){if(State!=SessionState.StageSucceeded)return false;StageIndex++;return EnterStage();}
        public bool TryConfigureTiming(int pins,float speed,float tolerance,int missLimit,bool restartNow,out string error)
        {
            if(dispatchingFinalStage||IsRotation||State==SessionState.Closed){error="타이밍 단계 실행 중에 설정하세요.";return false;}
            if(!timing.TryConfigure(pins,speed,tolerance,missLimit,out error))return false;
            // SO 원본은 보존하고 이 세션의 다음 시도 설정만 갱신한다. 완료 뒤 R에도 유지된다.
            timingStages[StageIndex]=new PCS_TimingStageData.Snapshot{stageId=StageId,displayNumber=DisplayNumber,count=pins,speed=speed,tolerance=tolerance,limit=missLimit};
            if(restartNow){if(State==SessionState.Completed)RestartSession();else RetryCurrentStage();}return true;
        }
        public void Advance(float seconds){if(State==SessionState.Running&&!IsRotation)timing.Advance(seconds);}
        public PCS_TimingPuzzle.Hit Press(long attempt)
        {if(State!=SessionState.Running||IsRotation||attempt!=AttemptId)return PCS_TimingPuzzle.Hit.Ignored;var hit=timing.Connect();ObserveOutcome(attempt);return hit;}
        public bool Rotate(int tile,long attempt){if(State!=SessionState.Running||!IsRotation||attempt!=AttemptId)return false;bool changed=rotation.Rotate(tile);if(changed)ObserveOutcome(attempt);return changed;}
        public bool ObserveOutcome(long attempt)
        {
            if(attempt!=AttemptId||State!=SessionState.Running)return false;
            long run=SessionId;string stage=StageId;
            if(!IsRotation&&timing.Failed){State=SessionState.StageFailed;OnStageFailed?.Invoke(run,attempt,stage);return false;}
            if(!(IsRotation?rotation.Succeeded:timing.Succeeded))return false;
            bool final=StageIndex==StageCount-1;State=final?SessionState.Completed:SessionState.StageSucceeded;if(final)Deliveries=1;
            // 최종 단계 알림 동안의 재진입은 거부한다. 전체 완료 콜백에서는 새 세션을 시작할 수 있다.
            dispatchingFinalStage=final;try{OnStageCompleted?.Invoke(run,attempt,stage);}finally{dispatchingFinalStage=false;}
            if(final)OnHackCompleted?.Invoke(run);
            return final;
        }
        public void CloseHack()=>Cancel("사용자 닫기");
        public void Cancel(string reason)
        {
            if(dispatchingFinalStage||State==SessionState.Closed)return;bool notify=State!=SessionState.Completed;long run=SessionId;timing.Cancel();rotation.Cancel();State=SessionState.Closed;AttemptId++;LastCancelReason=reason;Error="";if(notify)OnHackCancelled?.Invoke(run);
        }
        void OnDisable(){closing=true;try{Cancel("호스트 비활성화");}finally{closing=false;}}
        void OnDestroy(){closing=true;Cancel("호스트 종료");}
    }
}

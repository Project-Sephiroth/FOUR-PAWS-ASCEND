using UnityEngine;
namespace FourPaws.PCS.Hacking
{
    // 단계 식별과 다음 실행의 설정. 실행 세션은 복사본만 사용한다.
    public sealed class PCS_TimingStageData : ScriptableObject
    {
        [Header("단계 식별")] public string stageId="timing-01"; [Min(1)] public int displayNumber=1;
        [Header("다음 시도 설정")] [Range(2,24)] public int targetCount=6;
        [Tooltip("초당 회전 각도, 0 초과 720 이하")] public float degreesPerSecond=90;
        [Tooltip("핀 간격 절반보다 작은 양수 각도")] public float toleranceDegrees=12;
        [Tooltip("0 무제한 / 양수 N번째 오입력 실패")] [Min(0)] public int missLimit=6;
        public Snapshot Capture()=>new Snapshot{stageId=stageId,displayNumber=displayNumber,count=targetCount,speed=degreesPerSecond,tolerance=toleranceDegrees,limit=missLimit};
        public sealed class Snapshot
        {
            public string stageId;public int displayNumber,count,limit;public float speed,tolerance;
            public string Validate()=>string.IsNullOrWhiteSpace(stageId)||displayNumber<1?"단계 ID와 표시 번호가 필요합니다.":PCS_TimingPuzzle.ValidateConfiguration(count,speed,tolerance,limit);
            public bool Apply(PCS_TimingPuzzle model,out string error)=>model.TryConfigure(count,speed,tolerance,limit,out error);
        }
    }
}

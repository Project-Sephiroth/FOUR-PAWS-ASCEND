using System;
using UnityEngine;

namespace FourPaws.PCS.Hacking
{
    // 시도마다 튜닝값을 확정하고 시간 기반 회전·원형 최단 거리·핀 중복을 판정한다.
    [Serializable]
    public sealed class PCS_TimingPuzzle
    {
        [Tooltip("목표 핀 수 2~24개입니다. 새 시도부터 적용됩니다.")] public int targetCount = 6;
        [Tooltip("시계 방향 초당 회전 각도입니다. 0 초과 720 이하입니다.")] public float degreesPerSecond = 90f;
        [Tooltip("핀 연결 허용 각도입니다. 핀 간격 절반보다 작아야 합니다.")] public float toleranceDegrees = 12f;

        [Tooltip("0은 무제한, 양수 N은 N번째 실제 오입력에 실패합니다.")] public int missLimit;
        public enum Hit { Ignored, Miss, AlreadyConnected, Connected, Succeeded, Failed }
        public float Angle { get; private set; }
        public int ConnectedCount { get; private set; }
        public int MissCount { get; private set; }
        public bool Running { get; private set; }
        public bool Succeeded { get; private set; }
        public bool Failed { get; private set; }
        public int ActiveMissLimit { get; private set; }
        public float ActiveDegreesPerSecond => activeSpeed;
        public float ActiveToleranceDegrees => activeTolerance;
        public string Error { get; private set; } = "";
        public int ActiveTargetCount { get; private set; }
        private float activeSpeed;
        private float activeTolerance;
        private bool[] connected = Array.Empty<bool>();

        public bool Begin()
        {
            Running = false;
            Succeeded = Failed = false;
            Angle = 0f;
            ConnectedCount = MissCount = 0;
            Error = "";
            connected = Array.Empty<bool>();
            ActiveTargetCount = 0;
            string invalid=ValidateConfiguration(targetCount,degreesPerSecond,toleranceDegrees,missLimit);
            if(invalid!=null){Error=invalid;return false;}
            ActiveMissLimit=missLimit;
            connected = new bool[targetCount];
            ActiveTargetCount = targetCount;
            activeSpeed = degreesPerSecond;
            activeTolerance = toleranceDegrees;
            Running = true;
            return true;
        }

        public void Advance(float seconds)
        {
            if (Running && Finite(seconds) && seconds > 0f)
                Angle = Mathf.Repeat(Angle + activeSpeed * seconds, 360f);
        }

        public static float CircularDistance(float a, float b) => Mathf.Abs(Mathf.DeltaAngle(a, b));
        public bool IsConnected(int index) => index >= 0 && index < connected.Length && connected[index];
        public float TargetAngle(int index) => ActiveTargetCount > 0 ? index * 360f / ActiveTargetCount : 0f;

        public Hit Connect()
        {
            if (!Running) return Hit.Ignored;
            for (int i = 0; i < connected.Length; i++)
            {
                // 경계 포함 판정에서만 작은 부동소수점 반올림 오차를 흡수한다.
                if (CircularDistance(Angle, TargetAngle(i)) > activeTolerance + 0.00001f) continue;
                if (connected[i]) return Hit.AlreadyConnected;
                connected[i] = true;
                ConnectedCount++;
                if (ConnectedCount == connected.Length)
                {
                    Running = false;
                    Succeeded = true;
                    return Hit.Succeeded;
                }
                return Hit.Connected;
            }
            MissCount++;
            if(ActiveMissLimit>0&&MissCount>=ActiveMissLimit){Failed=true;Running=false;return Hit.Failed;}
            return Hit.Miss;
        }

        public static string ValidateConfiguration(int count,float speed,float tolerance,int limit)
        {
            if(count<2||count>24)return "설정 오류: 핀 수는 2~24개여야 합니다.";
            if(!Finite(speed)||speed<=0||speed>720)return "설정 오류: 회전속도는 초당 0 초과 720도 이하의 유한 값이어야 합니다.";
            if(!Finite(tolerance)||tolerance<=0||tolerance>=180f/count)return "설정 오류: 허용각은 0 초과 "+(180f/count).ToString("0.###")+"도 미만이어야 합니다.";
            if(limit<0)return "설정 오류: 오입력 한도는 0 이상이어야 합니다.";return null;
        }
        public bool TryConfigure(int count,float speed,float tolerance,int limit,out string error)
        {
            error=ValidateConfiguration(count,speed,tolerance,limit);if(error!=null)return false;
            targetCount=count;degreesPerSecond=speed;toleranceDegrees=tolerance;missLimit=limit;return true;
        }
        public bool SetPinCount(int value,out string error)=>TryConfigure(value,degreesPerSecond,toleranceDegrees,missLimit,out error);
        public bool SetSpeed(float value,out string error)=>TryConfigure(targetCount,value,toleranceDegrees,missLimit,out error);
        public bool SetTolerance(float value,out string error)=>TryConfigure(targetCount,degreesPerSecond,value,missLimit,out error);
        public bool SetMissLimit(int value,out string error)=>TryConfigure(targetCount,degreesPerSecond,toleranceDegrees,value,out error);
        public void Cancel() { Running = false; }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}

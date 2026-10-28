using UnityEngine;

namespace FourPaws.PCS.CircuitHack
{
    // Manager가 승인한 90도 전환 알림만 재생한다. 퍼즐 상태와 이동은 변경하지 않는다.
    [DisallowMultipleComponent]
    public sealed class CircuitTurnSfx : MonoBehaviour
    {
        [Header("방향 전환 전용 소리 / 충돌음과 분리")]
        [Tooltip("충돌음 Source와 다른 전용 Source입니다. Play On Awake/Loop는 끄고 Spatial Blend는 0으로 둡니다.")]
        public AudioSource turnSource;
        [Tooltip("나중에 이 한 칸에만 AudioClip을 연결합니다. Source.clip은 연결하지 않아도 됩니다.")]
        public AudioClip turnClip;
        [Range(0f,1f)] public float volumeScale=.5f;
        // 누적 호출 수와 재생 요청 수는 다르다. 장치에서 들렸음을 증명하는 카운터는 아니다.
        public int AcceptedTurnNotifications {get;private set;}
        public int PlaybackRequests {get;private set;}
        public string LastSkipReason {get;private set;}
        private CircuitHackManager owner;
        private AudioSource playedSource;
        private string warnedReason;

        public void HandleAcceptedTurn(CircuitHackManager caller)
        {
            AcceptedTurnNotifications++;
            if(!caller){Skip("전환 알림의 Manager 참조가 없습니다.",true);return;}
            if(owner&&owner!=caller){Skip("전환음 컴포넌트를 여러 Manager가 공유할 수 없습니다.",true);return;}
            owner=caller;
            if(!isActiveAndEnabled){Skip("전환음 컴포넌트가 비활성화되어 있습니다.");return;}
            if(!turnSource){Skip("전환음 전용 Source가 없습니다.");return;}
            if(turnSource==owner.failureAudio){Skip("전환음 Source와 충돌음 Source가 같습니다. 충돌음 설정을 보존하고 전환음만 거절합니다.",true);return;}
            if(!turnSource.isActiveAndEnabled){Skip("전환음 Source가 비활성화되어 있습니다.");return;}
            if(!turnClip){Skip("전환음 Clip이 비어 있습니다.");return;}
            if(float.IsNaN(volumeScale)||float.IsInfinity(volumeScale)){Skip("전환음 크기는 유한 값이어야 합니다.",true);return;}
            if(playedSource&&playedSource!=turnSource)StopOwnedPlayback();
            warnedReason=null;LastSkipReason=null;playedSource=turnSource;
            turnSource.PlayOneShot(turnClip,Mathf.Clamp01(volumeScale));PlaybackRequests++;
        }
        public void ClearPlayback(CircuitHackManager caller)
        {
            if(!caller)return;
            if(owner&&owner!=caller)return;
            owner=caller;StopOwnedPlayback();
        }
        private void StopOwnedPlayback()
        {
            if(!playedSource)return;
            // 재생 이후 Inspector 참조가 바뀐 경우도 현재 충돌음 Source를 다시 보호한다.
            if(!owner||playedSource==owner.failureAudio){Skip("전용 재생 정리를 보류했습니다. 충돌음 Source와의 분리를 확인하세요.",true);return;}
            playedSource.Stop();playedSource=null;
        }
        private void Skip(string reason,bool warn=false)
        {
            LastSkipReason=reason;
            if(warn&&warnedReason!=reason){warnedReason=reason;Debug.LogWarning(reason,this);}
        }
        private void OnDisable(){StopOwnedPlayback();}
    }
}

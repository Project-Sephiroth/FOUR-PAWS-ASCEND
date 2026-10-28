using UnityEngine;
using UnityEngine.UI;

namespace FourPaws.PCS.CircuitHack
{
    // 저장된 PCS 전용 UI 참조만 갱신한다. 논리 좌표는 해상도에 영향을 받지 않는다.
    public sealed class CircuitHackUI : MonoBehaviour
    {
        [Header("저장된 회로 표시")]
        public RectTransform board;public Image background;public RectTransform head,goal,start;
        [Tooltip("시작 때 필요한 만큼 준비하는 벽 표시 풀. 최대 128개")] public Image[] wallViews;
        public CircuitTrailRenderer trail;
        [Header("한국어 안내와 조작")]
        public Text title,status,counters;public Button close,restart,newHack;
        private Image[] decorationViews=System.Array.Empty<Image>();
        private CircuitMazeData.Snapshot data;
        private Vector2 lastSize;
        private int lastAttempt=-1,lastFailures=-1,lastStage=-1;
        private CircuitHackManager.HackState lastState;
        private bool lastPaused;private string lastError;
        private Image headImage,goalImage;
        private readonly Color runningColor=new Color(.25f,1f,.78f);
        public bool ReferencesValid=>board&&background&&head&&head.GetComponent<Image>()&&goal&&goal.GetComponent<Image>()&&start&&trail&&title&&status&&counters&&close&&restart&&newHack&&wallViews!=null&&wallViews.Length>0&&System.Array.TrueForAll(wallViews,i=>i!=null);
        private Vector2 Pixel(Vector2 p)=>new Vector2((p.x-data.bounds.center.x)/data.bounds.width*board.rect.width,(p.y-data.bounds.center.y)/data.bounds.height*board.rect.height);
        private void Put(RectTransform target,Rect r){target.anchoredPosition=Pixel(r.center);target.sizeDelta=new Vector2(r.width/data.bounds.width*board.rect.width,r.height/data.bounds.height*board.rect.height);}
        public void Bind(CircuitMazeData.Snapshot snapshot)
        {
            data=snapshot;
            EnsureWalls(data.walls.Length);
            EnsureDecorations(data.decorations.Length);
            Reflow();lastAttempt=-1;
            trail.ResetTrail(data.bounds,data.start);
        }
        private void EnsureWalls(int count)
        {
            if(wallViews==null)wallViews=System.Array.Empty<Image>();
            if(count<=wallViews.Length)return;
            var list=new System.Collections.Generic.List<Image>(wallViews);
            while(list.Count<count){var go=new GameObject("SnapshotWall"+list.Count,typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));var r=(RectTransform)go.transform;r.SetParent(board,false);r.anchorMin=r.anchorMax=r.pivot=Vector2.one*.5f;var im=go.GetComponent<Image>();im.color=new Color(.18f,.29f,.32f);im.raycastTarget=false;list.Add(im);}
            wallViews=list.ToArray();start.SetAsLastSibling();goal.SetAsLastSibling();trail.transform.SetAsLastSibling();head.SetAsLastSibling();
        }
        private void EnsureDecorations(int count)
        {
            if(count<=decorationViews.Length)return;
            var list=new System.Collections.Generic.List<Image>(decorationViews);
            while(list.Count<count){var go=new GameObject("SnapshotDecoration"+list.Count,typeof(RectTransform),typeof(Image));var r=(RectTransform)go.transform;r.SetParent(board,false);r.anchorMin=r.anchorMax=r.pivot=Vector2.one*.5f;var im=go.GetComponent<Image>();im.color=new Color(.12f,.21f,.23f);im.raycastTarget=false;list.Add(im);}
            decorationViews=list.ToArray();start.SetAsLastSibling();goal.SetAsLastSibling();trail.transform.SetAsLastSibling();head.SetAsLastSibling();
        }
        private void Reflow()
        {
            lastSize=board.rect.size;
            for(int i=0;i<wallViews.Length;i++){wallViews[i].gameObject.SetActive(i<data.walls.Length);if(i<data.walls.Length)Put(wallViews[i].rectTransform,data.walls[i]);}
            for(int i=0;i<decorationViews.Length;i++){decorationViews[i].gameObject.SetActive(i<data.decorations.Length);if(i<data.decorations.Length)Put(decorationViews[i].rectTransform,data.decorations[i]);}
            Put(goal,data.goal);Put(start,new Rect(data.start-Vector2.one*1.5f,Vector2.one*3f));
            head.sizeDelta=new Vector2(data.visualHeadWidth/data.bounds.width*board.rect.width,data.visualHeadWidth/data.bounds.height*board.rect.height);
        }
        public void Show(CircuitHackManager manager,bool paused=false)
        {
            if(!headImage||!goalImage){if(!ReferencesValid)return;headImage=head.GetComponent<Image>();goalImage=goal.GetComponent<Image>();}
            if(data!=null){if(lastSize!=board.rect.size)Reflow();head.anchoredPosition=Pixel(manager.Signal.Position);}
            if(lastAttempt==manager.AttemptId&&lastFailures==manager.Failures&&lastStage==manager.StageIndex&&lastState==manager.State&&lastPaused==paused&&lastError==manager.Error)return;
            lastAttempt=manager.AttemptId;lastFailures=manager.Failures;lastStage=manager.StageIndex;lastState=manager.State;lastPaused=paused;lastError=manager.Error;
            title.text="회로 차단 / "+manager.DisplayStage+"단계 · "+(manager.IsSingle?"단독 실행":"순차 "+(manager.StageIndex+1)+" / "+manager.StageCount);
            counters.text="시도 "+manager.AttemptId+" · 실패 "+manager.Failures+" · 속도 "+manager.Signal.Speed.ToString("0.00")+"\n자동 전진 · 방향키 / WASD / D-pad로 방향 전환";
            status.text=paused?"안전 일시정지: 게임 창으로 돌아와 방향키를 모두 놓으세요.":manager.Error??(StateName(manager.State)+(data!=null&&data.routeError!=null?" · 경로 미검증":""));
            Color color=manager.State==CircuitHackManager.HackState.FailFeedback?new Color(1f,.25f,.25f):runningColor;
            headImage.color=color;goalImage.color=manager.State==CircuitHackManager.HackState.StageFeedback||manager.State==CircuitHackManager.HackState.Completed?new Color(.07f,.42f,.32f):new Color(.12f,.52f,.23f);
            background.color=manager.State==CircuitHackManager.HackState.FailFeedback?new Color(.17f,.025f,.04f):new Color(.015f,.10f,.10f);
            restart.interactable=manager.State==CircuitHackManager.HackState.Running||manager.State==CircuitHackManager.HackState.FailFeedback||manager.State==CircuitHackManager.HackState.StageFeedback;
            newHack.interactable=manager.State==CircuitHackManager.HackState.Completed||manager.State==CircuitHackManager.HackState.Closed;
        }
        public static string StateName(CircuitHackManager.HackState state)
        {
            switch(state){case CircuitHackManager.HackState.Running:return "초록 출구로 신호를 보내세요. 반대 방향 입력은 무시됩니다.";case CircuitHackManager.HackState.FailFeedback:return "회로 충돌! 잠시 뒤 현재 단계를 다시 시작합니다.";case CircuitHackManager.HackState.StageFeedback:return "회로 연결 성공! 다음 단계로 이동합니다.";case CircuitHackManager.HackState.Completed:return "모든 회로 연결 완료! 결과가 한 번 전달되었습니다.";default:return "시작 버튼으로 회로 미로를 여세요.";}
        }
    }
}

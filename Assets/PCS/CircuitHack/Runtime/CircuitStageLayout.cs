using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
namespace FourPaws.PCS.CircuitHack
{
    // 씬 인스턴스 자체를 읽는다. 프리팹 원본이나 과거 SO의 벽으로 대체하지 않는다.
    public sealed class CircuitStageLayout : MonoBehaviour
    {
        [Header("독립 단계 식별")]
        public string stageId="circuit-01";
        [Min(1)] public int displayNumber=1;
        [Header("눈으로 편집하는 배치")]
        public RectTransform board;
        public RectTransform wallsRoot, start, goal, decorationRoot;
        [Tooltip("시작 표식의 방향. 상하좌우 단위 벡터입니다.")] public Vector2Int initialDirection=Vector2Int.right;
        [Tooltip("보드 크기와 무관한 논리 영역입니다.")] public Rect bounds=new Rect(0,0,100,60);
        [Header("단계별 조절값")]
        public float baseSpeed=8f, headWidth=1.4f, visualHeadWidth=1.4f;
        [Tooltip("검수 전용 경유점. 사용자 벽 변경으로 막혀도 구조가 유효하면 실행할 수 있습니다.")] public Vector2[] referenceRoute=Array.Empty<Vector2>();
        public const int MaxWalls=128;
        public bool TryCapture(out CircuitMazeData.Snapshot snapshot,out string error)
        {
            snapshot=null;error=null;
            if(string.IsNullOrWhiteSpace(stageId)||displayNumber<1){error="단계 ID와 표시 번호를 확인하세요.";return false;}
            if(!board||!board.IsChildOf(transform)||!wallsRoot||!start||!goal||!wallsRoot.IsChildOf(board)||!start.IsChildOf(board)||!goal.IsChildOf(board)){error="보드 내부의 Walls·Start·Goal 참조가 필요합니다.";return false;}
            if(board.rect.width<=0||board.rect.height<=0){error="보드 폭과 높이는 양수여야 합니다.";return false;}
            if(!ValidChain(board,out error))return false;
            var walls=new List<Rect>();
            foreach(var marker in wallsRoot.GetComponentsInChildren<CircuitWall>(true))
            {
                if(!marker.enabled||!ActiveWithin(marker.transform,transform))continue;
                var image=marker.GetComponent<Image>();
                if(!image||!image.enabled||image.color.a<=0){error="보이지 않는 충돌 벽입니다: "+marker.name;return false;}
                if(!ReadRect((RectTransform)marker.transform,out Rect rect,out error))return false;
                walls.Add(rect);if(walls.Count>MaxWalls){error="활성 벽은 최대 128개입니다.";return false;}
            }
            if(!ActiveWithin(start,transform)||!ActiveWithin(goal,transform)){error="시작·출구 표식과 그 부모는 활성 상태여야 합니다.";return false;}
            if(!ReadRect(start,out Rect startRect,out error)||!ReadRect(goal,out Rect goalRect,out error))return false;
            var decorations=new List<Rect>();
            if(decorationRoot)
            {
                if(!decorationRoot.IsChildOf(board)){error="장식 루트는 보드 안에 있어야 합니다.";return false;}
                foreach(var im in decorationRoot.GetComponentsInChildren<Image>(true))
                    if(im.enabled&&ActiveWithin(im.transform,transform)&&ReadRect(im.rectTransform,out Rect r,out _))decorations.Add(r);
            }
            snapshot=new CircuitMazeData.Snapshot{bounds=bounds,start=startRect.center,goal=goalRect,initialDirection=initialDirection,
                walls=walls.ToArray(),decorations=decorations.ToArray(),baseSpeed=baseSpeed,headWidth=headWidth,visualHeadWidth=visualHeadWidth,
                referenceRoute=referenceRoute==null?Array.Empty<Vector2>():(Vector2[])referenceRoute.Clone(),stageId=stageId,displayNumber=displayNumber};
            error=CircuitMazeData.Validate(snapshot,false);
            if(error!=null){snapshot=null;return false;}
            snapshot.routeError=CircuitMazeData.ValidateRoute(snapshot);
            return true;
        }
        private bool ValidChain(Transform t,out string error)
        {
            error=null;
            for(var p=t;p;p=p.parent)
            {
                Vector3 s=p.localScale;
                if(!CircuitCollisionChecker.Finite(s.x)||!CircuitCollisionChecker.Finite(s.y)||!CircuitCollisionChecker.Finite(s.z)||s.x<=0||s.y<=0||s.z<=0||Quaternion.Angle(p.localRotation,Quaternion.identity)>.01f)
                {error="회전·기울임·음수/0 배율은 지원하지 않습니다: "+p.name;return false;}
                if(p==transform)return true;
            }
            error="단계 밖의 좌표 참조입니다: "+t.name;return false;
        }
        private bool ReadRect(RectTransform target,out Rect result,out string error)
        {
            result=default;
            if(!ValidChain(target,out error))return false;
            var corners=new Vector3[4];target.GetWorldCorners(corners);
            for(int i=0;i<4;i++)corners[i]=board.InverseTransformPoint(corners[i]);
            if(Mathf.Abs(corners[0].x-corners[1].x)>.01f||Mathf.Abs(corners[1].y-corners[2].y)>.01f){error="축 정렬 직사각형이 아닙니다: "+target.name;return false;}
            Vector2 a=Logical(corners[0]),b=Logical(corners[2]);result=Rect.MinMaxRect(a.x,a.y,b.x,b.y);
            if(result.width<=0||result.height<=0){error="유효한 폭/높이가 필요합니다: "+target.name;return false;}
            return true;
        }
        private Vector2 Logical(Vector2 p)=>new Vector2(Coordinate(bounds.xMin+(p.x-board.rect.xMin)/board.rect.width*bounds.width),Coordinate(bounds.yMin+(p.y-board.rect.yMin)/board.rect.height*bounds.height));
        // Canvas의 큰 화면 좌표를 왕복할 때 생기는 미세 오차를 공통 스냅샷에서만 정규화한다.
        private static float Coordinate(float value)=>(float)System.Math.Round(value,4);
        private static bool ActiveWithin(Transform t,Transform root)
        {
            for(var p=t;p;p=p.parent){if(p==root)return true;if(!p.gameObject.activeSelf)return false;}return false;
        }
        // 선택한 배치의 머리 시작 윤곽만 보여 준다. 정답 경로는 기본 화면에 그리지 않는다.
        private void OnDrawGizmosSelected()
        {
            if(!board||!start||bounds.width<=0||bounds.height<=0)return;
            Gizmos.color=new Color(1f,.8f,.2f);Gizmos.matrix=board.localToWorldMatrix;
            Vector3 p=board.InverseTransformPoint(start.position);
            Gizmos.DrawWireCube(p,new Vector3(headWidth/bounds.width*board.rect.width,headWidth/bounds.height*board.rect.height,.1f));
        }
    }
}

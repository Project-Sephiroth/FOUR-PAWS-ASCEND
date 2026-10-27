using System;
using UnityEngine;

namespace FourPaws.PCS.CircuitHack
{
    // 편집용 데이터는 시작 시 복사한다. 실행 중에는 원본 SO를 수정하지 않는다.
    public sealed class CircuitMazeData : ScriptableObject
    {
        [Header("회로 판과 시작 / 출구")]
        [Tooltip("왼쪽 아래 원점의 논리 영역. 화면 크기와 무관합니다.")] public Rect bounds=new Rect(0,0,100,60);
        [Tooltip("신호 머리의 시작 중심")] public Vector2 start=new Vector2(5,30);
        [Tooltip("축 방향 단위 벡터만 허용합니다.")] public Vector2Int initialDirection=Vector2Int.right;
        [Tooltip("머리 전체가 들어가야 도착입니다.")] public Rect goal=new Rect(90,6,8,8);
        [Tooltip("실제 충돌 벽. 최대 128개이며 장식은 포함하지 않습니다.")] public Rect[] walls=Array.Empty<Rect>();
        [Header("속도와 크기")]
        [Tooltip("활성 초당 논리 이동 거리. 양의 유한 값")] public float baseSpeed=8f;
        [Tooltip("실제 충돌 정사각형 폭")] public float headWidth=1.4f;
        [Tooltip("화면 머리 코어 폭. 발광은 충돌에 포함하지 않습니다.")] public float visualHeadWidth=1.4f;
        [Header("도달성 검수 경로")]
        [Tooltip("시작부터 출구까지 축 정렬 경유점. 성공 판정에는 사용하지 않습니다.")] public Vector2[] referenceRoute=Array.Empty<Vector2>();
        public sealed class Snapshot
        {
            public string stageId="legacy"; public int displayNumber=1; public string routeError; public Rect[] decorations=Array.Empty<Rect>();
            public Rect bounds,goal; public Vector2 start; public Vector2Int initialDirection;
            public Rect[] walls;public float baseSpeed,headWidth,visualHeadWidth;public Vector2[] referenceRoute;
            public Snapshot(){}
            public Snapshot(CircuitMazeData d){bounds=d.bounds;goal=d.goal;start=d.start;initialDirection=d.initialDirection;walls=d.walls==null?null:(Rect[])d.walls.Clone();baseSpeed=d.baseSpeed;headWidth=d.headWidth;visualHeadWidth=d.visualHeadWidth;referenceRoute=d.referenceRoute==null?null:(Vector2[])d.referenceRoute.Clone();}
        }
        public Snapshot Capture()=>new Snapshot(this);
        private static bool RectValid(Rect r)=>CircuitCollisionChecker.Finite(r.x)&&CircuitCollisionChecker.Finite(r.y)&&CircuitCollisionChecker.Finite(r.width)&&CircuitCollisionChecker.Finite(r.height)&&r.width>0&&r.height>0;
        private static bool Inside(Rect outer,Rect r)=>r.xMin>=outer.xMin&&r.yMin>=outer.yMin&&r.xMax<=outer.xMax&&r.yMax<=outer.yMax;
        public static string Validate(Snapshot d,bool checkRoute=true)
        {
            if(d==null)return "단계 스냅샷이 없습니다.";
            if(!RectValid(d.bounds)||!RectValid(d.goal))return "보드와 출구의 좌표·크기는 유한한 양의 영역이어야 합니다.";
            if(!CircuitCollisionChecker.Finite(d.baseSpeed)||d.baseSpeed<=0||!CircuitCollisionChecker.Finite(d.headWidth)||d.headWidth<=0||!CircuitCollisionChecker.Finite(d.visualHeadWidth)||d.visualHeadWidth<=0)return "속도와 머리 크기는 양의 유한 값이어야 합니다.";
            if(!CircuitSignalController.Cardinal(d.initialDirection))return "시작 방향은 상하좌우 단위 벡터여야 합니다.";
            if(d.goal.width<d.headWidth||d.goal.height<d.headWidth||!Inside(d.bounds,d.goal))return "출구는 머리 크기 이상이며 보드 안에 있어야 합니다.";
            if(!CircuitCollisionChecker.Finite(d.start.x)||!CircuitCollisionChecker.Finite(d.start.y)||d.walls==null||d.walls.Length>128)return "시작 좌표 또는 벽 배열이 잘못되었습니다. 벽은 최대 128개입니다.";
            for(int i=0;i<d.walls.Length;i++)
            {
                if(!RectValid(d.walls[i])||!Inside(d.bounds,d.walls[i])||d.walls[i].Overlaps(d.goal))return "벽 크기·영역 또는 출구와의 겹침을 확인하세요: "+i;
                // 겹친 벽은 합집합으로 충돌한다. 장식은 이 배열에 포함하지 않는다.
            }
            if(CircuitCollisionChecker.Check(d,d.start,d.start).kind!=CircuitCollisionChecker.Kind.None)return "시작 위치가 벽·경계·출구에 닿습니다.";
            return checkRoute?ValidateRoute(d):null;
        }
        public static string ValidateRoute(Snapshot d)
        {
            if(d.referenceRoute==null||d.referenceRoute.Length<2||d.referenceRoute[0]!=d.start)return "검수 경로는 시작 위치에서 출발해야 합니다.";
            var signal=new CircuitSignalController();signal.Reset(d.start,d.initialDirection,d.baseSpeed);
            for(int i=1;i<d.referenceRoute.Length;i++)
            {
                Vector2 target=d.referenceRoute[i],delta=target-signal.Position;
                if(!CircuitCollisionChecker.Finite(target.x)||!CircuitCollisionChecker.Finite(target.y)||delta.sqrMagnitude<.0001f||(Mathf.Abs(delta.x)>.0001f&&Mathf.Abs(delta.y)>.0001f))return "검수 경로는 길이가 있는 축 정렬 구간이어야 합니다: "+i;
                var direction=new Vector2Int(Math.Sign(delta.x),Math.Sign(delta.y));
                if(i==1&&direction!=d.initialDirection)return "검수 경로의 첫 방향이 시작 방향과 다릅니다.";
                if(i>1&&!signal.Turn(direction))return "경유점은 90도 회전이어야 합니다: "+i;
                var hit=CircuitCollisionChecker.Check(d,signal.Position,signal.Propose(delta.magnitude/d.baseSpeed));
                if(hit.kind==CircuitCollisionChecker.Kind.Wall)return "머리 크기를 적용한 검수 경로가 충돌합니다: "+i;
                if(hit.kind==CircuitCollisionChecker.Kind.Goal)return i==d.referenceRoute.Length-1?null:"검수 경로가 마지막 경유점 전에 출구에 도착합니다.";
                signal.Commit(target);
            }
            return "검수 경로의 마지막 구간이 출구에 도착하지 않습니다.";
        }
    }
}

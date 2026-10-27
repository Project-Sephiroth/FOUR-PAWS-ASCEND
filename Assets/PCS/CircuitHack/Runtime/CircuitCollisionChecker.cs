using UnityEngine;

namespace FourPaws.PCS.CircuitHack
{
    // 논리 좌표의 전체 이동 구간을 검사한다. 장식과 궤적은 충돌 대상이 아니다.
    public static class CircuitCollisionChecker
    {
        public enum Kind { None, Wall, Goal }
        public struct Hit { public Kind kind; public float t; public Vector2 position; }
        public const float Epsilon = 0.00001f;
        public static Rect Expand(Rect r, float v) => Rect.MinMaxRect(r.xMin-v,r.yMin-v,r.xMax+v,r.yMax+v);
        public static bool Contains(Rect r, Vector2 p) => p.x>=r.xMin && p.x<=r.xMax && p.y>=r.yMin && p.y<=r.yMax;
        public static bool Finite(float n) => !float.IsNaN(n) && !float.IsInfinity(n);
        public static bool Segment(Rect r, Vector2 a, Vector2 b, out float entry)
        {
            float lo=0f, hi=1f; Vector2 d=b-a;
            bool ok=Slab(a.x,d.x,r.xMin,r.xMax,ref lo,ref hi) && Slab(a.y,d.y,r.yMin,r.yMax,ref lo,ref hi);
            entry=lo; return ok;
        }
        private static bool Slab(float a,float d,float min,float max,ref float lo,ref float hi)
        {
            if (d==0f) return a>=min && a<=max;
            float x=(min-a)/d,y=(max-a)/d;
            if(x>y){float t=x;x=y;y=t;}
            lo=Mathf.Max(lo,x);hi=Mathf.Min(hi,y);return lo<=hi && hi>=0f && lo<=1f;
        }
        public static Hit Check(CircuitMazeData.Snapshot data,Vector2 from,Vector2 to)
        {
            float half=data.headWidth*.5f,wall=float.PositiveInfinity,goal=float.PositiveInfinity;
            Rect inner=Expand(data.bounds,-half);
            if(from.x<=inner.xMin || from.x>=inner.xMax || from.y<=inner.yMin || from.y>=inner.yMax) wall=0f;
            else
            {
                Vector2 delta=to-from;
                if(to.x<=inner.xMin) wall=Mathf.Min(wall,(inner.xMin-from.x)/delta.x);
                if(to.x>=inner.xMax) wall=Mathf.Min(wall,(inner.xMax-from.x)/delta.x);
                if(to.y<=inner.yMin) wall=Mathf.Min(wall,(inner.yMin-from.y)/delta.y);
                if(to.y>=inner.yMax) wall=Mathf.Min(wall,(inner.yMax-from.y)/delta.y);
            }
            foreach(Rect r in data.walls) if(Segment(Expand(r,half),from,to,out float t))wall=Mathf.Min(wall,t);
            if(Segment(Expand(data.goal,-half),from,to,out float g))goal=g;
            // 같은 거리 오차만 동률로 취급한다. 긴 프레임에서 뒤의 벽을 먼저 고르지 않는다.
            float distanceScale=Mathf.Max(Mathf.Abs(to.x-from.x),Mathf.Abs(to.y-from.y));
            float tieTolerance=distanceScale>0f?Epsilon/distanceScale:0f;
            if(wall<=1f && wall<=goal+tieTolerance)return new Hit{kind=Kind.Wall,t=Mathf.Clamp01(wall),position=Vector2.Lerp(from,to,Mathf.Clamp01(wall))};
            if(goal<=1f)return new Hit{kind=Kind.Goal,t=Mathf.Clamp01(goal),position=Vector2.Lerp(from,to,Mathf.Clamp01(goal))};
            return new Hit{kind=Kind.None,t=1f,position=to};
        }
    }
}

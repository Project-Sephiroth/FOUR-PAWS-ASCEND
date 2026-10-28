using UnityEngine;
using UnityEngine.UI;

namespace FourPaws.PCS.CircuitHack
{
    // 표시 전용 고정 버퍼. 오래된 흔적을 버려도 퍼즐 규칙에는 영향이 없다.
    public sealed class CircuitTrailRenderer : MaskableGraphic
    {
        public const int Capacity=256;
        private readonly Vector2[] points=new Vector2[Capacity];
        private int count;private Rect board=new Rect(0,0,100,60);
        public int PointCount=>count;
        public void ResetTrail(Rect bounds,Vector2 start){board=bounds;count=2;points[0]=points[1]=start;SetVerticesDirty();}
        public void ClearTrail(){count=0;SetVerticesDirty();}
        public void UpdateEnd(Vector2 position,bool turned)
        {
            if(count<2)return;
            if(turned)
            {
                if(count==Capacity){for(int i=1;i<count;i++)points[i-1]=points[i];count--;}
                points[count]=points[count-1];count++;
            }
            points[count-1]=position;SetVerticesDirty();
        }
        private Vector2 Pixel(Vector2 p)=>new Vector2(rectTransform.rect.xMin+(p.x-board.xMin)/board.width*rectTransform.rect.width,rectTransform.rect.yMin+(p.y-board.yMin)/board.height*rectTransform.rect.height);
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            for(int i=1;i<count;i++){Vector2 a=Pixel(points[i-1]),b=Pixel(points[i]);Line(vh,a,b,7,new Color(.1f,1f,.75f,.14f));Line(vh,a,b,2.5f,new Color(.4f,1f,.8f,.95f));}
        }
        private static void Line(VertexHelper vh,Vector2 a,Vector2 b,float width,Color32 color)
        {
            Vector2 delta=b-a;if(delta.sqrMagnitude<.00001f)return;
            Vector2 n=new Vector2(-delta.y,delta.x).normalized*width*.5f;int i=vh.currentVertCount;
            vh.AddVert(a-n,color,Vector2.zero);vh.AddVert(a+n,color,Vector2.zero);vh.AddVert(b+n,color,Vector2.zero);vh.AddVert(b-n,color,Vector2.zero);
            vh.AddTriangle(i,i+1,i+2);vh.AddTriangle(i,i+2,i+3);
        }
    }
}

using UnityEngine;

namespace FourPaws.PCS.CircuitHack
{
    // 프레임 수와 무관한 이동 모델. 입력은 속도가 아닌 방향만 바꾼다.
    public sealed class CircuitSignalController
    {
        public Vector2 Position {get;private set;}
        public Vector2Int Direction {get;private set;}
        public float Speed {get;private set;}
        public static bool Cardinal(Vector2Int d)=>Mathf.Abs(d.x)+Mathf.Abs(d.y)==1;
        public void Reset(Vector2 p,Vector2Int d,float speed){Position=p;Direction=d;Speed=speed;}
        public bool Turn(Vector2Int d)
        {
            if(!Cardinal(d) || Direction.x*d.x+Direction.y*d.y!=0)return false;
            Direction=d;return true;
        }
        public Vector2 Propose(float dt)=>Position+(Vector2)Direction*(Speed*dt);
        public void Commit(Vector2 p){Position=p;}
    }
}

using System;
using UnityEngine;
namespace FourPaws.PCS.Hacking
{
    // 실제 핀 쌍과 BFS로 판정한다. 증명 회전 배열은 시작 생성에만 사용한다.
    [Serializable] public sealed class PCS_RotationPuzzle
    {
        static readonly int[] SolvedMasks={21,12,32,48,2,3,8};
        static readonly Vector2Int[] Cells={new Vector2Int(0,0),new Vector2Int(1,0),new Vector2Int(0,1),new Vector2Int(-1,1),new Vector2Int(-1,0),new Vector2Int(0,-1),new Vector2Int(1,-1)};
        static readonly Vector2Int[] Directions={new Vector2Int(1,0),new Vector2Int(0,1),new Vector2Int(-1,1),new Vector2Int(-1,0),new Vector2Int(0,-1),new Vector2Int(1,-1)};
        int[] masks=Array.Empty<int>(),initialMasks=Array.Empty<int>(),initialRotations=Array.Empty<int>();int[,] neighbors;PCS_RotationStageData.Snapshot data;
        public bool Running{get;private set;}public bool Succeeded{get;private set;}public int Seed{get;private set;}public int Candidates{get;private set;}public bool UsedFallback{get;private set;}
        public string Error{get;private set;}="";public string VariantId{get;private set;}public int GraphIndex{get;private set;}public int TileCount=>masks.Length;public int RootIndex=>data?.rootIndex??0;
        public Evaluation Status{get;private set;}public long Revision{get;private set;}
        public struct Evaluation{public int outside,unmatched,reachable,pairs,tileCount;public bool Solved=>tileCount>0&&outside==0&&unmatched==0&&reachable==tileCount;}
        public enum SearchResult{Found,Absent,Unknown}
        public int MaskAt(int index)=>masks[index];public int InitialRotation(int index)=>initialRotations[index];public bool IsFixed(int index)=>data!=null&&Array.IndexOf(data.fixedTiles,index)>=0;
        public Vector2 PositionAt(int index)=>PositionOf(data.cells[index]);
        public static Vector2 Position(int index)=>PositionOf(Cells[index]);
        static Vector2 PositionOf(Vector2Int cell)=>new Vector2(Mathf.Sqrt(3)*(cell.x+cell.y*.5f),-1.5f*cell.y);
        public static int RotateMask(int mask,int turns=1){turns=(turns%6+6)%6;return ((mask<<turns)|(mask>>(6-turns)))&63;}
        public static int Neighbor(int tile,int direction)=>Find(Cells,tile,direction);
        public int NeighborAt(int tile,int direction)=>neighbors[tile,direction];
        static int Find(Vector2Int[] cells,int tile,int direction){Vector2Int c=cells[tile]+Directions[direction];for(int i=0;i<cells.Length;i++)if(cells[i]==c)return i;return -1;}
        static int[,] Table(Vector2Int[] cells){var table=new int[cells.Length,6];for(int i=0;i<cells.Length;i++)for(int d=0;d<6;d++)table[i,d]=Find(cells,i,d);return table;}
        public static Evaluation Evaluate(int[] board)=>Evaluate(Cells,board,0);
        public static Evaluation Evaluate(Vector2Int[] cells,int[] board,int root)
        {if(cells==null||board==null||board.Length!=cells.Length||root<0||root>=cells.Length)throw new ArgumentException("좌표·핀 개수와 루트가 일치해야 합니다.");return Evaluate(board,Table(cells),root);}
        static Evaluation Evaluate(int[] board,int[,] table,int root)
        {
            var result=new Evaluation{tileCount=board.Length};
            for(int i=0;i<board.Length;i++){if(board[i]<0||board[i]>63)throw new ArgumentException("핀 마스크는 0~63입니다.");for(int d=0;d<6;d++)if((board[i]&(1<<d))!=0){int n=table[i,d];if(n<0)result.outside++;else if((board[n]&(1<<((d+3)%6)))==0)result.unmatched++;else if(i<n)result.pairs++;}}
            var visited=new bool[board.Length];var queue=new int[board.Length];visited[root]=true;queue[0]=root;int head=0,tail=1;
            while(head<tail){int i=queue[head++];for(int d=0;d<6;d++){int n=table[i,d];if(n>=0&&!visited[n]&&(board[i]&(1<<d))!=0&&(board[n]&(1<<((d+3)%6)))!=0){visited[n]=true;queue[tail++]=n;}}}result.reachable=tail;return result;
        }
        public static PCS_RotationStageData.Snapshot Legacy()=>new PCS_RotationStageData.Snapshot{stageId="rotation-01",displayNumber=1,cells=(Vector2Int[])Cells.Clone(),rootIndex=0,fixedTiles=new[]{0},shuffleLimit=16,minimumClicks=1,graphs=new[]{new PCS_RotationStageData.Graph{variantId="original",solvedMasks=(int[])SolvedMasks.Clone(),fallbackTurns=new[]{0,1,2,3,4,5,1}}}};
        public void Begin(int seed,Func<int> candidateTurn=null){Begin(Legacy(),seed,0,candidateTurn);}
        public bool Begin(PCS_RotationStageData.Snapshot selected,int seed,int graphIndex=0,Func<int> candidateTurn=null)
        {
            Revision++;Running=Succeeded=UsedFallback=false;data=null;initialMasks=Array.Empty<int>();masks=Array.Empty<int>();initialRotations=Array.Empty<int>();Status=default;Error=selected?.Validate()??(selected==null?"단계 데이터가 없습니다.":null);if(Error!=null)return false;
            data=selected.Copy();Seed=seed;GraphIndex=((graphIndex%data.graphs.Length)+data.graphs.Length)%data.graphs.Length;var graph=data.graphs[GraphIndex];VariantId=graph.variantId;neighbors=Table(data.cells);masks=new int[data.cells.Length];initialRotations=new int[masks.Length];var random=new System.Random(seed);
            for(Candidates=1;Candidates<=data.shuffleLimit;Candidates++){for(int i=0;i<masks.Length;i++){int turn=IsFixed(i)?0:candidateTurn==null?random.Next(6):candidateTurn();if(turn<0||turn>5)throw new ArgumentOutOfRangeException(nameof(candidateTurn));initialRotations[i]=turn;masks[i]=RotateMask(graph.solvedMasks[i],turn);}Status=Evaluate(masks,neighbors,RootIndex);if(!Status.Solved&&ShortSolution(masks,data,neighbors,data.minimumClicks-1,50000,out _)==SearchResult.Absent)return Accept();}
            Candidates=data.shuffleLimit;UsedFallback=true;for(int i=0;i<masks.Length;i++){initialRotations[i]=graph.fallbackTurns[i];masks[i]=RotateMask(graph.solvedMasks[i],initialRotations[i]);}Status=Evaluate(masks,neighbors,RootIndex);
            if(Status.Solved||ShortSolution(masks,data,neighbors,data.minimumClicks-1,50000,out _)!=SearchResult.Absent){Error="대체 배치의 비성공/최소 클릭 검증을 통과하지 못했습니다.";return false;}return Accept();
        }
        bool Accept(){Revision++;initialMasks=(int[])masks.Clone();Error="";Running=true;return true;}
        public bool Restart(){if(data==null||initialMasks.Length==0)return false;Revision++;masks=(int[])initialMasks.Clone();Status=Evaluate(masks,neighbors,RootIndex);Running=true;Succeeded=false;return true;}
        public bool Rotate(int index){if(!Running||index<0||index>=TileCount||IsFixed(index))return false;Revision++;masks[index]=RotateMask(masks[index]);Status=Evaluate(masks,neighbors,RootIndex);Succeeded=Status.Solved;if(Succeeded)Running=false;return true;}
        public void Cancel(){Running=false;}
        public static SearchResult ShortSolution(int[] board,PCS_RotationStageData.Snapshot d,int maxClicks,int limit,out int expanded)=>ShortSolution(board,d,Table(d.cells),maxClicks,limit,out expanded);
        static SearchResult ShortSolution(int[] board,PCS_RotationStageData.Snapshot d,int[,] table,int maxClicks,int limit,out int expanded)
        {
            int count=0;bool unknown=false;var trial=(int[])board.Clone();
            bool Search(int start,int remaining){if(++count>limit){unknown=true;return false;}if(Evaluate(trial,table,d.rootIndex).Solved)return true;if(remaining==0)return false;for(int i=start;i<trial.Length;i++){if(Array.IndexOf(d.fixedTiles,i)>=0)continue;int original=trial[i];for(int turn=1;turn<=Math.Min(5,remaining);turn++){trial[i]=RotateMask(original,turn);if(Search(i+1,remaining-turn)){trial[i]=original;return true;}if(unknown)break;}trial[i]=original;if(unknown)return false;}return false;}
            bool found=Search(0,maxClicks);expanded=count;return found?SearchResult.Found:unknown?SearchResult.Unknown:SearchResult.Absent;
        }
    }
}

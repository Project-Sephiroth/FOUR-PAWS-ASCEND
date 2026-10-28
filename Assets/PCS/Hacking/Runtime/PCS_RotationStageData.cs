using System;
using UnityEngine;
namespace FourPaws.PCS.Hacking
{
    // 좌표와 연결 그래프 변형을 별도 보관한다. 핀 마스크의 비트는 오른쪽부터 시계 방향이다.
    public sealed class PCS_RotationStageData : ScriptableObject
    {
        [Serializable] public sealed class Graph
        {public string variantId;public int[] solvedMasks;public int[] fallbackTurns;public Graph Copy()=>new Graph{variantId=variantId,solvedMasks=solvedMasks==null?null:(int[])solvedMasks.Clone(),fallbackTurns=fallbackTurns==null?null:(int[])fallbackTurns.Clone()};}
        public string stageId="rotation-01";[Min(1)]public int displayNumber=1;
        [Tooltip("축 좌표 q,r. 동일 좌표는 허용하지 않습니다.")]public Vector2Int[] cells;
        [Tooltip("연결 탐색을 시작하는 타일 배열 인덱스")]public int rootIndex;[Tooltip("클릭 회전을 금지할 타일 인덱스 목록")]public int[] fixedTiles;
        [Tooltip("성공 상태와 너무 쉬운 시작판을 거르는 최대 후보 수")] [Range(1,16)]public int shuffleLimit=16;
        [Tooltip("이 값보다 적은 클릭의 해답이 없음을 제한 탐색으로 확인합니다.")] [Range(1,5)]public int minimumClicks=1;
        [Tooltip("실제 연결된 기본 그래프와 검증할 대체 회전. 1~8개")]public Graph[] graphs;
        public Snapshot Capture()=>new Snapshot{stageId=stageId,displayNumber=displayNumber,cells=cells==null?null:(Vector2Int[])cells.Clone(),rootIndex=rootIndex,fixedTiles=fixedTiles==null?Array.Empty<int>():(int[])fixedTiles.Clone(),shuffleLimit=shuffleLimit,minimumClicks=minimumClicks,graphs=graphs==null?null:Array.ConvertAll(graphs,g=>g?.Copy())};
        public sealed class Snapshot
        {
            public string stageId;public int displayNumber,rootIndex,shuffleLimit,minimumClicks;public Vector2Int[] cells;public int[] fixedTiles;public Graph[] graphs;
            public Snapshot Copy()=>new Snapshot{stageId=stageId,displayNumber=displayNumber,cells=(Vector2Int[])cells.Clone(),rootIndex=rootIndex,fixedTiles=(int[])fixedTiles.Clone(),shuffleLimit=shuffleLimit,minimumClicks=minimumClicks,graphs=Array.ConvertAll(graphs,g=>g.Copy())};
            public string Validate()
            {
                if(string.IsNullOrWhiteSpace(stageId)||displayNumber<1||cells==null||cells.Length<2||cells.Length>19||rootIndex<0||rootIndex>=cells.Length)return "단계 ID·표시 번호·2~19개 좌표·루트를 확인하세요.";
                for(int i=0;i<cells.Length;i++){if(cells[i].x < -8||cells[i].x>8||cells[i].y < -8||cells[i].y>8)return "육각 좌표 범위는 -8~8입니다.";for(int j=0;j<i;j++)if(cells[i]==cells[j])return "중복 좌표가 있습니다.";}
                if(fixedTiles==null||Array.Exists(fixedTiles,i=>i<0||i>=cells.Length)||shuffleLimit<1||shuffleLimit>16||minimumClicks<1||minimumClicks>5||graphs==null||graphs.Length<1||graphs.Length>8)return "고정 타일·섞기 1~16회·최소 클릭 1~5·그래프 1~8개를 확인하세요.";
                foreach(var g in graphs){if(g==null||string.IsNullOrWhiteSpace(g.variantId)||g.solvedMasks==null||g.solvedMasks.Length!=cells.Length||g.fallbackTurns==null||g.fallbackTurns.Length!=cells.Length||Array.Exists(g.solvedMasks,m=>m<0||m>63)||Array.Exists(g.fallbackTurns,t=>t<0||t>5))return "그래프의 핀 마스크·대체 회전 배열을 확인하세요.";if(!PCS_RotationPuzzle.Evaluate(cells,g.solvedMasks,rootIndex).Solved)return "기본 그래프는 모든 타일이 실제로 연결되어야 합니다.";foreach(int i in fixedTiles)if(g.fallbackTurns[i]!=0)return "고정 타일의 대체 회전은 0이어야 합니다.";}
                return null;
            }
        }
    }
}

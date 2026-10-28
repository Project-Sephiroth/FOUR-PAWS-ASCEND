using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace FourPaws.PCS.StepPuzzle
{
    // 입력·화면과 분리된 발판 규칙. 시도마다 설계값을 복제하여 재도전이 같은 판을 복원한다.
    [Serializable]
    public sealed class PCS_StepPuzzle
    {
        public enum MoveResult { Ignored, Outside, Visited, Failed, Succeeded, Wall, BudgetEmpty }
        public enum Difficulty { [InspectorName("연습")] Practice, [InspectorName("보통")] Normal, [InspectorName("어려움")] Hard }
        [Serializable]
        public sealed class Board
        {
            [Tooltip("연습은 모든 칸 방문, 미로는 목표 도착으로 성공합니다.")] public bool practice;
            [Range(2, 6)] public int width = 5;
            [Range(2, 6)] public int height = 5;
            [Tooltip("좌하단이 (0,0)입니다.")] public Vector2Int start;
            public Vector2Int goal;
            [Tooltip("통과할 수 없는 칸의 좌표입니다.")] public Vector2Int[] walls = Array.Empty<Vector2Int>();
            [Tooltip("초기 이동 예산: 오른쪽, 위쪽, 왼쪽, 아래쪽 순서입니다.")] public int[] budgets = { 3, 3, 2, 2 };
            [Tooltip("개발자 검증용 경로입니다. 성공 판정은 이 배열과 비교하지 않습니다.")] public Vector2Int[] proof = Array.Empty<Vector2Int>();
            public Vector2Int[] alternateProof=Array.Empty<Vector2Int>();
            public int rejectedUnknown;public int solverExpanded;public string qualityStatus="미평가";
            public int seed;
            public int candidate;
            public int expanded;
            public int fallback = -1;
            public Board Copy() => new Board { practice = practice, width = width, height = height, start = start, goal = goal,
                walls = (Vector2Int[])walls.Clone(), budgets = (int[])budgets.Clone(), proof = (Vector2Int[])proof.Clone(),
                alternateProof=alternateProof==null?Array.Empty<Vector2Int>():(Vector2Int[])alternateProof.Clone(),rejectedUnknown=rejectedUnknown,solverExpanded=solverExpanded,qualityStatus=qualityStatus,
                seed = seed, candidate = candidate, expanded = expanded, fallback = fallback };
            public bool Inside(Vector2Int p) => p.x >= 0 && p.y >= 0 && p.x < width && p.y < height;
            public bool IsWall(Vector2Int p) { foreach (var w in walls) if (w == p) return true; return false; }
            public string Validate()
            {
                if (width < 2 || width > 6 || height < 2 || height > 6) return "보드 가로·세로는 2~6이어야 합니다.";
                if (walls == null || budgets == null || budgets.Length != 4 || proof == null) return "벽·예산·검증 경로 설정이 누락됐습니다.";
                if (!Inside(start) || !Inside(goal) || start == goal || IsWall(start) || IsWall(goal)) return "시작과 목표는 서로 다른 통과 가능한 칸이어야 합니다.";
                var unique = new HashSet<Vector2Int>();
                foreach (var w in walls) if (!Inside(w) || !unique.Add(w)) return "벽 좌표가 범위를 벗어나거나 중복됐습니다.";
                foreach (int b in budgets) if (b < 0 || b > 36) return "방향별 예산은 0~36이어야 합니다.";
                if (practice && (width != 3 || height != 3 || walls.Length != 0 || start != Vector2Int.zero)) return "연습 모드는 빈 3×3과 시작 (0,0)을 사용합니다.";
                return "";
            }
            public static Board Practice() => new Board { practice = true, width = 3, height = 3, start = Vector2Int.zero,
                goal = new Vector2Int(2, 2), budgets = new[] { 9, 9, 9, 9 },
                proof = Route(Vector2Int.zero, "RRULLURR") };
        }
        private ulong visited;
        private readonly int[] remaining = new int[4];
        public Board Current { get; private set; } = Board.Practice();
        public int X { get; private set; }
        public int Y { get; private set; }
        public int Count { get; private set; }
        public bool Running { get; private set; }
        public bool Failed { get; private set; }
        public bool Succeeded { get; private set; }
        public string Error { get; private set; } = "";
        public string FailureReason { get; private set; } = "";
        public int Remaining(int direction) => remaining[direction];
        public bool IsVisited(int x, int y) => x >= 0 && x < Current.width && y >= 0 && y < Current.height && (visited & (1UL << (y * Current.width + x))) != 0;
        public void Begin() { Begin(Board.Practice()); }
        public bool Begin(Board board)
        {
            Cancel();
            Error = board == null ? "보드가 없습니다." : board.Validate();
            if (Error.Length != 0) return false;
            Current = board.Copy();
            X = Current.start.x; Y = Current.start.y;
            visited = 1UL << (Y * Current.width + X); Count = 1;
            Array.Copy(Current.budgets, remaining, 4);
            Running = true;
            return true;
        }
        public void Cancel()
        {
            Running = Failed = Succeeded = false;
            visited = 0; Count = 0; X = Current.start.x; Y = Current.start.y;
            Error = FailureReason = "";
        }
        public MoveResult Move(int dx, int dy)
        {
            if (!Running || Mathf.Abs(dx) + Mathf.Abs(dy) != 1) return MoveResult.Ignored;
            var next = new Vector2Int(X + dx, Y + dy);
            int direction = DirectionIndex(new Vector2Int(dx, dy));
            if (!Current.Inside(next)) return MoveResult.Outside;
            if (Current.IsWall(next)) return MoveResult.Wall;
            if (!Current.practice && remaining[direction] == 0) return MoveResult.BudgetEmpty;
            if (IsVisited(next.x, next.y))
            {
                // 재방문 실패에서는 위치와 예산을 변경하지 않는다.
                Running = false; Failed = true; FailureReason = "이미 방문한 칸에 다시 들어가려 했습니다.";
                return MoveResult.Failed;
            }
            X = next.x; Y = next.y;
            visited |= 1UL << (Y * Current.width + X); Count++;
            if (!Current.practice) remaining[direction]--;
            if (Current.practice ? Count == 9 : next == Current.goal)
            {
                Running = false; Succeeded = true; return MoveResult.Succeeded;
            }
            if (!Current.practice && !HasLegalMove())
            {
                Running = false; Failed = true; FailureReason = "남은 예산으로 갈 수 있는 새 칸이 없습니다.";
                return MoveResult.Failed;
            }
            return MoveResult.Visited;
        }
        private bool HasLegalMove()
        {
            for (int i = 0; i < 4; i++) { var p = new Vector2Int(X, Y) + Directions[i]; if (remaining[i] > 0 && Current.Inside(p) && !Current.IsWall(p) && !IsVisited(p.x, p.y)) return true; }
            return false;
        }
        public static readonly Vector2Int[] Directions = { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down };
        public static int DirectionIndex(Vector2Int d) => d.x > 0 ? 0 : d.y > 0 ? 1 : d.x < 0 ? 2 : 3;
        public static Vector2Int ResolveDirections(bool right, bool up, bool left, bool down)
        {
            if ((right ? 1 : 0) + (up ? 1 : 0) + (left ? 1 : 0) + (down ? 1 : 0) != 1) return Vector2Int.zero;
            return right ? Vector2Int.right : up ? Vector2Int.up : left ? Vector2Int.left : Vector2Int.down;
        }
        public static Vector2Int[] Route(Vector2Int start, string steps)
        {
            var path = new Vector2Int[steps.Length + 1]; path[0] = start;
            for (int i = 0; i < steps.Length; i++) path[i + 1] = path[i] + Directions["RULD".IndexOf(steps[i])];
            return path;
        }
        public static bool VerifyProof(Board board,out string error)=>VerifyRoute(board,board?.proof,out error);
        public static bool VerifyRoute(Board board,Vector2Int[] route,out string error)
        {
            error=board==null?"보드 누락":board.Validate();if(error.Length>0)return false;
            if(route==null||route.Length<2||route[0]!=board.start){error="검증 경로의 시작이 다릅니다.";return false;}
            var model=new PCS_StepPuzzle();model.Begin(board);
            for(int i=1;i<route.Length;i++){var delta=route[i]-route[i-1];var result=model.Move(delta.x,delta.y);if(result!=MoveResult.Visited&&!(result==MoveResult.Succeeded&&i==route.Length-1)){error="검증 경로가 벽·예산·재방문 규칙을 위반합니다.";return false;}}
            if(!model.Succeeded){error="검증 경로가 성공으로 끝나지 않습니다.";return false;}return true;
        }

        // 생성기에는 프레임 입력이나 Unity 전역 난수를 사용하지 않는다. 모든 탐색은 상한을 가진다.
        public static Board Generate(Difficulty difficulty, int seed, ICollection<string> recent = null, int candidateLimit = 32, int expansionLimit = 4000)
        {
            if (difficulty == Difficulty.Practice) return Board.Practice();
            var random = new System.Random(seed); int size = difficulty == Difficulty.Hard ? 6 : 5;
            int expandedTotal = 0,unknownTotal=0;
            for(int attempt=0;attempt<Mathf.Clamp(candidateLimit,0,32);attempt++)
            {
                int length=size==5?random.Next(8,13):random.Next(12,19);
                var path=new List<Vector2Int>{new Vector2Int(random.Next(size),random.Next(size))};var used=new bool[size*size];used[path[0].y*size+path[0].x]=true;int expanded=0;
                if(!Grow(path,used,size,length,size==5?3:5,random,ref expanded,Mathf.Clamp(expansionLimit,0,4000))){expandedTotal+=expanded;continue;}expandedTotal+=expanded;
                Vector2Int[] alternate=Detour(path,size,random);if(alternate==null)continue;
                int[] budgetA=UseCounts(path),budgetB=UseCounts(alternate);bool different=false;for(int d=0;d<4;d++)if(budgetA[d]!=budgetB[d])different=true;if(!different)continue;
                var open=(bool[])used.Clone();foreach(var point in alternate)open[point.y*size+point.x]=true;
                var order=new int[size*size];for(int i=0;i<order.Length;i++)order[i]=i;Shuffle(order,random);int added=0,target=random.Next(1,5);
                foreach(int index in order){if(added>=target)break;if(open[index])continue;var point=new Vector2Int(index%size,index/size);if(Adjacent(point,open,size)==0)continue;open[index]=true;if(Shortest(open,size,size,path[0],path[path.Count-1])<(size==5?8:12))open[index]=false;else added++;}
                var board=MakeBoard(size,open,path.ToArray());for(int d=0;d<4;d++)board.budgets[d]=Math.Max(budgetA[d],budgetB[d]);board.alternateProof=alternate;board.seed=seed;board.candidate=attempt+1;board.expanded=expandedTotal;
                bool quality=Quality(board,difficulty);if(board.qualityStatus=="UnknownBudget")unknownTotal++;if(!quality||!VerifyProof(board,out _)||!VerifyRoute(board,alternate,out _)||(recent!=null&&recent.Contains(Canonical(board))))continue;board.rejectedUnknown=unknownTotal;return board;
            }
            for (int offset = 0; offset < 3; offset++)
            {
                int id = (int)(((uint)seed + (uint)offset) % 3); var board = Fallback(difficulty, id);
                board.seed = seed; board.candidate = Mathf.Clamp(candidateLimit, 0, 32); board.expanded = expandedTotal; board.rejectedUnknown=unknownTotal;board.fallback = id;
                string error; bool proofValid = VerifyProof(board, out error); if (!Quality(board, difficulty) || !proofValid) throw new InvalidOperationException("대체 미로 검증 실패: " + error);
                // 이력이 가득 차도 직전 보드와 같은 대체판은 선택하지 않는다.
                if (recent == null || !recent.Contains(Canonical(board))) return board;
            }
            // 최근 이력 16개에 세 대체판이 모두 있다면 오래된 이력만 완화한다.
            string last = null; if (recent != null) foreach (string key in recent) last = key;
            for (int id = 0; id < 3; id++) { var board = Fallback(difficulty, id); board.seed = seed; board.candidate = Mathf.Clamp(candidateLimit, 0, 32); board.expanded = expandedTotal; board.rejectedUnknown=unknownTotal;board.fallback = id; if(Canonical(board)!=last){if(!Quality(board,difficulty)||!VerifyProof(board,out string error))throw new InvalidOperationException("이력 완화 대체판 검증 실패");return board;} }
            throw new InvalidOperationException("서로 다른 대체 미로를 선택할 수 없습니다.");
        }
        private static bool Grow(List<Vector2Int> path, bool[] used, int size, int length, int turns, System.Random random, ref int expanded, int limit)
        {
            if (expanded >= limit) return false; expanded++;
            if (path.Count == length + 1) return Turns(path) >= turns;
            int[] order = { 0, 1, 2, 3 }; Shuffle(order, random);
            foreach (int direction in order)
            {
                var p = path[path.Count - 1] + Directions[direction];
                if (p.x < 0 || p.y < 0 || p.x >= size || p.y >= size || used[p.y * size + p.x] || Adjacent(p, used, size) != 1) continue;
                used[p.y * size + p.x] = true; path.Add(p);
                if (Grow(path, used, size, length, turns, random, ref expanded, limit)) return true;
                path.RemoveAt(path.Count - 1); used[p.y * size + p.x] = false;
            }
            return false;
        }
        private static void Shuffle(int[] values, System.Random random) { for (int i = values.Length - 1; i > 0; i--) { int j = random.Next(i + 1); int v = values[i]; values[i] = values[j]; values[j] = v; } }
        private static int Adjacent(Vector2Int p, bool[] open, int size) { int n = 0; foreach (var d in Directions) { var q = p + d; if (q.x >= 0 && q.y >= 0 && q.x < size && q.y < size && open[q.y * size + q.x]) n++; } return n; }
        private static Board MakeBoard(int size, bool[] open, Vector2Int[] path)
        {
            var walls = new List<Vector2Int>(); for (int i = 0; i < open.Length; i++) if (!open[i]) walls.Add(new Vector2Int(i % size, i / size));
            int[] budgets = new int[4]; for (int i = 1; i < path.Length; i++) budgets[DirectionIndex(path[i] - path[i - 1])]++;
            return new Board { width = size, height = size, walls = walls.ToArray(), start = path[0], goal = path[path.Length - 1], proof = path, budgets = budgets };
        }
        public static int Turns(IList<Vector2Int> path) { int n = 0; for (int i = 2; i < path.Count; i++) if (path[i] - path[i - 1] != path[i - 1] - path[i - 2]) n++; return n; }
        public static int Branches(Board b)
        {
            int n = 0; for (int y = 0; y < b.height; y++) for (int x = 0; x < b.width; x++) { var p = new Vector2Int(x, y); if (b.IsWall(p) || p == b.start || p == b.goal) continue; int neighbors = 0; foreach (var d in Directions) if (b.Inside(p + d) && !b.IsWall(p + d)) neighbors++; if (neighbors >= 3) n++; } return n;
        }
        public static int Shortest(Board b) { var open = new bool[b.width * b.height]; for (int i = 0; i < open.Length; i++) open[i] = !b.IsWall(new Vector2Int(i % b.width, i / b.width)); return Shortest(open, b.width, b.height, b.start, b.goal); }
        private static int Shortest(bool[] open, int width, int height, Vector2Int start, Vector2Int goal)
        {
            var distance = new int[open.Length]; for (int i = 0; i < distance.Length; i++) distance[i] = -1;
            var queue = new Queue<Vector2Int>(); queue.Enqueue(start); distance[start.y * width + start.x] = 0;
            while (queue.Count > 0) { var p = queue.Dequeue(); if (p == goal) return distance[p.y * width + p.x]; foreach (var d in Directions) { var q = p + d; if (q.x < 0 || q.y < 0 || q.x >= width || q.y >= height) continue; int i = q.y * width + q.x; if (!open[i] || distance[i] >= 0) continue; distance[i] = distance[p.y * width + p.x] + 1; queue.Enqueue(q); } } return -1;
        }
        public sealed class Analysis
        {
            public bool complete=true;public int expanded,solutions,winningChoices,delayedLosingChoices,budgetSensitiveChoices,minSteps=int.MaxValue;
            public string Status=>complete?"Complete":"UnknownBudget";
        }
        struct BranchOutcome{public int solutions,maxDepth;public bool complete;}
        // 방문 36비트와 잔여 예산은 별도 값으로 보관한다. 상한 결과를 해답 없음으로 캐시하지 않는다.
        public static Analysis Analyze(Board board,int limit=20000)
        {
            var report=new Analysis();if(board==null||board.practice||board.Validate().Length>0){report.complete=false;return report;}
            int count=board.width*board.height,goal=board.goal.y*board.width+board.goal.x;var walls=new bool[count];foreach(var wall in board.walls)walls[wall.y*board.width+wall.x]=true;
            int[,] next=new int[count,4];for(int cell=0;cell<count;cell++)for(int d=0;d<4;d++){var p=new Vector2Int(cell%board.width,cell/board.width)+Directions[d];next[cell,d]=board.Inside(p)&&!walls[p.y*board.width+p.x]?p.y*board.width+p.x:-1;}
            bool ReachIgnoringBudget(int cell,ulong visitedMask){var queue=new int[count];int head=0,tail=1;queue[0]=cell;ulong seen=visitedMask|(1UL<<cell);while(head<tail){int current=queue[head++];if(current==goal)return true;for(int d=0;d<4;d++){int n=next[current,d];if(n<0||(seen&(1UL<<n))!=0)continue;seen|=1UL<<n;queue[tail++]=n;}}return false;}
            BranchOutcome Search(int cell,ulong visitedMask,int r,int u,int l,int down,int depth)
            {
                if(report.expanded>=limit){report.complete=false;return new BranchOutcome{complete=false};}report.expanded++;
                if(cell==goal){report.solutions++;report.minSteps=Math.Min(report.minSteps,depth);return new BranchOutcome{complete=true,solutions=1,maxDepth=0};}
                // 선택한 첫 이동을 포함하여 세 수 이상 진행한 뒤 막히는 분기만 센다.
                int wins=0,losing=0,sensitive=0,total=0,maxDepth=0;bool complete=true;
                for(int d=0;d<4;d++)
                {
                    int budget=d==0?r:d==1?u:d==2?l:down,n=next[cell,d];if(budget<=0||n<0||(visitedMask&(1UL<<n))!=0)continue;
                    ulong nextVisited=visitedMask|(1UL<<n);var child=Search(n,nextVisited,r-(d==0?1:0),u-(d==1?1:0),l-(d==2?1:0),down-(d==3?1:0),depth+1);
                    total+=child.solutions;maxDepth=Math.Max(maxDepth,child.maxDepth+1);complete&=child.complete;
                    if(child.solutions>0)wins++;else if(child.complete&&child.maxDepth+1>=3){losing++;if(ReachIgnoringBudget(n,nextVisited))sensitive++;}
                    if(!report.complete)break;
                }
                if(complete){if(wins>=2)report.winningChoices++;if(wins>0){report.delayedLosingChoices+=losing;report.budgetSensitiveChoices+=sensitive;}}
                return new BranchOutcome{complete=complete,solutions=total,maxDepth=maxDepth};
            }
            int start=board.start.y*board.width+board.start.x;Search(start,1UL<<start,board.budgets[0],board.budgets[1],board.budgets[2],board.budgets[3],0);return report;
        }
        public static bool Quality(Board b,Difficulty difficulty)
        {
            if(b==null||b.Validate().Length>0||!VerifyProof(b,out _)||!VerifyRoute(b,b.alternateProof,out _))return false;
            bool different=false;var first=UseCounts(b.proof);var second=UseCounts(b.alternateProof);for(int i=0;i<4;i++)if(first[i]!=second[i])different=true;
            if(!different||Shortest(b)<(difficulty==Difficulty.Hard?12:8))return false;
            var analysis=Analyze(b);b.solverExpanded=analysis.expanded;b.qualityStatus=analysis.Status;
            return analysis.complete&&analysis.solutions>=2&&analysis.winningChoices>=1&&analysis.delayedLosingChoices>=1&&(difficulty!=Difficulty.Hard||analysis.budgetSensitiveChoices>=1);
        }
        static int[] UseCounts(IList<Vector2Int> route){var counts=new int[4];for(int i=1;i<route.Count;i++)counts[DirectionIndex(route[i]-route[i-1])]++;return counts;}
        static Vector2Int[] Detour(List<Vector2Int> original,int size,System.Random random)
        {
            for(int pair=0;pair<8;pair++)
            {
                int a=random.Next(1,original.Count-3),b=Math.Min(original.Count-2,a+random.Next(2,8));if(b<=a+1)continue;
                var used=new bool[size*size];foreach(var p in original)used[p.y*size+p.x]=true;used[original[b].y*size+original[b].x]=false;
                var route=new List<Vector2Int>{original[a]};int expanded=0,maxSteps=Math.Min(9,b-a+5);
                bool Search(){if(expanded>=1000)return false;expanded++;var p=route[route.Count-1];if(p==original[b])return route.Count>2;if(route.Count-1>=maxSteps||Mathf.Abs(p.x-original[b].x)+Mathf.Abs(p.y-original[b].y)>maxSteps-(route.Count-1))return false;
                    int[] order={0,1,2,3};Shuffle(order,random);foreach(int d in order){var n=p+Directions[d];if(n.x<0||n.y<0||n.x>=size||n.y>=size||used[n.y*size+n.x])continue;used[n.y*size+n.x]=true;route.Add(n);if(Search())return true;route.RemoveAt(route.Count-1);used[n.y*size+n.x]=false;if(expanded>=1000)break;}return false;}
                if(Search()){var combined=new List<Vector2Int>();for(int i=0;i<a;i++)combined.Add(original[i]);combined.AddRange(route);for(int i=b+1;i<original.Count;i++)combined.Add(original[i]);return combined.ToArray();}
            }return null;
        }
        public static string GeometryCanonical(Board board){var copy=board.Copy();copy.budgets=new int[4];return Canonical(copy);}
        public static string Canonical(Board b)
        {
            string best = null;
            for (int mirror = 0; mirror < 2; mirror++) for (int turns = 0; turns < 4; turns++)
            {
                int w = turns % 2 == 0 ? b.width : b.height, h = turns % 2 == 0 ? b.height : b.width;
                var cells = new char[w * h]; for (int i = 0; i < cells.Length; i++) cells[i] = '.';
                foreach (var p in b.walls) { var q = Transform(p, b.width, b.height, mirror, turns); cells[q.y * w + q.x] = '#'; }
                var start = Transform(b.start, b.width, b.height, mirror, turns); var goal = Transform(b.goal, b.width, b.height, mirror, turns); cells[start.y * w + start.x] = 'S'; cells[goal.y * w + goal.x] = 'G';
                int[] budgets = new int[4]; for (int d = 0; d < 4; d++) { var v = Directions[d]; if (mirror == 1) v.x = -v.x; for (int t = 0; t < turns; t++) v = new Vector2Int(-v.y, v.x); budgets[DirectionIndex(v)] = b.budgets[d]; }
                string key = w + "x" + h + "|" + string.Join(",", budgets) + "|" + new string(cells); if (best == null || string.CompareOrdinal(key, best) < 0) best = key;
            }
            return best;
        }
        private static Vector2Int Transform(Vector2Int p, int width, int height, int mirror, int turns)
        {
            if (mirror == 1) p.x = width - 1 - p.x;
            for (int t = 0; t < turns; t++) { p = new Vector2Int(height - 1 - p.y, p.x); int swap = width; width = height; height = swap; } return p;
        }
        public static Board Fallback(Difficulty difficulty,int id)
        {
            if(id<0||id>2)throw new ArgumentOutOfRangeException(nameof(id));
            string[] normal={"#.#G./.#.#./#..../S#.../.....","#...#/...../G##../#S.../.#...","....S/..#../##.#./G#.../....#"};
            string[] hard={".##.../....#./S..##./#..#../#..#../..#.G.",".....S/...###/#.#..G/#...../#...../......","#...../#..#../....../.#.#../.#..#./.G##.S"};
            string[] routes=difficulty==Difficulty.Hard?new[]{"RRURURRDDDLDRDL","LLLLLDRDDRRRRU","UUUUULLLLDRDLLDDDR"}:new[]{"DRRRRULURUUL","RRRUULULLDLD","LDRDDLLDLLU"};
            string[] alternatives=difficulty==Difficulty.Hard?new[]{"RURRURRDDDLDD","LLLLLDRDDRRRDLLDRRRUUU","UUUUULDDLLUULDDLDDDR"}:new[]{"DRRURRUUUL","RRRULULLLD","DDDLDLLLU"};
            int[][] budgets=difficulty==Difficulty.Hard?new[]{new[]{6,2,2,5},new[]{7,3,7,5},new[]{2,7,6,7}}:new[]{new[]{5,4,2,1},new[]{3,3,4,2},new[]{1,1,5,4}};
            string[] rows=(difficulty==Difficulty.Hard?hard[id]:normal[id]).Split('/');int size=rows.Length;var open=new bool[size*size];var start=Vector2Int.zero;
            for(int row=0;row<size;row++)for(int x=0;x<size;x++){char c=rows[row][x];int y=size-1-row;open[y*size+x]=c!='#';if(c=='S')start=new Vector2Int(x,y);}
            var board=MakeBoard(size,open,Route(start,routes[id]));board.budgets=budgets[id];board.alternateProof=Route(start,alternatives[id]);board.fallback=id;return board;
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FourPaws.PCS.StepPuzzle
{
    // 데모 입력과 표시만 소유한다. 본편 캐릭터·물리·네트워크에는 연결하지 않는다.
    public sealed class PCS_StepDemoController : MonoBehaviour
    {
        [Header("퍼즐과 저장된 자기 화면 참조")]
        public PCS_StepPuzzle puzzle = new PCS_StepPuzzle();
        [Tooltip("최대 36개 타일을 저장해 재사용합니다. 기존 9개 참조를 보존합니다.")] public PCS_StepTileView[] tiles;
        public RectTransform actor;
        public Text progress;
        public Text status;
        public Text result;
        public Button begin;
        public Button restart;
        public Button cancel;
        public Text budgets;
        public Text modeAndGoal;
        public Text routeHint;
        public Button newMaze;
        public Button difficulty;
        [Header("새 미로 설정")]
        public PCS_StepPuzzle.Difficulty selectedDifficulty = PCS_StepPuzzle.Difficulty.Normal;
        [Tooltip("고정 재현 모드에서 사용하는 시드입니다. R은 생성기를 호출하지 않습니다.")] public int seed = 20261015;
        [Tooltip("켜면 같은 seed와 난도로 재현합니다. 끄면 일반 진입은 새 판입니다.")] public bool useFixedSeed;
        [Tooltip("켜면 아래 수동 보드를 사용합니다. 검증 경로도 실제 규칙을 통과해야 합니다.")] public bool useManualBoard;
        public PCS_StepPuzzle.Board manualBoard = new PCS_StepPuzzle.Board();
        [Range(1, 32)] public int candidateLimit = 32;
        [Range(1, 4000)] public int expansionLimit = 4000;
        public long AttemptId { get; private set; }
        public int Deliveries { get; private set; }
        public bool IsOpen { get; private set; }
        public PCS_StepPuzzle.Board CurrentBoard { get; private set; }
        private readonly List<string> recent = new List<string>();
        private bool released;
        private bool appliedManual;private bool firstEntry=true;
        private readonly System.Random entryRandom=new System.Random(System.Guid.NewGuid().GetHashCode());
        private PCS_StepPuzzle.Difficulty appliedDifficulty;
        private int commandFrame = -1;
        private void Awake()
        {
            if(!useFixedSeed&&!useManualBoard)seed=entryRandom.Next();bool valid = SelectBoard(false); puzzle.Cancel();
            if (valid) status.text = "시작 버튼을 누르세요. 이동 전에 목표와 방향별 예산을 확인하세요.";
            result.text = "발판 결과: 대기"; Show();
        }
        private void OnEnable()
        {
            begin.onClick.AddListener(Enter); restart.onClick.AddListener(Begin); cancel.onClick.AddListener(Cancel);
            if (newMaze) newMaze.onClick.AddListener(NewMaze);
            if (difficulty) difficulty.onClick.AddListener(ChangeDifficulty);
        }
        private void OnDisable()
        {
            begin.onClick.RemoveListener(Enter); restart.onClick.RemoveListener(Begin); cancel.onClick.RemoveListener(Cancel);
            if (newMaze) newMaze.onClick.RemoveListener(NewMaze);
            if (difficulty) difficulty.onClick.RemoveListener(ChangeDifficulty);
            puzzle.Cancel(); AttemptId++; IsOpen = released = false; Deliveries = 0; commandFrame = Time.frameCount;
            status.text = "시작 버튼을 누르세요."; result.text = "발판 결과: 대기"; Show();
        }
        private bool SelectBoard(bool nextSeed)
        {
            if (nextSeed && !useManualBoard && selectedDifficulty != PCS_StepPuzzle.Difficulty.Practice) seed = unchecked(seed + 104729);
            if (useManualBoard)
            {
                if (!PCS_StepPuzzle.VerifyProof(manualBoard, out string error)) { CurrentBoard = null; status.text = "설정 오류: " + error; return false; }
                CurrentBoard = manualBoard.Copy();
            }
            else CurrentBoard = PCS_StepPuzzle.Generate(selectedDifficulty, seed, useFixedSeed?null:recent, candidateLimit, expansionLimit);
            appliedManual = useManualBoard; appliedDifficulty = selectedDifficulty;
            if (!CurrentBoard.practice) { recent.Add(PCS_StepPuzzle.Canonical(CurrentBoard)); while (recent.Count > 16) recent.RemoveAt(0); }
            puzzle.Begin(CurrentBoard); puzzle.Cancel(); return true;
        }
        public void Enter()
        {
            if(firstEntry){firstEntry=false;Begin();return;}
            if(!useFixedSeed&&!useManualBoard&&selectedDifficulty!=PCS_StepPuzzle.Difficulty.Practice){seed=entryRandom.Next();if(!SelectBoard(false)){Show();return;}}else if(useFixedSeed&&!useManualBoard&&!SelectBoard(false)){Show();return;}Begin();
        }
        public void NewMaze()
        {
            firstEntry=false;Cancel(); if (SelectBoard(true)) Begin(); else Show();
        }
        public void ChangeDifficulty()
        {
            useManualBoard = false;
            selectedDifficulty = selectedDifficulty == PCS_StepPuzzle.Difficulty.Normal ? PCS_StepPuzzle.Difficulty.Hard : selectedDifficulty == PCS_StepPuzzle.Difficulty.Hard ? PCS_StepPuzzle.Difficulty.Practice : PCS_StepPuzzle.Difficulty.Normal;
            NewMaze();
        }
        public void Begin()
        {
            firstEntry=false;
            if (CurrentBoard == null && !SelectBoard(false)) { Show(); return; }
            AttemptId++; Deliveries = 0; commandFrame = Time.frameCount; released = false;
            IsOpen = puzzle.Begin(CurrentBoard);
            status.text = !IsOpen ? "설정 오류: " + puzzle.Error : CurrentBoard.practice ? "연습: 모든 칸을 한 번씩 밟으세요.\nWASD / 방향키를 새로 누를 때 한 칸 이동합니다." : "방향별 남은 횟수 안에서 목표에 도착하세요.\n재방문 금지! 모든 칸을 밟을 필요는 없습니다.";
            result.text = $"발판 결과: 진행 중 | 시도 {AttemptId} | 결과 전달 0회"; Show();
        }
        public void Cancel()
        {
            if (!IsOpen) return;
            puzzle.Cancel(); AttemptId++; Deliveries = 0; IsOpen = released = false; commandFrame = Time.frameCount;
            status.text = "취소했습니다. 같은 판을 다시 시작할 수 있습니다."; result.text = "발판 결과: 대기"; Show();
        }
        public bool DeliverResult(long attempt)
        {
            if (!IsOpen || attempt != AttemptId || Deliveries != 0 || (!puzzle.Succeeded && !puzzle.Failed)) return false;
            Deliveries = 1;
            status.text = puzzle.Succeeded ? (puzzle.Current.practice ? "성공! 아홉 칸을 모두 방문했습니다." : "성공! 이동 예산 안에서 목표에 도착했습니다.") : "실패: " + puzzle.FailureReason;
            status.text += "\nR: 현재 판 다시 도전 / 아래 버튼: 새 판 또는 설정 적용";
            result.text = $"발판 결과: {(puzzle.Succeeded ? "성공" : "실패")} | 시도 {AttemptId} | 결과 전달 1회"; return true;
        }
        private void LateUpdate()
        {
            if (commandFrame == Time.frameCount) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { Cancel(); return; }
            if (Input.GetKeyDown(KeyCode.R)) { Begin(); return; }
            if (!released) { released = !AnyKeyHeld() && !Input.GetMouseButton(0); return; }
            if (!IsOpen || !puzzle.Running) return;
            var direction = PCS_StepPuzzle.ResolveDirections(Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow), Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow), Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow), Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow));
            if (direction == Vector2Int.zero) return;
            var move = puzzle.Move(direction.x, direction.y);
            if (move == PCS_StepPuzzle.MoveResult.Failed || move == PCS_StepPuzzle.MoveResult.Succeeded) DeliverResult(AttemptId);
            else status.text = move == PCS_StepPuzzle.MoveResult.Outside ? "보드 경계입니다. 이동과 예산 소비가 없습니다." : move == PCS_StepPuzzle.MoveResult.Wall ? "벽입니다. 이동과 예산 소비가 없습니다." : move == PCS_StepPuzzle.MoveResult.BudgetEmpty ? "이 방향의 이동 횟수를 모두 사용했습니다." : "방문하지 않은 칸으로 이동하세요.\n키를 누르고 있어도 연속 이동하지 않습니다.";
            Show();
        }
        private static bool AnyKeyHeld() => Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.R) || Input.GetKey(KeyCode.Escape) || Input.GetKey(KeyCode.Space);
        public void RefreshView()=>Show();
        private void Show()
        {
            var board = CurrentBoard ?? puzzle.Current; int total = board.width * board.height; float spacing = 390f / Mathf.Max(board.width, board.height);
            for (int i = 0; i < tiles.Length; i++)
            {
                var tile = tiles[i]; bool visible = i < total; tile.gameObject.SetActive(visible); if (!visible) continue;
                tile.x = i % board.width; tile.y = i / board.width;
                var rect = (RectTransform)tile.transform; rect.anchoredPosition = new Vector2((tile.x - (board.width - 1) * 0.5f) * spacing, (tile.y - (board.height - 1) * 0.5f) * spacing); rect.sizeDelta = Vector2.one * (spacing - 6f);
                tile.Show(puzzle); tile.label.fontSize = board.width >= 6 ? 12 : board.width > 3 ? 14 : 17;
                tile.label.rectTransform.anchoredPosition = new Vector2(0, -spacing * 0.20f); tile.label.rectTransform.sizeDelta = new Vector2(spacing - 4f, spacing * 0.55f);
            }
            int index = puzzle.Y * board.width + puzzle.X; actor.gameObject.SetActive(index >= 0 && index < tiles.Length && CurrentBoard != null);
            if (actor.gameObject.activeSelf) { actor.anchoredPosition = ((RectTransform)tiles[index].transform).anchoredPosition + Vector2.up * spacing * 0.24f; actor.sizeDelta = Vector2.one * (spacing * 0.23f); }
            progress.text = board.practice ? $"활성화한 발판 {puzzle.Count} / 9" : $"방문한 칸 {puzzle.Count}개";
            if (budgets) budgets.text = board.practice ? "연습은 방향별 횟수 제한이 없습니다." : $"남은 이동 / 초기 예산\n오른쪽 {puzzle.Remaining(0)} / {board.budgets[0]}   위쪽 {puzzle.Remaining(1)} / {board.budgets[1]}\n왼쪽 {puzzle.Remaining(2)} / {board.budgets[2]}   아래쪽 {puzzle.Remaining(3)} / {board.budgets[3]}";
            if (modeAndGoal) modeAndGoal.text = board.practice ? "연습 3×3 · 모든 칸 방문" : $"{(appliedManual ? "수동" : appliedDifficulty == PCS_StepPuzzle.Difficulty.Hard ? "어려움" : "보통")} {board.width}×{board.height} · 목표 ({board.goal.x},{board.goal.y}) · 시드 {board.seed} {(useFixedSeed?"(고정 재현)":"(일반 새 판)")}";
            if (routeHint) routeHint.text = board.practice ? "연습 경로: 오른쪽 2회 → 위 → 왼쪽 2회\n→ 위 → 오른쪽 2회" : "시작 / 목표 / 벽 / 방문 표시를 확인하세요.\n남은 횟수가 있어도 목표 도착이면 성공합니다.";
            begin.interactable = !IsOpen && CurrentBoard != null; restart.interactable = CurrentBoard != null; cancel.interactable = IsOpen;
            if (difficulty) difficulty.GetComponentInChildren<Text>().text = "모드 변경: " + (selectedDifficulty == PCS_StepPuzzle.Difficulty.Normal ? "보통" : selectedDifficulty == PCS_StepPuzzle.Difficulty.Hard ? "어려움" : "연습");
            if (newMaze) newMaze.GetComponentInChildren<Text>().text = useManualBoard ? "수동 설정 적용" : selectedDifficulty == PCS_StepPuzzle.Difficulty.Practice ? "연습 다시 시작" : "새 미로";
        }
    }
}

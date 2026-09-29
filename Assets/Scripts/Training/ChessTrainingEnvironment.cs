using System;
using ChessBot.Agents;
using ChessBot.Chess.Application;
using ChessBot.Chess.Core;
using Unity.MLAgents;
using Unity.MLAgents.Policies;
using UnityEngine;

namespace ChessBot.Training
{
    [DefaultExecutionOrder(-100)]
    public sealed class ChessTrainingEnvironment : MonoBehaviour, IChessAgentHost
    {
        [SerializeField] private ChessAgent _white;
        [SerializeField] private ChessAgent _black;
        [SerializeField, Min(0), Tooltip("Reward per material point: pawn 1, knight/bishop 3, rook 5, queen 9. Opponent receives the negative amount.")] private float _captureReward = 0.05f;
        [Header("Terminal rewards")]
        [SerializeField, Min(0), Tooltip("Maximum win reward at 0 plies.")] private float _winReward = 1f;
        [SerializeField, Min(0), Tooltip("Minimum win reward at or beyond Win Reward Decay Plies.")] private float _minimumWinReward = 0.5f;
        [SerializeField, Min(0), Tooltip("Plies to reach minimum reward. 0 uses Maximum Plies. One move by either color is one ply.")] private int _winRewardDecayPlies;
        [SerializeField, Min(0), Tooltip("Loser receives negative winner reward times this multiplier.")] private float _lossRewardMultiplier = 1f;
        [SerializeField] private float _whiteDrawReward = -0.2f;
        [SerializeField] private float _blackDrawReward = 0.2f;
        [SerializeField, Min(2)] private int _maximumPlies = 512;
        [SerializeField] private bool _allowHeuristicPreview;
        [SerializeField] private bool _recordGames = true;
        [Header("Curriculum")]
        [SerializeField] private bool _useCurriculum = true;
        [SerializeField, Range(0, 5)] private int _previewLesson;
        [SerializeField] private ChessLesson[] _lessons = ChessCurriculum.Defaults();
        private ChessLesson _lesson;
        private int _episodeLimit, _materialGain, _taskOutcome = 2, _colorSequence;
        private PieceColor _focusColor;
        private string _initialFen = BoardState.InitialFen;
        public int ActiveLesson { get; private set; } = -1;
        public int TaskEpisodes { get; private set; }
        public void ConfigureCurriculum(bool enabled, int previewLesson = 0)
        { _useCurriculum = enabled; _previewLesson = previewLesson; }
        private ChessGameController _game;
        private bool _whiteReady, _blackReady, _resetPending = true, _endPending, _interrupted;
        private bool _warnedMissingTrainer, _stopped;
        private int? _invalidActionToReport;
        private int _invalidActionFrame;
        private TrainingGameRecorder _recorder;
        public ChessGameController Game => _game ?? (_game = new ChessGameController());
        public int MaximumPlies => _lesson != null ? _episodeLimit : _maximumPlies;
        public int CompletedGames { get; private set; }
        public int InterruptedGames { get; private set; }
        public int RejectedActions { get; private set; }
        public ChessAgent White => _white;
        public ChessAgent Black => _black;
        public event Action<GameResult, bool, float, float> EpisodeCompleted;

        // Called by the scene authoring tool while the root is inactive; serialized references survive builds.
        public void Configure(ChessAgent white, ChessAgent black, bool heuristicPreview = false, int maximumPlies = 512, bool recordGames = true)
        {
            _white = white; _black = black; _allowHeuristicPreview = heuristicPreview;
            _maximumPlies = maximumPlies; _recordGames = recordGames;
            white.Bind(this, PieceColor.White); black.Bind(this, PieceColor.Black);
        }
        private void Awake()
        {
            if (_white == null || _black == null || _white == _black || _maximumPlies < 2 || !ValidReward(_captureReward) || !ValidReward(_winReward) || !ValidReward(_minimumWinReward) || _minimumWinReward > _winReward ||
                _winRewardDecayPlies < 0 || !ValidReward(_lossRewardMultiplier) || !Finite(_whiteDrawReward) || !Finite(_blackDrawReward))
                throw new InvalidOperationException("Training environment references or reward settings are invalid.");
            if (_useCurriculum)
            {
                if (_lessons == null || _lessons.Length != 6) throw new InvalidOperationException("Curriculum requires six lessons.");
                foreach (var lesson in _lessons)
                {
                    if (lesson == null || lesson.MaximumPlies < 1 || !ValidReward(lesson.SuccessReward) || !ValidReward(lesson.FailurePenalty) || lesson.Positions == null || lesson.Positions.Length == 0)
                        throw new InvalidOperationException("Invalid curriculum lesson settings.");
                    foreach (var fen in lesson.Positions)
                    {
                        var board = BoardState.FromFen(fen);
                        if (ChessRules.IsInCheck(board, ChessRules.Opposite(board.SideToMove)) || new ChessGameController(board).Result.IsFinished)
                            throw new InvalidOperationException("Curriculum position is illegal or already terminal: " + fen);
                    }
                }
            }
            _colorSequence = UnityEngine.Random.Range(0, 2);
            _game = new ChessGameController();
            if (_recordGames) _recorder = new TrainingGameRecorder();
        }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool ValidReward(float value) => value >= 0 && Finite(value);
        private void OnEnable()
        {
            Academy.Instance.AgentPreStep += PreStep;
            Academy.Instance.OnEnvironmentReset += OnAcademyReset;
        }
        private void OnDisable()
        {
            if (!Academy.IsInitialized) return;
            Academy.Instance.AgentPreStep -= PreStep;
            Academy.Instance.OnEnvironmentReset -= OnAcademyReset;
        }
        private void OnAcademyReset()
        {
            // Academy will call both Agent.OnEpisodeBegin after this callback.
            _resetPending = true; _endPending = false; _whiteReady = _blackReady = false;
        }
        public void AgentReady(ChessAgent agent)
        {
            if (agent == _white) _whiteReady = true;
            else if (agent == _black) _blackReady = true;
        }
        private void PreStep(int academyStep)
        {
            if (_stopped)
            {
                // RemotePolicy clears actions to zero when Python sends Quit. Report only if
                // the Academy continues in a later frame (Quit is deferred until frame end),
                // while rejecting the action immediately.
                if (_invalidActionToReport.HasValue && Time.frameCount > _invalidActionFrame)
                {
                    Debug.LogError($"ChessV1 received illegal action {_invalidActionToReport.Value} at turn {Game.TurnVersion}. Training stopped; inspect the action mask/schema.", this);
                    _invalidActionToReport = null;
                }
                return;
            }
            if (!Academy.Instance.IsCommunicatorOn && !_allowHeuristicPreview)
            {
                if (!_warnedMissingTrainer)
                {
                    Debug.LogWarning("Chess training is paused: start mlagents-learn, then enter Play. Enable Allow Heuristic Preview only for an untrained random preview.", this);
                    _warnedMissingTrainer = true;
                }
                return;
            }
            if (_endPending) { FinishEpisode(); return; }
            if (_resetPending)
            {
                if (!_whiteReady || !_blackReady) return;
                ResetEpisode(); _resetPending = false; _interrupted = false;
            }
            if (Game.Result.IsFinished) { _endPending = true; return; }
            if (_white.DecisionPending || _black.DecisionPending) return;
            (Game.Board.SideToMove == PieceColor.White ? _white : _black).RequestTurn();
        }
        private void ResetEpisode()
        {
            // Sample the global lesson only at a coordinated episode boundary.
            ActiveLesson = _useCurriculum ? Mathf.Clamp(Mathf.RoundToInt(Academy.Instance.EnvironmentParameters.GetWithDefault("curriculum_stage", _previewLesson)), 0, 5) : -1;
            _lesson = ActiveLesson < 0 ? null : _lessons[ActiveLesson];
            _episodeLimit = _lesson == null ? _maximumPlies : _lesson.MaximumPlies;
            _initialFen = _lesson == null ? BoardState.InitialFen : _lesson.Positions[UnityEngine.Random.Range(0, _lesson.Positions.Length)];
            if (_lesson != null && ActiveLesson < 5 && (_colorSequence++ % 2) != 0) _initialFen = ChessCurriculum.SwapColors(_initialFen);
            var board = BoardState.FromFen(_initialFen); _focusColor = board.SideToMove;
            _materialGain = 0; _taskOutcome = 2;
            Game.Reset(board);
        }
        public void Submit(ChessAgent agent, int action)
        {
            if (_stopped || _resetPending || _endPending || (agent != _white && agent != _black)) return;
            // Reject late responses before consuming the current decision; never substitute a random move.
            if (agent.RequestedGameId != Game.GameId || agent.RequestedTurn != Game.TurnVersion || agent.Color != Game.Board.SideToMove) return;
            if (!agent.TryConsume(action, out var command)) { FailAction(action); return; }
            var before = Game.Board;
            bool accepted = command.IsDrawClaim
                ? Game.ClaimDraw(agent.Color, agent.RequestedGameId, agent.RequestedTurn, command.Move)
                : command.Move.HasValue && Game.SubmitMove(agent.Color, agent.RequestedGameId, agent.RequestedTurn, command.Move.Value);
            if (!accepted) { FailAction(action); return; }
            float captureReward = command.IsDrawClaim ? 0f : ChessRewardPolicy.CaptureReward(before, command.Move.Value, _captureReward);
            if (captureReward > 0f)
            {
                agent.AddReward(captureReward);
                (agent == _white ? _black : _white).AddReward(-captureReward);
            }
            var captured = command.IsDrawClaim ? default(Piece) : ChessRewardPolicy.CapturedPiece(before, command.Move.Value);
            _materialGain += ChessRewardPolicy.MaterialValue(captured.Type) * (agent.Color == _focusColor ? 1 : -1);
            if (_lesson != null && _lesson.Ending != CurriculumEnding.FullGame && !Game.Result.IsFinished)
                _taskOutcome = ChessCurriculum.TaskOutcome(_lesson, Game.TurnVersion, !captured.IsEmpty, _materialGain);
            // Full-game time limits interrupt; finite puzzle horizons are task terminals.
            _interrupted = !Game.Result.IsFinished && _taskOutcome == 2 && Game.TurnVersion >= MaximumPlies;
            _endPending = Game.Result.IsFinished || _interrupted || _taskOutcome != 2;
        }
        private void FailAction(int action)
        {
            RejectedActions++; _stopped = true;
            _invalidActionToReport = action; _invalidActionFrame = Time.frameCount;
        }
        private void FinishEpisode()
        {
            if (!_interrupted && Game.Result.Winner.HasValue)
            {
                var winner = Game.Result.Winner.Value == PieceColor.White ? _white : _black;
                float reward = ChessRewardPolicy.WinReward(Game.TurnVersion,
                    _winRewardDecayPlies == 0 ? MaximumPlies : _winRewardDecayPlies, _winReward, _minimumWinReward);
                winner.AddReward(reward); (winner == _white ? _black : _white).AddReward(-reward * _lossRewardMultiplier);
            }
            else if (!_interrupted && Game.Result.IsFinished)
            {
                _white.AddReward(_whiteDrawReward); _black.AddReward(_blackDrawReward);
            }
            if (!Game.Result.IsFinished && !_interrupted && _lesson != null && _taskOutcome != 2)
            {
                float reward = _taskOutcome > 0 ? _lesson.SuccessReward : _taskOutcome < 0 ? -_lesson.FailurePenalty : 0;
                var focus = _focusColor == PieceColor.White ? _white : _black;
                focus.AddReward(reward); (focus == _white ? _black : _white).AddReward(-reward);
                TaskEpisodes++;
            }
            if (_lesson != null)
            {
                bool success = Game.Result.IsFinished ? Game.Result.Winner == _focusColor : _taskOutcome == 1;
                Academy.Instance.StatsRecorder.Add($"Curriculum/Lesson{ActiveLesson}/Success", success ? 1 : 0);
                Academy.Instance.StatsRecorder.Add("Curriculum/Stage", ActiveLesson);
            }
            float whiteReward = _white.GetCumulativeReward(), blackReward = _black.GetCumulativeReward();
            _recorder?.Write(Game, _interrupted, whiteReward, blackReward, _initialFen, ActiveLesson, _focusColor, _taskOutcome);
            if (_interrupted) InterruptedGames++; else if (Game.Result.IsFinished) CompletedGames++;
            Academy.Instance.StatsRecorder.Add("Chess/Plies", Game.TurnVersion);
            Academy.Instance.StatsRecorder.Add("Chess/Interrupted", _interrupted ? 1 : 0);
            Academy.Instance.StatsRecorder.Add("Chess/WhiteReward", whiteReward);
            Academy.Instance.StatsRecorder.Add("Chess/BlackReward", blackReward);
            EpisodeCompleted?.Invoke(Game.Result, _interrupted, whiteReward, blackReward);
            _whiteReady = _blackReady = false;
            // End both on the final board, then reset only at a later Academy step after callbacks finish.
            if (_interrupted) { _white.EpisodeInterrupted(); _black.EpisodeInterrupted(); }
            else { _white.EndEpisode(); _black.EndEpisode(); }
            _endPending = false; _resetPending = true;
        }
    }
}

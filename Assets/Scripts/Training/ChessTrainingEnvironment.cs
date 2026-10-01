using System;
using System.Collections.Generic;
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
        [SerializeField, Tooltip("PgnTraining uses bundled train PGNs. InspectorFen uses Lessons/Positions.")] private CurriculumPositionSource _positionSource = CurriculumPositionSource.PgnTraining;
        private ChessCurriculumDataset.Sample _sample;
        private bool _sampleMirrored;
        public string SourceGame => _sample?.SourceGame;
        public int SourceStartPly => _sample?.StartPly ?? 0;
        public void ConfigurePositionSource(CurriculumPositionSource source) => _positionSource = source;
        [SerializeField, Range(0, 5)] private int _previewLesson;
        [SerializeField, Range(0, 2), Tooltip("Stage 0: free capture, safe capture, favorable exchange. Python overrides this at episode boundaries.")] private int _previewCaptureDifficulty;
        public int ActiveCaptureDifficulty { get; private set; } = -1;
        [SerializeField, Range(0f, 1f), Tooltip("In capture levels 1 and 2, sample a previous level this fraction of episodes to retain earlier skills.")] private float _captureReviewProbability = 0.2f;
        [SerializeField, Range(0f, 1f), Tooltip("Stage 5: probability of starting from the standard board. Otherwise use a real opening after 3 or 4 moves per side.")] private float _standardStartProbability = 0.2f;
        public int SampledCaptureDifficulty => ActiveLesson == 0 ? (_sample?.Difficulty ?? ActiveCaptureDifficulty) : -1;
        [SerializeField] private ChessLesson[] _lessons = ChessCurriculum.Defaults();
        [Header("Early lesson repetition (stages 0-2 only)")]
        [SerializeField, Min(0), Tooltip("Penalty to the moving agent on revisiting an episode position. 0 disables it.")] private float _repetitionPenalty = 0.005f;
        [SerializeField, Min(2)] private int _repetitionPenaltyFromOccurrence = 2;
        [SerializeField, Min(0)] private float _maximumRepetitionPenaltyPerAgent = 0.05f;
        private readonly Dictionary<string, int> _episodePositions = new Dictionary<string, int>();
        private float _whiteRepetitionPenalty, _blackRepetitionPenalty;
        public int RepeatedPositions { get; private set; }
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
                _winRewardDecayPlies < 0 || !ValidReward(_lossRewardMultiplier) || !Finite(_whiteDrawReward) || !Finite(_blackDrawReward) || !ValidReward(_repetitionPenalty) || !ValidReward(_maximumRepetitionPenaltyPerAgent) || _repetitionPenaltyFromOccurrence < 2 || !Finite(_captureReviewProbability) || _captureReviewProbability < 0 || _captureReviewProbability > 1 || !Finite(_standardStartProbability) || _standardStartProbability < 0 || _standardStartProbability > 1)
                throw new InvalidOperationException("Training environment references or reward settings are invalid.");
            if (_useCurriculum)
            {
                if (_lessons == null || _lessons.Length != 6) throw new InvalidOperationException("Curriculum requires six lessons.");
                foreach (var lesson in _lessons)
                {
                    if (lesson == null || lesson.MaximumPlies < 1 || !ValidReward(lesson.SuccessReward) || !ValidReward(lesson.FailurePenalty) || (_positionSource == CurriculumPositionSource.InspectorFen && (lesson.Positions == null || lesson.Positions.Length == 0)))
                        throw new InvalidOperationException("Invalid curriculum lesson settings.");
                    if (_positionSource == CurriculumPositionSource.InspectorFen) foreach (var fen in lesson.Positions)
                    {
                        var board = BoardState.FromFen(fen);
                        if (ChessRules.IsInCheck(board, ChessRules.Opposite(board.SideToMove)) || new ChessGameController(board).Result.IsFinished)
                            throw new InvalidOperationException("Curriculum position is illegal or already terminal: " + fen);
                    }
                }
            }
            if (_useCurriculum && _positionSource == CurriculumPositionSource.PgnTraining) ChessCurriculumDataset.LoadBundled();
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
            ActiveCaptureDifficulty = ActiveLesson == 0 ? Mathf.Clamp(Mathf.RoundToInt(Academy.Instance.EnvironmentParameters.GetWithDefault("capture_difficulty", _previewCaptureDifficulty)), 0, 2) : -1;
            _lesson = ActiveLesson < 0 ? null : _lessons[ActiveLesson];
            _episodeLimit = _lesson == null ? _maximumPlies : (_lesson.Ending == CurriculumEnding.FirstMoveFavorableCapture ? 1 : _lesson.MaximumPlies);
            _sample = null;
            _sampleMirrored = _lesson != null && ActiveLesson < 5 && (_colorSequence++ % 2) != 0;
            if (_lesson != null && _positionSource == CurriculumPositionSource.PgnTraining)
            {
                var data = ChessCurriculumDataset.LoadBundled();
                int difficulty = ActiveCaptureDifficulty;
                if (ActiveLesson == 0 && difficulty > 0 && UnityEngine.Random.value < _captureReviewProbability)
                    difficulty = UnityEngine.Random.Range(0, difficulty);
                if (ActiveLesson == 5)
                {
                    bool standard = UnityEngine.Random.value < _standardStartProbability;
                    _sample = data.FullGameSample(standard, UnityEngine.Random.Range(0, data.FullGameCount(standard)));
                }
                else _sample = data.Get(ActiveLesson, difficulty, UnityEngine.Random.Range(0, data.Count(ActiveLesson, difficulty)));
                _sample.Apply(Game, _sampleMirrored);
            }
            else
            {
                var fen = _lesson == null ? BoardState.InitialFen : _lesson.Positions[UnityEngine.Random.Range(0, _lesson.Positions.Length)];
                Game.Reset(BoardState.FromFen(_sampleMirrored ? ChessCurriculum.SwapColors(fen) : fen));
            }
            _initialFen = Game.Board.ToFen(); _focusColor = Game.Board.SideToMove;
            _materialGain = 0; _taskOutcome = 2;
            _episodePositions.Clear(); _episodePositions[ChessRules.RepetitionKey(Game.Board)] = 1;
            RepeatedPositions = 0; _whiteRepetitionPenalty = _blackRepetitionPenalty = 0;
        }
        public void Submit(ChessAgent agent, int action)
        {
            if (_stopped || _resetPending || _endPending || (agent != _white && agent != _black)) return;
            // Reject late responses before consuming the current decision; never substitute a random move.
            if (agent.RequestedGameId != Game.GameId || agent.RequestedTurn != Game.TurnVersion || agent.Color != Game.Board.SideToMove) return;
            if (!agent.TryConsume(action, out var command)) { FailAction(action); return; }
            var before = Game.Board;
            bool favorable = _lesson != null && _lesson.Ending == CurriculumEnding.FirstMoveFavorableCapture &&
                !command.IsDrawClaim && command.Move.HasValue && ChessTacticalAssessment.FavorableCapture(before, command.Move.Value);
            bool accepted = command.IsDrawClaim
                ? Game.ClaimDraw(agent.Color, agent.RequestedGameId, agent.RequestedTurn, command.Move)
                : command.Move.HasValue && Game.SubmitMove(agent.Color, agent.RequestedGameId, agent.RequestedTurn, command.Move.Value);
            if (!accepted) { FailAction(action); return; }
            float captureReward = command.IsDrawClaim ? 0f : ChessRewardPolicy.CaptureReward(before, command.Move.Value, _captureReward);
            // A one-move puzzle cannot expose a later recapture: do not reward a failed sacrifice.
            if (_lesson != null && _lesson.Ending == CurriculumEnding.FirstMoveFavorableCapture && !favorable) captureReward = 0;
            if (captureReward > 0f)
            {
                agent.AddReward(captureReward);
                (agent == _white ? _black : _white).AddReward(-captureReward);
            }
            var captured = command.IsDrawClaim ? default(Piece) : ChessRewardPolicy.CapturedPiece(before, command.Move.Value);
            _materialGain += ChessRewardPolicy.MaterialValue(captured.Type) * (agent.Color == _focusColor ? 1 : -1);
            if (_lesson != null && _lesson.Ending != CurriculumEnding.FullGame && !Game.Result.IsFinished)
                _taskOutcome = ChessCurriculum.TaskOutcome(_lesson, Game.TurnVersion, !captured.IsEmpty, _materialGain, favorable);
            if (!command.IsDrawClaim) ApplyRepetitionPenalty(agent);
            // Full-game time limits interrupt; finite puzzle horizons are task terminals.
            _interrupted = !Game.Result.IsFinished && _taskOutcome == 2 && Game.TurnVersion >= MaximumPlies;
            _endPending = Game.Result.IsFinished || _interrupted || _taskOutcome != 2;
        }
        private void ApplyRepetitionPenalty(ChessAgent agent)
        {
            string key = ChessRules.RepetitionKey(Game.Board);
            _episodePositions.TryGetValue(key, out int count); _episodePositions[key] = ++count;
            if (count < 2) return;
            RepeatedPositions++;
            if (ActiveLesson < 0 || ActiveLesson > 2 || Game.Result.IsFinished || count < _repetitionPenaltyFromOccurrence) return;
            float spent = agent == _white ? _whiteRepetitionPenalty : _blackRepetitionPenalty;
            float penalty = Mathf.Min(_repetitionPenalty, Mathf.Max(0, _maximumRepetitionPenaltyPerAgent - spent));
            agent.AddReward(-penalty);
            if (agent == _white) _whiteRepetitionPenalty += penalty; else _blackRepetitionPenalty += penalty;
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
                if (ActiveLesson == 0)
                {
                    Academy.Instance.StatsRecorder.Add($"Curriculum/Capture{SampledCaptureDifficulty}/Success", success ? 1 : 0);
                    Academy.Instance.StatsRecorder.Add("Curriculum/CaptureDifficulty", ActiveCaptureDifficulty);
                }
            }
            float whiteReward = _white.GetCumulativeReward(), blackReward = _black.GetCumulativeReward();
            _recorder?.Write(Game, _interrupted, whiteReward, blackReward, _initialFen, ActiveLesson, _focusColor, _taskOutcome, _sample, _sampleMirrored, ActiveCaptureDifficulty);
            if (_interrupted) InterruptedGames++; else if (Game.Result.IsFinished) CompletedGames++;
            Academy.Instance.StatsRecorder.Add("Chess/RepeatedPositions", RepeatedPositions);
            Academy.Instance.StatsRecorder.Add("Chess/RepetitionPenalty", _whiteRepetitionPenalty + _blackRepetitionPenalty);
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

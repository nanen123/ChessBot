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
        [SerializeField, Min(0)] private float _captureReward = 0.1f;
        [SerializeField, Min(0)] private float _winReward = 1f;
        [SerializeField, Min(2)] private int _maximumPlies = 512;
        [SerializeField] private bool _allowHeuristicPreview;
        [SerializeField] private bool _recordGames = true;
        private ChessGameController _game;
        private bool _whiteReady, _blackReady, _resetPending = true, _endPending, _interrupted;
        private bool _warnedMissingTrainer, _stopped;
        private int? _invalidActionToReport;
        private int _invalidActionFrame;
        private TrainingGameRecorder _recorder;
        public ChessGameController Game => _game ?? (_game = new ChessGameController());
        public int MaximumPlies => _maximumPlies;
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
            if (_white == null || _black == null || _white == _black || _maximumPlies < 2 || !ValidReward(_captureReward) || !ValidReward(_winReward))
                throw new InvalidOperationException("Training environment references or reward settings are invalid.");
            _game = new ChessGameController();
            if (_recordGames) _recorder = new TrainingGameRecorder();
        }
        private static bool ValidReward(float value) => value >= 0 && !float.IsNaN(value) && !float.IsInfinity(value);
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
                Game.Reset(); _resetPending = false; _interrupted = false;
            }
            if (Game.Result.IsFinished) { _endPending = true; return; }
            if (_white.DecisionPending || _black.DecisionPending) return;
            (Game.Board.SideToMove == PieceColor.White ? _white : _black).RequestTurn();
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
            if (!command.IsDrawClaim && !ChessRewardPolicy.CapturedPiece(before, command.Move.Value).IsEmpty)
            {
                agent.AddReward(_captureReward);
                (agent == _white ? _black : _white).AddReward(-_captureReward);
            }
            // A training time limit is an interruption, never a chess draw or a loss.
            _interrupted = !Game.Result.IsFinished && Game.TurnVersion >= _maximumPlies;
            _endPending = Game.Result.IsFinished || _interrupted;
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
                winner.AddReward(_winReward); (winner == _white ? _black : _white).AddReward(-_winReward);
            }
            float whiteReward = _white.GetCumulativeReward(), blackReward = _black.GetCumulativeReward();
            _recorder?.Write(Game, _interrupted, whiteReward, blackReward);
            if (_interrupted) InterruptedGames++; else CompletedGames++;
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

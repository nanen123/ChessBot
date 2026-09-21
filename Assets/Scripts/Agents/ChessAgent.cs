using System;
using System.Collections.Generic;
using ChessBot.Chess.Core;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Sensors;
using UnityEngine;

namespace ChessBot.Agents
{
    [RequireComponent(typeof(BehaviorParameters))]
    public sealed class ChessAgent : Agent
    {
        [SerializeField] private MonoBehaviour _hostSource;
        [SerializeField] private PieceColor _color;
        private IChessAgentHost _host;
        private Dictionary<int, ChessCommand> _legalActions = new Dictionary<int, ChessCommand>();
        private System.Random _random;
        public PieceColor Color => _color;
        public Guid RequestedGameId { get; private set; }
        public int RequestedTurn { get; private set; }
        public bool DecisionPending { get; private set; }
        public IReadOnlyDictionary<int, ChessCommand> LegalActions => _legalActions;
        public void Bind(MonoBehaviour host, PieceColor color)
        {
            if (!(host is IChessAgentHost)) throw new ArgumentException("Host must implement IChessAgentHost.");
            _hostSource = host; _color = color;
        }
        public override void Initialize()
        {
            _host = _hostSource as IChessAgentHost ?? throw new InvalidOperationException("ChessAgent requires a training host.");
            var behavior = GetComponent<BehaviorParameters>(); var spec = behavior.BrainParameters.ActionSpec;
            if (behavior.BehaviorName != ChessActionEncoder.BehaviorName || behavior.BrainParameters.VectorObservationSize != ChessObservationEncoder.ObservationCount ||
                spec.NumContinuousActions != 0 || spec.NumDiscreteActions != 1 || spec.BranchSizes[0] != ChessActionEncoder.ActionCount)
                throw new InvalidOperationException("ChessV1 observation/action schema does not match Behavior Parameters.");
            if (GetComponent<DecisionRequester>() != null) throw new InvalidOperationException("Turn-based chess must not use DecisionRequester.");
            MaxStep = 0; _random = new System.Random(12345 + (int)_color);
        }
        public override void OnEpisodeBegin()
        {
            DecisionPending = false; _legalActions.Clear();
            _host?.AgentReady(this); // Shared board reset belongs exclusively to the host.
        }
        public void RequestTurn()
        {
            var game = _host.Game;
            if (DecisionPending || game.Result.IsFinished || game.Board.SideToMove != _color)
                throw new InvalidOperationException("Decision requested outside this Agent's turn.");
            _legalActions = ChessActionEncoder.LegalActions(game);
            if (_legalActions.Count == 0) throw new InvalidOperationException("A live decision must have a legal action.");
            RequestedGameId = game.GameId; RequestedTurn = game.TurnVersion; DecisionPending = true;
            RequestDecision();
        }
        public override void CollectObservations(VectorSensor sensor)
        {
            bool intendedClaim = false;
            foreach (int action in _legalActions.Keys) if (action >= ChessActionEncoder.MoveCount && action < ChessActionEncoder.ClaimCurrent) { intendedClaim = true; break; }
            // EndEpisode also collects observations: always read the final board, not the request snapshot.
            foreach (float value in ChessObservationEncoder.Encode(_host.Game, _color, _host.MaximumPlies, intendedClaim)) sensor.AddObservation(value);
        }
        public override void WriteDiscreteActionMask(IDiscreteActionMask actionMask)
        {
            if (!DecisionPending || _legalActions.Count == 0) throw new InvalidOperationException("Mask requested without a live chess decision.");
            for (int action = 0; action < ChessActionEncoder.ActionCount; action++)
                actionMask.SetActionEnabled(0, action, _legalActions.ContainsKey(action));
        }
        public override void OnActionReceived(ActionBuffers actions)
        {
            // Stale or repeated callbacks cannot become a move in a later turn/game.
            if (!DecisionPending) return;
            if (actions.DiscreteActions.Length != 1) throw new InvalidOperationException("Chess requires one discrete branch.");
            _host.Submit(this, actions.DiscreteActions[0]);
        }
        public bool TryConsume(int action, out ChessCommand command)
        {
            command = default;
            if (!DecisionPending || !_legalActions.TryGetValue(action, out command)) return false;
            DecisionPending = false; return true;
        }
        public override void Heuristic(in ActionBuffers actionsOut)
        {
            if (_legalActions.Count == 0) return;
            int choice = _random.Next(_legalActions.Count);
            foreach (int action in _legalActions.Keys) if (choice-- == 0) { var discrete = actionsOut.DiscreteActions; discrete[0] = action; return; }
        }
    }
}

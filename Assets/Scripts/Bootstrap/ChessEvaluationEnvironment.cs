using System;
using System.Linq;
using ChessBot.Agents;
using ChessBot.Chess.Application;
using ChessBot.Chess.Core;
using ChessBot.Chess.Presentation;
using Unity.InferenceEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace ChessBot.Bootstrap
{
    public enum ChessEvaluationMode { AgentVsAgent, HumanVsAgent }

    public sealed class ChessEvaluationEnvironment : MonoBehaviour, IChessAgentHost
    {
        [SerializeField] private ChessEvaluationMode _mode;
        [SerializeField] private ModelAsset _whiteModel;
        [SerializeField] private ModelAsset _blackModel;
        [SerializeField] private PieceColor _humanColor = PieceColor.White;
        [SerializeField, Min(0)] private float _secondsPerMove = 0.5f;
        [SerializeField, Min(2)] private int _maximumPlies = 512;
        [SerializeField] private bool _deterministic = true;
        private ChessAgent _white, _black;
        private BoardView _view;
        private bool _ready, _paused, _stopped, _resetPending, _ended;
        private Guid _gameId;
        private float _nextMove;
        public ChessGameController Game { get; private set; }
        public int MaximumPlies => _maximumPlies;
        public bool IsPaused => _paused;
        public bool IsStopped => _stopped;
        public int AgentMoves { get; private set; }

        private void Awake()
        {
            Game = new ChessGameController(); _gameId = Game.GameId;
            var art = Resources.Load<ChessArt>("ChessArt");
            if (art == null || !art.IsValid) { Debug.LogError("ChessArt is missing.", this); enabled = false; return; }
            var canvas = new GameObject("Evaluation Board", typeof(RectTransform)); canvas.transform.SetParent(transform, false);
            _view = canvas.AddComponent<BoardView>();
            _view.ConfigureSession(CanHumanInput, "CHESS / MODEL TEST", _mode == ChessEvaluationMode.AgentVsAgent ? "AGENT vs AGENT" : "HUMAN vs AGENT",
                _mode == ChessEvaluationMode.AgentVsAgent ? "Watch two trained models play.\nPause or start a new game at any time." : $"You play {_humanColor}.\nSelect a piece and a highlighted square.",
                _mode == ChessEvaluationMode.HumanVsAgent && _humanColor == PieceColor.Black, TogglePause);
            _view.Initialize(Game, art);
            if (EventSystem.current == null)
            {
                var events = new GameObject("Evaluation EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.transform.SetParent(transform, false);
            }
            Game.Changed += OnGameChanged;
            try
            {
                if (_maximumPlies < 2) throw new InvalidOperationException("Maximum Plies must be at least 2.");
                if (NeedsAgent(PieceColor.White)) ValidateModel(_whiteModel, "White");
                if (NeedsAgent(PieceColor.Black)) ValidateModel(_blackModel, "Black");
                if (NeedsAgent(PieceColor.White)) _white = CreateAgent(PieceColor.White, _whiteModel);
                if (NeedsAgent(PieceColor.Black)) _black = CreateAgent(PieceColor.Black, _blackModel);
                _ready = true; _nextMove = Time.unscaledTime + _secondsPerMove;
                _view.SetSaveMessage(ModelLabel());
            }
            catch (Exception error) { Stop("Model setup failed: " + error.Message); }
        }
        private bool NeedsAgent(PieceColor color) => _mode == ChessEvaluationMode.AgentVsAgent || color != _humanColor;
        public bool CanHumanInput() => _ready && !_paused && !_stopped && !_ended && Game.TurnVersion < _maximumPlies && _mode == ChessEvaluationMode.HumanVsAgent && Game.Board.SideToMove == _humanColor;
        public static void ValidateModel(ModelAsset asset, string side)
        {
            if (asset == null) throw new InvalidOperationException($"Assign the {side} ONNX model in Inspector before Play.");
            var model = ModelLoader.Load(asset);
            bool Input(string name, int size) => model.inputs.Any(i => i.name == name && i.shape.rank == 2 && i.shape.Get(1) == size);
            if (!Input("obs_0", ChessObservationEncoder.ObservationCount) || !Input("action_masks", ChessActionEncoder.ActionCount) || model.inputs.Count != 2 ||
                !model.outputs.Any(o => o.name == "discrete_actions") || !model.outputs.Any(o => o.name == "deterministic_discrete_actions"))
                throw new InvalidOperationException($"{side} model must use ChessV1: 844 observations, 8577 masked actions, no recurrent inputs.");
        }
        private ChessAgent CreateAgent(PieceColor color, ModelAsset model)
        {
            var go = new GameObject(color + " Inference Agent"); go.SetActive(false); go.transform.SetParent(transform, false);
            var behavior = go.AddComponent<BehaviorParameters>();
            behavior.BehaviorName = ChessActionEncoder.BehaviorName; behavior.TeamId = (int)color;
            behavior.BrainParameters.VectorObservationSize = ChessObservationEncoder.ObservationCount;
            behavior.BrainParameters.ActionSpec = ActionSpec.MakeDiscrete(ChessActionEncoder.ActionCount);
            behavior.Model = model; behavior.BehaviorType = BehaviorType.InferenceOnly;
            behavior.DeterministicInference = _deterministic;
            var agent = go.AddComponent<ChessAgent>(); agent.Bind(this, color); go.SetActive(true); return agent;
        }
        private void OnEnable() { Academy.Instance.AgentPreStep += PreStep; }
        private void OnDisable() { if (Academy.IsInitialized) Academy.Instance.AgentPreStep -= PreStep; }
        private void OnDestroy() { if (Game != null) Game.Changed -= OnGameChanged; }
        public void AgentReady(ChessAgent agent) { }
        private void OnGameChanged()
        {
            if (_gameId != Game.GameId)
            {
                _gameId = Game.GameId; _resetPending = true; _ended = false; AgentMoves = 0;
                _nextMove = Time.unscaledTime + _secondsPerMove;
                if (!_stopped) _view.SetSaveMessage(ModelLabel());
            }
        }
        public void TogglePause()
        {
            _paused = !_paused; _view.SetSaveMessage(_paused ? "Paused" : ModelLabel());
        }
        private string ModelLabel() => $"White: {(!NeedsAgent(PieceColor.White) ? "Human" : _whiteModel == null ? "Missing model" : _whiteModel.name)}\nBlack: {(!NeedsAgent(PieceColor.Black) ? "Human" : _blackModel == null ? "Missing model" : _blackModel.name)}";
        private void PreStep(int step)
        {
            if (!_ready || _stopped) return;
            if (_resetPending) { EndAgents(true); _resetPending = false; }
            if (!_ended && (Game.Result.IsFinished || Game.TurnVersion >= _maximumPlies))
            {
                _ended = true; EndAgents(!Game.Result.IsFinished);
                if (!Game.Result.IsFinished) _view.SetSaveMessage("Move limit reached. Test stopped without a chess result. Start a new game.");
                _view.RefreshSession();
            }
            if (_paused || _ended || Time.unscaledTime < _nextMove) return;
            var agent = Game.Board.SideToMove == PieceColor.White ? _white : _black;
            if (agent != null && !agent.DecisionPending) agent.RequestTurn();
        }
        private void EndAgents(bool interrupted)
        {
            foreach (var agent in new[] { _white, _black }) if (agent != null)
            { if (interrupted) agent.EpisodeInterrupted(); else agent.EndEpisode(); }
        }
        public void Submit(ChessAgent agent, int action)
        {
            if (_stopped || _ended || _resetPending || (agent != _white && agent != _black) || agent.RequestedGameId != Game.GameId || agent.RequestedTurn != Game.TurnVersion || agent.Color != Game.Board.SideToMove) return;
            if (!agent.TryConsume(action, out var command)) { Stop("Model returned an illegal action. Check the model schema."); return; }
            bool accepted = command.IsDrawClaim ? Game.ClaimDraw(agent.Color, Game.GameId, Game.TurnVersion, command.Move)
                : command.Move.HasValue && Game.SubmitMove(agent.Color, Game.GameId, Game.TurnVersion, command.Move.Value);
            if (!accepted) { Stop("Model move was rejected by chess rules."); return; }
            AgentMoves++; _nextMove = Time.unscaledTime + _secondsPerMove;
        }
        private void Stop(string message)
        {
            _stopped = true; _view?.SetSaveMessage(message); Debug.LogError(message, this);
        }
    }
}

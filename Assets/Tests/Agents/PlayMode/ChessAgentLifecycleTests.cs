using System.Collections;
using System.Linq;
using ChessBot.Agents;
using ChessBot.Chess.Core;
using ChessBot.Training;
using NUnit.Framework;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using UnityEngine;
using UnityEngine.TestTools;

namespace ChessBot.Tests
{
    public sealed class ChessAgentLifecycleTests
    {
        private GameObject _root;
        private ChessTrainingEnvironment _environment;
        [SetUp] public void SetUp()
        {
            Academy.Instance.AutomaticSteppingEnabled = false;
            _root = new GameObject("Training test"); _root.SetActive(false);
            _environment = _root.AddComponent<ChessTrainingEnvironment>();
            var white = AddAgent("White", 0); var black = AddAgent("Black", 1);
            _environment.Configure(white, black, true, 8, false); _root.SetActive(true);
            Academy.Instance.EnvironmentStep(); // Initial reset and one legal white move.
        }
        private ChessAgent AddAgent(string name, int team)
        {
            var go = new GameObject(name); go.transform.SetParent(_root.transform);
            var behavior = go.AddComponent<BehaviorParameters>(); behavior.BehaviorName = ChessActionEncoder.BehaviorName;
            behavior.TeamId = team; behavior.BehaviorType = BehaviorType.HeuristicOnly;
            behavior.BrainParameters.VectorObservationSize = ChessObservationEncoder.ObservationCount;
            behavior.BrainParameters.ActionSpec = ActionSpec.MakeDiscrete(ChessActionEncoder.ActionCount);
            return go.AddComponent<ChessAgent>();
        }
        [TearDown] public void TearDown()
        {
            Object.DestroyImmediate(_root);
            if (Academy.IsInitialized) Academy.Instance.Dispose();
        }
        [UnityTest] public IEnumerator BothAgentsInterruptAndResetTogether()
        {
            int completions = 0;
            _environment.EpisodeCompleted += (result, interrupted, white, black) => { Assert.That(interrupted, Is.True); Assert.That(white + black, Is.EqualTo(0).Within(0.0001)); completions++; };
            for (int i = 0; i < 28; i++) Academy.Instance.EnvironmentStep();
            Assert.That(completions, Is.GreaterThanOrEqualTo(3));
            Assert.That(_environment.White.CompletedEpisodes, Is.EqualTo(_environment.Black.CompletedEpisodes));
            Assert.That(_environment.RejectedActions, Is.Zero); Assert.That(_environment.Game.TurnVersion, Is.LessThanOrEqualTo(8));
            yield return null;
        }
        [UnityTest] public IEnumerator MaskContainsExactlyLegalActionsAndCallbackCannotRepeat()
        {
            var agent = _environment.Black; agent.RequestTurn(); var mask = new RecordingMask(); agent.WriteDiscreteActionMask(mask);
            Assert.That(mask.Enabled.Count(value => value), Is.EqualTo(agent.LegalActions.Count));
            for (int i = 0; i < mask.Enabled.Length; i++) Assert.That(mask.Enabled[i], Is.EqualTo(agent.LegalActions.ContainsKey(i)));
            int action = agent.LegalActions.Keys.First(); var buffers = new ActionBuffers(new float[0], new[] { action });
            int before = _environment.Game.TurnVersion; agent.OnActionReceived(buffers); agent.OnActionReceived(buffers);
            Assert.That(_environment.Game.TurnVersion, Is.EqualTo(before + 1)); yield return null;
        }
        [UnityTest] public IEnumerator CaptureAndWinRewardsReachBothAgentsBeforeSingleReset()
        {
            _environment.Game.Reset(BoardState.FromFen("7k/6r1/5KQ1/8/8/8/8/8 w - - 0 1"));
            var id = _environment.Game.GameId; var agent = _environment.White; agent.RequestTurn();
            agent.OnActionReceived(new ActionBuffers(new float[0], new[] { ChessActionEncoder.Encode(Move.Parse("g6g7")) }));
            Assert.That(agent.GetCumulativeReward(), Is.EqualTo(0.1f).Within(0.0001));
            Assert.That(_environment.Black.GetCumulativeReward(), Is.EqualTo(-0.1f).Within(0.0001));
            int completions = 0;
            _environment.EpisodeCompleted += (result, interrupted, white, black) =>
            {
                completions++; Assert.That(interrupted, Is.False); Assert.That(result.Winner, Is.EqualTo(PieceColor.White));
                Assert.That(white, Is.EqualTo(1.1f).Within(0.0001)); Assert.That(black, Is.EqualTo(-1.1f).Within(0.0001));
            };
            Academy.Instance.EnvironmentStep(); Assert.That(_environment.Game.GameId, Is.EqualTo(id));
            Academy.Instance.EnvironmentStep(); Assert.That(_environment.Game.GameId, Is.Not.EqualTo(id));
            Assert.That(completions, Is.EqualTo(1)); Assert.That(agent.GetCumulativeReward(), Is.Zero);
            Assert.That(_environment.White.CompletedEpisodes, Is.EqualTo(_environment.Black.CompletedEpisodes)); yield return null;
        }
        [UnityTest] public IEnumerator InvalidActionStopsImmediatelyAndReportsIfAcademyContinues()
        {
            var agent = _environment.Black; agent.RequestTurn();
            int before = _environment.Game.TurnVersion;
            agent.OnActionReceived(new ActionBuffers(new float[0], new[] { 0 }));
            Assert.That(_environment.Game.TurnVersion, Is.EqualTo(before));
            Assert.That(_environment.RejectedActions, Is.EqualTo(1));
            Academy.Instance.EnvironmentStep(); // Multiple fixed steps can occur before Quit is processed.
            yield return null;
            LogAssert.Expect(LogType.Error, $"ChessV1 received illegal action 0 at turn {before}. Training stopped; inspect the action mask/schema.");
            Academy.Instance.EnvironmentStep();
            Assert.That(_environment.Game.TurnVersion, Is.EqualTo(before));
            yield return null;
        }
        private sealed class RecordingMask : IDiscreteActionMask
        {
            public readonly bool[] Enabled = new bool[ChessActionEncoder.ActionCount];
            public void SetActionEnabled(int branch, int index, bool enabled) { Assert.That(branch, Is.Zero); Enabled[index] = enabled; }
        }
    }
}

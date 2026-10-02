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
            _environment.ConfigureCurriculum(false); _environment.Configure(white, black, true, 8, false); _root.SetActive(true);
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
        [UnityTest] public IEnumerator GeneratedCaptureUsesNormalMasksRewardsAndCoordinatedReset()
        {
            _environment.ConfigureCurriculum(true, 0);
            _environment.ConfigureCaptureGeneration(1, 32);
            bool seen = false;
            for (int i = 0; i < 100; i++)
            {
                Academy.Instance.EnvironmentStep();
                if (!_environment.GeneratedPosition || _environment.ActiveLesson != 0) continue;
                seen = true;
                Assert.That(_environment.MaximumPlies, Is.EqualTo(1));
                Assert.That(_environment.Game.TurnVersion, Is.LessThanOrEqualTo(1));
                Assert.That(_environment.Game.Board.CastlingRights, Is.Zero);
            }
            Assert.That(seen, Is.True);
            Assert.That(_environment.TaskEpisodes, Is.GreaterThan(0));
            Assert.That(_environment.RejectedActions, Is.Zero);
            Assert.That(_environment.White.EpisodeBeginCount, Is.EqualTo(_environment.Black.EpisodeBeginCount));
            yield return null;
        }
        [Test] public void RepeatedIdleEpisodeDoesNotCarryRewardIntoNextDecision()
        {
            var agent = _environment.White;
            agent.EndEpisode();
            int begins = agent.EpisodeBeginCount;
            agent.AddReward(-0.5f); agent.EndEpisode();
            Assert.That(agent.EpisodeBeginCount, Is.EqualTo(begins + 1));
            Assert.That(agent.GetCumulativeReward(), Is.Zero);
            agent.AddReward(0.25f); agent.EndEpisode();
            Assert.That(agent.GetCumulativeReward(), Is.Zero);
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
            Assert.That(agent.GetCumulativeReward(), Is.EqualTo(0.25f).Within(0.0001));
            Assert.That(_environment.Black.GetCumulativeReward(), Is.EqualTo(-0.25f).Within(0.0001));
            int completions = 0;
            _environment.EpisodeCompleted += (result, interrupted, white, black) =>
            {
                completions++; Assert.That(interrupted, Is.False); Assert.That(result.Winner, Is.EqualTo(PieceColor.White));
                Assert.That(white, Is.EqualTo(1.1875f).Within(0.0001)); Assert.That(black, Is.EqualTo(-1.1875f).Within(0.0001));
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
        [UnityTest] public IEnumerator DrawRewardsAreAppliedOnceBeforeReset()
        {
            _environment.Game.Reset(BoardState.FromFen("7k/8/8/8/8/8/8/K7 w - - 0 1"));
            int completions = 0;
            _environment.EpisodeCompleted += (result, interrupted, white, black) =>
            {
                completions++; Assert.That(interrupted, Is.False); Assert.That(result.Winner.HasValue, Is.False);
                Assert.That(white, Is.EqualTo(-0.2f).Within(0.0001)); Assert.That(black, Is.EqualTo(0.2f).Within(0.0001));
            };
            Academy.Instance.EnvironmentStep(); Academy.Instance.EnvironmentStep();
            Assert.That(completions, Is.EqualTo(1));
            Academy.Instance.EnvironmentStep();
            Assert.That(completions, Is.EqualTo(1));
            Assert.That(_environment.White.GetCumulativeReward(), Is.Zero);
            Assert.That(_environment.Black.GetCumulativeReward(), Is.Zero);
            yield return null;
        }
        [UnityTest] public IEnumerator PlyLimitDoesNotAwardDrawRewards()
        {
            typeof(ChessTrainingEnvironment).GetField("_maximumPlies", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(_environment, 2);
            int completions = 0;
            _environment.EpisodeCompleted += (result, interrupted, white, black) =>
            {
                completions++; Assert.That(interrupted, Is.True);
                Assert.That(white, Is.Zero); Assert.That(black, Is.Zero);
            };
            Academy.Instance.EnvironmentStep(); Academy.Instance.EnvironmentStep();
            Assert.That(completions, Is.EqualTo(1)); yield return null;
        }
        private void StartLesson(int stage, ChessLesson[] lessons = null)
        {
            if (lessons != null) typeof(ChessTrainingEnvironment).GetField("_lessons", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(_environment, lessons);
            _environment.ConfigurePositionSource(CurriculumPositionSource.InspectorFen);
            _environment.ConfigureCurriculum(true, stage);
            typeof(ChessTrainingEnvironment).GetMethod("ResetEpisode", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(_environment, null);
        }
        [UnityTest] public IEnumerator CurriculumCaptureEndsTaskAndChangesLessonOnlyOnReset()
        {
            var lessons = ChessCurriculum.Defaults(); lessons[0].Positions = new[] { "7k/8/8/8/8/8/p7/R6K w - - 0 1" };
            StartLesson(0, lessons); var id = _environment.Game.GameId;
            var board = _environment.Game.Board; var focus = board.SideToMove;
            var agent = focus == PieceColor.White ? _environment.White : _environment.Black;
            var capture = _environment.Game.LegalMoves.First(m => !board[m.To].IsEmpty);
            int completions = 0;
            _environment.EpisodeCompleted += (result, interrupted, white, black) =>
            {
                completions++; Assert.That(result.IsFinished, Is.False); Assert.That(interrupted, Is.False);
                Assert.That(focus == PieceColor.White ? white : black, Is.EqualTo(0.15f).Within(0.0001));
                Assert.That(white + black, Is.Zero.Within(0.0001));
            };
            agent.RequestTurn(); agent.OnActionReceived(new ActionBuffers(new float[0], new[] { ChessActionEncoder.Encode(capture) }));
            _environment.ConfigureCurriculum(true, 1); Assert.That(_environment.ActiveLesson, Is.Zero);
            Academy.Instance.EnvironmentStep(); Assert.That(completions, Is.EqualTo(1));
            Assert.That(_environment.Game.GameId, Is.EqualTo(id)); Assert.That(_environment.CompletedGames, Is.Zero);
            Academy.Instance.EnvironmentStep(); Assert.That(_environment.ActiveLesson, Is.EqualTo(1));
            Assert.That(_environment.Game.GameId, Is.Not.EqualTo(id)); yield return null;
        }
        [UnityTest] public IEnumerator MateTaskFailureIsNotChessDraw()
        {
            var lessons = ChessCurriculum.Defaults(); lessons[2].Positions = new[] { lessons[2].Positions[1] }; lessons[2].MaximumPlies = 1;
            StartLesson(2, lessons); var board = _environment.Game.Board; var focus = board.SideToMove;
            var agent = focus == PieceColor.White ? _environment.White : _environment.Black;
            var move = _environment.Game.LegalMoves.First(m => { ChessRules.TryApply(board, m, out var next); return !new ChessBot.Chess.Application.ChessGameController(next).Result.IsFinished; });
            int completions = 0;
            _environment.EpisodeCompleted += (result, interrupted, white, black) =>
            {
                completions++; Assert.That(result.IsFinished, Is.False); Assert.That(interrupted, Is.False);
                Assert.That(focus == PieceColor.White ? white : black, Is.EqualTo(-0.1f).Within(0.0001));
                Assert.That(white + black, Is.Zero.Within(0.0001));
            };
            agent.RequestTurn(); agent.OnActionReceived(new ActionBuffers(new float[0], new[] { ChessActionEncoder.Encode(move) }));
            Academy.Instance.EnvironmentStep(); Assert.That(completions, Is.EqualTo(1)); yield return null;
        }
        [UnityTest] public IEnumerator CurriculumEndgameLimitInterruptsWithoutTaskRewards()
        {
            var lessons = ChessCurriculum.Defaults(); lessons[3].MaximumPlies = 2; StartLesson(3, lessons);
            int completions = 0;
            _environment.EpisodeCompleted += (result, interrupted, white, black) =>
            { completions++; Assert.That(interrupted, Is.True); Assert.That(result.IsFinished, Is.False); Assert.That(white, Is.Zero); Assert.That(black, Is.Zero); };
            Academy.Instance.EnvironmentStep(); Academy.Instance.EnvironmentStep(); Academy.Instance.EnvironmentStep();
            Assert.That(completions, Is.EqualTo(1)); yield return null;
        }
        [UnityTest] public IEnumerator PgnLessonsStartWithZeroEpisodePliesAndNoHistoricalRewards()
        {
            _environment.ConfigurePositionSource(CurriculumPositionSource.PgnTraining);
            for (int stage = 0; stage < 6; stage++)
            {
                _environment.ConfigureCurriculum(true, stage);
                typeof(ChessTrainingEnvironment).GetMethod("ResetEpisode", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(_environment, null);
                Assert.That(_environment.SourceGame, Does.StartWith("https://lichess.org/"));
                Assert.That(_environment.Game.TurnVersion, Is.Zero); Assert.That(_environment.Game.Moves, Is.Empty);
                Assert.That(_environment.White.GetCumulativeReward(), Is.Zero);
                Assert.That(_environment.Black.GetCumulativeReward(), Is.Zero);
                Assert.That(_environment.Game.Result.IsFinished, Is.False);
                if (stage < 5) Assert.That(_environment.SourceStartPly, Is.GreaterThan(0));
                else Assert.That(_environment.SourceStartPly, Is.EqualTo(0).Or.EqualTo(6).Or.EqualTo(8));
                Assert.That(ChessActionEncoder.LegalActions(_environment.Game).Count, Is.GreaterThan(0));
            }
            yield return null;
        }
        [TestCase(false)]
        [TestCase(true)]
        public void FirstMoveQuietAndPoisonedCaptureFailImmediately(bool poison)
        {
            var lessons=ChessCurriculum.Defaults();
            lessons[0].Positions=new[] { poison ? "7k/8/8/8/8/r7/p7/R6K w - - 0 1" : "7k/8/8/8/8/8/p7/R6K w - - 0 1" };
            typeof(ChessTrainingEnvironment).GetField("_colorSequence", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(_environment,0);
            StartLesson(0,lessons);
            var agent=_environment.White; agent.SetReward(0); _environment.Black.SetReward(0);
            int finished=0;
            System.Action<ChessBot.Chess.Application.GameResult,bool,float,float> check=(result,interrupted,white,black)=>
            { finished++; Assert.That(interrupted,Is.False); Assert.That(white,Is.EqualTo(-0.1f).Within(0.0001)); };
            _environment.EpisodeCompleted+=check;
            agent.RequestTurn(); agent.OnActionReceived(new ActionBuffers(new float[0],new[] {ChessActionEncoder.Encode(Move.Parse(poison ? "a1a2" : "h1g1"))}));
            Assert.That(_environment.Game.TurnVersion,Is.EqualTo(1));
            Academy.Instance.EnvironmentStep(); Assert.That(finished,Is.EqualTo(1));
            _environment.EpisodeCompleted-=check;
        }
        [TestCase(1,0.005f,0.05f,-0.01f,-0.015f)]
        [TestCase(1,0.005f,0.006f,-0.006f,-0.006f)]
        [TestCase(1,0f,0.05f,0f,0f)]
        [TestCase(3,0.005f,0.05f,0f,0f)]
        public void RepetitionPenaltyIsScopedCappedAndKeepsMovesLegal(int stage,float penalty,float cap,float white,float black)
        {
            var flags=System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(ChessTrainingEnvironment).GetField("_colorSequence",flags).SetValue(_environment,0);
            typeof(ChessTrainingEnvironment).GetField("_repetitionPenalty",flags).SetValue(_environment,penalty);
            typeof(ChessTrainingEnvironment).GetField("_maximumRepetitionPenaltyPerAgent",flags).SetValue(_environment,cap);
            var lessons=ChessCurriculum.Defaults(); lessons[stage].Positions=new[] {BoardState.InitialFen}; StartLesson(stage,lessons);
            _environment.White.SetReward(0); _environment.Black.SetReward(0);
            foreach (var uci in new[] {"g1f3","g8f6","f3g1","f6g8","g1f3","g8f6","f3g1","f6g8"})
            {
                var agent=_environment.Game.Board.SideToMove==PieceColor.White ? _environment.White : _environment.Black;
                agent.RequestTurn(); int action=ChessActionEncoder.Encode(Move.Parse(uci));
                Assert.That(agent.LegalActions.ContainsKey(action),Is.True);
                agent.OnActionReceived(new ActionBuffers(new float[0],new[] {action}));
            }
            Assert.That(_environment.RepeatedPositions,Is.EqualTo(5));
            Assert.That(_environment.White.GetCumulativeReward(),Is.EqualTo(white).Within(0.0001));
            Assert.That(_environment.Black.GetCumulativeReward(),Is.EqualTo(black).Within(0.0001));
            Assert.That(ChessActionEncoder.LegalActions(_environment.Game).ContainsKey(8576),Is.True);
            StartLesson(stage,lessons); Assert.That(_environment.RepeatedPositions,Is.Zero);
        }
        [TestCase(0f)]
        [TestCase(1f)]
        public void FullGameStartProbabilitySelectsOpeningOrStandard(float probability)
        {
            var flags=System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            _environment.ConfigurePositionSource(CurriculumPositionSource.PgnTraining);
            _environment.ConfigureCurriculum(true,5);
            typeof(ChessTrainingEnvironment).GetField("_standardStartProbability",flags).SetValue(_environment,probability);
            for(int i=0;i<12;i++)
            {
                typeof(ChessTrainingEnvironment).GetMethod("ResetEpisode",flags).Invoke(_environment,null);
                Assert.That(_environment.SourceStartPly,probability==1 ? Is.EqualTo(0) : Is.EqualTo(6).Or.EqualTo(8));
                Assert.That(_environment.Game.Board.SideToMove,Is.EqualTo(PieceColor.White));
                Assert.That(_environment.Game.TurnVersion,Is.Zero);
                Assert.That(_environment.Game.Moves,Is.Empty);
                Assert.That(_environment.White.GetCumulativeReward(),Is.Zero);
                Assert.That(_environment.Black.GetCumulativeReward(),Is.Zero);
            }
        }
        [TestCase(0f)]
        [TestCase(1f)]
        public void CaptureDifficultyChangesOnlyWhenEpisodeResets(float reviewProbability)
        {
            var flags=System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            _environment.ConfigurePositionSource(CurriculumPositionSource.PgnTraining);
            _environment.ConfigureCurriculum(true,0);
            typeof(ChessTrainingEnvironment).GetField("_captureReviewProbability",flags).SetValue(_environment,reviewProbability);
            for(int difficulty=0;difficulty<3;difficulty++)
            {
                int previous=_environment.ActiveCaptureDifficulty;
                typeof(ChessTrainingEnvironment).GetField("_previewCaptureDifficulty",flags).SetValue(_environment,difficulty);
                Assert.That(_environment.ActiveCaptureDifficulty,Is.EqualTo(previous));
                typeof(ChessTrainingEnvironment).GetMethod("ResetEpisode",flags).Invoke(_environment,null);
                Assert.That(_environment.ActiveCaptureDifficulty,Is.EqualTo(difficulty));
                Assert.That(_environment.Game.TurnVersion,Is.Zero);
                var sample=(ChessCurriculumDataset.Sample)typeof(ChessTrainingEnvironment).GetField("_sample",flags).GetValue(_environment);
                if (reviewProbability == 0 || difficulty == 0) Assert.That(sample.Difficulty,Is.EqualTo(difficulty));
                else Assert.That(sample.Difficulty,Is.LessThan(difficulty));
                Assert.That(_environment.SampledCaptureDifficulty,Is.EqualTo(sample.Difficulty));
            }
        }
        private sealed class RecordingMask : IDiscreteActionMask
        {
            public readonly bool[] Enabled = new bool[ChessActionEncoder.ActionCount];
            public void SetActionEnabled(int branch, int index, bool enabled) { Assert.That(branch, Is.Zero); Enabled[index] = enabled; }
        }
    }
}

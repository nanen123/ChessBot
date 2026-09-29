#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using ChessBot.Bootstrap;
using ChessBot.Chess.Core;
using NUnit.Framework;
using UnityEditor;
using Unity.MLAgents;
using UnityEngine;
using UnityEngine.TestTools;

namespace ChessBot.Chess.Tests
{
    public sealed class ChessEvaluationTests
    {
        private GameObject _root;
        private ChessEvaluationEnvironment Create(ChessEvaluationMode mode, PieceColor human = PieceColor.White)
        {
            _root = new GameObject("Evaluation test"); _root.SetActive(false);
            var host = _root.AddComponent<ChessEvaluationEnvironment>();
            var model = AssetDatabase.LoadMainAssetAtPath("Assets/Models/ChessV1_Test.onnx");
            Assert.That(model, Is.Not.Null);
            Set(host, "_mode", mode); Set(host, "_humanColor", human);
            Set(host, "_whiteModel", model); Set(host, "_blackModel", model); Set(host, "_secondsPerMove", 0f);
            _root.SetActive(true); return host;
        }
        private static void Set(object host, string name, object value) => host.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(host, value);
        [TearDown] public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
            if (Academy.IsInitialized) Academy.Instance.Dispose();
        }
        [UnityTest] public IEnumerator ModelsAlternatePauseAndReset()
        {
            var host = Create(ChessEvaluationMode.AgentVsAgent);
            float deadline = Time.realtimeSinceStartup + 30;
            while (host.AgentMoves < 4 && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(host.IsStopped, Is.False); Assert.That(host.AgentMoves, Is.GreaterThanOrEqualTo(4));
            Assert.That(host.CanHumanInput(), Is.False);
            host.TogglePause(); int ply = host.Game.TurnVersion;
            yield return new WaitForSecondsRealtime(0.15f);
            Assert.That(host.Game.TurnVersion, Is.EqualTo(ply));
            host.Game.Reset(); yield return null;
            Assert.That(host.Game.TurnVersion, Is.Zero);
            host.TogglePause(); deadline = Time.realtimeSinceStartup + 10;
            while (host.AgentMoves < 2 && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(host.AgentMoves, Is.GreaterThanOrEqualTo(2)); Assert.That(host.IsStopped, Is.False);
        }
        [UnityTest] public IEnumerator HumanWhiteWaitsThenReceivesModelReply()
        {
            var host = Create(ChessEvaluationMode.HumanVsAgent);
            yield return new WaitForSecondsRealtime(0.1f);
            Assert.That(host.Game.TurnVersion, Is.Zero); Assert.That(host.CanHumanInput(), Is.True);
            host.Game.SubmitMove(PieceColor.White, host.Game.GameId, host.Game.TurnVersion, Move.Parse("e2e4"));
            Assert.That(host.CanHumanInput(), Is.False);
            float deadline = Time.realtimeSinceStartup + 15;
            while (host.AgentMoves < 1 && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(host.Game.TurnVersion, Is.EqualTo(2)); Assert.That(host.CanHumanInput(), Is.True);
        }
        [UnityTest] public IEnumerator HumanBlackWaitsForWhiteModel()
        {
            var host = Create(ChessEvaluationMode.HumanVsAgent, PieceColor.Black);
            Assert.That(host.CanHumanInput(), Is.False);
            float deadline = Time.realtimeSinceStartup + 15;
            while (host.AgentMoves < 1 && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(host.Game.TurnVersion, Is.EqualTo(1)); Assert.That(host.CanHumanInput(), Is.True);
        }
    }
}
#endif

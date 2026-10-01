using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ChessBot.Agents;
using ChessBot.Chess.Application;
using ChessBot.Chess.Core;
using ChessBot.Training;
using NUnit.Framework;
using UnityEngine;
namespace ChessBot.Tests
{
    public class ChessEvaluationParityTests
    {
        [Serializable] private class Row { public string initial; public string[] history, moves; public float[] observations; public int[] actions; }
        [Serializable] private class Rows { public List<Row> cases = new List<Row>(); }
        [Test] public void ExportCanonicalEvaluationFixtures()
        {
            var rows = new Rows(); var data = ChessCurriculumDataset.LoadBundled();
            for (int stage = 0; stage < 6; stage++) for (int i = 0; i < 4; i++) foreach (bool mirrored in new[] {false,true})
            {
                var sample = data.Get(stage,i); var game = new ChessGameController(); sample.Apply(game,mirrored);
                string initial = mirrored ? ChessCurriculum.SwapColors(sample.HistoryInitialFen) : sample.HistoryInitialFen;
                Add(rows,game,initial,sample.HistoryMoves(mirrored));
                var move=game.LegalMoves[0]; game.SubmitMove(game.Board.SideToMove,game.GameId,game.TurnVersion,move);
                if (!game.Result.IsFinished) Add(rows,game,initial,sample.HistoryMoves(mirrored));
            }
            foreach (string fen in new[] { "7k/P7/8/8/8/8/8/7K w - - 99 1", "4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 1" })
                Add(rows,new ChessGameController(BoardState.FromFen(fen)),fen,new string[0]);
            var repetition = new ChessGameController(); var history = new[] {"g1f3","g8f6","f3g1","f6g8","g1f3","g8f6","f3g1","f6g8"};
            repetition.ResetFromHistory(BoardState.Initial(),history.Select(Move.Parse)); Add(rows,repetition,BoardState.InitialFen,history);
            string folder=Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath,"../output/training-validation")); Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder,"evaluation-parity.json"),JsonUtility.ToJson(rows));
            Assert.That(rows.cases.Count,Is.GreaterThan(90));
        }
        private static void Add(Rows rows,ChessGameController game,string initial,string[] history)
        {
            var legal=ChessActionEncoder.LegalActions(game);
            rows.cases.Add(new Row {initial=initial,history=history,moves=game.Moves.ToArray(),actions=legal.Keys.OrderBy(x=>x).ToArray(),observations=ChessObservationEncoder.Encode(game,game.Board.SideToMove,512,legal.Keys.Any(a=>a>=4288&&a<8576))});
        }
    }
}

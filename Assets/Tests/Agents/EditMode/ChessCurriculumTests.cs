using System.Linq;
using ChessBot.Training;
using ChessBot.Chess.Core;
using ChessBot.Chess.Application;
using NUnit.Framework;
namespace ChessBot.Tests
{
    public class ChessCurriculumTests
    {
        [Test] public void AllLessonsHaveLegalNonTerminalPositionsForBothColors()
        {
            var lessons = ChessCurriculum.Defaults(); Assert.That(lessons.Length, Is.EqualTo(6));
            foreach (var lesson in lessons) foreach (var fen in lesson.Positions)
            {
                Assert.That(ChessCurriculum.SwapColors(ChessCurriculum.SwapColors(fen)), Is.EqualTo(fen));
                foreach (var candidate in new[] {fen, ChessCurriculum.SwapColors(fen)})
                {
                    var board = BoardState.FromFen(candidate);
                    Assert.That(ChessRules.IsInCheck(board, ChessRules.Opposite(board.SideToMove)), Is.False, candidate);
                    Assert.That(new ChessGameController(board).Result.IsFinished, Is.False, candidate);
                }
            }
        }
        [Test] public void CaptureLessonsOfferCapturesAndMateLessonsHaveForcedSolutions()
        {
            var lessons = ChessCurriculum.Defaults();
            foreach (var fen in lessons[0].Positions)
            {
                var board = BoardState.FromFen(fen);
                Assert.That(ChessRules.LegalMoves(board).Any(m => ChessTacticalAssessment.FavorableCapture(board, m)), Is.True);
            }
            foreach (var fen in lessons[2].Positions)
            {
                Assert.That(ForceMate(BoardState.FromFen(fen), PieceColor.White, 3), Is.True, fen);
                Assert.That(ForceMate(BoardState.FromFen(ChessCurriculum.SwapColors(fen)), PieceColor.Black, 3), Is.True);
            }
            Assert.That(ForceMate(BoardState.FromFen(lessons[2].Positions[1]), PieceColor.White, 1), Is.False);
        }
        [TestCase("7k/8/8/8/8/8/p7/R6K w - - 0 1", "a1a2", true)]
        [TestCase("7k/8/8/8/8/r7/p7/R6K w - - 0 1", "a1a2", false)]
        [TestCase("7k/8/8/8/8/r7/q7/R6K w - - 0 1", "a1a2", true)]
        [TestCase("4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 1", "e5d6", true)]
        [TestCase("r6k/1P6/8/8/8/8/8/7K w - - 0 1", "b7a8q", true)]
        public void TacticalCaptureChecksRepliesAndSpecialMoves(string fen, string move, bool expected)
        {
            Assert.That(ChessTacticalAssessment.FavorableCapture(BoardState.FromFen(fen), Move.Parse(move)), Is.EqualTo(expected));
            var mirrored = move[0].ToString() + (9-(move[1]-'0')) + move[2] + (9-(move[3]-'0')) + move.Substring(4);
            Assert.That(ChessTacticalAssessment.FavorableCapture(BoardState.FromFen(ChessCurriculum.SwapColors(fen)), Move.Parse(mirrored)), Is.EqualTo(expected));
        }
        [Test] public void SampledCaptureDifficultiesHaveFavorableMoves()
        {
            var data=ChessCurriculumDataset.LoadBundled();
            for(int difficulty=0;difficulty<3;difficulty++) for (int i=0;i<16;i++) foreach (bool mirrored in new[] {false,true})
            {
                var game=new ChessGameController(); data.Get(0,difficulty,i*64).Apply(game,mirrored);
                Assert.That(game.LegalMoves.Any(m=>ChessTacticalAssessment.FavorableCapture(game.Board,m)), Is.True, game.Board.ToFen());
            }
        }
        private static bool ForceMate(BoardState board, PieceColor attacker, int remaining)
        {
            var game = new ChessGameController(board);
            if (game.Result.IsFinished) return game.Result.Winner == attacker;
            if (remaining == 0) return false;
            var results = game.LegalMoves.Select(m => { ChessRules.TryApply(board, m, out var next); return ForceMate(next, attacker, remaining - 1); });
            return board.SideToMove == attacker ? results.Any(x => x) : results.All(x => x);
        }
        [Test] public void TaskHorizonsDoNotEndBeforeRecaptureAndDistinguishNeutral()
        {
            var lessons = ChessCurriculum.Defaults();
            Assert.That(ChessCurriculum.TaskOutcome(lessons[0], 1, true, 1, true), Is.EqualTo(1));
            Assert.That(ChessCurriculum.TaskOutcome(lessons[0], 2, true, -1), Is.EqualTo(-1));
            Assert.That(ChessCurriculum.TaskOutcome(lessons[1], 1, true, 9), Is.EqualTo(2));
            Assert.That(ChessCurriculum.TaskOutcome(lessons[1], 12, false, 4), Is.EqualTo(1));
            Assert.That(ChessCurriculum.TaskOutcome(lessons[1], 12, false, 0), Is.Zero);
            Assert.That(ChessCurriculum.TaskOutcome(lessons[2], 3, false, 0), Is.EqualTo(-1));
        }
    }
}
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
                Assert.That(ChessRules.LegalMoves(board).Any(m => !board[m.To].IsEmpty), Is.True);
            }
            foreach (var fen in lessons[2].Positions)
            {
                Assert.That(ForceMate(BoardState.FromFen(fen), PieceColor.White, 3), Is.True, fen);
                Assert.That(ForceMate(BoardState.FromFen(ChessCurriculum.SwapColors(fen)), PieceColor.Black, 3), Is.True);
            }
            Assert.That(ForceMate(BoardState.FromFen(lessons[2].Positions[1]), PieceColor.White, 1), Is.False);
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
            Assert.That(ChessCurriculum.TaskOutcome(lessons[0], 1, true, 1), Is.EqualTo(1));
            Assert.That(ChessCurriculum.TaskOutcome(lessons[0], 2, true, -1), Is.EqualTo(-1));
            Assert.That(ChessCurriculum.TaskOutcome(lessons[1], 1, true, 9), Is.EqualTo(2));
            Assert.That(ChessCurriculum.TaskOutcome(lessons[1], 12, false, 4), Is.EqualTo(1));
            Assert.That(ChessCurriculum.TaskOutcome(lessons[1], 12, false, 0), Is.Zero);
            Assert.That(ChessCurriculum.TaskOutcome(lessons[2], 3, false, 0), Is.EqualTo(-1));
        }
    }
}
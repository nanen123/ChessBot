using System;
using System.Linq;
using ChessBot.Chess.Core;
using ChessBot.Chess.Application;
using ChessBot.Training;
using NUnit.Framework;
namespace ChessBot.Tests
{
    public sealed class ChessCaptureGeneratorTests
    {
        [Test] public void IdentityProtectsColorAndFileSymmetriesAndIgnoresHistory()
        {
            var a = BoardState.FromFen("7k/8/8/8/8/8/p7/R6K w - - 0 1");
            var b = BoardState.FromFen("k7/8/8/8/8/8/7p/K6R w - - 80 90");
            Assert.That(ChessCaptureGenerator.Identity(a), Is.EqualTo(ChessCaptureGenerator.Identity(b)));
            Assert.That(ChessCaptureGenerator.Identity(a), Is.EqualTo(ChessCaptureGenerator.Identity(BoardState.FromFen(ChessCurriculum.SwapColors(a.ToFen())))));
        }
        [TestCase(0)] [TestCase(1)] [TestCase(2)]
        public void GeneratedBoardsRetainTaskAndResetHistory(int difficulty)
        {
            var data = ChessCurriculumDataset.LoadBundled();
            var excluded = ChessCaptureExclusions.LoadBundled();
            var generator = new ChessCaptureGenerator(20261002 + difficulty, excluded);
            int generatedCount = 0;
            for (int i = 0; i < 32; i++)
            {
                var sample = data.Get(0, difficulty, i * 19);
                var source = new ChessGameController(); sample.Apply(source, i % 2 == 1);
                var before = source.Board.ToFen(); var target = (PieceType)Enum.Parse(typeof(PieceType), sample.Stratum, true);
                if (!generator.TryGenerate(source.Board, difficulty, target, 12, out var generated)) continue;
                generatedCount++;
                Assert.That(source.Board.ToFen(), Is.EqualTo(before));
                Assert.That(ChessCaptureGenerator.MatchesDifficulty(generated, difficulty, target), Is.True);
                Assert.That(excluded, Does.Not.Contain(ChessCaptureGenerator.Identity(generated)));
                Assert.That(generated.CastlingRights, Is.Zero); Assert.That(generated.EnPassantIndex, Is.EqualTo(-1)); Assert.That(generated.HalfmoveClock, Is.Zero);
                var episode = new ChessGameController(generated);
                Assert.That(episode.Result.IsFinished, Is.False); Assert.That(episode.TurnVersion, Is.Zero); Assert.That(episode.CurrentPositionOccurrences, Is.EqualTo(1));
                Assert.That(episode.LegalMoves.Any(m => source.Board[m.From].IsEmpty && generated[m.From].Type == target && ChessTacticalAssessment.FavorableCapture(generated,m)), Is.True);
            }
            Assert.That(generatedCount, Is.GreaterThan(0));
        }
        [Test] public void ExcludedCandidateIsRejectedAndAttemptsAreBounded()
        {
            var source = BoardState.FromFen("7k/8/8/8/8/8/p7/R6K w - - 0 1");
            var first = new ChessCaptureGenerator(42, new string[0]);
            Assert.That(first.TryGenerate(source,0,PieceType.Rook,64,out var candidate), Is.True);
            var blocked = new ChessCaptureGenerator(42,new[] {ChessCaptureGenerator.Identity(candidate)});
            Assert.That(blocked.TryGenerate(source,0,PieceType.Rook,first.LastAttempts,out var ignored), Is.False);
            Assert.That(blocked.LastAttempts, Is.EqualTo(first.LastAttempts)); Assert.That(ignored, Is.Null);
        }
        [Test] public void SameSeedReproducesGeneratedBoard()
        {
            var source=BoardState.FromFen("7k/8/8/8/8/8/p7/R6K w - - 0 1");
            var a=new ChessCaptureGenerator(71,new string[0]); var b=new ChessCaptureGenerator(71,new string[0]);
            Assert.That(a.TryGenerate(source,0,PieceType.Rook,64,out var x),Is.True);
            Assert.That(b.TryGenerate(source,0,PieceType.Rook,64,out var y),Is.True);
            Assert.That(x.ToFen(),Is.EqualTo(y.ToFen()));
        }
    }
}

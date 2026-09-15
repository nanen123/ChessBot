using System;
using System.Linq;
using ChessBot.Chess.Core;
using ChessBot.Chess.Application;
using ChessBot.Chess.Presentation;
using NUnit.Framework;

namespace ChessBot.Chess.Tests
{
    public sealed class ChessRulesTests
    {
        private static BoardState Play(BoardState board, params string[] moves)
        {
            foreach (string text in moves) { Assert.That(ChessRules.TryApply(board, Move.Parse(text), out var next), Is.True, text); board = next; }
            return board;
        }
        private static void Play(ChessGameController game, params string[] moves)
        {
            foreach (string text in moves)
                Assert.That(game.SubmitMove(game.Board.SideToMove, game.GameId, game.TurnVersion, Move.Parse(text)), Is.True, text);
        }
        private static long Perft(BoardState board, int depth)
        {
            if (depth == 0) return 1;
            var moves = ChessRules.LegalMoves(board);
            if (depth == 1) return moves.Count;
            long count = 0;
            foreach (var move in moves) { Assert.That(ChessRules.TryApply(board, move, out var next), Is.True); count += Perft(next, depth - 1); }
            return count;
        }
        [Test] public void InitialPositionAndCoordinates()
        {
            var board = BoardState.Initial();
            Assert.That(board.ToFen(), Is.EqualTo(BoardState.InitialFen));
            Assert.That(Enumerable.Range(0, 64).Count(i => !board[i].IsEmpty), Is.EqualTo(32));
            Assert.That(Square.Parse("a1").Index, Is.EqualTo(0)); Assert.That(Square.Parse("h8").Index, Is.EqualTo(63));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Square(8, 0));
        }
        [TestCase(1, 20L)] [TestCase(2, 400L)] [TestCase(3, 8902L)]
        public void InitialPerft(int depth, long expected) => Assert.That(Perft(BoardState.Initial(), depth), Is.EqualTo(expected));
        [TestCase(1, 48L)] [TestCase(2, 2039L)]
        public void KiwipetePerft(int depth, long expected)
        {
            var board = BoardState.FromFen("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1");
            Assert.That(Perft(board, depth), Is.EqualTo(expected));
        }
        [Test] public void IllegalMoveLeavesEverythingUnchanged()
        {
            var game = new ChessGameController(); var id = game.GameId;
            Assert.That(game.SubmitMove(PieceColor.White, id, 0, Move.Parse("e2e5")), Is.False);
            Assert.That(game.Board.ToFen(), Is.EqualTo(BoardState.InitialFen)); Assert.That(game.TurnVersion, Is.Zero); Assert.That(game.Moves.Count, Is.Zero);
        }
        [Test] public void WrongSideStaleTurnAndOldGameAreRejected()
        {
            var game = new ChessGameController(); var id = game.GameId;
            Assert.That(game.SubmitMove(PieceColor.Black, id, 0, Move.Parse("e2e4")), Is.False);
            Play(game, "e2e4");
            Assert.That(game.SubmitMove(PieceColor.Black, id, 0, Move.Parse("e7e5")), Is.False);
            game.Reset(); Assert.That(game.SubmitMove(PieceColor.White, id, 0, Move.Parse("e2e4")), Is.False);
        }
        [Test] public void PawnCaptureAndCounters()
        {
            var board = Play(BoardState.Initial(), "e2e4", "d7d5", "e4d5");
            Assert.That(board[Square.Parse("d5")].Color, Is.EqualTo(PieceColor.White));
            Assert.That(board.HalfmoveClock, Is.Zero); Assert.That(board.FullmoveNumber, Is.EqualTo(2)); Assert.That(board.SideToMove, Is.EqualTo(PieceColor.Black));
        }
        [TestCase("e1g1", "f1", "g1")] [TestCase("e1c1", "d1", "c1")]
        public void WhiteCastlesInOnePly(string move, string rook, string king)
        {
            var board = Play(BoardState.FromFen("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1"), move);
            Assert.That(board[Square.Parse(rook)].Type, Is.EqualTo(PieceType.Rook)); Assert.That(board[Square.Parse(king)].Type, Is.EqualTo(PieceType.King));
            Assert.That(board.CastlingRights & 3, Is.Zero); Assert.That(board.HalfmoveClock, Is.EqualTo(1));
        }
        [TestCase("e8g8", "f8")] [TestCase("e8c8", "d8")]
        public void BlackCastles(string move, string rook)
        {
            var board = Play(BoardState.FromFen("r3k2r/8/8/8/8/8/8/R3K2R b KQkq - 0 1"), move);
            Assert.That(board[Square.Parse(rook)].Type, Is.EqualTo(PieceType.Rook)); Assert.That(board.CastlingRights & 12, Is.Zero);
        }
        [TestCase("4kr2/8/8/8/8/8/8/4K2R w K - 0 1")]
        [TestCase("4r1k1/8/8/8/8/8/8/4K2R w K - 0 1")]
        [TestCase("4k1r1/8/8/8/8/8/8/4K2R w K - 0 1")]
        public void CannotCastleFromThroughOrIntoCheck(string fen) => Assert.That(ChessRules.TryApply(BoardState.FromFen(fen), Move.Parse("e1g1"), out _), Is.False);
        [Test] public void RookReturnDoesNotRestoreRights()
        {
            var board = Play(BoardState.FromFen("4k3/8/8/8/8/8/8/R3K2R w KQ - 0 1"), "h1h2", "e8e7", "h2h1");
            Assert.That(board.CastlingRights, Is.EqualTo(2));
        }
        [Test] public void CapturedCornerRookLosesRight()
        {
            var board = Play(BoardState.FromFen("r3k3/8/8/8/8/8/8/R3K3 b Qq - 0 1"), "a8a1");
            Assert.That(board.CastlingRights, Is.Zero);
        }
        [Test] public void EnPassantRemovesActualPawn()
        {
            var board = Play(BoardState.Initial(), "e2e4", "a7a6", "e4e5", "d7d5", "e5d6");
            Assert.That(board[Square.Parse("d5")].IsEmpty, Is.True); Assert.That(board[Square.Parse("d6")].Type, Is.EqualTo(PieceType.Pawn));
            Assert.That(board.EnPassantIndex, Is.EqualTo(-1));
        }
        [Test] public void EnPassantExpires()
        {
            var board = Play(BoardState.Initial(), "e2e4", "a7a6", "e4e5", "d7d5", "g1f3", "a6a5");
            Assert.That(ChessRules.TryApply(board, Move.Parse("e5d6"), out _), Is.False);
        }
        [Test] public void EnPassantCannotExposeKing()
        {
            var board = BoardState.FromFen("k7/8/8/r4pPK/8/8/8/8 w - f6 0 1");
            Assert.That(ChessRules.TryApply(board, Move.Parse("g5f6"), out _), Is.False);
        }
        [TestCase("a7a8q", PieceType.Queen)] [TestCase("a7a8r", PieceType.Rook)]
        [TestCase("a7a8b", PieceType.Bishop)] [TestCase("a7a8n", PieceType.Knight)]
        public void FourPromotionChoices(string move, PieceType type)
        {
            var board = Play(BoardState.FromFen("7k/P7/8/8/8/8/8/7K w - - 0 1"), move);
            Assert.That(board[Square.Parse("a8")].Type, Is.EqualTo(type));
        }
        [Test] public void PromotionWaitsForChoice()
        {
            var game = new ChessGameController(BoardState.FromFen("7k/P7/8/8/8/8/8/7K w - - 0 1")); var input = new HumanMoveInput(game);
            input.Click(48); input.Click(56); Assert.That(input.PromotionPending, Is.True); Assert.That(game.TurnVersion, Is.Zero);
            input.Promote(PieceType.Knight); Assert.That(game.TurnVersion, Is.EqualTo(1)); Assert.That(game.Board[56].Type, Is.EqualTo(PieceType.Knight));
        }
        [Test] public void PinnedPieceCannotExposeKing()
        {
            var board = BoardState.FromFen("k3r3/8/8/8/8/8/4R3/4K3 w - - 0 1");
            Assert.That(ChessRules.TryApply(board, Move.Parse("e2f2"), out _), Is.False);
        }
        [Test] public void KingsCannotBecomeAdjacent()
        {
            var board = BoardState.FromFen("8/8/8/8/8/4k3/8/4K3 w - - 0 1");
            Assert.That(ChessRules.TryApply(board, Move.Parse("e1e2"), out _), Is.False);
        }
        [Test] public void CheckmateEndsOnceAndRejectsFurtherInput()
        {
            var game = new ChessGameController(); int events = 0; game.Finished += record => events++;
            Play(game, "f2f3", "e7e5", "g2g4", "d8h4");
            Assert.That(game.Result.Reason, Is.EqualTo(EndReason.Checkmate)); Assert.That(game.Result.Winner, Is.EqualTo(PieceColor.Black));
            Assert.That(game.SubmitMove(PieceColor.White, game.GameId, game.TurnVersion, Move.Parse("a2a3")), Is.False); Assert.That(events, Is.EqualTo(1));
            game.Reset(); Assert.That(game.Result.IsFinished, Is.False); Assert.That(game.Moves.Count, Is.Zero);
        }
        [Test] public void StalemateIsNotCheckmate()
        { Assert.That(new ChessGameController(BoardState.FromFen("7k/5K2/6Q1/8/8/8/8/8 b - - 0 1")).Result.Reason, Is.EqualTo(EndReason.Stalemate)); }
        [TestCase("7k/8/8/8/8/8/8/K7 w - - 0 1", true)]
        [TestCase("7k/8/8/8/8/8/8/KN6 w - - 0 1", true)]
        [TestCase("7k/8/8/8/8/8/8/KNN5 w - - 0 1", false)]
        [TestCase("5b1k/8/8/8/8/8/8/K1B5 w - - 0 1", true)]
        public void ConservativeDeadPositions(string fen, bool dead) => Assert.That(ChessRules.IsKnownDeadPosition(BoardState.FromFen(fen)), Is.EqualTo(dead));
        [Test] public void ThreefoldIsClaimableAndFivefoldIsAutomatic()
        {
            var game = new ChessGameController(); string[] cycle = { "g1f3", "g8f6", "f3g1", "f6g8" };
            Play(game, cycle); Play(game, cycle);
            Assert.That(game.Result.IsFinished, Is.False); Assert.That(game.CanClaimDraw(), Is.True);
            Play(game, cycle); Play(game, cycle); Assert.That(game.Result.Reason, Is.EqualTo(EndReason.FivefoldRepetition));
        }
        [Test] public void ThreefoldClaimWithIntendedMoveDoesNotPlayMove()
        {
            var game = new ChessGameController(); Play(game, "g1f3", "g8f6", "f3g1", "f6g8", "g1f3", "g8f6", "f3g1");
            string before = game.Board.ToFen();
            Assert.That(game.CanClaimDraw(), Is.False);
            Assert.That(game.ClaimDraw(PieceColor.Black, game.GameId, game.TurnVersion, Move.Parse("f6g8")), Is.True);
            Assert.That(game.Result.Reason, Is.EqualTo(EndReason.ThreefoldClaim)); Assert.That(game.Board.ToFen(), Is.EqualTo(before));
        }
        [TestCase(99, false, false)] [TestCase(100, true, false)] [TestCase(149, true, false)] [TestCase(150, false, true)]
        public void MoveCountBoundaries(int halfmoves, bool claim, bool finished)
        {
            var game = new ChessGameController(BoardState.FromFen("7k/8/8/8/8/8/8/KR6 w - - " + halfmoves + " 1"));
            Assert.That(game.CanClaimDraw(), Is.EqualTo(claim)); Assert.That(game.Result.IsFinished, Is.EqualTo(finished));
        }
        [Test] public void IntendedFiftyMoveClaim()
        {
            var game = new ChessGameController(BoardState.FromFen("7k/8/8/8/8/8/8/KR6 w - - 99 1"));
            Assert.That(game.ClaimDraw(PieceColor.White, game.GameId, 0, Move.Parse("b1b2")), Is.True);
            Assert.That(game.Result.Reason, Is.EqualTo(EndReason.FiftyMoveClaim)); Assert.That(game.TurnVersion, Is.Zero);
        }
        [Test] public void InvalidClaimDoesNotChangePosition()
        {
            var game = new ChessGameController(); Assert.That(game.ClaimDraw(PieceColor.White, game.GameId, 0, Move.Parse("e2e4")), Is.False);
            Assert.That(game.Board.ToFen(), Is.EqualTo(BoardState.InitialFen));
        }
        [Test] public void CheckmateTakesPrecedenceOverSeventyFiveMoves()
        {
            var game = new ChessGameController(BoardState.FromFen("7k/5K2/6Q1/8/8/8/8/8 w - - 149 1")); Play(game, "g6g7");
            Assert.That(game.Result.Reason, Is.EqualTo(EndReason.Checkmate));
        }
        [Test] public void RepetitionKeyIncludesOnlyLegalEnPassantAndRights()
        {
            var pinned = BoardState.FromFen("k7/8/8/r4pPK/8/8/8/8 w - f6 0 1");
            Assert.That(ChessRules.RepetitionKey(pinned), Is.EqualTo(ChessRules.RepetitionKey(BoardState.FromFen(pinned.ToFen().Replace("f6", "-")))));
            var legal = BoardState.FromFen("k7/8/8/5pP1/8/8/8/7K w - f6 0 1");
            Assert.That(ChessRules.RepetitionKey(legal), Is.Not.EqualTo(ChessRules.RepetitionKey(BoardState.FromFen(legal.ToFen().Replace("f6", "-")))));
            Assert.That(ChessRules.RepetitionKey(BoardState.Initial()), Is.Not.EqualTo(ChessRules.RepetitionKey(BoardState.FromFen(BoardState.InitialFen.Replace("KQkq", "-")))));
        }
        [Test] public void RepeatedClickAppliesMoveOnce()
        {
            var game = new ChessGameController(); var input = new HumanMoveInput(game);
            input.Click(12); input.Click(28); input.Click(28);
            Assert.That(game.TurnVersion, Is.EqualTo(1)); Assert.That(game.Moves.Count, Is.EqualTo(1));
        }
    }
}

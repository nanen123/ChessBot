using System;
using System.Linq;
using ChessBot.Agents;
using ChessBot.Chess.Application;
using ChessBot.Chess.Core;
using NUnit.Framework;

namespace ChessBot.Tests
{
    public sealed class ChessEncodingTests
    {
        [TestCase(BoardState.InitialFen)]
        [TestCase("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1")]
        [TestCase("7k/P7/8/8/8/8/8/7K w - - 0 1")]
        [TestCase("7k/8/8/8/8/8/p7/7K b - - 0 1")]
        [TestCase("k7/8/8/5pP1/8/8/8/7K w - f6 0 1")]
        public void EveryLegalMoveHasUniqueStableAction(string fen)
        {
            var game = new ChessGameController(BoardState.FromFen(fen));
            var actions = ChessActionEncoder.LegalActions(game);
            Assert.That(actions.Count, Is.EqualTo(game.LegalMoves.Count));
            foreach (var pair in actions)
            {
                Assert.That(pair.Key, Is.InRange(0, ChessActionEncoder.ActionCount - 1));
                Assert.That(ChessActionEncoder.Decode(pair.Key).Move, Is.EqualTo(pair.Value.Move));
                Assert.That(pair.Value.IsDrawClaim, Is.False);
            }
        }
        [Test] public void EveryPromotionRoundTripsForBothColors()
        {
            foreach (int rank in new[] { 1, 6 }) for (int file = 0; file < 8; file++) for (int dx = -1; dx <= 1; dx++)
            {
                if (file + dx < 0 || file + dx > 7) continue;
                foreach (var type in new[] { PieceType.Queen, PieceType.Rook, PieceType.Bishop, PieceType.Knight })
                {
                    var move = new Move(new Square(file, rank), new Square(file + dx, rank == 6 ? 7 : 0), type);
                    int code = ChessActionEncoder.Encode(move);
                    Assert.That(ChessActionEncoder.Decode(code).Move, Is.EqualTo(move));
                    Assert.That(ChessActionEncoder.Decode(code + ChessActionEncoder.MoveCount).IsDrawClaim, Is.True);
                }
            }
        }
        [Test] public void DrawClaimsUseSeparateActionsAndDoNotMaskOrdinaryMoves()
        {
            var game = new ChessGameController(BoardState.FromFen("7k/8/8/8/8/8/8/KR6 w - - 99 1"));
            var actions = ChessActionEncoder.LegalActions(game); var move = Move.Parse("b1b2");
            Assert.That(actions.ContainsKey(ChessActionEncoder.Encode(move)), Is.True);
            Assert.That(actions.ContainsKey(ChessActionEncoder.Encode(move) + ChessActionEncoder.MoveCount), Is.True);
            Assert.That(actions.ContainsKey(ChessActionEncoder.ClaimCurrent), Is.False);
            game.Reset(BoardState.FromFen("7k/8/8/8/8/8/8/KR6 w - - 100 1"));
            Assert.That(ChessActionEncoder.LegalActions(game).ContainsKey(ChessActionEncoder.ClaimCurrent), Is.True);
        }
        [Test] public void CheckmateHasNoDecisionActions()
        {
            var game = new ChessGameController(BoardState.FromFen("7k/6Q1/5K2/8/8/8/8/8 b - - 0 1"));
            Assert.That(game.Result.IsFinished, Is.True); Assert.That(ChessActionEncoder.LegalActions(game), Is.Empty);
        }
        [Test] public void ObservationsAreFixedSizedFiniteAndColorRelative()
        {
            var game = new ChessGameController(); var white = ChessObservationEncoder.Encode(game, PieceColor.White, 512, false);
            var black = ChessObservationEncoder.Encode(game, PieceColor.Black, 512, false);
            Assert.That(white.Length, Is.EqualTo(844)); Assert.That(white.All(value => !float.IsNaN(value) && !float.IsInfinity(value)), Is.True);
            Assert.That(white.Take(768).Sum(), Is.EqualTo(32)); Assert.That(black.Take(768).Sum(), Is.EqualTo(32));
            Assert.That(white[3], Is.EqualTo(1)); Assert.That(black[9], Is.EqualTo(1)); // a1 own/opponent rook
            Assert.That(white[768], Is.EqualTo(1)); Assert.That(black[768], Is.Zero);
            Assert.That(white.Skip(774).Take(65).Sum(), Is.EqualTo(1));
        }
        [TestCase("k7/8/8/5pP1/8/8/8/7K w - f6 0 1", "g5f6", PieceType.Pawn)]
        [TestCase("7k/6r1/5KQ1/8/8/8/8/8 w - - 0 1", "g6g7", PieceType.Rook)]
        [TestCase("7k/P7/8/8/8/8/8/7K w - - 0 1", "a7a8q", PieceType.None)]
        [TestCase("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1", "e1g1", PieceType.None)]
        public void RewardsIdentifyOnlyActualCapturedPieces(string fen, string uci, PieceType expected)
        {
            var before = BoardState.FromFen(fen); var move = Move.Parse(uci);
            Assert.That(ChessRules.TryApply(before, move, out _), Is.True);
            Assert.That(ChessRewardPolicy.CapturedPiece(before, move).Type, Is.EqualTo(expected));
        }
        [Test] public void IllegalPinnedEnPassantIsNeverEnabled()
        {
            var game = new ChessGameController(BoardState.FromFen("k7/8/8/r4pPK/8/8/8/8 w - f6 0 1"));
            Assert.That(ChessActionEncoder.LegalActions(game).ContainsKey(ChessActionEncoder.Encode(Move.Parse("g5f6"))), Is.False);
        }
    }
}

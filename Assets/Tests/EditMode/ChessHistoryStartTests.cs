using System;
using System.Linq;
using ChessBot.Chess.Application;
using ChessBot.Chess.Core;
using NUnit.Framework;
namespace ChessBot.Tests
{
    public class ChessHistoryStartTests
    {
        [Test] public void HistoryRetainsRepetitionButStartsNewEpisodeAtZero()
        {
            var game = new ChessGameController(); var oldId = game.GameId;
            var sequence = new[] { "g1f3", "g8f6", "f3g1", "f6g8", "g1f3", "g8f6", "f3g1", "f6g8" };
            game.ResetFromHistory(BoardState.Initial(), sequence.Select(Move.Parse));
            Assert.That(game.GameId, Is.Not.EqualTo(oldId));
            Assert.That(game.TurnVersion, Is.Zero); Assert.That(game.Moves, Is.Empty);
            Assert.That(game.CurrentPositionOccurrences, Is.EqualTo(3)); Assert.That(game.CanClaimDraw(), Is.True);
            var copy = new ChessGameController(); copy.ResetFromPosition(game);
            Assert.That(copy.CanClaimDraw(), Is.True);
            copy.SubmitMove(copy.Board.SideToMove, copy.GameId, 0, Move.Parse("e2e4"));
            Assert.That(copy.TurnVersion, Is.EqualTo(1)); Assert.That(game.TurnVersion, Is.Zero);
            Assert.That(game.CanClaimDraw(), Is.True);
        }
        [Test] public void InvalidHistoryDoesNotReplaceTheCurrentGame()
        {
            var game = new ChessGameController(); var id = game.GameId;
            Assert.Throws<ArgumentException>(() => game.ResetFromHistory(BoardState.Initial(), new[] { Move.Parse("e2e5") }));
            Assert.That(game.GameId, Is.EqualTo(id)); Assert.That(game.Board.ToFen(), Is.EqualTo(BoardState.InitialFen));
        }
    }
}

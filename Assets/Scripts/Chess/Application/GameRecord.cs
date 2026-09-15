using System;
using System.Collections.Generic;

namespace ChessBot.Chess.Application
{
    public sealed class GameRecord
    {
        public Guid GameId { get; }
        public string InitialFen { get; }
        public IReadOnlyList<string> Moves { get; }
        public GameResult Result { get; }
        public string IntendedClaimMove { get; }
        public GameRecord(Guid gameId, string initialFen, IList<string> moves, GameResult result, string intendedClaimMove)
        {
            GameId = gameId; InitialFen = initialFen; Result = result; IntendedClaimMove = intendedClaimMove;
            Moves = new List<string>(moves).AsReadOnly();
        }
    }
}

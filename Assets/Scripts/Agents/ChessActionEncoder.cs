using System;
using System.Collections.Generic;
using ChessBot.Chess.Application;
using ChessBot.Chess.Core;

namespace ChessBot.Agents
{
    public readonly struct ChessCommand
    {
        public Move? Move { get; }
        public bool IsDrawClaim { get; }
        public ChessCommand(Move? move, bool isDrawClaim = false) { Move = move; IsDrawClaim = isDrawClaim; }
    }

    public static class ChessActionEncoder
    {
        public const string BehaviorName = "ChessV1";
        public const int SchemaVersion = 1;
        // Stable, absolute a1=0 coordinates. Never index into a variable-length legal move list.
        public const int MoveCount = 4096 + 2 * 8 * 3 * 4;
        public const int ClaimCurrent = MoveCount * 2;
        public const int ActionCount = ClaimCurrent + 1;
        private static readonly PieceType[] Promotions = { PieceType.Queen, PieceType.Rook, PieceType.Bishop, PieceType.Knight };

        public static int Encode(Move move)
        {
            if (move.Promotion == PieceType.None) return move.From.Index * 64 + move.To.Index;
            int promotion = Array.IndexOf(Promotions, move.Promotion);
            int side = move.From.Rank == 6 && move.To.Rank == 7 ? 0 : move.From.Rank == 1 && move.To.Rank == 0 ? 1 : -1;
            int offset = move.To.File - move.From.File + 1;
            if (promotion < 0 || side < 0 || offset < 0 || offset > 2) throw new ArgumentException("Invalid promotion geometry.");
            return 4096 + ((side * 8 + move.From.File) * 3 + offset) * 4 + promotion;
        }
        public static ChessCommand Decode(int action)
        {
            if (action < 0 || action >= ActionCount) throw new ArgumentOutOfRangeException(nameof(action));
            if (action == ClaimCurrent) return new ChessCommand(null, true);
            bool claim = action >= MoveCount;
            int index = action % MoveCount;
            if (index < 4096) return new ChessCommand(new Move(new Square(index / 64), new Square(index % 64)), claim);
            int code = index - 4096, promotion = code % 4; code /= 4;
            int offset = code % 3 - 1; code /= 3;
            int file = code % 8, side = code / 8;
            return new ChessCommand(new Move(new Square(file, side == 0 ? 6 : 1), new Square(file + offset, side == 0 ? 7 : 0), Promotions[promotion]), claim);
        }
        public static Dictionary<int, ChessCommand> LegalActions(ChessGameController game)
        {
            var result = new Dictionary<int, ChessCommand>();
            if (game.Result.IsFinished) return result;
            foreach (var move in game.LegalMoves)
            {
                int action = Encode(move);
                result.Add(action, new ChessCommand(move));
                if (game.CanClaimDraw(move)) result.Add(action + MoveCount, new ChessCommand(move, true));
            }
            if (game.CanClaimDraw()) result.Add(ClaimCurrent, new ChessCommand(null, true));
            return result;
        }
    }
}

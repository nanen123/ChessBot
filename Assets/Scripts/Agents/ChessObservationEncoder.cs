using System;
using ChessBot.Chess.Application;
using ChessBot.Chess.Core;

namespace ChessBot.Agents
{
    public static class ChessObservationEncoder
    {
        public const int ObservationCount = 844;
        // Board channels are own/opponent x six piece types. Coordinates stay absolute for both sides.
        // This v1 policy is partially observable: full repetition history is not encoded.
        public static float[] Encode(ChessGameController game, PieceColor self, int maximumPlies, bool claimWithMove)
        {
            var values = new float[ObservationCount]; var board = game.Board;
            for (int square = 0; square < 64; square++)
            {
                var piece = board[square];
                if (!piece.IsEmpty) values[square * 12 + (piece.Color == self ? 0 : 6) + (int)piece.Type - 1] = 1;
            }
            int cursor = 768;
            values[cursor++] = self == PieceColor.White ? 1 : 0;
            values[cursor++] = board.SideToMove == self ? 1 : 0;
            for (int bit = 0; bit < 4; bit++) values[cursor++] = (board.CastlingRights & (1 << bit)) != 0 ? 1 : 0;
            values[cursor + (board.EnPassantIndex < 0 ? 64 : board.EnPassantIndex)] = 1; cursor += 65;
            values[cursor++] = Math.Min(board.HalfmoveClock, 150) / 150f;
            values[cursor++] = Math.Min(game.CurrentPositionOccurrences, 5) / 5f;
            values[cursor++] = game.CanClaimDraw() ? 1 : 0;
            values[cursor++] = claimWithMove ? 1 : 0;
            values[cursor] = Math.Min(game.TurnVersion / (float)Math.Max(1, maximumPlies), 1);
            return values;
        }
    }
}

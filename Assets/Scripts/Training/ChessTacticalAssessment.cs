using System;
using ChessBot.Agents;
using ChessBot.Chess.Core;
namespace ChessBot.Training
{
    public static class ChessTacticalAssessment
    {
        // Immediate gain minus the strongest material reply; not a full engine evaluation.
        public static bool FavorableCapture(BoardState board, Move move)
        {
            if (ChessRewardPolicy.CapturedPiece(board, move).IsEmpty || !ChessRules.TryApply(board, move, out var next)) return false;
            int gain = Gain(board, move), replyGain = 0;
            foreach (var reply in ChessRules.LegalMoves(next))
            {
                ChessRules.TryApply(next, reply, out var after);
                if (ChessRules.IsInCheck(after, after.SideToMove) && ChessRules.LegalMoves(after).Count == 0) return false;
                replyGain = Math.Max(replyGain, Gain(next, reply));
            }
            return gain > replyGain;
        }
        private static int Gain(BoardState board, Move move) =>
            ChessRewardPolicy.MaterialValue(ChessRewardPolicy.CapturedPiece(board, move).Type) +
            (move.Promotion == PieceType.None ? 0 : ChessRewardPolicy.MaterialValue(move.Promotion) - 1);
    }
}

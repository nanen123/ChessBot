using ChessBot.Chess.Core;

namespace ChessBot.Agents
{
    public static class ChessRewardPolicy
    {
        public static float WinReward(int plies, int decayPlies, float maximum, float minimum)
        {
            float progress = System.Math.Max(0f, System.Math.Min(1f, (float)plies / System.Math.Max(1, decayPlies)));
            return maximum + (minimum - maximum) * progress;
        }

        // Material value of the captured piece, not captured minus attacking piece.
        // A recapture later applies its own negative reward to the original attacker.
        public static int MaterialValue(PieceType type) => type switch
        {
            PieceType.Pawn => 1,
            PieceType.Knight => 3,
            PieceType.Bishop => 3,
            PieceType.Rook => 5,
            PieceType.Queen => 9,
            _ => 0 // Kings are never captured; checkmate uses the terminal reward.
        };

        public static float CaptureReward(BoardState before, Move acceptedMove, float rewardPerPoint)
            => MaterialValue(CapturedPiece(before, acceptedMove).Type) * rewardPerPoint;

        // Call only after the controller accepted this move. En passant captures behind the destination.
        public static Piece CapturedPiece(BoardState before, Move acceptedMove)
        {
            var captured = before[acceptedMove.To]; var mover = before[acceptedMove.From];
            if (captured.IsEmpty && mover.Type == PieceType.Pawn && acceptedMove.To.Index == before.EnPassantIndex && acceptedMove.From.File != acceptedMove.To.File)
                captured = before[acceptedMove.To.Index + (mover.Color == PieceColor.White ? -8 : 8)];
            return captured;
        }
    }
}

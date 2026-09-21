using ChessBot.Chess.Core;

namespace ChessBot.Agents
{
    public static class ChessRewardPolicy
    {
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

using ChessBot.Chess.Core;

namespace ChessBot.Chess.Application
{
    public enum EndReason { None, Checkmate, Stalemate, KnownDeadPosition, ThreefoldClaim, FiftyMoveClaim, FivefoldRepetition, SeventyFiveMoves, Resignation }
    public readonly struct GameResult
    {
        public EndReason Reason { get; }
        public PieceColor? Winner { get; }
        public bool IsFinished => Reason != EndReason.None;
        public GameResult(EndReason reason, PieceColor? winner = null) { Reason = reason; Winner = winner; }
    }
}

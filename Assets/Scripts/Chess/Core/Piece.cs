namespace ChessBot.Chess.Core
{
    public enum PieceColor { White, Black }
    public enum PieceType { None, Pawn, Knight, Bishop, Rook, Queen, King }

    public readonly struct Piece
    {
        public PieceType Type { get; }
        public PieceColor Color { get; }
        public bool IsEmpty => Type == PieceType.None;
        public Piece(PieceType type, PieceColor color) { Type = type; Color = color; }
    }
}

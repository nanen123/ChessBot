using System;

namespace ChessBot.Chess.Core
{
    public readonly struct Move : IEquatable<Move>
    {
        public Square From { get; }
        public Square To { get; }
        public PieceType Promotion { get; }
        public Move(Square from, Square to, PieceType promotion = PieceType.None)
        { From = from; To = to; Promotion = promotion; }
        public static Move Parse(string text)
        {
            if (text == null || (text.Length != 4 && text.Length != 5)) throw new FormatException("Expected UCI move.");
            PieceType promotion = PieceType.None;
            if (text.Length == 5)
            {
                switch (text[4])
                {
                    case 'q': promotion = PieceType.Queen; break;
                    case 'r': promotion = PieceType.Rook; break;
                    case 'b': promotion = PieceType.Bishop; break;
                    case 'n': promotion = PieceType.Knight; break;
                    default: throw new FormatException("Invalid promotion.");
                }
            }
            return new Move(Square.Parse(text.Substring(0, 2)), Square.Parse(text.Substring(2, 2)), promotion);
        }
        public bool Equals(Move other) => From.Equals(other.From) && To.Equals(other.To) && Promotion == other.Promotion;
        public override bool Equals(object obj) => obj is Move move && Equals(move);
        public override int GetHashCode() => (From.Index * 64 + To.Index) * 7 + (int)Promotion;
        public override string ToString() => From.ToString() + To +
            (Promotion == PieceType.None ? "" : Promotion == PieceType.Knight ? "n" : Promotion.ToString().Substring(0, 1).ToLowerInvariant());
    }
}

using System;

namespace ChessBot.Chess.Core
{
    public readonly struct Square : IEquatable<Square>
    {
        // White's perspective: a1=0, h8=63; white pawns advance toward higher ranks.
        public int Index { get; }
        public int File => Index % 8;
        public int Rank => Index / 8;
        public Square(int index)
        {
            if (index < 0 || index >= 64) throw new ArgumentOutOfRangeException(nameof(index));
            Index = index;
        }
        public Square(int file, int rank) : this(CheckedIndex(file, rank)) { }
        private static int CheckedIndex(int file, int rank)
        {
            if (file < 0 || file > 7 || rank < 0 || rank > 7) throw new ArgumentOutOfRangeException();
            return rank * 8 + file;
        }
        public static Square Parse(string value)
        {
            if (value == null || value.Length != 2) throw new FormatException("Expected a square such as e4.");
            return new Square(value[0] - 'a', value[1] - '1');
        }
        public bool Equals(Square other) => Index == other.Index;
        public override bool Equals(object obj) => obj is Square square && Equals(square);
        public override int GetHashCode() => Index;
        public override string ToString() => $"{(char)('a' + File)}{Rank + 1}";
    }
}

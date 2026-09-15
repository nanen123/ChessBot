using System;
using System.Globalization;
using System.Text;

namespace ChessBot.Chess.Core
{
    public sealed class BoardState
    {
        public const string InitialFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
        private readonly Piece[] _squares = new Piece[64];
        public PieceColor SideToMove { get; internal set; }
        // Bits: white king/queen side, black king/queen side. Rights never return.
        public int CastlingRights { get; internal set; }
        public int EnPassantIndex { get; internal set; } = -1;
        public int HalfmoveClock { get; internal set; }
        public int FullmoveNumber { get; internal set; } = 1;
        public Piece this[int index] => _squares[index];
        public Piece this[Square square] => _squares[square.Index];
        internal void Set(int index, Piece piece) => _squares[index] = piece;
        public BoardState Copy()
        {
            var copy = new BoardState();
            Array.Copy(_squares, copy._squares, 64);
            copy.SideToMove = SideToMove; copy.CastlingRights = CastlingRights;
            copy.EnPassantIndex = EnPassantIndex; copy.HalfmoveClock = HalfmoveClock; copy.FullmoveNumber = FullmoveNumber;
            return copy;
        }
        public static BoardState Initial() => FromFen(InitialFen);
        public static BoardState FromFen(string fen)
        {
            var parts = fen.Split(' ');
            if (parts.Length != 6) throw new FormatException("FEN requires six fields.");
            var board = new BoardState();
            var ranks = parts[0].Split('/');
            if (ranks.Length != 8) throw new FormatException("FEN requires eight ranks.");
            int whiteKings = 0, blackKings = 0;
            for (int row = 0; row < 8; row++)
            {
                int file = 0;
                foreach (char c in ranks[row])
                {
                    if (c >= '1' && c <= '8') { file += c - '0'; continue; }
                    int type = " pnbrqk".IndexOf(char.ToLowerInvariant(c));
                    if (type < 1 || file >= 8) throw new FormatException("Invalid FEN piece/rank.");
                    var color = char.IsUpper(c) ? PieceColor.White : PieceColor.Black;
                    board.Set((7 - row) * 8 + file++, new Piece((PieceType)type, color));
                    if (type == 6) { if (color == PieceColor.White) whiteKings++; else blackKings++; }
                }
                if (file != 8) throw new FormatException("Invalid FEN rank width.");
            }
            if (whiteKings != 1 || blackKings != 1) throw new FormatException("Exactly one king per side is required.");
            if (parts[1] != "w" && parts[1] != "b") throw new FormatException("Invalid side.");
            board.SideToMove = parts[1] == "w" ? PieceColor.White : PieceColor.Black;
            if (parts[2] != "-") foreach (char c in parts[2])
            {
                int bit = "KQkq".IndexOf(c);
                if (bit < 0) throw new FormatException("Invalid castling rights.");
                board.CastlingRights |= 1 << bit;
            }
            board.EnPassantIndex = parts[3] == "-" ? -1 : Square.Parse(parts[3]).Index;
            if (board.EnPassantIndex >= 0 && board.EnPassantIndex / 8 != (board.SideToMove == PieceColor.White ? 5 : 2))
                throw new FormatException("Invalid en passant rank.");
            board.HalfmoveClock = int.Parse(parts[4], CultureInfo.InvariantCulture);
            board.FullmoveNumber = int.Parse(parts[5], CultureInfo.InvariantCulture);
            if (board.HalfmoveClock < 0 || board.FullmoveNumber < 1) throw new FormatException("Invalid counters.");
            return board;
        }
        public string ToFen()
        {
            var text = new StringBuilder();
            for (int rank = 7; rank >= 0; rank--)
            {
                int empty = 0;
                for (int file = 0; file < 8; file++)
                {
                    Piece piece = this[rank * 8 + file];
                    if (piece.IsEmpty) { empty++; continue; }
                    if (empty > 0) { text.Append(empty); empty = 0; }
                    char c = " pnbrqk"[(int)piece.Type];
                    text.Append(piece.Color == PieceColor.White ? char.ToUpperInvariant(c) : c);
                }
                if (empty > 0) text.Append(empty);
                if (rank > 0) text.Append('/');
            }
            text.Append(SideToMove == PieceColor.White ? " w " : " b ");
            if (CastlingRights == 0) text.Append('-');
            else for (int i = 0; i < 4; i++) if ((CastlingRights & (1 << i)) != 0) text.Append("KQkq"[i]);
            text.Append(' ').Append(EnPassantIndex < 0 ? "-" : new Square(EnPassantIndex).ToString());
            return text.Append(' ').Append(HalfmoveClock).Append(' ').Append(FullmoveNumber).ToString();
        }
    }
}

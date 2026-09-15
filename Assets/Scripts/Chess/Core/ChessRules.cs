using System;
using System.Collections.Generic;

namespace ChessBot.Chess.Core
{
    public static class ChessRules
    {
        public static PieceColor Opposite(PieceColor color) => color == PieceColor.White ? PieceColor.Black : PieceColor.White;
        public static bool IsInCheck(BoardState board, PieceColor color)
        {
            for (int i = 0; i < 64; i++)
                if (board[i].Type == PieceType.King && board[i].Color == color)
                    return IsAttacked(board, i, Opposite(color));
            throw new InvalidOperationException("Position has no king.");
        }
        // Attack geometry is independent of legal moves: pinned pieces still attack squares.
        public static bool IsAttacked(BoardState board, int target, PieceColor attacker)
        {
            for (int from = 0; from < 64; from++)
            {
                Piece piece = board[from];
                if (piece.IsEmpty || piece.Color != attacker || from == target) continue;
                int dx = target % 8 - from % 8, dy = target / 8 - from / 8;
                int ax = Math.Abs(dx), ay = Math.Abs(dy);
                switch (piece.Type)
                {
                    case PieceType.Pawn:
                        if (ax == 1 && dy == (attacker == PieceColor.White ? 1 : -1)) return true;
                        break;
                    case PieceType.Knight:
                        if (ax * ay == 2) return true;
                        break;
                    case PieceType.King:
                        if (Math.Max(ax, ay) == 1) return true;
                        break;
                    default:
                        bool straight = dx == 0 || dy == 0, diagonal = ax == ay;
                        if ((piece.Type == PieceType.Bishop && diagonal) ||
                            (piece.Type == PieceType.Rook && straight) ||
                            (piece.Type == PieceType.Queen && (straight || diagonal)))
                            if (PathClear(board, from, target)) return true;
                        break;
                }
            }
            return false;
        }
        private static bool PathClear(BoardState board, int from, int to)
        {
            int step = Math.Sign(to % 8 - from % 8) + 8 * Math.Sign(to / 8 - from / 8);
            for (int i = from + step; i != to; i += step) if (!board[i].IsEmpty) return false;
            return true;
        }
        public static IReadOnlyList<Move> LegalMoves(BoardState board)
        {
            var moves = new List<Move>();
            for (int from = 0; from < 64; from++)
            {
                var piece = board[from];
                if (piece.IsEmpty || piece.Color != board.SideToMove) continue;
                for (int to = 0; to < 64; to++)
                {
                    if (!IsPseudoLegal(board, from, to)) continue;
                    if (piece.Type == PieceType.Pawn && (to / 8 == 0 || to / 8 == 7))
                    {
                        foreach (var promotion in new[] { PieceType.Queen, PieceType.Rook, PieceType.Bishop, PieceType.Knight })
                            AddIfSafe(board, new Move(new Square(from), new Square(to), promotion), moves);
                    }
                    else AddIfSafe(board, new Move(new Square(from), new Square(to)), moves);
                }
            }
            return moves.AsReadOnly();
        }
        private static void AddIfSafe(BoardState board, Move move, List<Move> moves)
        {
            var next = ApplyUnchecked(board, move);
            if (!IsInCheck(next, board.SideToMove)) moves.Add(move);
        }
        private static bool IsPseudoLegal(BoardState board, int from, int to)
        {
            if (from == to) return false;
            var piece = board[from]; var target = board[to];
            if (!target.IsEmpty && (target.Color == piece.Color || target.Type == PieceType.King)) return false;
            int dx = to % 8 - from % 8, dy = to / 8 - from / 8;
            int ax = Math.Abs(dx), ay = Math.Abs(dy);
            switch (piece.Type)
            {
                case PieceType.Pawn:
                    int direction = piece.Color == PieceColor.White ? 1 : -1;
                    if (dx == 0 && target.IsEmpty)
                        return dy == direction || (dy == 2 * direction && from / 8 == (direction == 1 ? 1 : 6) && board[from + direction * 8].IsEmpty);
                    if (ax != 1 || dy != direction) return false;
                    if (!target.IsEmpty) return true;
                    var captured = board[to - direction * 8];
                    return to == board.EnPassantIndex && captured.Type == PieceType.Pawn && captured.Color != piece.Color;
                case PieceType.Knight: return ax * ay == 2;
                case PieceType.Bishop: return ax == ay && PathClear(board, from, to);
                case PieceType.Rook: return (dx == 0 || dy == 0) && PathClear(board, from, to);
                case PieceType.Queen: return (dx == 0 || dy == 0 || ax == ay) && PathClear(board, from, to);
                case PieceType.King:
                    if (Math.Max(ax, ay) == 1) return true;
                    return dy == 0 && ax == 2 && CanCastle(board, from, to);
                default: return false;
            }
        }
        private static bool CanCastle(BoardState board, int from, int to)
        {
            bool white = board.SideToMove == PieceColor.White, kingSide = to > from;
            if (from != (white ? 4 : 60)) return false;
            int right = white ? (kingSide ? 1 : 2) : (kingSide ? 4 : 8);
            if ((board.CastlingRights & right) == 0) return false;
            int rookIndex = from + (kingSide ? 3 : -4);
            var rook = board[rookIndex];
            if (rook.Type != PieceType.Rook || rook.Color != board.SideToMove || !PathClear(board, from, rookIndex)) return false;
            if (IsInCheck(board, board.SideToMove)) return false;
            // Move the king on the transit square before checking, so its old square cannot hide a ray.
            var transit = ApplyUnchecked(board, new Move(new Square(from), new Square(from + (kingSide ? 1 : -1))));
            return !IsInCheck(transit, board.SideToMove);
        }
        public static bool TryApply(BoardState board, Move move, out BoardState next)
        {
            foreach (var legal in LegalMoves(board)) if (legal.Equals(move))
            { next = ApplyUnchecked(board, move); return true; }
            next = board; return false;
        }
        // Only the rule engine uses this path; simulations never touch live history or emit events.
        private static BoardState ApplyUnchecked(BoardState board, Move move)
        {
            var next = board.Copy();
            int from = move.From.Index, to = move.To.Index;
            var piece = board[from]; var captured = board[to];
            next.Set(from, default);
            if (piece.Type == PieceType.Pawn && to == board.EnPassantIndex && captured.IsEmpty && from % 8 != to % 8)
            {
                int capturedIndex = to + (piece.Color == PieceColor.White ? -8 : 8);
                captured = board[capturedIndex]; next.Set(capturedIndex, default);
            }
            next.Set(to, move.Promotion == PieceType.None ? piece : new Piece(move.Promotion, piece.Color));
            if (piece.Type == PieceType.King)
            {
                next.CastlingRights &= piece.Color == PieceColor.White ? ~3 : ~12;
                if (Math.Abs(to - from) == 2)
                {
                    int rookFrom = from + (to > from ? 3 : -4), rookTo = from + (to > from ? 1 : -1);
                    next.Set(rookTo, board[rookFrom]); next.Set(rookFrom, default);
                }
            }
            if (piece.Type == PieceType.Rook) RemoveRookRight(next, from);
            if (captured.Type == PieceType.Rook) RemoveRookRight(next, to);
            next.EnPassantIndex = piece.Type == PieceType.Pawn && Math.Abs(to - from) == 16 ? (from + to) / 2 : -1;
            next.HalfmoveClock = piece.Type == PieceType.Pawn || !captured.IsEmpty ? 0 : board.HalfmoveClock + 1;
            next.FullmoveNumber = board.FullmoveNumber + (piece.Color == PieceColor.Black ? 1 : 0);
            next.SideToMove = Opposite(piece.Color);
            return next;
        }
        private static void RemoveRookRight(BoardState board, int square)
        {
            switch (square) { case 0: board.CastlingRights &= ~2; break; case 7: board.CastlingRights &= ~1; break;
                case 56: board.CastlingRights &= ~8; break; case 63: board.CastlingRights &= ~4; break; }
        }
        public static string RepetitionKey(BoardState board)
        {
            var fields = board.ToFen().Split(' ');
            bool legalEnPassant = false;
            if (board.EnPassantIndex >= 0)
                foreach (var move in LegalMoves(board))
                    if (move.To.Index == board.EnPassantIndex && board[move.From].Type == PieceType.Pawn && move.From.File != move.To.File)
                    { legalEnPassant = true; break; }
            return fields[0] + " " + fields[1] + " " + fields[2] + " " + (legalEnPassant ? fields[3] : "-");
        }
        public static bool IsKnownDeadPosition(BoardState board)
        {
            int minors = 0, bishops = 0, bishopSquareColor = -1;
            bool sameBishopColor = true;
            for (int i = 0; i < 64; i++)
            {
                var type = board[i].Type;
                if (type == PieceType.None || type == PieceType.King) continue;
                if (type != PieceType.Bishop && type != PieceType.Knight) return false;
                minors++;
                if (type == PieceType.Bishop)
                {
                    bishops++; int color = (i % 8 + i / 8) % 2;
                    if (bishopSquareColor >= 0 && bishopSquareColor != color) sameBishopColor = false;
                    bishopSquareColor = color;
                }
            }
            // Conservative proof only. Arbitrary blocked/dead positions need a separate solver.
            return minors <= 1 || (minors == bishops && sameBishopColor);
        }
    }
}

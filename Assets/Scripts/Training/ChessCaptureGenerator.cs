using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using ChessBot.Agents;
using ChessBot.Chess.Application;
using ChessBot.Chess.Core;

namespace ChessBot.Training
{
    // Synthetic capture exercises derived from training boards, not historical game positions.
    // No policy output or held-out solutions influence generation.
    public sealed class ChessCaptureGenerator
    {
        public const int Version = 1;
        private readonly Random _random;
        private readonly HashSet<string> _excluded;
        public int LastAttempts { get; private set; }
        public ChessCaptureGenerator(int seed, IEnumerable<string> excluded)
        { _random = new Random(seed); _excluded = new HashSet<string>(excluded ?? throw new ArgumentNullException(nameof(excluded))); }

        public bool TryGenerate(BoardState source, int difficulty, PieceType targetPiece, int maximumAttempts, out BoardState generated)
        {
            if (difficulty < 0 || difficulty > 2 || maximumAttempts < 1 || maximumAttempts > 64 || targetPiece == PieceType.None)
                throw new ArgumentOutOfRangeException();
            generated = null; LastAttempts = 0;
            var occupied = new List<int>();
            for (int i = 0; i < 64; i++) if (!source[i].IsEmpty) occupied.Add(i);
            string sourceIdentity = Identity(source);
            for (int attempt = 0; attempt < maximumAttempts; attempt++)
            {
                LastAttempts++;
                var pieces = new Piece[64];
                for (int i = 0; i < 64; i++) pieces[i] = source[i];
                // Always relocate the target attacker, not just irrelevant background pieces.
                var attackers = occupied.FindAll(i => source[i].Color == source.SideToMove && source[i].Type == targetPiece);
                if (attackers.Count == 0) return false;
                int attacker = attackers[_random.Next(attackers.Count)];
                var origins = new List<int>();
                for (int to = 0; to < 64; to++)
                {
                    if (!pieces[to].IsEmpty || (targetPiece == PieceType.Pawn && (to / 8 == 0 || to / 8 == 7))) continue;
                    foreach (int victim in occupied)
                        if (pieces[victim].Color != source.SideToMove && pieces[victim].Type != PieceType.King && AttacksGeometry(targetPiece, source.SideToMove, to, victim))
                        { origins.Add(to); break; }
                }
                if (origins.Count == 0) continue;
                int newOrigin = origins[_random.Next(origins.Count)];
                pieces[newOrigin] = pieces[attacker]; pieces[attacker] = default;
                var available = new List<int>(occupied); available.Remove(attacker);
                int mutations = _random.Next(0, Math.Min(2, available.Count) + 1);
                for (int j = 0; j < mutations; j++)
                {
                    int pick = _random.Next(available.Count), from = available[pick]; available.RemoveAt(pick);
                    var destinations = new List<int>();
                    for (int to = 0; to < 64; to++)
                        if (pieces[to].IsEmpty && (pieces[from].Type != PieceType.Pawn || (to / 8 != 0 && to / 8 != 7))) destinations.Add(to);
                    if (destinations.Count == 0) continue;
                    int destination = destinations[_random.Next(destinations.Count)];
                    pieces[destination] = pieces[from]; pieces[from] = default;
                }
                var candidate = BoardState.FromFen(Fen(pieces, source.SideToMove));
                // Reject impossible king/check states before the expensive two-ply test.
                if (ChessRules.IsInCheck(candidate, PieceColor.White) || ChessRules.IsInCheck(candidate, PieceColor.Black)) continue;
                if (_excluded.Contains(Identity(candidate)) || Identity(candidate) == sourceIdentity) continue;
                if (!MatchesDifficulty(candidate, difficulty, targetPiece, newOrigin)) continue;
                generated = candidate; return true;
            }
            return false; // Bounded work: caller keeps the original PGN exercise.
        }

        public static bool MatchesDifficulty(BoardState board, int difficulty, PieceType targetPiece, int requiredFrom = -1)
        {
            int count = 0;
            for (int i = 0; i < 64; i++) if (!board[i].IsEmpty) count++;
            if (count < 4 || count > 20 || (difficulty == 0 && count > 6) || (difficulty == 1 && (count < 7 || count > 12))) return false;
            if (ChessRules.IsInCheck(board, board.SideToMove) || ChessRules.IsInCheck(board, ChessRules.Opposite(board.SideToMove))) return false;
            if (new ChessGameController(board).Result.IsFinished) return false;
            bool safe = false, targetSafe = false, targetTrade = false;
            foreach (var move in ChessRules.LegalMoves(board))
            {
                if (difficulty < 2 && (board[move.From].Type != targetPiece || (requiredFrom >= 0 && move.From.Index != requiredFrom))) continue;
                if (ChessRewardPolicy.CapturedPiece(board, move).IsEmpty || !ChessTacticalAssessment.FavorableCapture(board, move)) continue;
                ChessRules.TryApply(board, move, out var next);
                if (new ChessGameController(next).Result.IsFinished) continue;
                bool recaptured = false;
                foreach (var reply in ChessRules.LegalMoves(next))
                    if (reply.To.Equals(move.To) && !ChessRewardPolicy.CapturedPiece(next, reply).IsEmpty) { recaptured = true; break; }
                bool target = board[move.From].Type == targetPiece && (requiredFrom < 0 || move.From.Index == requiredFrom);
                if (!recaptured) { safe = true; targetSafe |= target; if (difficulty < 2 && target) return true; }
                else targetTrade |= target;
            }
            return difficulty < 2 ? targetSafe : targetTrade && !safe;
        }

        private static bool AttacksGeometry(PieceType type, PieceColor side, int from, int to)
        {
            int dx = Math.Abs(to % 8 - from % 8), dy = to / 8 - from / 8, ay = Math.Abs(dy);
            switch (type)
            {
                case PieceType.Pawn: return dx == 1 && dy == (side == PieceColor.White ? 1 : -1);
                case PieceType.Knight: return dx * ay == 2;
                case PieceType.Bishop: return dx == ay;
                case PieceType.Rook: return dx == 0 || dy == 0;
                case PieceType.Queen: return dx == 0 || dy == 0 || dx == ay;
                case PieceType.King: return Math.Max(dx, ay) == 1;
                default: return false;
            }
        }
        public static string Identity(BoardState board)
        {
            string best = null;
            for (int symmetry = 0; symmetry < 4; symmetry++)
            {
                bool swap = (symmetry & 1) != 0, horizontal = (symmetry & 2) != 0;
                var pieces = new Piece[64];
                for (int i = 0; i < 64; i++)
                {
                    var p = board[i]; if (p.IsEmpty) continue;
                    pieces[i ^ (swap ? 56 : 0) ^ (horizontal ? 7 : 0)] = new Piece(p.Type, swap ? ChessRules.Opposite(p.Color) : p.Color);
                }
                string fen = Fen(pieces, swap ? ChessRules.Opposite(board.SideToMove) : board.SideToMove);
                string key = fen.Substring(0, fen.IndexOf(" - -", StringComparison.Ordinal));
                if (best == null || string.CompareOrdinal(key, best) < 0) best = key;
            }
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.ASCII.GetBytes(best))).Replace("-", "").ToLowerInvariant();
        }
        private static string Fen(Piece[] pieces, PieceColor side)
        {
            var text = new StringBuilder();
            for (int rank = 7; rank >= 0; rank--)
            {
                int empty = 0;
                for (int file = 0; file < 8; file++)
                {
                    var p = pieces[rank * 8 + file];
                    if (p.IsEmpty) { empty++; continue; }
                    if (empty > 0) { text.Append(empty); empty = 0; }
                    char c = " pnbrqk"[(int)p.Type]; text.Append(p.Color == PieceColor.White ? char.ToUpperInvariant(c) : c);
                }
                if (empty > 0) text.Append(empty);
                if (rank > 0) text.Append('/');
            }
            return text.Append(side == PieceColor.White ? " w - - 0 1" : " b - - 0 1").ToString();
        }
    }
}

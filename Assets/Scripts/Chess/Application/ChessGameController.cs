using System;
using System.Collections.Generic;
using ChessBot.Chess.Core;

namespace ChessBot.Chess.Application
{
    public sealed class ChessGameController
    {
        private BoardState _board;
        private readonly Dictionary<string, int> _history = new Dictionary<string, int>();
        private readonly List<string> _moves = new List<string>();
        private string _initialFen;
        private string _intendedClaimMove;
        public Guid GameId { get; private set; }
        public int TurnVersion { get; private set; }
        public GameResult Result { get; private set; }
        public BoardState Board => _board.Copy();
        public IReadOnlyList<Move> LegalMoves { get; private set; }
        public IReadOnlyList<string> Moves => _moves.AsReadOnly();
        public event Action Changed;
        public event Action<GameRecord> Finished;
        public ChessGameController(BoardState initial = null) { Reset(initial); }
        public void Reset(BoardState initial = null)
        {
            // A fresh identity invalidates delayed UI/Agent responses from the previous game.
            _board = (initial ?? BoardState.Initial()).Copy(); _initialFen = _board.ToFen();
            _history.Clear(); _moves.Clear(); _intendedClaimMove = null;
            GameId = Guid.NewGuid(); TurnVersion = 0; Result = default;
            AddHistory(); RefreshResult(); Changed?.Invoke();
        }
        private bool Accepts(PieceColor color, Guid gameId, int version) =>
            !Result.IsFinished && color == _board.SideToMove && gameId == GameId && version == TurnVersion;
        public bool SubmitMove(PieceColor color, Guid gameId, int version, Move move)
        {
            if (!Accepts(color, gameId, version)) return false;
            if (!ChessRules.TryApply(_board, move, out var next)) return false;
            _board = next; _moves.Add(move.ToString()); TurnVersion++;
            AddHistory(); RefreshResult(); Changed?.Invoke(); return true;
        }
        public bool CanClaimDraw(Move? intended = null)
        {
            if (Result.IsFinished) return false;
            var position = _board;
            if (intended.HasValue && !ChessRules.TryApply(_board, intended.Value, out position)) return false;
            int occurrences = Occurrences(position) + (intended.HasValue ? 1 : 0);
            return occurrences >= 3 || position.HalfmoveClock >= 100;
        }
        public bool ClaimDraw(PieceColor color, Guid gameId, int version, Move? intended = null)
        {
            if (!Accepts(color, gameId, version) || !CanClaimDraw(intended)) return false;
            var position = _board;
            if (intended.HasValue) ChessRules.TryApply(_board, intended.Value, out position);
            _intendedClaimMove = intended?.ToString();
            End(new GameResult(Occurrences(position) + (intended.HasValue ? 1 : 0) >= 3 ? EndReason.ThreefoldClaim : EndReason.FiftyMoveClaim));
            Changed?.Invoke(); return true;
        }
        public bool Resign(PieceColor color, Guid gameId, int version)
        {
            if (!Accepts(color, gameId, version)) return false;
            End(new GameResult(EndReason.Resignation, ChessRules.Opposite(color))); Changed?.Invoke(); return true;
        }
        private void AddHistory()
        {
            string key = ChessRules.RepetitionKey(_board);
            _history.TryGetValue(key, out int count); _history[key] = count + 1;
        }
        private int Occurrences(BoardState board) => _history.TryGetValue(ChessRules.RepetitionKey(board), out int count) ? count : 0;
        private void RefreshResult()
        {
            LegalMoves = ChessRules.LegalMoves(_board);
            if (LegalMoves.Count == 0)
            {
                bool check = ChessRules.IsInCheck(_board, _board.SideToMove);
                End(new GameResult(check ? EndReason.Checkmate : EndReason.Stalemate,
                    check ? (PieceColor?)ChessRules.Opposite(_board.SideToMove) : null));
            }
            else if (ChessRules.IsKnownDeadPosition(_board)) End(new GameResult(EndReason.KnownDeadPosition));
            else if (Occurrences(_board) >= 5) End(new GameResult(EndReason.FivefoldRepetition));
            else if (_board.HalfmoveClock >= 150) End(new GameResult(EndReason.SeventyFiveMoves));
        }
        private void End(GameResult result)
        {
            if (Result.IsFinished) return;
            Result = result;
            Finished?.Invoke(new GameRecord(GameId, _initialFen, _moves, result, _intendedClaimMove));
        }
    }
}

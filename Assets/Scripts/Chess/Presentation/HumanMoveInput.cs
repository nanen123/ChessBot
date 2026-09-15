using System;
using System.Collections.Generic;
using ChessBot.Chess.Core;
using ChessBot.Chess.Application;

namespace ChessBot.Chess.Presentation
{
    // A click adapter owns only temporary selection. The controller owns every board mutation.
    public sealed class HumanMoveInput
    {
        private readonly ChessGameController _game;
        private Guid _gameId;
        private int _version;
        private readonly List<Move> _promotionMoves = new List<Move>();
        public int Selected { get; private set; } = -1;
        public bool PromotionPending => _promotionMoves.Count > 0;
        public bool ClaimWithNextMove { get; set; }
        public string Message { get; private set; }
        public HumanMoveInput(ChessGameController game) { _game = game; }
        public void Clear()
        {
            Selected = -1; _promotionMoves.Clear(); ClaimWithNextMove = false; Message = "";
        }
        public bool IsDestination(int index)
        {
            foreach (var move in _game.LegalMoves) if (move.From.Index == Selected && move.To.Index == index) return true;
            return false;
        }
        public void Click(int index)
        {
            if (_game.Result.IsFinished || PromotionPending) return;
            var board = _game.Board;
            if (Selected >= 0)
            {
                var matches = new List<Move>();
                foreach (var move in _game.LegalMoves)
                    if (move.From.Index == Selected && move.To.Index == index) matches.Add(move);
                if (matches.Count > 0)
                {
                    if (matches[0].Promotion != PieceType.None) _promotionMoves.AddRange(matches);
                    else Submit(matches[0]);
                    return;
                }
            }
            if (!board[index].IsEmpty && board[index].Color == board.SideToMove)
            {
                Selected = Selected == index ? -1 : index;
                _gameId = _game.GameId; _version = _game.TurnVersion; Message = "";
            }
            else { Selected = -1; Message = "Select a piece of the side to move."; }
        }
        public void Promote(PieceType type)
        {
            foreach (var move in _promotionMoves) if (move.Promotion == type) { Submit(move); return; }
        }
        private void Submit(Move move)
        {
            bool accepted = ClaimWithNextMove
                ? _game.ClaimDraw(_game.Board.SideToMove, _gameId, _version, move)
                : _game.SubmitMove(_game.Board.SideToMove, _gameId, _version, move);
            Clear();
            if (!accepted) Message = "Move or draw claim rejected. Position unchanged.";
        }
    }
}

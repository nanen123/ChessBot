using UnityEngine;
using ChessBot.Chess.Core;

namespace ChessBot.Chess.Presentation
{
    public sealed class ChessArt : ScriptableObject
    {
        [SerializeField] private Sprite[] _white = new Sprite[6];
        [SerializeField] private Sprite[] _black = new Sprite[6];
        [SerializeField] private Sprite _board = null;
        public Sprite Board => _board;
        public Sprite For(Piece piece) => piece.IsEmpty ? null : (piece.Color == PieceColor.White ? _white : _black)[(int)piece.Type - 1];
        public bool IsValid => Valid(_white) && Valid(_black) && _board != null;
        private static bool Valid(Sprite[] pieces)
        {
            if (pieces == null || pieces.Length != 6) return false;
            foreach (var sprite in pieces) if (sprite == null) return false;
            return true;
        }
    }
}

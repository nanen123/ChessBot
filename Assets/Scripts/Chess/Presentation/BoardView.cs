using System;
using System.Text;
using ChessBot.Chess.Application;
using ChessBot.Chess.Core;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace ChessBot.Chess.Presentation
{
    public sealed class BoardView : MonoBehaviour
    {
        private ChessGameController _game;
        private HumanMoveInput _input;
        private ChessArt _art;
        private Font _font;
        private readonly Image[] _pieces = new Image[64];
        private readonly Image[] _highlights = new Image[64];
        private readonly Button[] _squares = new Button[64];
        private readonly Text[] _files = new Text[8];
        private readonly Text[] _ranks = new Text[8];
        private Text _status, _message, _history;
        private Button _claim, _claimNext, _resign;
        private GameObject _promotion;
        private bool _flipped;
        private string _saveMessage = "";
        private static readonly Color Ink = new Color(0.075f, 0.095f, 0.12f);
        private static readonly Color Cream = new Color(0.96f, 0.92f, 0.83f);

        public void Initialize(ChessGameController game, ChessArt art)
        {
            if (_game != null) throw new InvalidOperationException("View is already initialized.");
            if (art == null || !art.IsValid) throw new InvalidOperationException("Chess sprites are not configured.");
            _game = game; _art = art; _input = new HumanMoveInput(game);
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Build(); _game.Changed += OnChanged; Refresh();
        }
        private void OnDestroy() { if (_game != null) _game.Changed -= OnChanged; }
        private void OnChanged() { _input.Clear(); Refresh(); }
        public void SetSaveMessage(string message) { _saveMessage = message; Refresh(); }
        private RectTransform Rect(string label, Transform parent, float x, float y, float width, float height)
        {
            var go = new GameObject(label, typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>(); rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height);
            return rect;
        }
        private Image Panel(string label, Transform parent, float x, float y, float width, float height, Color color)
        {
            var image = Rect(label, parent, x, y, width, height).gameObject.AddComponent<Image>();
            image.color = color; image.raycastTarget = false; return image;
        }
        private Text Label(string value, Transform parent, float x, float y, float width, float height, int size, Color color)
        {
            var text = Rect(value, parent, x, y, width, height).gameObject.AddComponent<Text>();
            text.font = _font; text.fontSize = size; text.text = value; text.color = color;
            text.raycastTarget = false; text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }
        private Button ActionButton(string label, Transform parent, float x, float y, float width, UnityAction action)
        {
            var image = Panel(label, parent, x, y, width, 44, new Color(0.19f, 0.24f, 0.27f));
            image.raycastTarget = true;
            var button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image; button.onClick.AddListener(action);
            var text = Label(label, image.transform, 8, 0, width - 16, 44, 18, Cream); text.alignment = TextAnchor.MiddleCenter;
            return button;
        }
        private void Build()
        {
            var canvas = gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1200, 800); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            gameObject.AddComponent<GraphicRaycaster>();
            var background = Panel("Background", transform, 0, 0, 1200, 800, Ink);
            background.rectTransform.anchorMax = Vector2.one; background.rectTransform.anchorMin = Vector2.zero;
            background.rectTransform.offsetMin = background.rectTransform.offsetMax = Vector2.zero;
            // Center the fixed design area. CanvasScaler keeps it inside both wide and tall windows.
            var content = Rect("Local chess", transform, 0, 0, 1200, 800);
            content.anchorMin = content.anchorMax = new Vector2(0.5f, 0.5f); content.pivot = new Vector2(0.5f, 0.5f);
            Label("CHESS / LOCAL PLAY", content, 36, 18, 700, 45, 28, Cream);
            var board = Panel("Board artwork", content, 30, 75, 690, 690, Color.white); board.sprite = _art.Board;
            // Imported board has a coordinate frame; the playable 8x8 region occupies its inner 85.4%.
            const float edge = 51.75f, cell = 73.65f;
            Panel("File label backing", board.transform, 30, 651, 625, 35, new Color(0.035f, 0.025f, 0.03f));
            Panel("Rank label backing", board.transform, 1, 40, 35, 610, new Color(0.035f, 0.025f, 0.03f));
            for (int i = 0; i < 8; i++)
            {
                _files[i] = Label("", board.transform, edge + i * cell, 650, cell, 30, 18, Cream);
                _files[i].alignment = TextAnchor.MiddleCenter;
                _ranks[i] = Label("", board.transform, 0, edge + i * cell, 34, cell, 18, Cream);
                _ranks[i].alignment = TextAnchor.MiddleCenter;
            }
            for (int display = 0; display < 64; display++)
            {
                int capturedDisplay = display;
                var tile = Panel("Square " + display, board.transform, edge + display % 8 * cell, edge + display / 8 * cell, cell, cell, Color.clear);
                tile.raycastTarget = true; _highlights[display] = tile;
                var button = tile.gameObject.AddComponent<Button>(); button.targetGraphic = tile; button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() => { _input.Click(BoardIndex(capturedDisplay)); Refresh(); }); _squares[display] = button;
                var piece = Panel("Piece", tile.transform, 5, 4, cell - 10, cell - 8, Color.white);
                piece.preserveAspect = true; _pieces[display] = piece;
            }
            Label("PASS & PLAY", content, 775, 86, 385, 30, 19, new Color(0.6f, 0.76f, 0.77f));
            _status = Label("", content, 775, 127, 390, 88, 30, Cream);
            Label("Select a piece, then a highlighted square.\nWhite and Black take turns on this screen.", content, 775, 226, 380, 60, 18, Cream);
            ActionButton("New game", content, 775, 305, 180, () => { _saveMessage = ""; _game.Reset(); });
            ActionButton("Flip board", content, 975, 305, 180, () => { _flipped = !_flipped; Refresh(); });
            _resign = ActionButton("Resign", content, 775, 361, 180, () => _game.Resign(_game.Board.SideToMove, _game.GameId, _game.TurnVersion));
            _claim = ActionButton("Claim draw", content, 975, 361, 180, () => _game.ClaimDraw(_game.Board.SideToMove, _game.GameId, _game.TurnVersion));
            _claimNext = ActionButton("Claim draw with next move", content, 775, 417, 380,
                () => { bool enabled = !_input.ClaimWithNextMove; _input.Clear(); _input.ClaimWithNextMove = enabled; Refresh(); });
            _message = Label("", content, 775, 476, 380, 89, 17, new Color(0.65f, 0.82f, 0.82f));
            Label("RECENT MOVES", content, 775, 574, 380, 25, 16, Cream);
            _history = Label("", content, 775, 610, 380, 150, 18, Cream);
            var modal = Panel("Promotion dialog", content, 210, 290, 780, 210, Ink); _promotion = modal.gameObject;
            modal.raycastTarget = true;
            Label("Choose promotion", modal.transform, 25, 20, 730, 40, 28, Cream);
            var types = new[] { PieceType.Queen, PieceType.Rook, PieceType.Bishop, PieceType.Knight };
            for (int i = 0; i < types.Length; i++)
            {
                PieceType type = types[i];
                ActionButton(type.ToString(), modal.transform, 25 + 185 * i, 88, 170, () => { _input.Promote(type); Refresh(); });
            }
            ActionButton("Cancel", modal.transform, 305, 150, 170, () => { _input.Clear(); Refresh(); });
        }
        private int BoardIndex(int display) => _flipped ? (display / 8) * 8 + (7 - display % 8) : (7 - display / 8) * 8 + display % 8;
        private void Refresh()
        {
            if (_game == null || _status == null) return;
            var state = _game.Board; bool check = ChessRules.IsInCheck(state, state.SideToMove);
            for (int display = 0; display < 64; display++)
            {
                int index = BoardIndex(display); var piece = state[index];
                _pieces[display].sprite = _art.For(piece); _pieces[display].enabled = !piece.IsEmpty;
                var color = Color.clear;
                if (_input.IsDestination(index)) color = new Color(0.18f, 0.8f, 0.57f, 0.46f);
                if (check && piece.Type == PieceType.King && piece.Color == state.SideToMove) color = new Color(0.95f, 0.15f, 0.15f, 0.65f);
                if (_input.Selected == index) color = new Color(0.95f, 0.7f, 0.16f, 0.65f);
                _highlights[display].color = color;
                _squares[display].interactable = !_game.Result.IsFinished && !_input.PromotionPending;
            }
            for (int i = 0; i < 8; i++)
            { _files[i].text = ((char)('a' + (_flipped ? 7 - i : i))).ToString(); _ranks[i].text = (_flipped ? i + 1 : 8 - i).ToString(); }
            _status.text = _game.Result.IsFinished ? ResultText(_game.Result) : state.SideToMove + " to move" + (check ? "\nCheck!" : "");
            _promotion.SetActive(_input.PromotionPending);
            _resign.interactable = !_game.Result.IsFinished && !_input.PromotionPending;
            _claim.interactable = !_input.PromotionPending && _game.CanClaimDraw();
            _claimNext.interactable = !_game.Result.IsFinished && !_input.PromotionPending;
            _message.text = _input.ClaimWithNextMove ? "DRAW CLAIM MODE\nChoose the intended move. It will not be played if the claim is invalid. Click the button again to cancel."
                : !string.IsNullOrEmpty(_input.Message) ? _input.Message : _saveMessage;
            var history = new StringBuilder(); int first = Math.Max(0, _game.Moves.Count - 12); first -= first % 2;
            for (int i = first; i < _game.Moves.Count; i += 2)
            {
                history.Append(i / 2 + 1).Append(".  ").Append(_game.Moves[i]);
                if (i + 1 < _game.Moves.Count) history.Append("    ").Append(_game.Moves[i + 1]);
                history.AppendLine();
            }
            _history.text = history.Length == 0 ? "No moves yet." : history.ToString();
        }
        private static string ResultText(GameResult result)
        {
            string reason;
            switch (result.Reason)
            {
                case EndReason.Checkmate: reason = "Checkmate"; break;
                case EndReason.KnownDeadPosition: reason = "Insufficient material"; break;
                case EndReason.ThreefoldClaim: reason = "Threefold repetition"; break;
                case EndReason.FiftyMoveClaim: reason = "50-move claim"; break;
                case EndReason.FivefoldRepetition: reason = "Fivefold repetition"; break;
                case EndReason.SeventyFiveMoves: reason = "75-move rule"; break;
                default: reason = result.Reason.ToString(); break;
            }
            return (result.Winner.HasValue ? result.Winner + " wins" : "Draw") + "\n" + reason;
        }
    }
}

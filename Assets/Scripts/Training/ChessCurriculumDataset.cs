using System;
using System.Collections.Generic;
using ChessBot.Chess.Application;
using ChessBot.Chess.Core;
using UnityEngine;

namespace ChessBot.Training
{
    public enum CurriculumPositionSource { InspectorFen, PgnTraining }

    // Only train data is bundled. Validated immutable templates are shared across boards.
    public sealed class ChessCurriculumDataset
    {
        [Serializable] private sealed class Document
        {
            public int version;
            public string split, datasetId;
            public Entry[] samples;
        }
        [Serializable] private sealed class Entry
        {
            public int stage, startPly;
            public string split, sourceGame, initialFen, fen;
            public string[] moves;
        }
        public sealed class Sample
        {
            private readonly ChessGameController _original, _mirrored;
            public string SourceGame { get; }
            public int StartPly { get; }
            public string HistoryInitialFen { get; }
            private readonly string[] _moves;
            internal Sample(string source, string initial, string[] moves, ChessGameController original, ChessGameController mirrored)
            { SourceGame = source; HistoryInitialFen = initial; _moves = (string[])moves.Clone(); StartPly = moves.Length; _original = original; _mirrored = mirrored; }
            public void Apply(ChessGameController target, bool mirrored) => target.ResetFromPosition(mirrored ? _mirrored : _original);
            public string[] HistoryMoves(bool mirrored)
            {
                var moves = (string[])_moves.Clone();
                if (mirrored) for (int i = 0; i < moves.Length; i++) moves[i] = MirrorMove(moves[i]);
                return moves;
            }
        }
        private static ChessCurriculumDataset _bundled;
        private readonly List<Sample>[] _stages = new List<Sample>[6];
        public string Id { get; private set; }
        public int Count(int stage) => _stages[stage].Count;
        public Sample Get(int stage, int index) => _stages[stage][index];
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ClearCache() => _bundled = null;
        public static ChessCurriculumDataset LoadBundled()
        {
            if (_bundled != null) return _bundled;
            var asset = Resources.Load<TextAsset>("ChessCurriculumTraining");
            if (asset == null) throw new InvalidOperationException("Missing ChessCurriculumTraining resource. Run Tools/BuildCurriculumDataset.py before building.");
            _bundled = Parse(asset.text);
            Debug.Log($"Chess PGN curriculum loaded: {_bundled.Id}; train samples per stage: {string.Join(", ", Array.ConvertAll(_bundled._stages, s => s.Count))}");
            return _bundled;
        }
        public static ChessCurriculumDataset Parse(string json)
        {
            var document = JsonUtility.FromJson<Document>(json);
            if (document == null || document.version != 1 || document.split != "train" || string.IsNullOrEmpty(document.datasetId) || document.samples == null)
                throw new FormatException("Expected version 1 training curriculum dataset.");
            var data = new ChessCurriculumDataset { Id = document.datasetId };
            for (int i = 0; i < 6; i++) data._stages[i] = new List<Sample>();
            foreach (var entry in document.samples)
            {
                if (entry == null || entry.stage < 0 || entry.stage >= 6 || entry.split != "train" || entry.moves == null || entry.startPly != entry.moves.Length || entry.moves.Length > 1000 || string.IsNullOrEmpty(entry.sourceGame))
                    throw new FormatException("Invalid or non-training curriculum entry.");
                var original = Replay(entry.initialFen, entry.moves, false);
                if (original.Board.ToFen() != entry.fen || original.CanClaimDraw())
                    throw new FormatException("Curriculum replay disagrees with starting FEN or is already claimable: " + entry.sourceGame);
                if (entry.stage == 5 && (entry.startPly != 0 || entry.fen != BoardState.InitialFen))
                    throw new FormatException("Full-game lesson must start at the standard initial position.");
                var mirrored = Replay(entry.initialFen, entry.moves, true);
                data._stages[entry.stage].Add(new Sample(entry.sourceGame, entry.initialFen, entry.moves, original, mirrored));
            }
            foreach (var stage in data._stages) if (stage.Count == 0) throw new FormatException("Each of six curriculum stages needs training samples.");
            return data;
        }
        private static ChessGameController Replay(string initial, string[] moves, bool mirrored)
        {
            var board = BoardState.FromFen(mirrored ? ChessCurriculum.SwapColors(initial) : initial);
            if (ChessRules.IsInCheck(board, ChessRules.Opposite(board.SideToMove))) throw new FormatException("Illegal initial position.");
            var parsed = new Move[moves.Length];
            for (int i = 0; i < moves.Length; i++) parsed[i] = Move.Parse(mirrored ? MirrorMove(moves[i]) : moves[i]);
            var game = new ChessGameController(); game.ResetFromHistory(board, parsed); return game;
        }
        public static string MirrorMove(string uci)
        {
            // Reflect ranks and swap colors, preserving files, castling sides and promotion.
            if (string.IsNullOrEmpty(uci) || (uci.Length != 4 && uci.Length != 5)) throw new FormatException("Invalid UCI move.");
            Move.Parse(uci);
            var chars = uci.ToCharArray(); chars[1] = (char)('9' - chars[1] + '0'); chars[3] = (char)('9' - chars[3] + '0');
            return new string(chars);
        }
    }
}

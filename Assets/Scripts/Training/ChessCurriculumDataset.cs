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
            public int stage, startPly, difficulty;
            public string split, sourceGame, initialFen, fen, category;
            public string[] moves;
        }
        public sealed class Sample
        {
            private ChessGameController _original, _mirrored;
            private readonly string _expectedFen;
            public string Category { get; }
            public int Difficulty { get; }
            public string SourceGame { get; }
            public int StartPly { get; }
            public string HistoryInitialFen { get; }
            private readonly string[] _moves;
            internal Sample(string category, int difficulty, string source, string initial, string[] moves, string expectedFen)
            { Category = category; Difficulty = difficulty; SourceGame = source; HistoryInitialFen = initial; _moves = (string[])moves.Clone(); StartPly = moves.Length; _expectedFen = expectedFen; }
            public void Apply(ChessGameController target, bool mirrored)
            {
                // Validate before replacing the live game; share validated templates across environments.
                if (_original == null)
                {
                    var original = Replay(HistoryInitialFen, _moves, false);
                    if (original.Board.ToFen() != _expectedFen || original.CanClaimDraw())
                        throw new FormatException("Curriculum replay disagrees with starting FEN or is already claimable: " + SourceGame);
                    _original = original;
                }
                if (mirrored && _mirrored == null) _mirrored = Replay(HistoryInitialFen, _moves, true);
                target.ResetFromPosition(mirrored ? _mirrored : _original);
            }
            public string[] HistoryMoves(bool mirrored)
            {
                var moves = (string[])_moves.Clone();
                if (mirrored) for (int i = 0; i < moves.Length; i++) moves[i] = MirrorMove(moves[i]);
                return moves;
            }
        }
        private static ChessCurriculumDataset _bundled;
        private readonly List<Sample>[] _stages = new List<Sample>[6];
        private readonly List<Sample>[] _capture = {new List<Sample>(),new List<Sample>(),new List<Sample>()};
        private readonly List<Sample> _openingStarts = new List<Sample>(), _standardStarts = new List<Sample>();
        public int FullGameCount(bool standard) => (standard ? _standardStarts : _openingStarts).Count;
        public Sample FullGameSample(bool standard, int index) => (standard ? _standardStarts : _openingStarts)[index];
        public string Id { get; private set; }
        public int Count(int stage) => _stages[stage].Count;
        public Sample Get(int stage, int index) => _stages[stage][index];
        public int Count(int stage, int difficulty) => stage == 0 ? _capture[difficulty].Count : Count(stage);
        public Sample Get(int stage, int difficulty, int index) => stage == 0 ? _capture[difficulty][index] : Get(stage,index);
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
            if (document == null || document.version != 3 || document.split != "train" || string.IsNullOrEmpty(document.datasetId) || document.samples == null)
                throw new FormatException("Expected version 3 training curriculum dataset.");
            var data = new ChessCurriculumDataset { Id = document.datasetId };
            for (int i = 0; i < 6; i++) data._stages[i] = new List<Sample>();
            foreach (var entry in document.samples)
            {
                if (entry == null || entry.stage < 0 || entry.stage >= 6 || entry.split != "train" || entry.moves == null || entry.startPly != entry.moves.Length || entry.moves.Length > 1000 || string.IsNullOrEmpty(entry.sourceGame))
                    throw new FormatException("Invalid or non-training curriculum entry.");
                if (entry.stage == 0 && (entry.difficulty < 0 || entry.difficulty > 2)) throw new FormatException("Invalid capture difficulty.");
                if (string.IsNullOrEmpty(entry.initialFen) || string.IsNullOrEmpty(entry.fen)) throw new FormatException("Missing curriculum FEN.");
                if (entry.stage == 5 && (entry.initialFen != BoardState.InitialFen ||
                    !((entry.category == "standard" && entry.startPly == 0 && entry.fen == BoardState.InitialFen) ||
                      (entry.category == "opening3" && entry.startPly == 6) || (entry.category == "opening4" && entry.startPly == 8))))
                    throw new FormatException("Full-game lesson requires a standard start or a 6/8-ply opening history.");
                var sample = new Sample(entry.category, entry.difficulty, entry.sourceGame, entry.initialFen, entry.moves, entry.fen);
                data._stages[entry.stage].Add(sample);
                if (entry.stage == 5) (entry.startPly == 0 ? data._standardStarts : data._openingStarts).Add(sample);
                if (entry.stage == 0) data._capture[entry.difficulty].Add(sample);
            }
            foreach (var stage in data._stages) if (stage.Count == 0) throw new FormatException("Each of six curriculum stages needs training samples.");
            foreach (var level in data._capture) if (level.Count == 0) throw new FormatException("Each capture difficulty needs training samples.");
            if (data._standardStarts.Count == 0 || data._openingStarts.Count == 0) throw new FormatException("Full-game standard and opening samples are required.");
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

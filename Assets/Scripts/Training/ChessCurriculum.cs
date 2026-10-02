using System;
using System.Text;
using ChessBot.Chess.Core;
using UnityEngine;
namespace ChessBot.Training
{
    public enum CurriculumEnding { FirstCapture, MaterialAfterHorizon, MateWithinHorizon, FullGame, FirstMoveFavorableCapture }
    [Serializable] public sealed class ChessLesson
    {
        public string Name;
        public CurriculumEnding Ending;
        [Min(1)] public int MaximumPlies;
        [Min(0)] public float SuccessReward;
        [Min(0)] public float FailurePenalty;
        [TextArea] public string[] Positions;
        public ChessLesson(string name, CurriculumEnding ending, int plies, float success, float failure, params string[] positions)
        { Name = name; Ending = ending; MaximumPlies = plies; SuccessReward = success; FailurePenalty = failure; Positions = positions; }
    }
    public static class ChessCurriculum
    {
        public static ChessLesson[] Defaults() => new[]
        {
            new ChessLesson("Immediate capture", CurriculumEnding.FirstMoveFavorableCapture, 1, 0.1f, 0.1f,
                "7k/8/8/8/8/8/p7/R6K w - - 0 1", "7k/8/8/8/8/2p5/8/1N5K w - - 0 1", "7k/8/8/8/8/8/1r6/B6K w - - 0 1"),
            new ChessLesson("Capture and defense", CurriculumEnding.MaterialAfterHorizon, 12, 0.1f, 0.1f,
                "7k/8/8/8/8/r7/p7/R6K w - - 0 1", "7k/8/8/8/8/r7/q7/R6K w - - 0 1", "7k/8/8/8/8/2p5/1p6/B6K w - - 0 1"),
            new ChessLesson("Mate in one or two", CurriculumEnding.MateWithinHorizon, 3, 0f, 0.1f,
                "7k/8/5KQ1/8/8/8/8/8 w - - 0 1", "7k/8/5K2/4Q3/8/8/8/8 w - - 0 1"),
            new ChessLesson("Simple endgames", CurriculumEnding.FullGame, 100, 0f, 0f,
                "7k/8/8/8/3K4/8/8/Q7 w - - 0 1", "7k/8/8/8/3K4/8/8/R7 w - - 0 1"),
            new ChessLesson("Middlegames", CurriculumEnding.FullGame, 256, 0f, 0f,
                Replay("e2e4", "e7e5", "g1f3", "b8c6", "f1b5", "a7a6", "b5a4", "g8f6", "e1g1", "f8e7", "f1e1", "b7b5", "a4b3", "d7d6", "c2c3", "e8g8", "h2h3", "c6b8", "d2d4", "b8d7"),
                Replay("d2d4", "d7d5", "c2c4", "e7e6", "b1c3", "g8f6", "c1g5", "f8e7", "e2e3", "e8g8", "g1f3", "h7h6", "g5h4", "b7b6", "f1d3", "c8b7", "e1g1", "b8d7", "a1c1", "c7c5")),
            new ChessLesson("Full games", CurriculumEnding.FullGame, 512, 0f, 0f, BoardState.InitialFen)
        };
        private static string Replay(params string[] moves)
        {
            var board = BoardState.Initial();
            foreach (var move in moves)
            {
                if (!ChessRules.TryApply(board, Move.Parse(move), out var next)) throw new InvalidOperationException("Invalid curriculum opening: " + move);
                board = next;
            }
            return board.ToFen();
        }
        public static string SwapColors(string fen)
        {
            var fields = fen.Split(' '); var ranks = fields[0].Split('/'); Array.Reverse(ranks);
            var text = new StringBuilder();
            foreach (char c in string.Join("/", ranks)) text.Append(char.IsLetter(c) ? (char.IsUpper(c) ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c)) : c);
            fields[0] = text.ToString(); fields[1] = fields[1] == "w" ? "b" : "w";
            var rights = new StringBuilder();
            foreach (char c in "KQkq") if (fields[2].Contains(char.IsUpper(c) ? char.ToLowerInvariant(c).ToString() : char.ToUpperInvariant(c).ToString())) rights.Append(c);
            fields[2] = rights.Length == 0 ? "-" : rights.ToString();
            if (fields[3] != "-") fields[3] = fields[3][0].ToString() + (9 - (fields[3][1] - '0'));
            return string.Join(" ", fields);
        }
        public static int SampleCaptureDifficulty(int active, float review, bool repair, float a, float b, float draw)
        {
            if (active < 0 || active > 2 || !(review >= 0 && review <= 1) || !(a >= 0 && b >= 0 && a + b <= 1) || !(draw >= 0 && draw < 1)) throw new ArgumentOutOfRangeException();
            if (active == 2 && repair) return draw < a ? 0 : draw < a + b ? 1 : 2;
            if (active == 0 || draw >= review) return active;
            return Math.Min(active - 1, (int)(draw / review * active));
        }
        public static int TaskOutcome(ChessLesson lesson, int plies, bool capture, int materialGain, bool favorableCapture = false)
        {
            if (lesson.Ending == CurriculumEnding.FirstMoveFavorableCapture) return plies < 1 ? 2 : favorableCapture ? 1 : -1;
            if (lesson.Ending == CurriculumEnding.FirstCapture && capture) return Math.Sign(materialGain);
            if (plies < lesson.MaximumPlies) return 2;
            if (lesson.Ending == CurriculumEnding.MaterialAfterHorizon) return Math.Sign(materialGain);
            return -1;
        }
    }
}
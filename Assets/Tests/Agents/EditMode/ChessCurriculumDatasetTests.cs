using System;
using ChessBot.Training;
using ChessBot.Chess.Application;
using NUnit.Framework;
using UnityEngine;
namespace ChessBot.Tests
{
    public class ChessCurriculumDatasetTests
    {
        [Test] public void AllBundledTrainSamplesReplayForBothColorsWithoutEpisodeMoves()
        {
            var data = ChessCurriculumDataset.LoadBundled();
            for (int stage = 0; stage < 6; stage++)
            {
                Assert.That(data.Count(stage), Is.EqualTo(16));
                for (int i = 0; i < data.Count(stage); i++)
                {
                    var sample = data.Get(stage, i); var game = new ChessGameController();
                    sample.Apply(game, false); var original = game.Board;
                    Assert.That(game.Result.IsFinished, Is.False); Assert.That(game.TurnVersion, Is.Zero); Assert.That(game.Moves, Is.Empty);
                    sample.Apply(game, true);
                    var expected = ChessCurriculum.SwapColors(original.ToFen()).Split(' ');
                    var actual = game.Board.ToFen().Split(' ');
                    // A black-first mirrored history advances fullmove numbering differently.
                    for (int field = 0; field < 5; field++) Assert.That(actual[field], Is.EqualTo(expected[field]));
                    Assert.That(game.Result.IsFinished, Is.False); Assert.That(game.TurnVersion, Is.Zero);
                }
            }
        }
        [Test] public void EvaluationDataAndMismatchedReplayFailClosed()
        {
            string json = Resources.Load<TextAsset>("ChessCurriculumTraining").text;
            Assert.Throws<FormatException>(() => ChessCurriculumDataset.Parse(json.Replace("\"train\"", "\"eval\"")));
            Assert.Throws<FormatException>(() => ChessCurriculumDataset.Parse(json.Replace("\"startPly\": 0", "\"startPly\": 999")));
        }
    }
}

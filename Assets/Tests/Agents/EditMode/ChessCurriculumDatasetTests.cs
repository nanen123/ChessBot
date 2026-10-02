using System;
using ChessBot.Training;
using ChessBot.Chess.Application;
using NUnit.Framework;
using UnityEngine;
namespace ChessBot.Tests
{
    public class ChessCurriculumDatasetTests
    {
        [Test] public void BundledSamplesReplayForBothColorsWithoutEpisodeMoves()
        {
            var data = ChessCurriculumDataset.LoadBundled();
            for (int stage = 0; stage < 6; stage++)
            {
                Assert.That(data.Count(stage), Is.GreaterThanOrEqualTo(new[] {3072,3072,2048,2048,5120,1025}[stage]));
                if (stage == 0) for (int difficulty=0;difficulty<3;difficulty++) Assert.That(data.Count(0,difficulty),Is.GreaterThanOrEqualTo(1024));
                for (int i = 0; i < data.Count(stage); i += Math.Max(1, data.Count(stage) / 32))
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
        [Test] public void FullGameBucketsKeepHistoryWithoutEpisodeCredit()
        {
            var data=ChessCurriculumDataset.LoadBundled();
            Assert.That(data.FullGameCount(true),Is.EqualTo(1));
            Assert.That(data.FullGameCount(false),Is.EqualTo(1024));
            foreach(bool standard in new[]{true,false})
                for(int i=0;i<data.FullGameCount(standard);i+=17)
                {
                    var sample=data.FullGameSample(standard,i);var game=new ChessGameController();sample.Apply(game,false);
                    Assert.That(sample.StartPly,standard ? Is.EqualTo(0) : Is.EqualTo(6).Or.EqualTo(8));
                    Assert.That(game.Board.SideToMove,Is.EqualTo(ChessBot.Chess.Core.PieceColor.White));
                    Assert.That(game.TurnVersion,Is.Zero);Assert.That(game.Moves,Is.Empty);
                    Assert.That(game.Board.ToFen()==ChessBot.Chess.Core.BoardState.InitialFen,Is.EqualTo(standard));
                }
        }
        [Test] public void BalancedCaptureSamplingAndAugmentedHistories()
        {
            var data=ChessCurriculumDataset.LoadBundled();var state=UnityEngine.Random.state;UnityEngine.Random.InitState(1701);
            try
            {
                var counts=new System.Collections.Generic.Dictionary<string,int>();int horizontal=0;
                for(int i=0;i<1200;i++)
                {
                    var sample=data.SampleBalanced(0,0);if(!counts.ContainsKey(sample.Stratum))counts[sample.Stratum]=0;counts[sample.Stratum]++;
                    if(sample.Horizontal)horizontal++;
                }
                Assert.That(counts.Count,Is.EqualTo(6));foreach(var count in counts.Values)Assert.That(count,Is.InRange(140,260));
                Assert.That(horizontal,Is.InRange(400,800));
                for(int stage=0;stage<5;stage++)
                {
                    int checkedSamples=0;
                    for(int i=0;i<data.Count(stage) && checkedSamples<8;i++)
                    {
                        var sample=data.Get(stage,i);if(!sample.Horizontal)continue;
                        Assert.That(sample.HistoryMoves(false).Length+sample.HistoryOffset,Is.EqualTo(sample.StartPly));
                        Assert.That(sample.HistoryInitialFen.Split(' ')[2],Is.EqualTo("-"));
                        var game=new ChessGameController();sample.Apply(game,false);Assert.That(game.TurnVersion,Is.Zero);Assert.That(game.CanClaimDraw(),Is.False);
                        sample.Apply(game,true);Assert.That(game.TurnVersion,Is.Zero);Assert.That(game.Result.IsFinished,Is.False);checkedSamples++;
                    }
                    Assert.That(checkedSamples,Is.GreaterThan(0));
                }
            }
            finally {UnityEngine.Random.state=state;}
        }
        [Test] public void CaptureBucketsAreDisjointAndMissingDifficultyFailsClosed()
        {
            var data=ChessCurriculumDataset.LoadBundled();
            var sources=new System.Collections.Generic.HashSet<string>();
            for(int difficulty=0;difficulty<3;difficulty++)
            {
                sources.Clear();
                for(int i=0;i<data.Count(0,difficulty);i++)
                {
                    var sample=data.Get(0,difficulty,i);
                    Assert.That(sample.Difficulty,Is.EqualTo(difficulty));
                    Assert.That(sources.Add(sample.SourceGame+":"+sample.StartPly+":"+sample.Horizontal),Is.True);
                }
            }
            string json=Resources.Load<TextAsset>("ChessCurriculumTraining").text;
            Assert.Throws<FormatException>(()=>ChessCurriculumDataset.Parse(json.Replace("\"difficulty\": 2","\"difficulty\": 1")));
        }
        [Test] public void CorruptLazyReplayDoesNotReplaceLiveGame()
        {
            string json=Resources.Load<TextAsset>("ChessCurriculumTraining").text;
            var data=ChessCurriculumDataset.LoadBundled();var reference=new ChessGameController();data.Get(0,0).Apply(reference,false);
            string corrupt=json.Replace("\"fen\": \""+reference.Board.ToFen()+"\"", "\"fen\": \""+ChessBot.Chess.Core.BoardState.InitialFen+"\"");
            var parsed=ChessCurriculumDataset.Parse(corrupt);var live=new ChessGameController();var id=live.GameId;
            Assert.Throws<FormatException>(()=>parsed.Get(0,0).Apply(live,false));Assert.That(live.GameId,Is.EqualTo(id));
        }
        [Test] public void EvaluationDataAndMismatchedReplayFailClosed()
        {
            string json = Resources.Load<TextAsset>("ChessCurriculumTraining").text;
            Assert.Throws<FormatException>(() => ChessCurriculumDataset.Parse(json.Replace("\"train\"", "\"eval\"")));
            Assert.Throws<FormatException>(() => ChessCurriculumDataset.Parse(json.Replace("\"startPly\": 0", "\"startPly\": 999")));
        }
    }
}

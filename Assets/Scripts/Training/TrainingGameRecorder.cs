using System;
using System.Collections.Generic;
using System.IO;
using ChessBot.Agents;
using ChessBot.Chess.Application;
using ChessBot.Chess.Core;
using UnityEngine;

namespace ChessBot.Training
{
    public sealed class TrainingGameRecorder
    {
        private readonly string _path = Path.Combine(UnityEngine.Application.persistentDataPath, "ChessTraining", Guid.NewGuid() + ".jsonl");
        private bool _failed;
        public void Write(ChessGameController game, bool interrupted, float whiteReward, float blackReward, string initialFen = BoardState.InitialFen, int lesson = -1, PieceColor focus = PieceColor.White, int taskOutcome = 2, ChessCurriculumDataset.Sample sample = null, bool mirrored = false)
        {
            if (_failed) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                var record = new Record
                {
                    sourceGame = sample?.SourceGame, sourceStartPly = sample?.StartPly ?? 0, sourceMirrored = mirrored,
                    datasetId = sample == null ? null : ChessCurriculumDataset.LoadBundled().Id,
                    historyInitialFen = sample == null ? initialFen : mirrored ? ChessCurriculum.SwapColors(sample.HistoryInitialFen) : sample.HistoryInitialFen,
                    historyMoves = sample?.HistoryMoves(mirrored) ?? new string[0],
                    schemaVersion = ChessActionEncoder.SchemaVersion, behavior = ChessActionEncoder.BehaviorName,
                    gameId = game.GameId.ToString(), initialFen = initialFen, finalFen = game.Board.ToFen(),
                    moves = new List<string>(game.Moves).ToArray(), reason = interrupted ? "TrainingPlyLimit" : game.Result.IsFinished ? game.Result.Reason.ToString() : "CurriculumTask",
                    curriculumStage = lesson, focusColor = focus.ToString(), taskResult = taskOutcome == 2 ? "NotApplicable" : taskOutcome > 0 ? "Success" : taskOutcome < 0 ? "Failure" : "Neutral",
                    winner = game.Result.Winner?.ToString() ?? "None", interrupted = interrupted,
                    whiteReward = whiteReward, blackReward = blackReward, finishedUtc = DateTime.UtcNow.ToString("O")
                };
                File.AppendAllText(_path, JsonUtility.ToJson(record) + Environment.NewLine);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            { _failed = true; Debug.LogWarning("Training game recording disabled after write failure: " + error.Message); }
        }
        [Serializable] private sealed class Record
        {
            public int schemaVersion, whiteTeam = 0, blackTeam = 1, curriculumStage;
            public string sourceGame, datasetId, historyInitialFen;
            public int sourceStartPly;
            public bool sourceMirrored;
            public string[] historyMoves;
            public string focusColor, taskResult;
            public string behavior, gameId, initialFen, finalFen, reason, winner, finishedUtc;
            public string[] moves;
            public bool interrupted;
            public float whiteReward, blackReward;
        }
    }
}

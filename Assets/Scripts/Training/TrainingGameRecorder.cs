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
        public void Write(ChessGameController game, bool interrupted, float whiteReward, float blackReward)
        {
            if (_failed) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                var record = new Record
                {
                    schemaVersion = ChessActionEncoder.SchemaVersion, behavior = ChessActionEncoder.BehaviorName,
                    gameId = game.GameId.ToString(), initialFen = BoardState.InitialFen, finalFen = game.Board.ToFen(),
                    moves = new List<string>(game.Moves).ToArray(), reason = interrupted ? "TrainingPlyLimit" : game.Result.Reason.ToString(),
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
            public int schemaVersion, whiteTeam = 0, blackTeam = 1;
            public string behavior, gameId, initialFen, finalFen, reason, winner, finishedUtc;
            public string[] moves;
            public bool interrupted;
            public float whiteReward, blackReward;
        }
    }
}

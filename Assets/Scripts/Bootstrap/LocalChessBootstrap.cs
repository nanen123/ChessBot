using System;
using System.IO;
using ChessBot.Chess.Application;
using ChessBot.Chess.Presentation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

namespace ChessBot.Bootstrap
{
    public sealed class LocalChessBootstrap : MonoBehaviour
    {
        private ChessGameController _game;
        private BoardView _view;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartLocalScene()
        {
            // Keep automatic composition confined to the existing sample scene; training scenes stay headless.
            if (SceneManager.GetActiveScene().path != "Assets/Scenes/SampleScene.unity") return;
            if (FindFirstObjectByType<LocalChessBootstrap>() != null) return;
            new GameObject("Local Chess").AddComponent<LocalChessBootstrap>();
        }
        private void Awake()
        {
            var art = Resources.Load<ChessArt>("ChessArt");
            if (art == null || !art.IsValid)
            {
                Debug.LogError("Chess artwork missing. Run ChessBot/Prepare Local Chess in the Editor.");
                enabled = false; return;
            }
            _game = new ChessGameController();
            var canvas = new GameObject("Chess Canvas", typeof(RectTransform)); canvas.transform.SetParent(transform, false);
            _view = canvas.AddComponent<BoardView>(); _view.Initialize(_game, art);
            if (EventSystem.current == null)
            {
                var events = new GameObject("Chess EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.transform.SetParent(transform, false);
            }
            _game.Finished += SaveRecord;
        }
        private void OnDestroy() { if (_game != null) _game.Finished -= SaveRecord; }
        private void SaveRecord(GameRecord record)
        {
            try
            {
                var directory = Path.Combine(UnityEngine.Application.persistentDataPath, "ChessGames");
                Directory.CreateDirectory(directory);
                var dto = new RecordFile
                {
                    gameId = record.GameId.ToString(), initialFen = record.InitialFen,
                    moves = new System.Collections.Generic.List<string>(record.Moves).ToArray(),
                    winner = record.Result.Winner?.ToString() ?? "Draw", reason = record.Result.Reason.ToString(),
                    intendedClaimMove = record.IntendedClaimMove, finishedUtc = DateTime.UtcNow.ToString("O")
                };
                File.WriteAllText(Path.Combine(directory, dto.gameId + ".json"), JsonUtility.ToJson(dto, true));
                _view.SetSaveMessage("Game result saved to ChessGames.");
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogWarning("Could not save chess record: " + exception.Message);
                _view.SetSaveMessage("Game finished, but its record could not be saved.");
            }
        }
        [Serializable]
        private sealed class RecordFile
        {
            public string gameId, initialFen, winner, reason, intendedClaimMove, finishedUtc;
            public string[] moves;
        }
    }
}

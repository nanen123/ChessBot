using System;
using UnityEngine;
namespace ChessBot.Training
{
    public static class ChessCaptureExclusions
    {
        [Serializable] private sealed class Document
        { public int version; public string datasetId, positionsSha256; public string[] hashes; }
        private static Document _cached;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ClearCache() => _cached = null;
        public static string[] LoadBundled()
        {
            if (_cached == null)
            {
                var asset = Resources.Load<TextAsset>("ChessCaptureExclusions");
                if (asset == null) throw new InvalidOperationException("Run Tools/BuildCaptureExclusions.py before training/building.");
                var data = JsonUtility.FromJson<Document>(asset.text);
                if (data == null || data.version != 1 || data.hashes == null || data.hashes.Length == 0 || data.datasetId != ChessCurriculumDataset.LoadBundled().Id)
                    throw new FormatException("Capture exclusions do not match the training dataset. Rebuild curriculum resources.");
                _cached = data;
            }
            return _cached.hashes;
        }
    }
}

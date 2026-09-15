using System;
using System.Linq;
using ChessBot.Chess.Presentation;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ChessBot.Bootstrap.Editor
{
    [InitializeOnLoad]
    public sealed class LocalChessSetup : IPreprocessBuildWithReport
    {
        private const string ArtPath = "Assets/Resources/ChessArt.asset";
        public int callbackOrder => 0;
        static LocalChessSetup() { EditorApplication.delayCall += EnsureArt; }
        public void OnPreprocessBuild(BuildReport report) { Prepare(); }
        private static void EnsureArt()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
            if (AssetDatabase.LoadAssetAtPath<ChessArt>(ArtPath) == null) Prepare();
        }
        [MenuItem("ChessBot/Prepare Local Chess")]
        public static void Prepare()
        {
            var sprites = AssetDatabase.LoadAllAssetsAtPath("Assets/Sprites/chess-pieces-sheet.png").OfType<Sprite>().ToDictionary(sprite => sprite.name);
            var board = AssetDatabase.LoadAllAssetsAtPath("Assets/Sprites/board-coordinates.png").OfType<Sprite>().FirstOrDefault();
            // Existing sheet: top row white, bottom row black; retain authored sub-asset IDs.
            int[] white = { 7, 5, 4, 6, 3, 1 }, black = { 12, 9, 8, 10, 2, 0 };
            foreach (int id in white.Concat(black))
                if (!sprites.ContainsKey("chess-pieces-sheet_" + id)) throw new InvalidOperationException("Missing chess sprite " + id);
            if (board == null) throw new InvalidOperationException("Missing board sprite.");
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            var art = AssetDatabase.LoadAssetAtPath<ChessArt>(ArtPath);
            if (art == null) { art = ScriptableObject.CreateInstance<ChessArt>(); AssetDatabase.CreateAsset(art, ArtPath); }
            var serialized = new SerializedObject(art);
            SetPieces(serialized.FindProperty("_white"), white, sprites);
            SetPieces(serialized.FindProperty("_black"), black, sprites);
            serialized.FindProperty("_board").objectReferenceValue = board;
            serialized.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(art); AssetDatabase.SaveAssets();
        }
        private static void SetPieces(SerializedProperty property, int[] ids, System.Collections.Generic.Dictionary<string, Sprite> sprites)
        {
            property.arraySize = ids.Length;
            for (int i = 0; i < ids.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = sprites["chess-pieces-sheet_" + ids[i]];
        }
    }
}

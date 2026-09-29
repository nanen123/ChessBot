using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ChessBot.Bootstrap.Editor
{
    public static class ChessEvaluationSetup
    {
        [MenuItem("ChessBot/Testing/Open Agent Test")]
        public static void OpenTest()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene("Assets/Scenes/AgentTest.unity");
        }

        // Used once to resolve the importer-generated local file ID of the bundled model.
        public static void BindBundledModel()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/AgentTest.unity", OpenSceneMode.Additive);
            try
            {
                var model = AssetDatabase.LoadMainAssetAtPath("Assets/Models/ChessV1_Test.onnx");
                if (model == null) throw new InvalidOperationException("Test ONNX model was not imported.");
                foreach (var root in scene.GetRootGameObjects())
                {
                    var host = root.GetComponent<ChessEvaluationEnvironment>();
                    if (host == null) continue;
                    var serialized = new SerializedObject(host);
                    serialized.FindProperty("_whiteModel").objectReferenceValue = model;
                    serialized.FindProperty("_blackModel").objectReferenceValue = model;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                EditorSceneManager.SaveScene(scene);
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }
    }
}

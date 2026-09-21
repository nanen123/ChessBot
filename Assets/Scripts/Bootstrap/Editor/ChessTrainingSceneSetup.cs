using System;
using System.Collections.Generic;
using System.IO;
using ChessBot.Agents;
using ChessBot.Training;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ChessBot.Bootstrap.Editor
{
    public static class ChessTrainingSceneSetup
    {
        public const string LocalScene = "Assets/Scenes/LocalPlay.unity";
        public const string TrainingScene = "Assets/Scenes/Training.unity";

        [MenuItem("ChessBot/Training/Create Missing Scenes")]
        public static void CreateScenes()
        {
            // Never overwrite authored scenes or discard an unsaved scene.
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save your open scenes before creating the chess scenes.");
            var previousSetup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                LocalChessSetup.Prepare();
                if (!File.Exists(LocalScene))
                {
                    var local = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                    new GameObject("Local Chess").AddComponent<LocalChessBootstrap>();
                    EditorSceneManager.SaveScene(local, LocalScene);
                }
                if (!File.Exists(TrainingScene))
                {
                    var training = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                    var root = new GameObject("Chess Training Environment"); root.SetActive(false);
                    var environment = root.AddComponent<ChessTrainingEnvironment>();
                    var white = CreateAgent(root.transform, "White Agent", 0);
                    var black = CreateAgent(root.transform, "Black Agent", 1);
                    environment.Configure(white, black); root.SetActive(true);
                    EditorSceneManager.SaveScene(training, TrainingScene);
                }
                var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
                if (!scenes.Exists(scene => scene.path == LocalScene)) scenes.Insert(0, new EditorBuildSettingsScene(LocalScene, true));
                // Training builds use the dedicated menu, so a normal player build starts in LocalPlay.
                if (!scenes.Exists(scene => scene.path == TrainingScene)) scenes.Add(new EditorBuildSettingsScene(TrainingScene, false));
                EditorBuildSettings.scenes = scenes.ToArray(); AssetDatabase.SaveAssets();
            }
            finally { if (previousSetup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(previousSetup); }
        }
        private static ChessAgent CreateAgent(Transform parent, string name, int team)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            var behavior = go.AddComponent<BehaviorParameters>();
            behavior.BehaviorName = ChessActionEncoder.BehaviorName; behavior.TeamId = team;
            behavior.BehaviorType = BehaviorType.Default;
            behavior.BrainParameters.VectorObservationSize = ChessObservationEncoder.ObservationCount;
            behavior.BrainParameters.NumStackedVectorObservations = 1;
            behavior.BrainParameters.ActionSpec = ActionSpec.MakeDiscrete(ChessActionEncoder.ActionCount);
            var agent = go.AddComponent<ChessAgent>(); agent.MaxStep = 0; return agent;
        }
        [MenuItem("ChessBot/Training/Build Windows Training Player")]
        public static void BuildTrainingPlayer()
        {
            CreateScenes();
            var path = Path.GetFullPath("output/chess-training/ChessTraining.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { TrainingScene }, locationPathName = path,
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.None
            });
            if (result.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Training player build failed: " + result.summary.result);
            Debug.Log("Training player built: " + path);
        }
    }
}

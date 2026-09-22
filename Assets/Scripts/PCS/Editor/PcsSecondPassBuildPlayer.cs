using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

public static partial class PcsSecondPassTools
{
    [Serializable] private sealed class DevelopmentPlayerBuildResult
    {
        public string utc, unity, target, result, outputPath;
        public bool developmentBuild, succeeded;
        public string[] scenes;
        public int errors, warnings;
        public ulong sizeBytes;
        public double durationSeconds;
        public string exceptionType;
    }

    private static void BuildDevelopmentPlayer()
    {
        var result = new DevelopmentPlayerBuildResult
        {
            utc = DateTime.UtcNow.ToString("O"), unity = Application.unityVersion,
            target = BuildTarget.StandaloneWindows64.ToString(), developmentBuild = true,
            outputPath = Path.Combine(EvidenceDirectory, "Player", "FOURPAWS.exe"), result = "NotStarted"
        };
        try
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Player build requires the idle Editor outside Play mode.");
            for (int index = 0; index < SceneManager.sceneCount; index++)
                if (SceneManager.GetSceneAt(index).isDirty)
                    throw new InvalidOperationException("Save every loaded scene before requesting the development build.");
            result.scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
            if (result.scenes.Length == 0 || result.scenes[0] != "Assets/Scenes/LobbyScene.unity" ||
                !result.scenes.Contains("Assets/Scenes/Puzzle.unity") || !result.scenes.Contains("Assets/Scenes/GameScene.unity"))
                throw new InvalidOperationException("Enabled scenes must start with LobbyScene and include GameScene and Puzzle.");
            if (result.scenes.Any(path => !File.Exists(path)))
                throw new InvalidOperationException("An enabled build scene does not exist.");
            Directory.CreateDirectory(Path.GetDirectoryName(result.outputPath));
            BuildReport build = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = result.scenes, locationPathName = result.outputPath,
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development
            });
            result.result = build.summary.result.ToString();
            result.errors = build.summary.totalErrors;
            result.warnings = build.summary.totalWarnings;
            result.sizeBytes = build.summary.totalSize;
            result.durationSeconds = build.summary.totalTime.TotalSeconds;
            result.succeeded = build.summary.result == BuildResult.Succeeded && File.Exists(result.outputPath);
        }
        catch (Exception exception)
        {
            result.result = "Exception";
            result.exceptionType = exception.GetType().Name;
            throw;
        }
        finally
        {
            Directory.CreateDirectory(EvidenceDirectory);
            File.WriteAllText(Path.Combine(EvidenceDirectory, "development-player-build.json"), JsonUtility.ToJson(result, true));
        }
        if (!result.succeeded) throw new InvalidOperationException("Development player build did not succeed; see development-player-build.json.");
    }
}

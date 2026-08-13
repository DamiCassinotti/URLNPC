#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Standalone Linux64 build of the FPS scene, invoked from the CLI with
//   -executeMethod BuildPlayer.BuildLinux
// so an ML-Agents --env run can drive the game without the editor. The
// runtime args the trainer passes (-playerDriver, -runSeed) are read by the
// game at startup, so the build itself only has to produce the player.
public static class BuildPlayer
{
    const string Scene = "Assets/Scenes/FPS.unity";
    const string OutputPath = "Builds/Linux/URLNPC.x86_64";

    public static void BuildLinux()
    {
        var options = new BuildPlayerOptions
        {
            scenes = new[] { Scene },
            locationPathName = OutputPath,
            target = BuildTarget.StandaloneLinux64,
            targetGroup = BuildTargetGroup.Standalone,
            options = BuildOptions.None,
        };

        // BuildPlayer can throw (e.g. Linux Build Support not installed) rather
        // than returning a failed report. Without this catch the exception rides
        // -quit to a 0 exit code and build-player.sh reports a phantom success.
        try
        {
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[BuildPlayer] Succeeded: {summary.outputPath} ({summary.totalSize} bytes)");
                EditorApplication.Exit(0);
            }
            else
            {
                Debug.LogError($"[BuildPlayer] {summary.result} with {summary.totalErrors} error(s).");
                EditorApplication.Exit(1);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[BuildPlayer] Build threw: {e}");
            EditorApplication.Exit(1);
        }
    }
}
#endif

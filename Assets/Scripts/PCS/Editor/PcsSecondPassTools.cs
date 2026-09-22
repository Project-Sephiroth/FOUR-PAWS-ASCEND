using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Explicit development commands for the already-open Editor. No command means no mutation.
[InitializeOnLoad]
public static partial class PcsSecondPassTools
{
    public static readonly string EvidenceDirectory = Path.Combine(Path.GetTempPath(), "FourPawsSecondPass");
    private static double nextPoll;
    [Serializable] private class Command { public string id; public string action; public string argument; }

    static PcsSecondPassTools()
    {
        EditorApplication.update += Poll;
        CompilationPipeline.assemblyCompilationFinished += (path, messages) =>
        {
            Directory.CreateDirectory(EvidenceDirectory);
            File.AppendAllLines(Path.Combine(EvidenceDirectory, "compilation.txt"),
                new[] { DateTime.UtcNow.ToString("O") + " " + Path.GetFileName(path) }
                .Concat(messages.Select(m => m.type + " " + m.file + ":" + m.line + " " + m.message)));
        };
    }

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll || EditorApplication.isCompiling || EditorApplication.isUpdating)
            return;
        nextPoll = EditorApplication.timeSinceStartup + 0.5;
        string path = Path.Combine(EvidenceDirectory, "command.json");
        if (!File.Exists(path)) return;
        Command command;
        try { command = JsonUtility.FromJson<Command>(File.ReadAllText(path)); }
        catch (IOException) { return; } // The producer may still be atomically replacing its command.
        if (command == null || string.IsNullOrEmpty(command.id) || SessionState.GetString("PCS.Command", "") == command.id)
            return;
        SessionState.SetString("PCS.Command", command.id);
        try
        {
            switch (command.action)
            {
                case "status": WriteStatus(command.id); break;
                case "layout-inspect": InvokeStep("InspectCurrentLayout"); break;
                case "layout-apply": InvokeStep("ApplyCurrentLayoutRefinement"); break;
                case "layout-resume": SessionState.SetBool("PCS.Layout.OwnedMigration", true); InvokeStep("ApplyCurrentLayoutRefinement"); break;
                case "layout-validate": InvokeStep("ValidateCurrentLayoutRefinement"); break;
                case "layout-adjust": InvokeStep("AdjustLayoutAfterPlay"); break;
                case "layout-column-tests": InvokeStep("RunLayoutColumnTests"); break;
                case "layout-movement-tests": InvokeStep("RunLayoutMovementTests"); break;
                case "layout-tutorial-tests": InvokeStep("RunLayoutTutorialTests"); break;
                case "layout-access-tests": InvokeStep("RunLayoutAccessTests"); break;
                case "layout-door-tests": InvokeStep("RunLayoutDoorTests"); break;
                case "manual-device-tests": InvokeStep("RunManualDeviceTests"); break;
                case "manual-device-window": PcsDeviceTestWindow.OpenWindow(); break;
                case "manual-normal-session": InvokeStep("RunManualNormalSession"); break;
                case "manual-direct-play": InvokeStep("RunManualDirectPlay"); break;
                case "manual-observe": ObserveManualDeviceSession(command.id); break;
                case "refresh": AssetDatabase.Refresh(); break;
                case "build":
                case "resume-owned-build":
                    throw new InvalidOperationException("전체 씬 재생성은 폐기되었습니다. 저장된 Scene·Prefab·배치를 유지하세요.");
                case "validate": InvokeStep("ValidatePuzzle"); WriteStatus(command.id); break;
                case "configure-camera": InvokeStep("ConfigureCameraRegions"); WriteStatus(command.id); break;
                case "validate-camera": InvokeStep("ValidateCameraRegions"); break;
                case "play-tests": InvokeStep("BeginPlayTests"); break;
                case "device-tests": InvokeStep("BeginDeviceContractTests"); break;
                case "shared-device-tests": InvokeStep("BeginSharedDeviceContractTests"); break;
                case "shared-play-tests": InvokeStep("BeginSharedPlayTests"); break;
                case "build-player": InvokeStep("BuildDevelopmentPlayer"); break;
                case "play":
                    if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Already playing.");
                    if (SceneManager.GetActiveScene().isDirty) throw new InvalidOperationException("Save scene before Play command.");
                    EditorSceneManager.OpenScene("Assets/Scenes/LobbyScene.unity");
                    EditorApplication.EnterPlaymode();
                    break;
                case "stop": EditorApplication.ExitPlaymode(); break;
                case "capture":
                    ScreenCapture.CaptureScreenshot(Path.Combine(EvidenceDirectory, command.id + ".png"));
                    break;
                case "render-audit": InvokeStep("RenderAuditViews"); break;
                default: throw new InvalidOperationException("Unknown PCS development command: " + command.action);
            }
            File.WriteAllText(Path.Combine(EvidenceDirectory, command.id + ".done"), DateTime.UtcNow.ToString("O") + " " + command.action);
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(EvidenceDirectory, command.id + ".failed"), ex.ToString());
            Debug.LogException(ex);
        }
    }

    private static void InvokeStep(string name)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play before editing assets.");
        var method = typeof(PcsSecondPassTools).GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (method == null) throw new InvalidOperationException("PCS step has not compiled yet: " + name);
        method.Invoke(null, null);
    }

    private static void WriteStatus(string id)
    {
        Directory.CreateDirectory(EvidenceDirectory);
        var scene = SceneManager.GetActiveScene();
        var lines = new System.Collections.Generic.List<string>
        {
            DateTime.UtcNow.ToString("O"), "Unity=" + Application.unityVersion,
            "scene=" + scene.path, "dirty=" + scene.isDirty,
            "playing=" + EditorApplication.isPlaying, "compiling=" + EditorApplication.isCompiling,
            "buildScenes=" + string.Join(",", EditorBuildSettings.scenes.Select(s => s.path)),
            "gameObjects=" + UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length
        };
        File.WriteAllLines(Path.Combine(EvidenceDirectory, id + "-status.txt"), lines);
    }

    [MenuItem("Tools/FOUR PAWS/Validate Second Pass")]
    private static void ValidateMenu() { InvokeStep("ValidatePuzzle"); }

    private static void RenderAuditViews()
    {
        Camera source = Camera.main;
        if (source == null) throw new InvalidOperationException("The open scene needs its camera.");
        var temporary = new GameObject("PCS temporary audit camera") { hideFlags = HideFlags.HideAndDontSave };
        Camera camera = temporary.AddComponent<Camera>();
        camera.CopyFrom(source);
        camera.enabled = false;
        camera.orthographic = true;
        camera.aspect = 16f / 9f;
        var texture = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
        var pixels = new Texture2D(1600, 900, TextureFormat.RGB24, false);
        RenderTexture previous = RenderTexture.active;
        try
        {
            string[] names = { "stage-one", "tutorials", "shaft", "corridor" };
            Vector3[] views = { new Vector3(0f, 1f, 7f), new Vector3(-31f, 13f, 22f),
                new Vector3(0f, 37f, 25f), new Vector3(-12f, 7.5f, 10f) };
            for (int i = 0; i < names.Length; i++)
            {
                camera.transform.SetPositionAndRotation(new Vector3(views[i].x, views[i].y, -10f), Quaternion.identity);
                camera.orthographicSize = views[i].z;
                var request = new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest { destination = texture };
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera, request);
                RenderTexture.active = texture;
                pixels.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
                pixels.Apply();
                File.WriteAllBytes(Path.Combine(EvidenceDirectory, "scene-" + names[i] + ".png"), pixels.EncodeToPNG());
            }
        }
        finally
        {
            RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(pixels);
            UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(temporary);
        }
    }
}

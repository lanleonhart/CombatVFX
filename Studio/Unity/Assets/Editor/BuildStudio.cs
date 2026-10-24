// Batch build of the Effects Studio: Unity -batchmode -projectPath . -executeMethod BuildStudio.Build -quit
// Makes the materials the studio needs (so their shaders ship in the player), a scene with the Boot component, and a
// Windows 64-bit player under ..\build.
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class BuildStudio
{
    public static void Build()
    {
        Directory.CreateDirectory("Assets/Resources");
        Material("GNFX_Additive", "Legacy Shaders/Particles/Additive");
        Material("GNFX_Blended", "Legacy Shaders/Particles/Alpha Blended");
        Material("GNFX_Unlit", "Unlit/Color");
        // Shaders the game's effect materials reference; a built-in shader ships in the player only when something uses it.
        Material("GNFX_AdditiveSoft", "Legacy Shaders/Particles/Additive (Soft)");
        Material("GNFX_Premultiply", "Legacy Shaders/Particles/Alpha Blended Premultiply");
        Material("GNFX_Multiply", "Legacy Shaders/Particles/Multiply");
        Material("GNFX_UnlitTransparent", "Unlit/Transparent");
        Material("GNFX_Bloom", "GNFX/Bloom");
        Material("GNFX_Sky", "Skybox/Procedural");
        Material("GNFX_Ground", "Standard");
        AssetDatabase.SaveAssets();

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var go = new GameObject("Studio");
        go.AddComponent<GNFX.Boot>();
        EditorSceneManager.SaveScene(scene, "Assets/Studio.unity");

        PlayerSettings.productName = "Effects Studio";
        PlayerSettings.companyName = "GN Wars";
        PlayerSettings.runInBackground = true;
        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.resizableWindow = true;
        PlayerSettings.displayResolutionDialog = ResolutionDialogSetting.Disabled;   // no "Configuration" window before the studio opens
        PlayerSettings.defaultScreenWidth = 1600; PlayerSettings.defaultScreenHeight = 900;

        string outDir = Path.GetFullPath("../build");
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-out") outDir = Path.GetFullPath(args[i + 1]);   // build somewhere else while the app is open
        Directory.CreateDirectory(outDir);
        var report = BuildPipeline.BuildPlayer(new[] { "Assets/Studio.unity" }, Path.Combine(outDir, "EffectsStudio.exe"), BuildTarget.StandaloneWindows64, BuildOptions.None);
        Debug.Log("[BuildStudio] result " + report.summary.result + ", " + report.summary.totalErrors + " errors, " + report.summary.totalSize / 1048576 + " MB");
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorApplication.Exit(2);
    }

    private static void Material(string name, string shader)
    {
        string path = "Assets/Resources/" + name + ".mat";
        if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) return;
        Shader sh = Shader.Find(shader);
        if (sh == null) { Debug.LogError("[BuildStudio] shader not found: " + shader); return; }
        var m = new Material(sh);
        if (name == "GNFX_Ground") { m.color = new Color(0.55f, 0.55f, 0.5f); m.SetFloat("_Glossiness", 0.1f); }
        AssetDatabase.CreateAsset(m, path);
    }
}

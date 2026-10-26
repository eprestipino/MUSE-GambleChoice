using System;
using System.IO;
using System.Linq;
using System.Globalization;
using Newtonsoft.Json;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using GambleChoice;

public static class GambleChoiceSetup
{
    static string Root => GambleChoicePaths.WorkingFolder;
    const string Folder = "Assets/_USE_Tasks/GambleChoice";
    [MenuItem("USE/Gamble Choice/Create scenes and defaults")]
    public static void Setup()
    {
        string task = Folder + "/GambleChoice.unity";
        if (!File.Exists(task))
        {
            TaskCreation.CreateTaskScene("GambleChoice");
            var camera = GameObject.Find("GambleChoice_Camera").GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = GambleView.Parse("#17212B");
            var canvas = new GameObject("GambleChoice_Canvas", typeof(Canvas), typeof(UnityEngine.UI.GraphicRaycaster));
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceCamera;
            canvas.GetComponent<Canvas>().worldCamera = camera;
            GameObject.FindObjectOfType<GambleChoice_TaskLevel>().TaskCanvasses = new[] { canvas.GetComponent<Canvas>() };
            GameObject.Find("Directional Light").name = "GambleChoice_DirectionalLight";
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), task);
        }
        EnsureTaskCanvas();
        string demo = Folder + "/GambleChoiceDemo.unity";
        if (!File.Exists(demo))
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            new GameObject("GambleDemo", typeof(GambleDemo));
            EditorSceneManager.SaveScene(scene, demo);
        }
        if (!EditorBuildSettings.scenes.Any(s => s.path == task))
            EditorBuildSettings.scenes = EditorBuildSettings.scenes.Concat(new[] { new EditorBuildSettingsScene(task, true) }).ToArray();
        var folder = Path.Combine(Root, "DevConfigs/GambleChoice");
        Directory.CreateDirectory(folder);
        var td = Path.Combine(folder, "GambleChoice_TaskDef_json.json");
        if (!File.Exists(td))
            File.WriteAllText(td, JsonConvert.SerializeObject(new
            {
                FeedbackControllers = new[] { "Audio" },
                TotalTokensNum = 6,
                TouchFeedbackDuration = .3,
                Gamble = new GambleConfig()
            }, Formatting.Indented));
        var bd = Path.Combine(folder, "GambleChoice_BlockDef_array.txt");
        if (!File.Exists(bd))
            File.WriteAllText(bd, "BlockName\tNumTrials\tChoiceCount\tRenderer\nTwoChoices\t12\t2\tBar\nTriangle\t6\t3\tBar\nQuadrants\t6\t4\tBar\n");
        var ec = Path.Combine(folder, "GambleChoice_EventCodeConfig_json.json");
        if (!File.Exists(ec))
        {
            var names = new[] { "OfferVisibleSoftware", "ChoiceCommitted", "OutcomeResolved", "RevealVisibleSoftware", "RewardRequested", "RewardSettled", "ZeroReward", "TrialFinalized", "LedgerSettled" };
            var codes = new System.Collections.Generic.Dictionary<string, object>();
            for (int i = 0; i < names.Length; i++)
                codes[names[i]] = new
                {
                    Value = 12360 + i,
                    Description = "GambleChoice " + names[i]
                };
            File.WriteAllText(ec, JsonConvert.SerializeObject(codes, Formatting.Indented));
        }
        var session = Path.Combine(Root, "DevConfigs/SessionConfig_singleType.txt");
        if (!File.Exists(session))
        {
            foreach (string source in Directory.GetFiles(GambleChoicePaths.TemplateFolder))
                File.Copy(source, Path.Combine(Root, "DevConfigs", Path.GetFileName(source)));
            var defaults = File.ReadAllText(session)
                .Replace("__TASK_ICONS__", Path.Combine(Root, "DevConfigs/TaskIcons").Replace('\\', '/'))
                .Replace("__CONTEXTS__", Path.Combine(Root, "DevConfigs/Contexts").Replace('\\', '/'));
            File.WriteAllText(session, defaults);
        }
        var lines = File.ReadAllLines(session);
        for (int i = 0; i < lines.Length; i++)
        if ((lines[i].Contains("\tTaskMappings\t") || lines[i].Contains("\tTaskIcons\t")) && !lines[i].Contains("GambleChoice:"))
            lines[i] = lines[i].Replace("}", ", GambleChoice:GambleChoice}");
        File.WriteAllLines(session, lines);
        string icon = Path.Combine(Root, "DevConfigs/TaskIcons/GambleChoice.png");
        Directory.CreateDirectory(Path.GetDirectoryName(icon));
        if (!File.Exists(icon))
        {
            var tex = new Texture2D(128, 128);
            var pixels = new Color[128 * 128];
            for (int y = 0; y < 128; y++)
            for (int x = 0; x < 128; x++)
                pixels[y * 128 + x] = x > 25 && x < 52 || x > 76 && x < 103 ? (y > 64 ? new Color(.4f, .65f, .8f) : new Color(.8f, .55f, .3f)) : new Color(.09f, .13f, .17f);
            tex.SetPixels(pixels);
            tex.Apply();
            File.WriteAllBytes(icon, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }
        AssetDatabase.Refresh();
        Debug.Log("GambleChoice setup complete.");
    }
    [MenuItem("USE/Gamble Choice/Build demo")]
    public static void BuildDemo()
    {
        Setup();
        Build(true);
    }
    public static void EnsureTaskCanvas()
    {
        string path = Folder + "/GambleChoice.unity";
        var scene = EditorSceneManager.OpenScene(path);
        var canvas = GameObject.Find("GambleChoice_Canvas").GetComponent<Canvas>();
        var level = UnityEngine.Object.FindObjectOfType<GambleChoice_TaskLevel>();
        bool changed = false;
        if (canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>() == null)
        {
            canvas.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            changed = true;
        }
        if (level.TaskCanvasses == null || !level.TaskCanvasses.Contains(canvas))
        {
            level.TaskCanvasses = new[] { canvas };
            changed = true;
        }
        if (changed) EditorSceneManager.SaveScene(scene, path);
    }
    public static void CheckAndBuildDemo()
    {
        GambleChoiceChecks.RunAll();
        Setup();
        Build(true);
    }
    [MenuItem("USE/Gamble Choice/Build full suite")]
    public static void BuildSuite()
    {
        Setup();
        Build(false);
    }
    public static void CheckAndBuildSuite()
    {
        GambleChoiceChecks.RunAll();
        Setup();
        Build(false);
    }
    static void Build(bool demo)
    {
        var now = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "Central Standard Time");
        string name = "MUSE_" + now.Month + "_" + now.Day + " " + now.ToString("h-mmtt", CultureInfo.InvariantCulture).ToLowerInvariant();
        string dest = Path.Combine(Root, "Builds", name);
        if (Directory.Exists(dest))
            throw new IOException("Build exists: " + dest);
        Directory.CreateDirectory(dest);
        var scenes = demo ? new[] { Folder + "/GambleChoiceDemo.unity" } : EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        var report = BuildPipeline.BuildPlayer(scenes, Path.Combine(dest, name + ".exe"), BuildTarget.StandaloneWindows64, BuildOptions.Development);
        File.WriteAllText(Path.Combine(Root, "Builds", "gamble-build-status.txt"), report.summary.result + "\n" + dest + "\nErrors: " + report.summary.totalErrors);
        if (report.summary.result != BuildResult.Succeeded)
            throw new Exception("GambleChoice build failed");
    }
}

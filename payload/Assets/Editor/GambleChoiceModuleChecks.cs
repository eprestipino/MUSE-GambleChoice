using System;
using System.IO;
using System.Linq;
using GambleChoice;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Portable software checks. No injected input is ever routed into participant data.
public static class GambleChoiceModuleChecks
{
    static int assertions;
    static void Check(bool value, string message)
    {
        assertions++;
        if (!value) throw new Exception("Module check: " + message);
    }

    [MenuItem("USE/Gamble Choice/Validate installed module")]
    public static void Run()
    {
        string folder = Path.Combine(GambleChoicePaths.ProjectFolder, "UserSettings/GambleChoiceModule");
        Directory.CreateDirectory(folder);
        string token = Environment.GetEnvironmentVariable("GAMBLE_VALIDATION_ID");
        assertions = 0;
        try
        {
            Check(Application.unityVersion == "2020.3.40f1", "supported Unity version");
            Check(USE_Tasks_CustomTypes.CustomTaskDictionary.ContainsKey("GambleChoice"), "registry entry");
            Check(USE_Tasks_CustomTypes.CustomTaskDictionary["GambleChoice"].TaskLevelType == typeof(GambleChoice_TaskLevel), "registered task type");
            Check(EditorBuildSettings.scenes.Any(s => s.enabled && s.path.EndsWith("/GambleChoice.unity")), "enabled task scene");
            GambleChoiceChecks.OutputOverride = Path.Combine(folder, "model");
            GambleChoiceChecks.RunAll();
            CheckSignedSettlement();
            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                EditorSceneManager.OpenScene("Assets/_USE_Tasks/GambleChoice/GambleChoice.unity");
                var level = UnityEngine.Object.FindObjectOfType<GambleChoice_TaskLevel>();
                // M-USE assigns TaskCam during session task loading, not in the scene asset.
                var cameraObject = GameObject.Find("GambleChoice_Camera");
                var camera = cameraObject == null ? null : cameraObject.GetComponent<Camera>();
                Check(level != null && camera != null, "task scene camera");
                var canvas = level.TaskCanvasses.FirstOrDefault(c => c != null && c.name == "GambleChoice_Canvas");
                Check(canvas != null && canvas.worldCamera == camera, "registered camera canvas");
                Check(canvas.GetComponent<GraphicRaycaster>() != null, "canvas raycaster");
                foreach (string renderer in GambleRepresentations.Names)
                foreach (int choices in new[] { 2, 3, 6 })
                {
                    var config = GamblePresets.Create("Mixed", choices, renderer);
                    config.PhysicalReward = false;
                    config.Validate();
                    var protocol = new GamblePreparedProtocol(config);
                    var engine = new GambleEngine();
                    engine.BeginPrepared(protocol, 0, 0);
                    Check(engine.Result.Options.Length == choices, "resolved choice count " + renderer);
                    var gameObject = new GameObject("ModuleValidation", typeof(RectTransform), typeof(CanvasRenderer), typeof(GambleView));
                    try
                    {
                        gameObject.transform.SetParent(canvas.transform, false);
                        var rect = gameObject.GetComponent<RectTransform>();
                        rect.anchorMin = Vector2.zero;
                        rect.anchorMax = Vector2.one;
                        rect.offsetMin = rect.offsetMax = Vector2.zero;
                        var view = gameObject.GetComponent<GambleView>();
                        view.Engine = engine;
                        view.Now = 0;
                        view.Refresh();
                        Canvas.ForceUpdateCanvases();
                        Check(gameObject.GetComponentsInChildren<Text>().All(t => !string.IsNullOrEmpty(t.text)), "populated labels " + renderer);
                    }
                    finally { UnityEngine.Object.DestroyImmediate(gameObject); }
                }
            }
            finally
            {
                if (setup.Any(s => s.isLoaded && s.isActive)) EditorSceneManager.RestoreSceneManagerSetup(setup);
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            string export = Path.Combine(folder, "export-" + Guid.NewGuid().ToString("N"));
            GambleCompiler.Export(GamblePresets.Create("Mixed"), export);
            Check(File.Exists(Path.Combine(export, "COMPLETE.txt")), "portable compiler export");
            Check(File.Exists(Path.Combine(export, "TaskIcons/GambleChoice.png")), "exported task icon");
            Check(!File.ReadAllText(Path.Combine(export, "SessionConfig_singleType.txt")).Contains("__TASK_ICONS__"), "resolved template paths");
            // A ready-to-select demo configuration, outside Assets and never removed on disconnect.
            string example = Path.Combine(GambleChoicePaths.ProjectFolder, "GambleChoiceConfigs");
            if (!Directory.Exists(example)) GambleCompiler.Export(GamblePresets.Create("Mixed"), example);
            File.WriteAllText(Path.Combine(folder, "checks.json"), JsonConvert.SerializeObject(new
            {
                passed = true, validationId = token, assertions, unity = Application.unityVersion,
                model = "model/csharp-checks.json", configurations = example,
                nativeInputQualified = false, apparatusQualified = false
            }, Formatting.Indented));
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(folder, "checks.json"), JsonConvert.SerializeObject(new
            {
                passed = false, validationId = token, assertions, error = error.ToString()
            }, Formatting.Indented));
            throw;
        }
    }

    static void CheckSignedSettlement()
    {
        foreach (int amount in Enumerable.Range(-6, 13))
        foreach (int choices in Enumerable.Range(2, 5))
        {
            var config = GamblePresets.Create("Mixed", choices);
            config.Design.PositionPolicy = "Fixed";
            config.ShufflePositions = false;
            foreach (var option in config.Options)
                option.Outcomes = new[] { new GambleOutcome { Id = "certain", TokenDelta = amount, Probability = 1 } };
            var engine = new GambleEngine();
            int settlements = 0;
            engine.Event = record => { if (record.Type == "LedgerSettled") settlements++; };
            engine.Begin(config, 0, 0);
            engine.Press(0, 0);
            engine.Tick(config.HoldMs, 0, true);
            Check(engine.Result.Tokens == amount, "committed signed outcome");
            engine.Tick(config.RevealMs);
            engine.Tick(config.RevealMs + config.HighlightMs);
            double settled = config.RevealMs + config.HighlightMs + config.TransferMs;
            engine.Tick(settled);
            engine.Tick(settled + config.RewardWindowMs);
            engine.Tick(settled + config.RewardWindowMs + config.ItiMs);
            engine.Tick(settled + config.RewardWindowMs + config.ItiMs + 1);
            Check(engine.Done && engine.Result.Status == "Completed", "completed signed trial");
            Check(engine.Result.BalanceAfter == engine.Result.BalanceBefore + amount, "signed bank arithmetic");
            Check(settlements == 1 && engine.Result.RequestedPulses == 0, "one virtual settlement, no physical reward");
        }
    }
}

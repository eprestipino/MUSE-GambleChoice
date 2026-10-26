using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace GambleChoice
{
    public sealed class GambleDemo : MonoBehaviour
    {
        GambleRuntime runtime; GambleConfig config; int index, block = 1, presetIndex; string error;
        string smokePath; int smokeCompleted; double smokeStarted; bool smokeFinished;
        readonly string[] presets = { "Mixed", "Gain", "Loss", "CertaintyEquivalent" };
        void Start()
        {
            Application.runInBackground = true;
            Application.targetFrameRate = 60;
            Cursor.visible = true;
            config = GamblePresets.Create("Mixed");
            Camera.main.clearFlags = CameraClearFlags.SolidColor;
            Camera.main.backgroundColor = GambleView.Parse(config.Background);
            var args = Environment.GetCommandLineArgs();
            try
            {
                for (int i = 0; i < args.Length - 1; i++)
                {
                    if (args[i] == "--gamble-config")
                        config = GambleProtocol.Read(File.ReadAllText(args[i + 1]));
                    if (args[i] == "--gamble-smoke")
                        smokePath = Path.GetFullPath(args[i + 1]);
                }
                config.PhysicalReward = false;
                config.Validate();
            }
            catch (Exception ex) { error = ex.Message; }
            runtime = gameObject.AddComponent<GambleRuntime>();
            string output = smokePath == null ? Path.Combine(Application.persistentDataPath, "GambleDemo") : Path.Combine(smokePath, "validation-data");
            runtime.Initialize(output, null, false);
            smokeStarted = Time.realtimeSinceStartup;
            if (smokePath != null)
                runtime.Completed = r => { if (r.Status != "Completed") error = "Smoke trial did not complete: " + r.Status; smokeCompleted++; };
        }
        void NewPreset(int choices, string renderer)
        {
            config = GamblePresets.Create(presets[presetIndex], choices, renderer);
            block++;
            index = 0;
            error = null;
        }
        void OnGUI()
        {
            if (runtime == null)
                return;
            GUI.depth = -100;
            GUI.skin.label.fontSize = 16;
            GUI.skin.button.fontSize = 16;
            bool running = runtime.Engine.Result != null && !runtime.Engine.Done;
            if (!running)
            {
                GUI.Box(new Rect(20, 160, 540, 350), "Gamble Choice — virtual token demonstration");
                if (GUI.Button(new Rect(40, 200, 240, 35), "Preset: " + presets[presetIndex]))
                {
                    presetIndex = (presetIndex + 1) % presets.Length;
                    NewPreset(config.ChoiceCount, config.Renderer);
                }
                if (GUI.Button(new Rect(300, 200, 240, 35), "Choices: " + config.ChoiceCount))
                    NewPreset(config.ChoiceCount == 6 ? 2 : config.ChoiceCount + 1, config.Renderer);
                if (GUI.Button(new Rect(40, 245, 240, 35), "View: " + config.Renderer))
                {
                    var names = GambleRepresentations.Names;
                    string next = names[(Array.IndexOf(names, config.Renderer) + 1) % names.Length];
                    NewPreset(config.ChoiceCount, next);
                }
                if (GUI.Button(new Rect(300, 245, 240, 35), "New block / reset bank"))
                {
                    block++;
                    index = 0;
                }
                if (GUI.Button(new Rect(40, 300, 500, 45), "START — touch and hold a choice"))
                    try
                    {
                        runtime.Begin(config, index++, block);
                        error = null;
                    }
                    catch (Exception ex) { error = ex.Message; }
                GUI.Label(new Rect(40, 360, 500, 130), "Mouse or touch: hold " + config.HoldMs + " ms. Esc aborts.\nKeys 1–6 inject DEMO holds and are labelled in data.\nLoaded JSON retains explicit options and trial tables.\nNo physical reward commands.\nData: " + runtime.OutputPath);
            }
            else if (GUI.Button(new Rect(20, 20, 140, 35), "Abort trial"))
                runtime.Abort("OperatorAbort");
            var r = runtime.Engine.Result;
            if (r != null)
                GUI.Label(new Rect(20, 65, 600, 70), "Trial " + (r.TrialIndex + 1) + " | " + runtime.Engine.Phase + " | " + r.Status + "\n" + (r.ChosenId ?? "No committed choice") + (r.RevealActualMs > 0 && r.Tokens.HasValue ? " | Change " + r.Tokens + " tokens" : ""));
            if (error != null || runtime.Error != null)
                GUI.Label(new Rect(580, 160, 700, 300), error ?? runtime.Error);
        }
        void Update()
        {
            if (smokePath != null && runtime != null && !smokeFinished)
            {
                try
                {
                    if (error != null || runtime.Error != null || Time.realtimeSinceStartup - smokeStarted > 120)
                    {
                        FinishSmoke(false);
                        return;
                    }
                    if (smokeCompleted >= 30)
                    {
                        FinishSmoke(true);
                        return;
                    }
                    if (runtime.Engine.Result == null || runtime.Engine.Done)
                    {
                        int n = 2 + smokeCompleted % 5;
                        config = GamblePresets.Create("Mixed", n, GambleRepresentations.Names[smokeCompleted / 5]);
                        config.Ledger.InitialBalance = 1000;
                        config.HoldMs = 300;
                        config.DeadlineMs = config.RevealMs = 1000;
                        config.HighlightMs = 100;
                        config.TransferMs = 200;
                        config.RewardWindowMs = 100;
                        config.ItiMs = 50;
                        if (smokeCompleted % 2 == 1)
                            config.AmountRenderer = "Numeric";
                        runtime.Begin(config, smokeCompleted, 1);
                        runtime.ValidationHold(n - 1);
                    }
                }
                catch (Exception ex) { error = ex.ToString(); FinishSmoke(false); }
                return;
            }
            if (Input.GetKeyDown(KeyCode.Escape))
                runtime?.Abort("OperatorEscape");
            for (int i = 0; i < 6; i++)
            if (Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + i)) && runtime?.Engine?.Result != null && i < runtime.Engine.Result.Options.Length)
                runtime.ValidationHold(i);
        }
        void FinishSmoke(bool passed)
        {
            smokeFinished = true;
            Directory.CreateDirectory(smokePath);
            File.WriteAllText(Path.Combine(smokePath, "player-smoke.json"), JsonConvert.SerializeObject(new
            {
                Passed = passed,
                Completed = smokeCompleted,
                Error = error ?? runtime.Error,
                Output = runtime.OutputPath,
                Input = "ValidationInjected",
                NativeInputQualified = false,
                ElapsedSeconds = Time.realtimeSinceStartup - smokeStarted
            }, Formatting.Indented));
            Application.Quit(passed ? 0 : 1);
        }
    }
}

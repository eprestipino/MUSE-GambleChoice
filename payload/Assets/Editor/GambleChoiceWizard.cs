using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using GambleChoice;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public sealed class GambleChoiceWizard : EditorWindow
{
    GambleConfig config; string json, error, exportPath, preset = "Mixed"; Vector2 scroll; int choiceCount = 3, rendererIndex = 2, previewIndex;
    RenderTexture preview; UnityEngine.SceneManagement.Scene previewScene; Camera previewCamera; GambleView view; GambleEngine engine; bool showJson;
    [MenuItem("USE/Gamble Choice/Setup wizard")]
    static void Open()
    {
        GetWindow<GambleChoiceWizard>("Gamble Choice Setup");
    }
    void OnEnable()
    {
        config = GamblePresets.Create("Mixed");
        exportPath = Path.Combine(GambleChoicePaths.ProjectFolder, "GambleProtocol_New");
        Sync();
    }
    void OnDisable()
    {
        if (previewCamera != null)
            DestroyImmediate(previewCamera.gameObject);
        if (previewScene.IsValid())
            EditorSceneManager.ClosePreviewScene(previewScene);
        if (preview != null)
        {
            preview.Release();
            DestroyImmediate(preview);
        }
    }
    void Sync()
    {
        json = JsonConvert.SerializeObject(config, Formatting.Indented);
        ValidateAndPreview();
    }
    void ValidateAndPreview()
    {
        try
        {
            config.Validate();
            GambleProtocol.Compile(config);
            error = null;
            RenderPreview();
        }
        catch (Exception ex) { error = ex.Message; }
    }
    void RenderPreview()
    {
        if (previewCamera == null)
        {
            previewScene = EditorSceneManager.NewPreviewScene();
            var camGo = new GameObject("GamblePreviewCamera", typeof(Camera));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camGo, previewScene);
            previewCamera = camGo.GetComponent<Camera>();
            previewCamera.scene = previewScene;
            previewCamera.orthographic = true;
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = GambleView.Parse(config.Background);
            previewCamera.transform.position = new Vector3(0, 0, -10);
            preview = new RenderTexture(960, 540, 24);
            previewCamera.targetTexture = preview;
            var canvasGo = new GameObject("PreviewCanvas", typeof(Canvas));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(canvasGo, previewScene);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = previewCamera;
            canvas.planeDistance = 1;
            var go = new GameObject("PreviewDisplay", typeof(RectTransform), typeof(CanvasRenderer), typeof(GambleView));
            go.transform.SetParent(canvasGo.transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            view = go.GetComponent<GambleView>();
            view.raycastTarget = false;
        }
        engine = new GambleEngine();
        engine.Begin(config, Math.Max(0, Math.Min(config.Trials - 1, previewIndex)), 1);
        view.Engine = engine;
        Canvas.ForceUpdateCanvases();
        view.Refresh();
        Canvas.ForceUpdateCanvases();
        previewCamera.Render();
    }
    void OnGUI()
    {
        if (config == null)
            return;
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.LabelField("Gamble Choice protocol setup", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Demonstration profiles use a virtual endowed bank. Participant protocols require your protocol ID and qualification reference. Preview is a Unity camera render; native input and apparatus require separate qualification.", MessageType.Info);
        string[] presets = { "Mixed", "Gain", "Loss", "CertaintyEquivalent" };
        int p = Array.IndexOf(presets, preset);
        preset = presets[EditorGUILayout.Popup("New preset", Math.Max(0, p), presets)];
        choiceCount = EditorGUILayout.IntSlider("New preset choices", preset == "CertaintyEquivalent" ? 2 : choiceCount, 2, 6);
        rendererIndex = EditorGUILayout.Popup("New preset display", rendererIndex, GambleRepresentations.Names);
        if (GUILayout.Button("Create preset — replaces current protocol"))
        {
            config = GamblePresets.Create(preset, choiceCount, GambleRepresentations.Names[rendererIndex]);
            choiceCount = config.ChoiceCount;
            Sync();
        }
        EditorGUI.BeginChangeCheck();
        config.Renderer = GambleRepresentations.Names[EditorGUILayout.Popup("Current default display", Math.Max(0, Array.IndexOf(GambleRepresentations.Names, config.Renderer)), GambleRepresentations.Names)];
        config.Trials = EditorGUILayout.IntField("Trials per block", config.Trials);
        config.Seed = EditorGUILayout.IntField("Seed", config.Seed);
        config.Design.PracticeTrials = EditorGUILayout.IntField("Initial practice trials", config.Design.PracticeTrials);
        config.HoldMs = EditorGUILayout.DoubleField("Hold (ms)", config.HoldMs);
        config.DeadlineMs = EditorGUILayout.DoubleField("Choice deadline (ms)", config.DeadlineMs);
        config.RevealMs = EditorGUILayout.DoubleField("Reveal from onset (ms)", config.RevealMs);
        config.HighlightMs = EditorGUILayout.DoubleField("Highlight (ms)", config.HighlightMs);
        config.TransferMs = EditorGUILayout.DoubleField("Transfer (ms)", config.TransferMs);
        config.RewardWindowMs = EditorGUILayout.DoubleField("Settlement window (ms)", config.RewardWindowMs);
        config.ItiMs = EditorGUILayout.DoubleField("ITI (ms)", config.ItiMs);
        config.Ledger.InitialBalance = EditorGUILayout.IntField("Endowment (tokens)", config.Ledger.InitialBalance);
        config.Ledger.InsufficientFunds = EditorGUILayout.Popup("Funding policy", config.Ledger.InsufficientFunds == "Stop" ? 0 : 1, new[] { "Stop", "AllowDebt" }) == 0 ? "Stop" : "AllowDebt";
        config.Ledger.Scope = EditorGUILayout.Popup("Bank resets", config.Ledger.Scope == "Block" ? 0 : 1, new[] { "Each block", "Each task" }) == 0 ? "Block" : "Task";
        config.AmountRenderer = EditorGUILayout.Popup("Amount display", config.AmountRenderer == "TokenSlots" ? 0 : 1, new[] { "TokenSlots", "Numeric" }) == 0 ? "TokenSlots" : "Numeric";
        config.GridCells = EditorGUILayout.IntField("Grid denominator", config.GridCells);
        config.Design.Order = EditorGUILayout.Popup("Condition order", config.Design.Order == "Sequential" ? 0 : 1, new[] { "Sequential", "Randomized" }) == 0 ? "Sequential" : "Randomized";
        string[] positions = { "Independent", "Balanced", "Fixed" };
        config.Design.PositionPolicy = positions[EditorGUILayout.Popup("Choice positions", Math.Max(0, Array.IndexOf(positions, config.Design.PositionPolicy)), positions)];
        config.Design.PalettePolicy = EditorGUILayout.Popup("Palette policy", config.Design.PalettePolicy == "Balanced" ? 1 : 0, new[] { "Configured", "Balanced" }) == 0 ? "Configured" : "Balanced";
        config.Purpose = EditorGUILayout.Toggle("Participant protocol", config.Purpose == "Participant") ? "Participant" : "Demonstration";
        config.ProtocolId = EditorGUILayout.TextField("Protocol ID", config.ProtocolId);
        config.QualificationReference = EditorGUILayout.TextField("Qualification reference", config.QualificationReference);
        if (EditorGUI.EndChangeCheck())
            Sync();
        if (config.Options != null)
        {
            EditorGUILayout.LabelField("Outcomes (probabilities are fractions)", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            foreach (var o in config.Options)
            {
                o.Id = EditorGUILayout.TextField("Choice ID", o.Id);
                foreach (var x in o.Outcomes)
                {
                    EditorGUILayout.BeginHorizontal();
                    x.Id = EditorGUILayout.TextField(x.Id);
                    x.TokenDelta = EditorGUILayout.IntField(x.TokenDelta);
                    x.Probability = EditorGUILayout.DoubleField(x.Probability);
                    EditorGUILayout.EndHorizontal();
                }
            }
            if (EditorGUI.EndChangeCheck())
                Sync();
        }
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Import JSON"))
        {
            string path = EditorUtility.OpenFilePanel("Import protocol or TaskDef", "", "json");
            if (path.Length > 0)
                Try(() => { config = ReadConfig(File.ReadAllText(path)); Sync(); });
        }
        if (GUILayout.Button("Import trial CSV"))
        {
            string path = EditorUtility.OpenFilePanel("Import resolved trials", "", "csv");
            if (path.Length > 0)
                Try(() => { config = GambleTrialCsv.Import(config, File.ReadAllText(path)); Sync(); });
        }
        if (GUILayout.Button("Export trial CSV"))
        {
            string path = EditorUtility.SaveFilePanel("Export resolved trials", "", "resolved-trials.csv", "csv");
            if (path.Length > 0)
                Try(() => { if (File.Exists(path)) throw new IOException("Choose a new filename to preserve existing work."); File.WriteAllText(path, GambleTrialCsv.Export(GambleProtocol.Compile(config))); });
        }
        EditorGUILayout.EndHorizontal();
        showJson = EditorGUILayout.Foldout(showJson, "Advanced JSON (outcome counts, representations, adaptive bounds, trial tables)");
        if (showJson)
        {
            json = EditorGUILayout.TextArea(json, GUILayout.MinHeight(200));
            if (GUILayout.Button("Apply and validate JSON"))
                Try(() => { config = ReadConfig(json); Sync(); });
        }
        previewIndex = EditorGUILayout.IntSlider("Preview trial", previewIndex, 0, Math.Max(0, config.Trials - 1));
        if (GUILayout.Button("Refresh preview"))
            ValidateAndPreview();
        if (preview != null)
            GUILayout.Label(preview, GUILayout.Width(Math.Min(position.width - 30, 960)), GUILayout.Height(Math.Min(position.width - 30, 960) * .5625f));
        if (error != null)
            EditorGUILayout.HelpBox(error, MessageType.Error);
        exportPath = EditorGUILayout.TextField("New session folder", exportPath);
        if (GUILayout.Button("Validate, review and export session"))
            Try(() => { GambleCompiler.Export(config, exportPath); EditorUtility.RevealInFinder(Path.Combine(exportPath, "review.json")); });
        EditorGUILayout.EndScrollView();
    }
    void Try(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) { error = ex.Message; }
    }
    public static GambleConfig ReadConfig(string json) => GambleProtocol.Read(json);
    public static void BatchCompile()
    {
        var args = Environment.GetCommandLineArgs();
        string input = null, output = null, csv = null;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--gamble-input")
                input = args[i + 1];
            if (args[i] == "--gamble-output")
                output = args[i + 1];
            if (args[i] == "--gamble-csv")
                csv = args[i + 1];
        }
        if (input == null || output == null)
            throw new ArgumentException("Require --gamble-input protocol.json --gamble-output NEW_FOLDER");
        var c = ReadConfig(File.ReadAllText(input));
        if (csv != null)
            c = GambleTrialCsv.Import(c, File.ReadAllText(csv));
        GambleCompiler.Export(c, output);
        Debug.Log("Gamble protocol exported: " + output);
    }
}

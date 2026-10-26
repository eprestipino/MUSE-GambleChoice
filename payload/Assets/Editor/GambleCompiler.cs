using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GambleChoice
{
    // Repository templates and filesystem export belong to the editor, never the player model.
    public static class GambleCompiler
    {
        public static void Export(GambleConfig config, string destination)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            if (string.IsNullOrWhiteSpace(destination))
                throw new ArgumentException("Choose a new export folder.");
            destination = Path.GetFullPath(destination);
            if (destination != Path.GetPathRoot(destination))
                destination = destination.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            config.Validate();
            if (config.SchemaVersion != 2)
                throw new ArgumentException("Use the schema 1 setup tool for legacy profiles.");
            if (Directory.Exists(destination) || File.Exists(destination))
                throw new IOException("Destination already exists; choose a new folder.");

            string template = GambleChoicePaths.TemplateFolder;
            string eventTemplate = Path.Combine(template, "GambleChoice/GambleChoice_EventCodeConfig_json.json");
            foreach (string required in new[] { Path.Combine(template, "SessionConfig_singleType.txt"),
                Path.Combine(template, "SessionEventCodeConfig_json.json"), eventTemplate })
                if (!File.Exists(required))
                    throw new FileNotFoundException("Export template is missing.", required);
            var eventCodes = ReadEventCodes(eventTemplate);
            var plans = GambleProtocol.Compile(config);
            string csv = GambleTrialCsv.Export(plans);

            // A failed write leaves an inspectable partial folder without COMPLETE.txt.
            Directory.CreateDirectory(destination);
            WriteJson(Path.Combine(destination, "protocol.json"), config);
            File.WriteAllText(Path.Combine(destination, "resolved-trials.csv"), csv);
            WriteJson(Path.Combine(destination, "resolved-trials.json"), plans.Select((plan, index) => new
            {
                Trial = index,
                plan.ConditionId,
                plan.Practice,
                plan.Config.Renderer,
                plan.Options,
                AdaptiveOfferPending = config.Adaptive.Enabled
            }));
            foreach (string file in Directory.GetFiles(template))
                if (Path.GetExtension(file) == ".txt" || Path.GetExtension(file) == ".json")
                    File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
            ConfigureSession(Path.Combine(destination, "SessionConfig_singleType.txt"));
            string icons = Path.Combine(destination, "TaskIcons");
            Directory.CreateDirectory(icons);
            File.Copy(Path.Combine(template, "TaskIcons/GambleChoice.png"), Path.Combine(icons, "GambleChoice.png"));

            string task = Path.Combine(destination, "GambleChoice");
            Directory.CreateDirectory(task);
            WriteJson(Path.Combine(task, "GambleChoice_TaskDef_json.json"), new
            {
                FeedbackControllers = new[] { "Audio" },
                TotalTokensNum = 6,
                TouchFeedbackDuration = .3,
                Gamble = config
            });
            File.WriteAllText(Path.Combine(task, "GambleChoice_BlockDef_array.txt"),
                "BlockName\tNumTrials\tChoiceCount\tRenderer\nProtocol\t" + config.Trials + "\t"
                + config.ChoiceCount + "\t" + config.Renderer + "\n");
            WriteJson(Path.Combine(task, Path.GetFileName(eventTemplate)), eventCodes);
            WriteReview(config, plans, csv, Path.Combine(destination, "review.json"));

            var files = Directory.GetFiles(destination, "*", SearchOption.AllDirectories).ToDictionary(
                path => path.Substring(destination.Length + 1).Replace('\\', '/'), path => Sha256(File.ReadAllBytes(path)));
            string runtimeAssembly = typeof(GambleEngine).Assembly.Location;
            WriteJson(Path.Combine(destination, "manifest.json"), new
            {
                Files = files,
                Unity = Application.unityVersion,
                BuildGuid = Application.buildGUID,
                NativeQualification = "Not established by software export",
                AssemblyModuleVersionId = typeof(GambleEngine).Assembly.ManifestModule.ModuleVersionId,
                AssemblySha256 = Sha256(File.ReadAllBytes(runtimeAssembly)),
                ExporterAssemblySha256 = Sha256(File.ReadAllBytes(typeof(GambleCompiler).Assembly.Location))
            });
            File.WriteAllText(Path.Combine(destination, "COMPLETE.txt"),
                "Validated export. Config SHA256 (canonical Newtonsoft JSON): " + GambleProtocol.Hash(config));
        }

        static JObject ReadEventCodes(string path)
        {
            var codes = JObject.Parse(File.ReadAllText(path));
            if (codes["LedgerSettled"] != null)
                return codes;
            if (codes.Properties().Any(property => (int?)property.Value["Value"] == 12368))
                throw new ArgumentException("Event code 12368 is in use; add an explicit LedgerSettled mapping to the template.");
            codes["LedgerSettled"] = JObject.FromObject(new
            {
                Value = 12368,
                Description = "GambleChoice virtual ledger settlement"
            });
            return codes;
        }

        static void ConfigureSession(string path)
        {
            var lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                var fields = lines[i].Split('\t');
                if (fields.Length < 3)
                    continue;
                if (new[] { "SerialPortActive", "SyncBoxActive", "EyeTrackerActive", "EventCodesActive" }.Contains(fields[1]))
                    fields[2] = "false";
                if (fields[1] == "TaskMappings" || fields[1] == "TaskIcons")
                    fields[2] = "{GambleChoice:GambleChoice}";
                if (fields[1] == "GuidedTaskSelection")
                    fields[2] = "true";
                if (fields[1] == "SelectionType")
                    fields[2] = "\"MouseButton0Click\"";
                if (fields[1] == "TaskIconsFolderPath")
                    fields[2] = JsonConvert.SerializeObject(Path.Combine(Path.GetDirectoryName(path), "TaskIcons").Replace('\\', '/'));
                if (fields[1] == "ContextExternalFilePath")
                    fields[2] = JsonConvert.SerializeObject(Path.Combine(Path.GetDirectoryName(path), "Contexts").Replace('\\', '/'));
                lines[i] = string.Join("\t", fields);
            }
            File.WriteAllLines(path, lines);
        }

        static void WriteReview(GambleConfig config, GamblePlannedTrial[] plans, string csv, string path)
        {
            WriteJson(path, new
            {
                config.SchemaVersion,
                config.Purpose,
                config.ProtocolId,
                config.QualificationReference,
                ConfigHash = GambleProtocol.Hash(config),
                CsvSha256 = Sha256(Encoding.UTF8.GetBytes(csv)),
                config.Ledger,
                config.Adaptive,
                Trials = plans.Length,
                PracticeTrials = plans.Count(plan => plan.Practice),
                Conditions = plans.GroupBy(plan => plan.ConditionId).ToDictionary(group => group.Key, group => group.Count()),
                Exposure = plans.SelectMany(plan => plan.Options.Select((option, index) => new
                {
                    plan.ConditionId,
                    plan.Practice,
                    Position = index,
                    option.Id,
                    option.Palette,
                    option.ProbabilityRepresentation,
                    option.RewardRepresentation
                })).GroupBy(value => JsonConvert.SerializeObject(value)).Select(group => new
                {
                    Assignment = JsonConvert.DeserializeObject(group.Key),
                    Count = group.Count()
                }),
                EstimatedSeconds = plans.Length * (config.RevealMs + config.HighlightMs + config.TransferMs
                    + config.RewardWindowMs + config.ItiMs) / 1000,
                DurationAssumption = "Completed choices; omissions/stops are shorter. Loading and operator pauses excluded.",
                AdaptiveNote = config.Adaptive.Enabled
                    ? "Initial sure offers only. Runtime bisection and snapshots determine subsequent offers."
                    : "Nonadaptive schedule is resolved."
            });
        }

        static void WriteJson(string path, object value) => File.WriteAllText(path, JsonConvert.SerializeObject(value, Formatting.Indented));
        static string Sha256(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
    }
}

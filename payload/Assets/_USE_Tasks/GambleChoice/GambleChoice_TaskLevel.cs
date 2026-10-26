using System;
using System.Collections.Specialized;
using System.Linq;
using GambleChoice;
using GambleChoice_Namespace;
using USE_ExperimentTemplate_Task;

public class GambleChoice_TaskLevel : ControlLevel_Task_Template
{
    public GamblePreparedProtocol CurrentProtocol
    {
        get; private set;
    }
    public GambleStatistics TaskStatistics { get; private set; } = new GambleStatistics();
    public GambleStatistics BlockStatistics { get; private set; } = new GambleStatistics();
    public bool RecordingFailed { get; private set; }

    public override void DefineControlLevel()
    {
        var config = GetTaskDef<GambleChoice_TaskDef>().Gamble;
        ValidateInputMode(Session.SessionDef.SelectionType);
        if (Session.WebBuild || Session.StoringDataOnServer || !Session.StoreData)
            throw new NotSupportedException("GambleChoice requires local Windows data recording.");
        if (config.PhysicalReward && (!Session.SessionDef.SyncBoxActive || Session.SyncBoxController == null))
            throw new InvalidOperationException("Physical GambleChoice rewards require an active, qualified SyncBox.");
        config.Validate();
        foreach (var block in BlockDefs)
        {
            var gambleBlock = block as GambleChoice_BlockDef;
            if (gambleBlock == null)
                throw new ArgumentException("GambleChoice requires its typed BlockDef file.");
            ValidateBlock(config, gambleBlock);
        }
        if (config.SchemaVersion == 2 && Session.SessionDef.EventCodesActive
            && (TrialLevel.TaskEventCodes == null || !TrialLevel.TaskEventCodes.ContainsKey("LedgerSettled")))
            throw new ArgumentException("Schema 2 event-code profiles require a LedgerSettled mapping.");

        DefineBlockData();
        RunBlock.AddSpecificInitializationMethod(() =>
        {
            BlockStatistics = new GambleStatistics();
            CurrentProtocol = new GamblePreparedProtocol(ForBlock(config, GetCurrentBlockDef<GambleChoice_BlockDef>()));
        });
        BlockFeedback.AddUpdateMethod(() =>
        {
            if (!RecordingFailed) return;
            // The template consumes ForceTaskEnd only inside FinishTask. Route there
            // explicitly, letting BlockFeedback's normal termination save this block.
            // Clear the flag so FinishTask does not append that same block again.
            ForceTaskEnd = false;
            BlockFeedback.TerminateState(FinishTask, BlockFeedback.StateDefaultTermination);
        });

        if (Session.SessionDef.IsHuman)
        {
            Session.HumanStartPanel.AddTaskDisplayName("GambleChoice", "Gamble Choice");
            Session.HumanStartPanel.AddTaskInstructions("GambleChoice", config.SchemaVersion == 1
                ? "Touch and hold a choice button. Colored regions show the probabilities of the two matching token amounts. Exactly one outcome occurs; zero is valid. Faster choices do not reveal the outcome sooner."
                : "Touch and hold a choice button. Matching colors connect outcomes to their probabilities. Exactly one outcome occurs. Signed token changes add to or subtract from the virtual bank; zero is valid. Faster choices do not reveal the outcome sooner. Practice uses a separate bank.");
        }
    }

    public static void ValidateInputMode(string selectionType)
    {
        if (!string.Equals(selectionType?.Trim(), "MouseButton0Click", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(selectionType?.Trim(), "TouchShotgun", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("GambleChoice supports mouse/single touch with a qualified hold. Gaze and hover choice modes are not implemented.");
    }

    public static GambleConfig ForBlock(GambleConfig source, GambleChoice_BlockDef block)
    {
        var config = source.CopySettings();
        config.Options = source.Options;
        config.Schedule = source.Schedule;
        config.TrialTable = source.TrialTable;
        config.Trials = block.NumTrials;
        config.ChoiceCount = block.ChoiceCount;
        config.Renderer = block.Renderer?.Trim();
        return config;
    }

    public static void ValidateBlock(GambleConfig source, GambleChoice_BlockDef block)
    {
        var config = ForBlock(source, block);
        config.Validate();
        if (config.SchemaVersion != 2)
            return;
        var rows = config.TrialTable ?? (config.Schedule ?? new[] { config.Options })
            .Select(options => new GamblePlannedTrial { Options = options }).ToArray();
        foreach (var row in rows)
        {
            var renderers = row.Renderer != null ? new[] { row.Renderer }
                : config.Design.RepresentationCycle ?? new[] { config.Renderer };
            foreach (string renderer in renderers)
            {
                var settings = config.CopySettings();
                settings.Renderer = renderer;
                foreach (var option in row.Options)
                    GambleProtocol.ValidateOption(option, settings);
            }
        }
    }

    public void RecordTrial(GambleResult result)
    {
        TaskStatistics.Record(result);
        BlockStatistics.Record(result);
        SetBlockSummaryString();
        SetTaskSummaryString();
    }

    public void StopAfterRecordingFailure()
    {
        RecordingFailed = true;
        ForceTaskEnd = true;
        // A recording fault also latches M-USE's error panel. End this session via
        // its normal save path; selecting another task after that latch is unsafe.
        if (Session.SessionLevel != null)
            Session.SessionLevel.TasksFinished = true;
    }

    void DefineBlockData()
    {
        BlockData.AddDatum("GambleCompleted", () => BlockStatistics.Completed);
        BlockData.AddDatum("GambleOmissions", () => BlockStatistics.Omissions);
        BlockData.AddDatum("GambleAborted", () => BlockStatistics.Aborted);
        BlockData.AddDatum("GambleFundingStops", () => BlockStatistics.FundingStops);
        BlockData.AddDatum("GamblePracticeTrials", () => BlockStatistics.PracticeTrials);
        BlockData.AddDatum("GambleNonpracticeSettledDelta", () => BlockStatistics.SettledDelta);
        BlockData.AddDatum("GamblePracticeSettledDelta", () => BlockStatistics.PracticeSettledDelta);
        BlockData.AddDatum("GambleBalance", () => BlockStatistics.LastBalance);
    }

    public override OrderedDictionary GetTaskSummaryData()
    {
        var data = base.GetTaskSummaryData();
        data["Gamble Completed"] = TaskStatistics.Completed;
        data["Gamble Omissions"] = TaskStatistics.Omissions;
        data["Gamble Funding Stops"] = TaskStatistics.FundingStops;
        data["Gamble Practice Trials"] = TaskStatistics.PracticeTrials;
        data["Gamble Nonpractice Settled Tokens"] = TaskStatistics.SettledDelta;
        data["Gamble Practice Settled Tokens"] = TaskStatistics.PracticeSettledDelta;
        return data;
    }

    public override OrderedDictionary GetTaskResultsData()
    {
        var data = base.GetTaskResultsData();
        data["Completed choices"] = TaskStatistics.Completed - TaskStatistics.PracticeCompleted;
        data["Practice completed"] = TaskStatistics.PracticeCompleted;
        data["Virtual token change"] = TaskStatistics.SettledDelta;
        return data;
    }

    public override void SetBlockSummaryString()
    {
        if (CurrentBlockSummaryString == null)
            return;
        CurrentBlockSummaryString.Clear();
        CurrentBlockSummaryString.Append("\nGamble completed: ").Append(BlockStatistics.Completed)
            .Append("\nOmissions: ").Append(BlockStatistics.Omissions)
            .Append("\nFunding stops: ").Append(BlockStatistics.FundingStops)
            .Append("\nNonpractice settled tokens: ").Append(BlockStatistics.SettledDelta)
            .Append("\nPractice settled tokens: ").Append(BlockStatistics.PracticeSettledDelta);
    }

    public override void SetTaskSummaryString()
    {
        if (CurrentTaskSummaryString == null)
            return;
        base.SetTaskSummaryString();
        CurrentTaskSummaryString.Append("\nGamble completed: ").Append(TaskStatistics.Completed)
            .Append("\nOmissions: ").Append(TaskStatistics.Omissions)
            .Append("\nFunding stops: ").Append(TaskStatistics.FundingStops);
    }
}

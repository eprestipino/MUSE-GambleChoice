using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using GambleChoice;
using GambleChoice_Namespace;
using Newtonsoft.Json;
using UnityEngine;
using USE_ExperimentTemplate_Trial;
using USE_States;

public class GambleChoice_TrialLevel : ControlLevel_Trial_Template
{
    GambleRuntime runtime;
    GambleResult completedRecord;
    string snapshot = "";
    GambleChoice_TaskLevel CurrentTask => GetTaskLevel<GambleChoice_TaskLevel>();
    GambleResult CurrentResult => runtime?.Engine?.Result != null
        && runtime.Engine.Result.TrialIndex == TrialCount_InTask ? runtime.Engine.Result : null;

    public override void DefineControlLevel()
    {
        var start = new State("AwaitGambleStart");
        var run = new State("GambleTrial");
        AddActiveStates(new List<State> { start, run });
        DefineGambleData();
        LoadTrialTextures.AddSpecificInitializationMethod(ClearGambleRecord);
        SetupTrial.SpecifyTermination(() => true,
            () => Session.SessionDef.IsHuman && TrialCount_InTask == 0 ? start : run);

        if (Session.SessionDef.IsHuman)
        {
            Session.HumanStartPanel.SetVisibilityOnOffStates(start, start);
            start.AddSpecificInitializationMethod(() =>
            {
                runtime?.SetVisible(false);
                SelectionHandler = Session.SelectionTracker.SetupSelectionHandler("trial", "MouseButton0Click",
                    Session.MouseTracker, start, start);
                SelectionHandler.TimeBeforeChoiceStarts = Session.SessionDef.StartButtonSelectionDuration;
                SelectionHandler.TotalChoiceDuration = Session.SessionDef.StartButtonSelectionDuration;
                SelectionHandler.ClearChoices();
            });
            start.SpecifyTermination(() => SelectionHandler.LastSuccessfulSelectionMatchesStartButton(), run);
        }

        run.AddSpecificInitializationMethod(BeginGamble);
        run.SpecifyTermination(() => runtime != null && runtime.Engine.Done, FinishTrial);
        run.AddUpdateMethod(UpdateGambleSummary);
        run.AddUniversalTerminationMethod(() =>
        {
            if (runtime != null && !runtime.Engine.Done)
                runtime.Abort("MUSEStateExit");
            runtime?.SetVisible(false);
        });
    }

    void BeginGamble()
    {
        if (runtime == null)
        {
            var canvas = TaskLevel.TaskCanvasses?.FirstOrDefault(c => c != null && c.name == "GambleChoice_Canvas");
            if (canvas == null)
                throw new InvalidOperationException("GambleChoice scene canvas is not wired to the task.");
            runtime = gameObject.AddComponent<GambleRuntime>();
            runtime.Initialize(Path.Combine(TaskLevel.TaskDataPath, "GambleChoice"), TaskLevel.TaskCam, true, canvas);
            runtime.EventObserved = ObserveGambleEvent;
            runtime.Completed = OnGambleCompleted;
        }
        runtime.BeginPrepared(CurrentTask.CurrentProtocol, TrialCount_InTask, BlockCount, TrialCount_InBlock);
        if (TokenFBController != null)
            TokenFBController.enabled = false;
        Cursor.visible = Session.SessionDef.IsHuman;
    }

    void ObserveGambleEvent(GambleEvent record)
    {
        if (Session.SessionDef.EventCodesActive && TaskEventCodes != null
            && TaskEventCodes.TryGetValue(record.Type, out var code))
            Session.EventCodeManager.SendCodeThisFrame(code);
        if (record.Type == "RewardRequested" && runtime.Engine.Result.Config.PhysicalReward)
        {
            // Framework reward counters represent requests, never measured delivery.
            TaskLevel.NumRewardPulses_InBlock += runtime.Engine.Result.RequestedPulses;
            TaskLevel.NumRewardPulses_InTask += runtime.Engine.Result.RequestedPulses;
        }
    }

    void UpdateGambleSummary()
    {
        var result = CurrentResult;
        if (result == null)
            return;
        TrialSummaryString = "Gamble Choice | " + runtime.Engine.Phase
            + "\nChoices: " + result.Options.Length + "\nCommitted: " + (result.ChosenId ?? "None")
            + "\nStatus: " + result.Status + (result.Reason == null ? "" : " / " + result.Reason)
            + (result.RevealActualMs > 0 && result.Tokens.HasValue ? "\nOutcome tokens: " + result.Tokens : "")
            + "\nReward: " + result.RewardStatus + "\nRequested pulses: " + result.RequestedPulses;
    }

    public override void ResetTrialVariables()
    {
        base.ResetTrialVariables();
        ClearGambleRecord();
    }

    void ClearGambleRecord()
    {
        snapshot = "";
        completedRecord = null;
        runtime?.SetVisible(false);
    }

    public override void FinishTrialCleanup()
    {
        if (runtime != null && !runtime.Engine.Done)
            runtime.Abort("MUSETrialEnded");
        runtime?.SetVisible(false);
        base.FinishTrialCleanup();
    }

    void OnGambleCompleted(GambleResult result)
    {
        completedRecord = result;
        snapshot = JsonConvert.SerializeObject(result);
        // Keep existing M-USE operator reasons, especially calibration code 7.
        if (result.Status == "Stopped")
            ForceBlockEnd = true;
        if (runtime?.Error != null)
        {
            ForceBlockEnd = true;
            if (TaskLevel != null)
                TaskLevel.ForceTaskEnd = true;
            (TaskLevel as GambleChoice_TaskLevel)?.StopAfterRecordingFailure();
        }
        if (AbortCode == 0)
        {
            if (result.Status == "Omission")
                AbortCode = 6;
            else if (result.Status == "Aborted" || result.Status == "Stopped")
                AbortCode = 8;
        }
        (TaskLevel as GambleChoice_TaskLevel)?.RecordTrial(result);
    }

    void DefineGambleData()
    {
        TrialData.AddDatum("GambleRecordJson", () => snapshot);
        TrialData.AddDatum("GambleTrialId", () => completedRecord?.TrialId ?? "");
        TrialData.AddDatum("GambleStatus", () => completedRecord?.Status ?? "NotStarted");
        TrialData.AddDatum("GambleReason", () => FlatText(completedRecord?.Reason));
        TrialData.AddDatum("GambleSchema", () => completedRecord?.SchemaVersion);
        TrialData.AddDatum("GambleChoiceCount", () => completedRecord?.Options?.Length);
        TrialData.AddDatum("GambleChosenId", () => FlatText(completedRecord?.ChosenId));
        TrialData.AddDatum("GambleChosenPosition", () => completedRecord?.ChosenIndex);
        TrialData.AddDatum("GambleOutcomeId", () => FlatText(completedRecord?.OutcomeId ?? completedRecord?.Role));
        TrialData.AddDatum("GambleTokens", () => completedRecord?.Tokens);
        TrialData.AddDatum("GambleDraw", () => Number(completedRecord?.Draw));
        TrialData.AddDatum("GambleContactRTMs", () => Number(completedRecord?.FirstContactRTMs));
        TrialData.AddDatum("GambleCommitRTMs", () => Number(completedRecord?.CommitRTMs));
        TrialData.AddDatum("GambleBalanceBefore", () => completedRecord?.BalanceBefore);
        TrialData.AddDatum("GambleBalanceAfter", () => completedRecord?.BalanceAfter);
        TrialData.AddDatum("GamblePractice", () => completedRecord?.Practice);
        TrialData.AddDatum("GambleCondition", () => FlatText(completedRecord?.ConditionId));
        TrialData.AddDatum("GambleRequestedPulses", () => completedRecord?.RequestedPulses);
        TrialData.AddDatum("GambleRewardStatus", () => completedRecord?.RewardStatus ?? "");
        TrialData.AddDatum("GambleInputSource", () => completedRecord?.InputSource ?? "");
        TrialData.AddDatum("GambleConfigHash", () => completedRecord?.ConfigHash ?? "");
        FrameData.AddDatum("GamblePhase", () => CurrentResult == null ? "NotStarted" : runtime.Engine.Phase.ToString());
        FrameData.AddDatum("GambleTrialId", () => CurrentResult?.TrialId ?? "");
        FrameData.AddDatum("GambleHoldingIndex", () => CurrentResult == null ? -1 : runtime.Engine.HoldingIndex);
    }

    static string Number(double? value) => value?.ToString("R", CultureInfo.InvariantCulture) ?? "null";
    public static string FlatText(string value) => (value ?? "").Replace("\\", "\\\\")
        .Replace("\t", "\\t").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\"", "\\u0022");

    protected override void DefineTrialStims()
    {
    }
}

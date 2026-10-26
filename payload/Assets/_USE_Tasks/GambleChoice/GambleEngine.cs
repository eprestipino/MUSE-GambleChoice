using System;

namespace GambleChoice
{
    public sealed class GambleEngine
    {
        public GambleResult Result
        {
            get; private set;
        }
        public GamblePhase Phase
        {
            get; private set;
        }
        public double PhaseStartMs
        {
            get; private set;
        }
        public int HoldingIndex { get; private set; } = -1;
        public double HoldStartMs
        {
            get; private set;
        }
        public bool Done => Phase == GamblePhase.Finished;

        public Action<GambleEvent> Event;
        public Action<int> Reward;
        int ordinal;
        bool rewardSettled;
        readonly GambleLedger mainLedger = new GambleLedger(), practiceLedger = new GambleLedger();
        GambleLedger ledger;
        readonly GambleAdaptiveController adaptive = new GambleAdaptiveController();
        public void Begin(GambleConfig config, int trial, double now, int block = 0, int planTrial = -1)
        {
            BeginPrepared(new GamblePreparedProtocol(config), trial, now, block, planTrial);
        }

        public void BeginPrepared(GamblePreparedProtocol protocol, int trial, double now, int block = 0,
            int planTrial = -1, Func<double> onsetClock = null)
        {
            if (trial < 0)
                throw new ArgumentException("Trial index must be nonnegative.");
            if (planTrial < 0)
                planTrial = trial;
            if (protocol == null)
                throw new ArgumentNullException(nameof(protocol));
            var planned = protocol.Resolve(planTrial, trial);
            var config = planned.Config;
            config.Validate();

            Result = new GambleResult
            {
                Config = config,
                TrialIndex = trial,
                BlockIndex = block,
                PlanTrialIndex = planTrial,
                Options = GambleSchedule.Resolve(config, trial),
                OnsetMs = now
            };
            if (config.SchemaVersion == 2)
            {
                ledger = planned.Practice ? practiceLedger : mainLedger;
                ledger.Prepare(config.Ledger, block);
                adaptive.Prepare(config.Adaptive, block);
                adaptive.Apply(Result.Options);
                foreach (var o in Result.Options)
                    config.ValidateOption(o);
                Result.SchemaVersion = 2;
                Result.ConditionId = planned.ConditionId;
                Result.Practice = planned.Practice;
                Result.SamplerAlgorithm = "categorical-cdf-v1";
                Result.BalanceBefore = Result.BalanceAfter = ledger.Balance;
                Result.AdaptiveBefore = adaptive.Snapshot();
            }

            Result.ConfigHash = GambleProtocol.Hash(config);
            HoldingIndex = -1;
            ordinal = 0;
            rewardSettled = false;
            // Model preparation is not part of the participant's response interval.
            now = onsetClock == null ? now : onsetClock();
            Result.OnsetMs = now;
            if (config.SchemaVersion == 2 && !ledger.CanFund(Result.Options))
            {
                Result.Status = "Stopped";
                Result.Reason = "InsufficientFunds";
                Result.EndMs = now;
                SetPhase(GamblePhase.Finished, now);
                Emit("TrialFinalized", now);
                return;
            }

            SetPhase(GamblePhase.Offer, now);
            Emit("OfferVisibleSoftware", now);
        }

        public void Press(int index, double now)
        {
            if (Phase != GamblePhase.Offer || index < 0 || index >= Result.Options.Length || now > Result.OnsetMs + Result.Config.DeadlineMs)
                return;
            if (HoldingIndex >= 0)
                CancelHold(now, "NewPress");
            HoldingIndex = index;
            HoldStartMs = now;
            Result.Attempts++;
            if (!Result.FirstContactRTMs.HasValue)
                Result.FirstContactRTMs = now - Result.OnsetMs;
            Emit("TouchAttemptStarted", now, index);
        }

        public void CancelHold(double now, string reason)
        {
            if (HoldingIndex < 0)
                return;
            Result.FailedHolds++;
            Emit("TouchAttemptFailed", now, HoldingIndex, reason);
            HoldingIndex = -1;
        }

        public void Tick(double now, int hovered = -1, bool held = false)
        {
            if (Done || Result == null)
                return;
            var c = Result.Config;
            if (Phase == GamblePhase.Offer)
            {
                if (HoldingIndex >= 0)
                {
                    if (!held || hovered != HoldingIndex)
                        CancelHold(now, held ? "LeftButton" : "ReleasedEarly");
                    else if (now - HoldStartMs >= c.HoldMs && now <= Result.OnsetMs + c.DeadlineMs)
                    {
                        Result.ChosenIndex = HoldingIndex;
                        Result.ChosenId = Result.Options[HoldingIndex].Id;
                        Result.CommitRTMs = now - Result.OnsetMs;
                        Emit("ChoiceCommitted", now, HoldingIndex);
                        Result.Draw = new GambleRandom(c.Seed, Result.TrialIndex, 0x1234ABCDu).Next();
                        var o = Result.Options[HoldingIndex];
                        if (c.SchemaVersion == 1)
                        {
                            Result.Role = Result.Draw.Value < o.PHigh ? "High" : "Low";
                            Result.Tokens = Result.Role == "High" ? o.High : o.Low;
                        }
                        else
                        {
                            Result.OutcomeIndex = GambleOutcomeSampler.Select(o.Outcomes, Result.Draw.Value);
                            var outcome = o.Outcomes[Result.OutcomeIndex];
                            Result.OutcomeId = Result.Role = outcome.Id;
                            Result.Tokens = outcome.TokenDelta;
                        }

                        Emit("OutcomeResolved", now, HoldingIndex, Result.Role);
                        HoldingIndex = -1;
                        SetPhase(GamblePhase.AwaitReveal, now);
                    }
                }

                if (Phase == GamblePhase.Offer && now >= Result.OnsetMs + c.DeadlineMs)
                {
                    CancelHold(now, "Deadline");
                    Result.Status = "Omission";
                    Result.Reason = "NoQualifiedChoice";
                    SetPhase(GamblePhase.AwaitReveal, now);
                }
            }

            if (Phase == GamblePhase.AwaitReveal && now >= Result.OnsetMs + c.RevealMs)
            {
                Result.RevealActualMs = now;
                Result.RevealLateMs = now - (Result.OnsetMs + c.RevealMs);
                if (Result.Status == "Omission")
                    StartIti(now);
                else
                {
                    SetPhase(GamblePhase.Reveal, now);
                    Emit("RevealVisibleSoftware", now, Result.ChosenIndex);
                }
            }
            else if (Phase == GamblePhase.Reveal && now - PhaseStartMs >= c.HighlightMs)
                SetPhase(GamblePhase.Transfer, now);
            else if (Phase == GamblePhase.Transfer && now - PhaseStartMs >= c.TransferMs)
            {
                Result.TransferEndMs = now;
                SetPhase(GamblePhase.Reward, now);
                Result.RequestedPulses = c.SchemaVersion == 2 ? 0 : Result.Tokens.Value * c.PulsesPerToken;
                if (c.SchemaVersion == 2)
                {
                    ledger.Apply(Result.TrialId, Result.Tokens.Value);
                    Result.BalanceAfter = ledger.Balance;
                    Result.LedgerTransactionId = Result.TrialId;
                    Result.RewardStatus = "VirtualSettled";
                    rewardSettled = true;
                    Emit("LedgerSettled", now);
                }
                else if (Result.Tokens == 0)
                {
                    Result.RewardStatus = "ZeroNoCommand";
                    rewardSettled = true;
                    Emit("ZeroReward", now);
                }
                else
                {
                    Result.RewardStatus = "Requested";
                    Emit("RewardRequested", now);
                    Reward?.Invoke(Result.RequestedPulses);
                }
            }
            else if (Phase == GamblePhase.Reward)
            {
                if (rewardSettled && now - PhaseStartMs >= c.RewardWindowMs)
                    StartIti(now);
                else if (!rewardSettled && now - PhaseStartMs > c.RewardWindowMs + 5000)
                    Abort(now, "RewardTimeout");
            }
            else if (Phase == GamblePhase.ITI && now - PhaseStartMs >= c.ItiMs)
            {
                if (Result.Status == "Running")
                    Result.Status = "Completed";
                if (c.SchemaVersion == 2)
                {
                    if (Result.Status == "Completed" && !Result.Practice)
                        adaptive.Observe(Result.ChosenId);
                    Result.AdaptiveAfter = adaptive.Snapshot();
                }

                Result.EndMs = now;
                SetPhase(GamblePhase.Finished, now);
                Emit("TrialFinalized", now);
            }
        }

        public void RewardCompleted(double now, string status)
        {
            if (Phase != GamblePhase.Reward || rewardSettled)
                return;
            rewardSettled = true;
            Result.RewardStatus = status;
            Emit("RewardSettled", now, -1, status);
        }

        public void Abort(double now, string reason)
        {
            if (Result == null || Done)
                return;
            CancelHold(now, reason);
            Result.Status = "Aborted";
            Result.Reason = reason;
            Result.EndMs = now;
            SetPhase(GamblePhase.Finished, now);
            Emit("TrialFinalized", now, -1, reason);
        }

        void StartIti(double now)
        {
            Result.ItiStartMs = now;
            SetPhase(GamblePhase.ITI, now);
        }

        void SetPhase(GamblePhase phase, double now)
        {
            Phase = phase;
            PhaseStartMs = now;
            Emit("StateEntered", now);
        }

        void Emit(string type, double now, int option = -1, string detail = null)
        {
            Event?.Invoke(new GambleEvent { TrialId = Result.TrialId, Ordinal = ordinal++, Type = type, TimeMs = now, Phase = Phase.ToString(), Option = option, Detail = detail });
        }
    }
}

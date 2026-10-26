using System;

namespace GambleChoice
{
    public enum GamblePhase
    {
        Offer,
        AwaitReveal,
        Reveal,
        Transfer,
        Reward,
        ITI,
        Finished
    }

    public sealed class GambleResult
    {
        public int SchemaVersion = 1, TrialIndex, ChosenIndex = -1, Attempts, FailedHolds, RequestedPulses;
        public string TrialId = Guid.NewGuid().ToString("N"), Status = "Running", Reason, Role, ChosenId, RewardStatus = "NotRequested";
        public GambleConfig Config;
        public Lottery[] Options;
        public double OnsetMs, EndMs, RevealActualMs, TransferEndMs, ItiStartMs, RevealLateMs;
        public double? FirstContactRTMs, CommitRTMs, Draw;
        public int? Tokens;
        public int OutcomeIndex = -1, BlockIndex, PlanTrialIndex;
        public string OutcomeId, ConditionId, ConfigHash, LedgerTransactionId, SamplerAlgorithm = "binary-high-first-v1";
        public bool Practice;
        public int? BalanceBefore, BalanceAfter;
        public GambleAdaptiveSnapshot AdaptiveBefore, AdaptiveAfter;
        public int? ObservedPulses; // Null unless an external measurement is supplied; queueing is not observation.
        public string RandomAlgorithm = GambleRandom.Algorithm, InputSource = "MouseOrSingleTouch";
    }

    public sealed class GambleEvent
    {
        public string TrialId, Type, Phase, Detail;
        public int Ordinal, Option, Frame;
        public double TimeMs;
    }
}

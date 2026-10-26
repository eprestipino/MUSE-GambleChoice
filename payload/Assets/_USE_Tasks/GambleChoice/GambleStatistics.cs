using System.Collections.Generic;

namespace GambleChoice
{
    // Summary counters supplement the complete journal; settled aborts still affect balances.
    public sealed class GambleStatistics
    {
        readonly HashSet<string> recorded = new HashSet<string>();
        public int Trials
        {
            get; private set;
        }
        public int Completed
        {
            get; private set;
        }
        public int Omissions
        {
            get; private set;
        }
        public int Aborted
        {
            get; private set;
        }
        public int FundingStops
        {
            get; private set;
        }
        public int PracticeTrials
        {
            get; private set;
        }
        public int PracticeCompleted
        {
            get; private set;
        }
        public int Attempts
        {
            get; private set;
        }
        public int FailedHolds
        {
            get; private set;
        }
        public int SettledDelta
        {
            get; private set;
        }
        public int PracticeSettledDelta
        {
            get; private set;
        }
        public int? LastBalance
        {
            get; private set;
        }

        public void Record(GambleResult result)
        {
            if (result == null || !recorded.Add(result.TrialId))
                return;
            Trials++;
            if (result.Status == "Completed")
                Completed++;
            if (result.Status == "Omission")
                Omissions++;
            if (result.Status == "Aborted")
                Aborted++;
            if (result.Status == "Stopped")
                FundingStops++;
            if (result.Practice)
            {
                PracticeTrials++;
                if (result.Status == "Completed")
                    PracticeCompleted++;
            }
            Attempts += result.Attempts;
            FailedHolds += result.FailedHolds;
            if (result.LedgerTransactionId != null)
            {
                if (result.Practice)
                    PracticeSettledDelta += result.Tokens.Value;
                else
                    SettledDelta += result.Tokens.Value;
            }
            if (!result.Practice)
                LastBalance = result.BalanceAfter;
        }
    }
}

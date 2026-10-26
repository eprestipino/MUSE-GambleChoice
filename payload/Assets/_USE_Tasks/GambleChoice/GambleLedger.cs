using System;
using System.Linq;
using System.Collections.Generic;

namespace GambleChoice
{
    public sealed class GambleLedger
    {
        public int Balance
        {
            get; private set;
        }

        string key;
        int blockKey;
        GambleLedgerSettings settings;
        readonly HashSet<string> transactions = new HashSet<string>();
        public void Prepare(GambleLedgerSettings config, int block)
        {
            string next = GambleProtocol.Hash(config);
            if (key != next || config.Scope == "Block" && blockKey != block)
            {
                Balance = config.InitialBalance;
                transactions.Clear();
            }

            key = next;
            blockKey = block;
            settings = config;
        }

        public bool CanFund(Lottery[] offers) => settings.InsufficientFunds == "AllowDebt" || offers.SelectMany(o => o.Outcomes).Where(o => o.Probability > 0).All(o => (long)Balance + o.TokenDelta >= 0);
        public void Apply(string transaction, int delta)
        {
            if (transactions.Contains(transaction))
                throw new InvalidOperationException("Duplicate ledger transaction.");
            int next = checked(Balance + delta);
            if (settings.InsufficientFunds == "Stop" && next < 0)
                throw new InvalidOperationException("Unfunded loss; never clip a realized outcome.");
            Balance = next;
            transactions.Add(transaction);
        }
    }
}

using System;
using System.Collections.Generic;
using USE_Def_Namespace;
using USE_StimulusManagement;
using GambleChoice;

namespace GambleChoice_Namespace
{
    public class GambleChoice_TaskDef : TaskDef
    {
        public GambleConfig Gamble = new GambleConfig();
    }
    public class GambleChoice_BlockDef : BlockDef
    {
        public int ChoiceCount = 2;
        public string Renderer = "Bar";
        public override void GenerateTrialDefsFromBlockDef()
        {
            Renderer = Renderer?.Trim(); // Stock TSV parser retains Windows line endings in the last string field.
            if (!GambleRepresentations.Valid(Renderer))
                throw new ArgumentException("Unknown GambleChoice representation.");
            if (NumTrials < 1 || NumTrials > 100000 || ChoiceCount < 2 || ChoiceCount > 6)
                throw new ArgumentException("GambleChoice block needs 1-100000 trials and 2-6 choices.");
            TrialDefs = new List<TrialDef>();
            MaxTrials = NumTrials;
            for (int i = 0; i < NumTrials; i++)
                TrialDefs.Add(new GambleChoice_TrialDef
                {
                    BlockName = BlockName,
                    BlockCount = BlockCount,
                    ChoiceCount = ChoiceCount,
                    Renderer = Renderer,
                    TrialDefSelectionStyle = "Sequential",
                    MinTrialsBeforeTermProcedure = -1
                });
        }
    }
    public class GambleChoice_TrialDef : TrialDef
    {
        public int ChoiceCount = 2; public string Renderer = "Bar";
    }
    public class GambleChoice_StimDef : StimDef
    {
    }
}

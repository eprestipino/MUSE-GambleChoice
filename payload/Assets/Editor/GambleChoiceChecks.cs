using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using GambleChoice;
using GambleChoice_Namespace;

public static class GambleChoiceChecks
{
    public static string OutputOverride;
    static int assertions;
    static void Check(bool condition, string why)
    {
        assertions++;
        if (!condition)
            throw new Exception("GambleChoice check failed: " + why);
    }
    static GambleConfig Config(int lo, int hi, double p, int count = 2)
    {
        var c = new GambleConfig { ChoiceCount = count, ShufflePositions = false };
        c.Options = Enumerable.Range(0, count).Select(i => new Lottery { Id = "o" + i, Low = lo, High = hi, PHigh = p }).ToArray();
        return c;
    }
    static GambleEngine Run(GambleConfig c, int index, double press = 0, double commit = 300, int choice = 0)
    {
        var e = new GambleEngine();
        int requests = 0;
        e.Reward = n => { requests++; e.RewardCompleted(e.PhaseStartMs, "Simulated"); };
        e.Begin(c, index, 0);
        e.Press(choice, press);
        e.Tick(commit, choice, true);
        e.Tick(c.RevealMs);
        Check(e.Phase == GamblePhase.Reveal, "reveal");
        e.Tick(c.RevealMs + c.HighlightMs);
        Check(e.Phase == GamblePhase.Transfer, "transfer");
        e.Tick(c.RevealMs + c.HighlightMs + c.TransferMs);
        Check(e.Result.RequestedPulses == e.Result.Tokens * c.PulsesPerToken, "pulses");
        Check(requests == (e.Result.Tokens == 0 ? 0 : 1), "one request; zero none");
        e.Tick(c.RevealMs + c.HighlightMs + c.TransferMs + c.RewardWindowMs);
        e.Tick(c.RevealMs + c.HighlightMs + c.TransferMs + c.RewardWindowMs + c.ItiMs);
        Check(e.Done && e.Result.Status == "Completed", "completion");
        e.Tick(100000);
        Check(requests == (e.Result.Tokens == 0 ? 0 : 1), "no duplicate request after completion");
        return e;
    }
    public static void RunAll()
    {
        assertions = 0;
        var results = new List<GambleResult>();
        for (int lo = 0; lo <= 6; lo++)
        for (int hi = lo; hi <= 6; hi++)
        foreach (double p in new[] { 0, .2, .5, .8, 1.0 })
        foreach (int n in new[] { 2, 3, 4 })
        {
            var e = Run(Config(lo, hi, p, n), results.Count, choice: n - 1);
            Check(e.Result.Tokens == (e.Result.Draw < p ? hi : lo), "lottery agrees with draw");
            Check(e.Result.ChosenId == "o" + (n - 1), "choice identity");
            results.Add(e.Result);
        }
        foreach (double commit in new[] { 300, 1500, 2999, 3000 })
        {
            var e = Run(Config(0, 6, .5), 55, commit - 300, commit);
            Check(e.Result.RevealActualMs == 3000, "fixed reveal independent of RT");
        }
        var omission = new GambleEngine();
        omission.Begin(Config(0, 6, .5), 0, 0);
        omission.Press(0, 2800);
        omission.Tick(3000, 0, true);
        Check(omission.Result.Status == "Omission" && !omission.Result.Tokens.HasValue, "late hold omission, not zero");
        omission.Tick(3500);
        Check(omission.Done, "omission exits");
        var hold = new GambleEngine();
        hold.Begin(Config(0, 6, .5), 0, 0);
        hold.Press(0, 1);
        hold.Tick(100, 0, false);
        Check(hold.Result.FailedHolds == 1 && hold.Result.ChosenIndex == -1, "short release");
        hold.Press(1, 200);
        hold.Tick(400, 0, true);
        Check(hold.Result.FailedHolds == 2, "drag off");
        hold.Press(1, 500);
        hold.Tick(800, 1, true);
        hold.Press(0, 900);
        Check(hold.Result.ChosenIndex == 1, "commit lock");
        hold.Abort(950, "test");
        Check(hold.Done && hold.Result.Status == "Aborted" && hold.Result.RequestedPulses == 0, "abort before reward");
        var expected = Run(Config(0, 6, .5), 99).Result;
        var styled = Config(0, 6, .5);
        styled.PaletteMode = "Random";
        styled.Renderer = "Pie";
        styled.SwapRoles = true;
        Check(Run(styled, 99).Result.Draw == expected.Draw, "style-independent RNG");
        var schedule = new GambleConfig { ShufflePositions = false, Schedule = new[] { Config(1, 1, 1, 2).Options, Config(4, 4, 1, 4).Options } };
        Check(Run(schedule, 1, choice: 3).Result.Tokens == 4, "explicit four-choice schedule row");
        Check(Run(schedule, 2).Result.Tokens == 1, "explicit schedule cycles");
        var timeout = new GambleEngine();
        var timeoutConfig = Config(1, 1, 1);
        timeout.Begin(timeoutConfig, 0, 0);
        timeout.Press(0, 0);
        timeout.Tick(300, 0, true);
        timeout.Tick(3000);
        timeout.Tick(3400);
        timeout.Tick(4200);
        timeout.Tick(11001);
        Check(timeout.Done && timeout.Result.Reason == "RewardTimeout" && timeout.Result.RequestedPulses == 1, "unacknowledged reward aborts without retry");
        var geometry = Config(0, 6, .5);
        geometry.Geometry.BarWidth = 80;
        geometry.Geometry.TokenRadius = 18;
        Check(Run(geometry, 99).Result.Draw == expected.Draw, "geometry-independent lottery");
        foreach (var bad in new[] { new GambleConfig { Schedule = new Lottery[0][] }, new GambleConfig { Geometry = null }, new GambleConfig { Geometry = new GambleGeometry { TokenRadius = 30 } } })
        {
            bool rejected = false;
            try
            {
                bad.Validate();
            }
            catch (ArgumentException) { rejected = true; }
            Check(rejected, "invalid schedule/geometry rejected before run");
        }
        foreach (var change in new Action<GambleConfig>[] { c => c.HoldMs = 0, c => c.RevealMs = 100, c => c.ItiMs = 9000, c => c.ChoiceCount = 5, c => c.Options[0].Low = -1, c => c.Options[0].PHigh = double.NaN, c => c.Options[1].Id = c.Options[0].Id, c => c.HighColors[0] = "invalid", c => c.PhysicalReward = true })
        {
            var c = Config(0, 6, .5);
            change(c);
            bool threw = false;
            try
            {
                c.Validate();
            }
            catch (ArgumentException) { threw = true; }
            Check(threw, "invalid config rejected");
        }
        // Real M-USE parser, including nested JSON and TSV block generation.
        var go = new GameObject("GambleParserChecks");
        var parser = go.AddComponent<ImportSettings_Level>();
        var td = parser.ConvertTextToSettings_JSON<GambleChoice_TaskDef>(JsonConvert.SerializeObject(new
        {
            Gamble = Config(2, 5, .8)
        }));
        td.Gamble.Validate();
        Check(td.Gamble.Options[0].High == 5, "MUSE JSON import");
        var blocks = parser.ConvertTextToSettings_Array<GambleChoice_BlockDef>("BlockName\tNumTrials\tChoiceCount\tRenderer\r\nTest\t3\t4\tPie\r\n");
        blocks[0].GenerateTrialDefsFromBlockDef();
        Check(blocks[0].TrialDefs.Count == 3 && ((GambleChoice_TrialDef)blocks[0].TrialDefs[0]).ChoiceCount == 4, "MUSE TSV block import");
        Check(((GambleChoice_TrialDef)blocks[0].TrialDefs[0]).Renderer == "Pie", "Windows TSV renderer normalized");
        UnityEngine.Object.DestroyImmediate(go);
        for (int i = 0; i < 1000; i++)
            Run(Config(i % 7, 6, new[] { .2, .5, .8, 1.0 }[i % 4]), i);
        string output = OutputOverride ?? Path.GetFullPath(Path.Combine(Application.dataPath, "../../../Plans/GambleChoice/validation"));
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "csharp-checks.json"), JsonConvert.SerializeObject(new
        {
            Passed = true,
            Assertions = assertions,
            ExhaustiveTrials = results.Count,
            SoakTrials = 1000,
            Unity = Application.unityVersion,
            Utc = DateTime.UtcNow
        }, Formatting.Indented));
        File.WriteAllLines(Path.Combine(output, "csharp-fixtures.jsonl"), results.Select(r => JsonConvert.SerializeObject(new { kind = "trial", data = r })));
        Debug.Log("GAMBLE CHECKS PASSED: " + assertions);
    }
}

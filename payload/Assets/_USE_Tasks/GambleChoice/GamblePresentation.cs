using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace GambleChoice
{
    // Schema 2 presentation has no access to sampling or settlement methods.
    public static class GamblePresentation
    {
        static readonly string[] colors = { "#649ECC", "#C48B55", "#77AA77", "#AD85C4", "#EEC75E", "#DB8398" };
        public static Vector2 Center(int n, int i) => n <= 3 ? new Vector2((i + .5f) * 1920 / n, 500) : new Vector2((i % (n <= 4 ? 2 : 3) + .5f) * 1920 / (n <= 4 ? 2 : 3), i < (n <= 4 ? 2 : 3) ? 320 : 740);
        public static Rect Button(int n, int i)
        {
            var p = Center(n, i);
            return new Rect(p.x - 100, p.y + 158, 200, 58);
        }
        static Rect Probability(Vector2 p) => new Rect(p.x - 170, p.y - 140, 340, 100);
        static Vector2 Outcome(Vector2 p, int i) => new Vector2(p.x - 158, p.y - 15 + i * 25);
        static Vector2 Bank(int i) => new Vector2(740 + i * 70, 100);
        static Color ColorFor(Lottery o, int i) => GambleView.Parse(o.Outcomes[i].Color ?? colors[(i + 3 * o.Palette) % colors.Length]);
        static bool Revealed(GambleView v, int i) => v.Engine.Result.ChosenIndex == i && v.Engine.Result.Tokens.HasValue && v.Engine.Result.RevealActualMs > 0 && v.Engine.Phase >= GamblePhase.Reveal;
        public static void Draw(GambleView v, VertexHelper mesh)
        {
            var e = v.Engine;
            var r = e.Result;
            var c = r.Config;
            var bg = GambleView.Parse(c.Background);
            int n = r.Options.Length;
            v.Box(mesh, new Rect(0, 0, 1920, 1080), bg);
            for (int k = 0; k < 6; k++)
            if (!(e.Phase == GamblePhase.Transfer && r.Tokens < 0 && k < Math.Abs(r.Tokens.Value)))
                v.Token(mesh, Bank(k), 21, k < Math.Max(0, Math.Min(6, r.BalanceAfter ?? 0)), GambleView.Parse(c.ActualizedColor), bg);
            for (int i = 0; i < n; i++)
            {
                var o = r.Options[i];
                var p = Center(n, i);
                var area = Probability(p);
                var rep = o.ProbabilityRepresentation ?? c.Renderer;
                float sum = 0;
                for (int j = 0; j < o.Outcomes.Length; j++)
                {
                    float f = (float)o.Outcomes[j].Probability;
                    var col = ColorFor(o, j);
                    if (rep == "HorizontalBar")
                        v.Box(mesh, new Rect(area.x + sum * area.width, area.y, area.width * f, area.height), col);
                    if (rep == "Bar")
                        v.Box(mesh, new Rect(p.x - 26, area.y + sum * area.height, 52, area.height * f), col);
                    if (rep == "Pie" || rep == "Donut")
                        v.Wedge(mesh, new Vector2(p.x, area.center.y), 50, sum, f, col);
                    if (rep == "Numeric")
                        v.Box(mesh, new Rect(area.x + 8, area.y + j * area.height / o.Outcomes.Length + 3, 12, 12), col);
                    if (rep == "Grid")
                    {
                        int start = (int)Math.Round(sum * c.GridCells), end = (int)Math.Round((sum + f) * c.GridCells);
                        int cols = Math.Min(10, c.GridCells), rows = (c.GridCells + cols - 1) / cols;
                        for (int k = start; k < end; k++)
                            v.Box(mesh, new Rect(area.x + k % cols * area.width / cols, area.y + k / cols * area.height / rows, area.width / cols - 2, area.height / rows - 2), col);
                    }
                    var source = Outcome(p, j);
                    v.Box(mesh, new Rect(source.x, source.y - 8, 16, 16), col);
                    bool selected = Revealed(v, i) && r.OutcomeIndex == j;
                    if (selected)
                        v.Border(mesh, new Rect(source.x - 6, source.y - 12, 330, 24), 2, Color.white);
                    if ((o.RewardRepresentation ?? c.AmountRenderer) == "TokenSlots")
                    for (int k = 0; k < 6; k++)
                    {
                        bool filled = k < Math.Abs(o.Outcomes[j].TokenDelta);
                        bool moved = selected && e.Phase >= GamblePhase.Transfer && filled && o.Outcomes[j].TokenDelta > 0;
                        if (!moved)
                            v.Token(mesh, source + new Vector2(205 + k * 19, 0), 7, filled, col, bg);
                    }
                    sum += f;
                }
                if (rep == "Donut")
                    v.Circle(mesh, new Vector2(p.x, area.center.y), 28, bg);
                var button = Button(n, i);
                v.ChoiceRects[i] = button;
                v.Box(mesh, button, GambleView.Parse(r.ChosenIndex == i ? c.CommittedColor : e.HoldingIndex == i ? c.HoldColor : c.IdleColor));
                if (e.HoldingIndex == i)
                    v.Box(mesh, new Rect(button.x, button.yMax - 6, button.width * Mathf.Clamp01((float)((v.Now - e.HoldStartMs) / c.HoldMs)), 6), Color.white);
                if (r.ChosenIndex == i)
                    v.Border(mesh, button, 3, Color.white);
            }
            if (e.Phase == GamblePhase.Transfer && r.Tokens.HasValue && r.ChosenIndex >= 0)
            {
                int delta = r.Tokens.Value;
                var p = Outcome(Center(n, r.ChosenIndex), r.OutcomeIndex);
                float t = Mathf.Clamp01((float)((v.Now - e.PhaseStartMs) / c.TransferMs));
                t = t * t * (3 - 2 * t);
                for (int k = 0; k < Math.Abs(delta); k++)
                {
                    var source = p + new Vector2(205 + k * 19, 0);
                    var from = delta < 0 ? Bank(k) : source;
                    var to = delta < 0 ? source : Bank(k);
                    v.Token(mesh, Vector2.Lerp(from, to, t), Mathf.Lerp(delta < 0 ? 21 : 7, delta < 0 ? 7 : 21, t), true, ColorFor(r.Options[r.ChosenIndex], r.OutcomeIndex), bg);
                }
            }
            float fraction = (float)(c.ItiMs / c.ItiReferenceMs);
            if (e.Phase == GamblePhase.ITI)
            {
                if (c.ItiMode == "Remaining")
                    fraction = (float)(Math.Max(0, c.ItiMs - (v.Now - e.PhaseStartMs)) / c.ItiReferenceMs);
                if (c.ItiMode == "Elapsed")
                    fraction = c.ItiMs == 0 ? 1 : Mathf.Clamp01((float)((v.Now - e.PhaseStartMs) / c.ItiMs));
            }
            v.Circle(mesh, new Vector2(960, 1010), 30, new Color(.3f, .36f, .4f));
            v.Wedge(mesh, new Vector2(960, 1010), 30, 0, fraction, GambleView.Parse(c.ItiColor));
        }
        struct Label
        {
            public string Text; public Rect Rect; public int Size; public Color Color; public Label(string t, Rect r, int s = 20)
            {
                Text = t;
                Rect = r;
                Size = s;
                Color = UnityEngine.Color.white;
            }
        }
        public static void Labels(GambleView v)
        {
            var labels = new List<Label>();
            var r = v.Engine?.Result;
            if (r != null && r.SchemaVersion == 2)
            {
                labels.Add(new Label("Virtual token bank: " + r.BalanceAfter + " (up to 6 shown)   " + (r.Practice ? "PRACTICE" : ""), new Rect(460, 20, 1000, 50), 28));
                if (r.Status == "Stopped")
                    labels.Add(new Label("Stopped: insufficient funds for the offered losses", new Rect(460, 190, 1000, 40), 26));
                for (int i = 0; i < r.Options.Length; i++)
                {
                    var o = r.Options[i];
                    var p = Center(r.Options.Length, i);
                    labels.Add(new Label(o.Id, new Rect(p.x - 170, p.y - 182, 340, 35), 24));
                    for (int j = 0; j < o.Outcomes.Length; j++)
                    {
                        var x = o.Outcomes[j];
                        var s = Outcome(p, j);
                        string text = x.TokenDelta.ToString("+0;-0;0", CultureInfo.InvariantCulture) + " tokens";
                        if ((o.ProbabilityRepresentation ?? r.Config.Renderer) == "Numeric")
                        {
                            var area = Probability(p);
                            // Six rows need a smaller font so uGUI's line height fits;
                            // vertical truncation otherwise hides the entire percentage.
                            int probabilitySize = Math.Min(16, 80 / o.Outcomes.Length);
                            labels.Add(new Label((x.Probability * 100).ToString("G17", CultureInfo.InvariantCulture) + "%", new Rect(area.x + 28, area.y + j * area.height / o.Outcomes.Length, 310, area.height / o.Outcomes.Length), probabilitySize));
                        }
                        labels.Add(new Label(text, new Rect(s.x + 24, s.y - 12, 180, 25), 18));
                    }
                    var buttonLabel = new Label("Choose", Button(r.Options.Length, i), 23);
                    if (r.ChosenIndex == i)
                        buttonLabel.Color = GambleView.Parse(r.Config.Background);
                    labels.Add(buttonLabel);
                }
            }
            var existing = v.LabelPool;
            while (existing.Count < labels.Count)
            {
                var go = new GameObject("GambleLabel" + existing.Count, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
                go.layer = v.gameObject.layer;
                go.transform.SetParent(v.transform, false);
                var t = go.GetComponent<Text>();
                t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                t.raycastTarget = false;
                t.alignment = TextAnchor.MiddleCenter;
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
                t.verticalOverflow = VerticalWrapMode.Truncate;
                existing.Add(t);
            }
            float scale = Mathf.Min(v.rectTransform.rect.width / 1920, v.rectTransform.rect.height / 1080);
            for (int i = 0; i < existing.Count; i++)
            {
                var t = existing[i];
                t.gameObject.SetActive(i < labels.Count);
                if (i >= labels.Count)
                    continue;
                var l = labels[i];
                var rt = t.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(.5f, .5f);
                rt.pivot = new Vector2(.5f, .5f);
                rt.anchoredPosition = v.Map(l.Rect.center);
                rt.sizeDelta = l.Rect.size * scale;
                t.fontSize = Math.Max(1, (int)(l.Size * scale));
                t.text = l.Text;
                t.color = l.Color;
            }
        }
    }
}

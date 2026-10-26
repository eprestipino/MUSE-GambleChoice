using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GambleChoice
{
    // One task-local uGUI mesh. Geometry and hit areas use a uniform 1920x1080 reference space.
    // Outcome data never comes from the rendered mesh or its colors.
    public sealed class GambleView : MaskableGraphic
    {
        public GambleEngine Engine;
        public double Now;
        public readonly Rect[] ChoiceRects = new Rect[6];
        internal readonly List<Text> LabelPool = new List<Text>();
        public int LabelRefreshCount
        {
            get; private set;
        }
        public int MeshRefreshCount
        {
            get; private set;
        }
        string labelTrial, labelStatus, meshTrial;
        int labelChoice = -2, meshChoice = -2, meshHolding = -2;
        int? labelBalance;
        Vector2 labelSize, meshSize;
        GamblePhase meshPhase;

        public void Refresh()
        {
            var result = Engine?.Result;
            if (result == null)
                return;
            int count = result.Options.Length;
            for (int i = 0; i < count; i++)
            {
                if (result.SchemaVersion == 2)
                    ChoiceRects[i] = GamblePresentation.Button(count, i);
                else
                {
                    var geometry = result.Config.Geometry;
                    float scale = count == 2 ? geometry.TwoChoiceScale : geometry.MultiChoiceScale;
                    var center = Center(count, i);
                    ChoiceRects[i] = new Rect(center.x - geometry.ButtonWidth * .5f * scale,
                        center.y + 165 * scale, geometry.ButtonWidth * scale, geometry.ButtonHeight * scale);
                }
            }

            var size = rectTransform.rect.size;
            if (labelTrial != result.TrialId || labelStatus != result.Status || labelChoice != result.ChosenIndex
                || labelBalance != result.BalanceAfter || labelSize != size)
            {
                GamblePresentation.Labels(this);
                labelTrial = result.TrialId;
                labelStatus = result.Status;
                labelChoice = result.ChosenIndex;
                labelBalance = result.BalanceAfter;
                labelSize = size;
                LabelRefreshCount++;
            }

            bool animated = Engine.Phase == GamblePhase.Transfer
                || Engine.Phase == GamblePhase.Offer && Engine.HoldingIndex >= 0
                || Engine.Phase == GamblePhase.ITI && result.Config.ItiMode != "Static";
            if (animated || meshTrial != result.TrialId || meshPhase != Engine.Phase
                || meshChoice != result.ChosenIndex || meshHolding != Engine.HoldingIndex || meshSize != size)
            {
                meshTrial = result.TrialId;
                meshPhase = Engine.Phase;
                meshChoice = result.ChosenIndex;
                meshHolding = Engine.HoldingIndex;
                meshSize = size;
                MeshRefreshCount++;
                SetVerticesDirty();
            }
        }
        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            if (Engine?.Result != null)
                Refresh();
        }
        public Vector2 ToReference(Vector2 screen)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screen, canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, out var local);
            var r = rectTransform.rect;
            float scale = Mathf.Min(r.width / 1920, r.height / 1080);
            return new Vector2((local.x - r.center.x) / scale + 960, 540 - (local.y - r.center.y) / scale);
        }
        public int Hit(Vector2 screen)
        {
            if (Engine?.Result == null)
                return -1;
            Vector2 p = ToReference(screen);
            for (int i = 0; i < Engine.Result.Options.Length; i++)
            if (ChoiceRects[i].Contains(p))
                return i;
            return -1;
        }
        public static Color Parse(string s)
        {
            ColorUtility.TryParseHtmlString(s, out var c);
            return c;
        }
        static Vector2 Center(int n, int i)
        {
            if (n == 2)
                return new Vector2(i == 0 ? 480 : 1440, 500);
            if (n == 3)
                return i == 0 ? new Vector2(960, 290) : new Vector2(i == 1 ? 480 : 1440, 680);
            return new Vector2(i % 2 == 0 ? 480 : 1440, i < 2 ? 290 : 680);
        }
        public Vector2 Slot(int n, int option, bool high, int slot)
        {
            var g = Engine.Result.Config.Geometry;
            float s = n == 2 ? g.TwoChoiceScale : g.MultiChoiceScale;
            var center = Center(n, option);
            float direction = center.x <= 960 ? -1 : 1;
            return center + new Vector2(direction * (high ? 120 : 180) * s, (-130 + slot * g.TokenSpacing) * s);
        }
        static Vector2 TopSlot(int slot) => new Vector2(735 + 90 * slot, 90);
        Color low, high;
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (Engine?.Result == null)
                return;
            var r = Engine.Result;
            var c = r.Config;
            if (c.SchemaVersion == 2)
            {
                GamblePresentation.Draw(this, vh);
                return;
            }
            Box(vh, new Rect(0, 0, 1920, 1080), Parse(c.Background));
            int n = r.Options.Length;
            for (int i = 0; i < 6; i++)
                Token(vh, TopSlot(i), c.Geometry.TopTokenRadius, r.TransferEndMs > 0 && r.Tokens.HasValue && i < r.Tokens.Value, Parse(c.ActualizedColor), Parse(c.Background));
            for (int i = 0; i < n; i++)
            {
                var o = r.Options[i];
                var center = Center(n, i);
                var g = Engine.Result.Config.Geometry;
                float s = n == 2 ? g.TwoChoiceScale : g.MultiChoiceScale;
                low = Parse(c.SwapRoles ? c.HighColors[o.Palette] : c.LowColors[o.Palette]);
                high = Parse(c.SwapRoles ? c.LowColors[o.Palette] : c.HighColors[o.Palette]);
                bool revealed = r.Status != "Aborted" && r.ChosenIndex == i && Engine.Phase >= GamblePhase.Reveal && r.Tokens.HasValue;
                bool moving = revealed && Engine.Phase >= GamblePhase.Transfer;
                float direction = center.x <= 960 ? 1 : -1;
                var bar = new Rect(center.x + direction * 120 * s - g.BarWidth * .5f * s, center.y - 145 * s, g.BarWidth * s, g.BarHeight * s);
                if (c.Renderer == "Bar")
                {
                    Box(vh, new Rect(bar.x - 3, bar.y - 3, bar.width + 6, bar.height + 6), new Color(.75f, .8f, .84f));
                    Box(vh, bar, low);
                    Box(vh, new Rect(bar.x, bar.y, bar.width, (float)o.PHigh * bar.height), high);
                    if (revealed)
                    {
                        bool h = r.Role == "High";
                        var selected = h ? new Rect(bar.x, bar.y, bar.width, (float)o.PHigh * bar.height) : new Rect(bar.x, bar.y + (float)o.PHigh * bar.height, bar.width, (float)(1 - o.PHigh) * bar.height);
                        Border(vh, selected, 5 * s, Color.white);
                    }
                }
                else
                {
                    Vector2 pc = new Vector2(bar.center.x, center.y - 15 * s);
                    Circle(vh, pc, 79 * s, new Color(.75f, .8f, .84f));
                    Circle(vh, pc, 76 * s, low);
                    Wedge(vh, pc, 76 * s, 0, (float)o.PHigh, high);
                    if (revealed)
                    {
                        float start = r.Role == "High" ? 0 : (float)o.PHigh;
                        float length = r.Role == "High" ? (float)o.PHigh : (float)(1 - o.PHigh);
                        Arc(vh, pc, 83 * s, 3 * s, start, length, Color.white);
                    }
                }
                for (int role = 0; role < 2; role++)
                {
                    bool h = role == 1;
                    int amount = h ? o.High : o.Low;
                    Color col = h ? high : low;
                    for (int j = 0; j < 6; j++)
                    {
                        bool filled = j >= 6 - amount;
                        bool selectedRole = revealed && (h == (r.Role == "High"));
                        Vector2 pos = Slot(n, i, h, j);
                        // Empty source slots remain; only earned tokens transfer in this version.
                        if (!(moving && selectedRole && filled))
                            Token(vh, pos, g.TokenRadius * s, filled, col, Parse(c.Background));
                        if (selectedRole)
                            Ring(vh, pos, (g.TokenRadius + 4) * s, 2 * s, Color.white);
                    }
                }
                var button = new Rect(center.x - g.ButtonWidth * .5f * s, center.y + 165 * s, g.ButtonWidth * s, g.ButtonHeight * s);
                ChoiceRects[i] = button;
                Color bc = Parse(r.ChosenIndex == i ? c.CommittedColor : Engine.HoldingIndex == i ? c.HoldColor : c.IdleColor);
                Box(vh, button, bc);
                if (Engine.HoldingIndex == i)
                {
                    float f = Mathf.Clamp01((float)((Now - Engine.HoldStartMs) / c.HoldMs));
                    Box(vh, new Rect(button.x, button.y + button.height - 7, button.width * f, 7), Color.white);
                }
                if (r.ChosenIndex == i)
                    Border(vh, button, 4, Color.white);
            }
            if (Engine.Phase == GamblePhase.Transfer && r.Tokens.HasValue)
            {
                var o = r.Options[r.ChosenIndex];
                bool h = r.Role == "High";
                Color col = Parse(h ? (c.SwapRoles ? c.LowColors[o.Palette] : c.HighColors[o.Palette]) : (c.SwapRoles ? c.HighColors[o.Palette] : c.LowColors[o.Palette]));
                float t = Mathf.Clamp01((float)((Now - Engine.PhaseStartMs) / c.TransferMs));
                float ease = t * t * (3 - 2 * t);
                for (int j = 0; j < r.Tokens.Value; j++)
                {
                    var from = Slot(n, r.ChosenIndex, h, 6 - r.Tokens.Value + j);
                    Token(vh, Vector2.Lerp(from, TopSlot(j), ease), Mathf.Lerp(c.Geometry.TokenRadius * (n == 2 ? c.Geometry.TwoChoiceScale : c.Geometry.MultiChoiceScale), c.Geometry.TopTokenRadius, ease), true, col, Parse(c.Background));
                }
            }
            double fraction = c.ItiMs / c.ItiReferenceMs;
            if (Engine.Phase == GamblePhase.ITI)
            {
                if (c.ItiMode == "Remaining")
                    fraction = Math.Max(0, c.ItiMs - (Now - Engine.PhaseStartMs)) / c.ItiReferenceMs;
                if (c.ItiMode == "Elapsed")
                    fraction = c.ItiMs == 0 ? 1 : Math.Min(1, (Now - Engine.PhaseStartMs) / c.ItiMs);
            }
            Circle(vh, new Vector2(960, 1000), 39, new Color(.75f, .8f, .84f));
            Circle(vh, new Vector2(960, 1000), 36, new Color(.3f, .36f, .4f));
            Wedge(vh, new Vector2(960, 1000), 36, 0, (float)fraction, Parse(c.ItiColor));
        }
        internal Vector2 Map(Vector2 p)
        {
            var r = rectTransform.rect;
            float s = Mathf.Min(r.width / 1920, r.height / 1080);
            return new Vector2(r.center.x + (p.x - 960) * s, r.center.y + (540 - p.y) * s);
        }
        void Triangle(VertexHelper v, Vector2 a, Vector2 b, Vector2 c, Color color)
        {
            int n = v.currentVertCount;
            v.AddVert(Map(a), color, Vector2.zero);
            v.AddVert(Map(b), color, Vector2.zero);
            v.AddVert(Map(c), color, Vector2.zero);
            v.AddTriangle(n, n + 1, n + 2);
        }
        internal void Box(VertexHelper v, Rect r, Color c)
        {
            if (r.width <= 0 || r.height <= 0)
                return;
            Triangle(v, new Vector2(r.x, r.y), new Vector2(r.xMax, r.y), new Vector2(r.xMax, r.yMax), c);
            Triangle(v, new Vector2(r.x, r.y), new Vector2(r.xMax, r.yMax), new Vector2(r.x, r.yMax), c);
        }
        internal void Border(VertexHelper v, Rect r, float w, Color c)
        {
            if (r.height <= 0)
                return;
            Box(v, new Rect(r.x - w, r.y - w, r.width + 2 * w, w), c);
            Box(v, new Rect(r.x - w, r.yMax, r.width + 2 * w, w), c);
            Box(v, new Rect(r.x - w, r.y, w, r.height), c);
            Box(v, new Rect(r.xMax, r.y, w, r.height), c);
        }
        internal void Circle(VertexHelper v, Vector2 p, float radius, Color c) => Wedge(v, p, radius, 0, 1, c);
        internal void Wedge(VertexHelper v, Vector2 p, float radius, float start, float fraction, Color c)
        {
            if (fraction <= 0)
                return;
            int steps = Math.Max(1, (int)Math.Ceiling(fraction * (Engine?.Result?.SchemaVersion == 2 ? 32 : 72)));
            for (int j = 0; j < steps; j++)
            {
                float a = (start + fraction * j / steps) * 2 * Mathf.PI - Mathf.PI / 2;
                float b = (start + fraction * (j + 1) / steps) * 2 * Mathf.PI - Mathf.PI / 2;
                Triangle(v, p, p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius, p + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * radius, c);
            }
        }
        internal void Ring(VertexHelper v, Vector2 p, float radius, float width, Color color)
        {
            Arc(v, p, radius, width, 0, 1, color);
        }
        internal void Arc(VertexHelper v, Vector2 p, float radius, float width, float start, float fraction, Color color)
        {
            int steps = Math.Max(1, (int)Math.Ceiling((Engine?.Result?.SchemaVersion == 2 ? 24 : 72) * fraction));
            for (int j = 0; j < steps; j++)
            {
                float a = (start + fraction * j / steps) * 2 * Mathf.PI - Mathf.PI / 2, b = (start + fraction * (j + 1) / steps) * 2 * Mathf.PI - Mathf.PI / 2;
                var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                var e = new Vector2(Mathf.Cos(b), Mathf.Sin(b));
                Triangle(v, p + d * radius, p + e * radius, p + e * (radius - width), color);
                Triangle(v, p + d * radius, p + e * (radius - width), p + d * (radius - width), color);
            }
        }
        internal void Token(VertexHelper v, Vector2 p, float radius, bool filled, Color col, Color bg)
        {
            if (filled)
                Circle(v, p, radius, col);
            else
                Ring(v, p, radius, 2.5f, col);
        }
    }
}

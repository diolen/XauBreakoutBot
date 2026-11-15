using System;
using System.Collections.Generic;
using cAlgo.API;

namespace cAlgo.Robots.SupportResistance;

/// <summary>Visualización configurable; no altera reglas de detección.</summary>
public sealed class LevelChartVisualizer
{
    private const string Prefix = "SR_";
    private readonly Chart _chart;
    private readonly VisualizationMode _mode;
    private readonly bool _showInvalidated;
    private readonly bool _showSwings;
    private readonly bool _showTouches;
    private readonly bool _showConfluence;
    private readonly bool _showEntryCandidates;
    private readonly HashSet<string> _drawn = new();

    public LevelChartVisualizer(
        Chart chart,
        VisualizationMode mode = VisualizationMode.TradingView,
        bool showInvalidated = true,
        bool showSwings = true,
        bool showTouches = true,
        bool showConfluence = true,
        bool showEntryCandidates = false)
    {
        _chart = chart;
        _mode = mode;
        _showInvalidated = showInvalidated;
        _showSwings = showSwings;
        _showTouches = showTouches;
        _showConfluence = showConfluence;
        _showEntryCandidates = showEntryCandidates;
    }

    public VisualizationMode Mode => _mode;

    public void Clear()
    {
        foreach (var name in _drawn)
            _chart.RemoveObject(name);
        _drawn.Clear();
    }

    /// <summary>
    /// Redibuja desde cero: elimina objetos previos y crea solo los del modo actual.
    /// </summary>
    public void Render(
        IReadOnlyList<SrLevel> levels,
        IReadOnlyList<SwingPoint> swings,
        IReadOnlyList<OhlcBar> recentBarsForExtent,
        CandidateSnapshot candidates,
        double bid,
        double ask,
        int referenceBarIndex)
    {
        // Siempre Clear primero → los que dejan de estar seleccionados desaparecen; sin duplicados.
        Clear();

        if (recentBarsForExtent.Count == 0)
            return;

        // Extent temporal de datos M15 (solo geometría histórica de líneas TV/debug).
        var t0 = recentBarsForExtent[0].OpenTime;
        var t1 = recentBarsForExtent[^1].OpenTime.AddMinutes(15);

        // Viewport del Chart (M15 o H1): solo posición gráfica de etiquetas de candidatos.
        var viewport = ResolveChartViewport(t1);

        if (_mode == VisualizationMode.TradingView)
            RenderTradingView(levels, t0, t1, bid, ask, referenceBarIndex);
        else
            RenderFullDebug(levels, swings, t0, t1, candidates);

        // Capa independiente: candidatos estructurales T=2 (no señales de ejecución).
        if (_showEntryCandidates)
            RenderEntryCandidates(levels, t0, viewport, bid, ask, referenceBarIndex);
    }

    /// <summary>
    /// Lee First/LastVisibleBarIndex del Chart únicamente para anclar etiquetas.
    /// No altera niveles, ATR ni candidatos (calculados en M15).
    /// </summary>
    private ChartViewport ResolveChartViewport(DateTime dataExtentEnd)
    {
        var bars = _chart.Bars;
        if (bars != null &&
            ChartVisibleLabelAnchor.IsUsableVisibleRange(
                _chart.FirstVisibleBarIndex,
                _chart.LastVisibleBarIndex,
                bars.Count))
        {
            var first = _chart.FirstVisibleBarIndex;
            var last = _chart.LastVisibleBarIndex;
            var labelBarIndex = ChartVisibleLabelAnchor.ResolveLabelBarIndex(first, last);
            var visibleEnd = bars.OpenTimes[last];
            var labelTime = bars.OpenTimes[labelBarIndex];
            var lineEnd = ChartVisibleLabelAnchor.ResolveLineEnd(dataExtentEnd, visibleEnd);
            return new ChartViewport(labelBarIndex, labelTime, lineEnd, useBarIndex: true);
        }

        // Fallback (viewport no usable / Cloud): tiempo derivado del extent M15.
        var fallbackTime = ChartVisibleLabelAnchor.ResolveLabelTime(null, null, dataExtentEnd);
        return new ChartViewport(barIndex: -1, fallbackTime, dataExtentEnd, useBarIndex: false);
    }

    private void RenderEntryCandidates(
        IReadOnlyList<SrLevel> levels,
        DateTime dataT0,
        ChartViewport viewport,
        double bid,
        double ask,
        int referenceBarIndex)
    {
        // Selección siempre con niveles/precios/índice M15 del motor (referenceBarIndex).
        var snapshot = EntryCandidateSelector.Select(levels, bid, ask, referenceBarIndex);

        if (snapshot.IsEmpty)
        {
            var infoName = Prefix + "ENTRY_NONE";
            DrawViewportText(infoName, EntryCandidateSelector.EmptyMessage, viewport, ask, Color.DimGray);
            return;
        }

        foreach (var level in snapshot.LongCandidates)
            DrawEntryCandidate(level, dataT0, viewport, isLong: true);

        foreach (var level in snapshot.ShortCandidates)
            DrawEntryCandidate(level, dataT0, viewport, isLong: false);

        // T1/T2: Time/Price históricos originales (no se reanclan al viewport).
        if (_showTouches)
        {
            var markers = TradingViewTouchMarkers.Build(snapshot.All(), showTouches: true);
            foreach (var m in markers)
            {
                if (_drawn.Contains(m.ObjectName))
                    continue;
                _chart.DrawIcon(m.ObjectName, ChartIconType.Diamond, m.Time, m.Price, Color.DodgerBlue);
                _drawn.Add(m.ObjectName);

                var textName = m.ObjectName + "_L";
                if (_drawn.Contains(textName))
                    continue;
                _chart.DrawText(textName, m.Label, m.Time, m.Price, Color.DodgerBlue);
                _drawn.Add(textName);
            }
        }
    }

    private void DrawEntryCandidate(SrLevel level, DateTime dataT0, ChartViewport viewport, bool isLong)
    {
        // Estilos diferenciados: LONG (resistencia) vs SHORT (soporte). Sin flechas de ejecución.
        var color = isLong ? Color.MediumVioletRed : Color.DarkCyan;
        const int thickness = 3;
        const LineStyle style = LineStyle.DotsRare;
        var tag = isLong ? "LONG CANDIDATE" : "SHORT CANDIDATE";
        var lineEnd = viewport.LineEnd;

        if (level.Geometry == LevelGeometry.Horizontal)
        {
            var lineName = Prefix + "EC_H_" + level.Id;
            if (!_drawn.Contains(lineName))
            {
                _chart.DrawTrendLine(lineName, dataT0, level.Center, lineEnd, level.Center, color, thickness, style);
                _drawn.Add(lineName);
            }
        }
        else if (level.Anchors.Count >= 2)
        {
            var a0 = level.Anchors[0];
            var lineName = Prefix + "EC_D_" + level.Id;
            if (!_drawn.Contains(lineName))
            {
                var pEnd = level.ProjectPriceAtTime(lineEnd);
                _chart.DrawTrendLine(lineName, a0.Time, a0.Price, lineEnd, pEnd, color, thickness, style);
                _drawn.Add(lineName);
            }
        }

        var labelPrice = level.Geometry == LevelGeometry.Horizontal
            ? level.Center
            : level.ProjectPriceAtTime(viewport.LabelTime);
        var labelName = Prefix + "EC_LBL_" + level.Id;
        var text = $"{tag} #{level.Id} T={level.IndependentTouchCount}";
        DrawViewportText(labelName, text, viewport, labelPrice, color);
    }

    private void DrawViewportText(string name, string text, ChartViewport viewport, double price, Color color)
    {
        if (_drawn.Contains(name))
            return;

        // Preferir índice de barra visible del Chart (M15/H1) para evitar clip en el borde.
        if (viewport.UseBarIndex)
            _chart.DrawText(name, text, viewport.LabelBarIndex, price, color);
        else
            _chart.DrawText(name, text, viewport.LabelTime, price, color);

        _drawn.Add(name);
    }

    private readonly struct ChartViewport
    {
        public ChartViewport(int barIndex, DateTime labelTime, DateTime lineEnd, bool useBarIndex)
        {
            LabelBarIndex = barIndex;
            LabelTime = labelTime;
            LineEnd = lineEnd;
            UseBarIndex = useBarIndex;
        }

        public int LabelBarIndex { get; }
        public DateTime LabelTime { get; }
        public DateTime LineEnd { get; }
        public bool UseBarIndex { get; }
    }

    private void RenderTradingView(
        IReadOnlyList<SrLevel> levels,
        DateTime t0,
        DateTime t1,
        double bid,
        double ask,
        int referenceBarIndex)
    {
        var selected = TradingViewLevelSelector.Select(levels, bid, ask, referenceBarIndex);

        foreach (var level in selected)
        {
            var color = ResolveColor(level);
            const int thickness = 2;
            const LineStyle style = LineStyle.Solid;

            if (level.Geometry == LevelGeometry.Horizontal)
            {
                DrawHorizontalLineOnly(level, t0, t1, color, thickness, style);
            }
            else if (level.Anchors.Count >= 2)
            {
                DrawDiagonalLineOnly(level, t1, color, thickness, style);
            }

            DrawLabel(level, t1, color, showConfluence: false);
        }

        // Show Touches también aplica en Trading View (solo niveles seleccionados).
        DrawTradingViewTouchMarkers(selected);
    }

    private void DrawTradingViewTouchMarkers(IReadOnlyList<SrLevel> selected)
    {
        var markers = TradingViewTouchMarkers.Build(selected, _showTouches);
        foreach (var m in markers)
        {
            // Diamante + texto corto: visibles sin saturar (≤6 niveles × toques).
            _chart.DrawIcon(m.ObjectName, ChartIconType.Diamond, m.Time, m.Price, Color.DodgerBlue);
            _drawn.Add(m.ObjectName);

            var textName = m.ObjectName + "_L";
            _chart.DrawText(textName, m.Label, m.Time, m.Price, Color.DodgerBlue);
            _drawn.Add(textName);
        }
    }

    private void RenderFullDebug(
        IReadOnlyList<SrLevel> levels,
        IReadOnlyList<SwingPoint> swings,
        DateTime t0,
        DateTime t1,
        CandidateSnapshot candidates)
    {
        foreach (var level in levels)
        {
            if (level.State == LevelState.Invalidated && !_showInvalidated)
                continue;

            var color = ResolveColor(level);
            var thickness = level.State == LevelState.Active ? 2 : 1;
            var style = level.State == LevelState.Invalidated ? LineStyle.Dots : LineStyle.Solid;

            if (level.Geometry == LevelGeometry.Horizontal)
            {
                DrawHorizontalZone(level, t0, t1, color, thickness, style);
            }
            else if (level.Anchors.Count >= 2)
            {
                DrawDiagonalFull(level, t1, color, thickness, style);
            }

            DrawLabel(level, t1, color, _showConfluence);
        }

        if (_showSwings)
        {
            foreach (var s in swings)
            {
                var name = Prefix + "SW_" + s.Id;
                var icon = s.Kind == SwingKind.High ? ChartIconType.DownArrow : ChartIconType.UpArrow;
                var color = s.Kind == SwingKind.High ? Color.OrangeRed : Color.LimeGreen;
                _chart.DrawIcon(name, icon, s.Time, s.Price, color);
                _drawn.Add(name);
            }
        }

        if (_showTouches)
        {
            foreach (var level in levels)
            {
                if (level.State == LevelState.Invalidated && !_showInvalidated)
                    continue;
                foreach (var t in level.Touches)
                {
                    if (!t.IsIndependent)
                        continue;
                    var name = Prefix + "TH_" + level.Id + "_" + t.BarIndex;
                    _chart.DrawIcon(name, ChartIconType.Circle, t.Time, t.Price, Color.DodgerBlue);
                    _drawn.Add(name);
                }
            }
        }

        HighlightCandidate(candidates.NearestResistanceAboveAsk, "CAN_R", Color.Gold);
        HighlightCandidate(candidates.NearestSupportBelowBid, "CAN_S", Color.Gold);
    }

    private void DrawLabel(SrLevel level, DateTime t1, Color color, bool showConfluence)
    {
        var labelName = Prefix + "LBL_" + level.Id;
        var labelPrice = level.Geometry == LevelGeometry.Horizontal
            ? level.Center
            : level.ProjectPriceAtTime(t1);
        var conf = showConfluence && level.ConfluenceIds.Count > 0
            ? " C[" + string.Join(",", level.ConfluenceIds) + "]"
            : "";
        var text = $"#{level.Id} {Geom(level)} {Role(level)} {level.State} T={level.IndependentTouchCount}{conf}";
        _chart.DrawText(labelName, text, t1, labelPrice, color);
        _drawn.Add(labelName);
    }

    private void DrawHorizontalLineOnly(SrLevel level, DateTime t0, DateTime t1, Color color, int thickness, LineStyle style)
    {
        var mid = Prefix + "H_" + level.Id;
        _chart.DrawTrendLine(mid, t0, level.Center, t1, level.Center, color, thickness, style);
        _drawn.Add(mid);
    }

    private void DrawHorizontalZone(SrLevel level, DateTime t0, DateTime t1, Color color, int thickness, LineStyle style)
    {
        DrawHorizontalLineOnly(level, t0, t1, color, thickness, style);

        var zone = Prefix + "HZ_" + level.Id;
        var fill = Color.FromArgb(level.State == LevelState.Active ? 40 : 20, color);
        var rect = _chart.DrawRectangle(zone, t0, level.Center - level.HalfWidth, t1, level.Center + level.HalfWidth, fill);
        rect.IsFilled = true;
        _drawn.Add(zone);
    }

    private void DrawDiagonalLineOnly(SrLevel level, DateTime tEnd, Color color, int thickness, LineStyle style)
    {
        var a0 = level.Anchors[0];
        var name = Prefix + "D_" + level.Id;
        var pEnd = level.ProjectPriceAtTime(tEnd);
        _chart.DrawTrendLine(name, a0.Time, a0.Price, tEnd, pEnd, color, thickness, style);
        _drawn.Add(name);
    }

    private void DrawDiagonalFull(SrLevel level, DateTime tEnd, Color color, int thickness, LineStyle style)
    {
        var a0 = level.Anchors[0];
        DrawDiagonalLineOnly(level, tEnd, color, thickness, style);

        var pEnd = level.ProjectPriceAtTime(tEnd);
        var band = Prefix + "DZ_" + level.Id;
        var fill = Color.FromArgb(level.State == LevelState.Active ? 35 : 15, color);
        var y1a = a0.Price - level.HalfWidth;
        var y1b = a0.Price + level.HalfWidth;
        var rect = _chart.DrawRectangle(band, a0.Time, Math.Min(y1a, pEnd - level.HalfWidth), tEnd,
            Math.Max(y1b, pEnd + level.HalfWidth), fill);
        rect.IsFilled = true;
        _drawn.Add(band);

        var up = Prefix + "DU_" + level.Id;
        var dn = Prefix + "DD_" + level.Id;
        _chart.DrawTrendLine(up, a0.Time, a0.Price + level.HalfWidth, tEnd, pEnd + level.HalfWidth, color, 1, LineStyle.DotsRare);
        _chart.DrawTrendLine(dn, a0.Time, a0.Price - level.HalfWidth, tEnd, pEnd - level.HalfWidth, color, 1, LineStyle.DotsRare);
        _drawn.Add(up);
        _drawn.Add(dn);
    }

    private void HighlightCandidate(SrLevel? level, string tag, Color color)
    {
        if (level == null)
            return;
        var name = Prefix + tag + "_" + level.Id;
        _chart.DrawText(name, "◆ CAND #" + level.Id, level.UpdatedAt, level.Center, color);
        _drawn.Add(name);
    }

    private static Color ResolveColor(SrLevel level)
    {
        if (level.State == LevelState.Invalidated)
            return Color.Gray;
        if (level.State == LevelState.Provisional)
            return level.Role == LevelRole.Resistance ? Color.FromArgb(180, Color.Orange) : Color.FromArgb(180, Color.Turquoise);
        return level.Role == LevelRole.Resistance ? Color.Red : Color.SeaGreen;
    }

    private static string Geom(SrLevel l) => l.Geometry == LevelGeometry.Horizontal ? "H" : "D";
    private static string Role(SrLevel l) => l.Role == LevelRole.Resistance ? "R" : "S";
}

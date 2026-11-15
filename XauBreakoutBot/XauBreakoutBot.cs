using cAlgo.Robots.SupportResistance;

namespace cAlgo.Robots;

/// <summary>
/// cBot XAUUSD — módulo de detección de soportes/resistencias M15.
/// Sin lógica de entrada ni ejecución de órdenes.
/// </summary>
[Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
public partial class XauBreakoutBot : Robot
{
    private const string ModuleVersion = "1.5-t3-breakout-signal";

    [Parameter("Enable T3 Breakout Signals", DefaultValue = true, Group = "Signals")]
    public bool EnableT3BreakoutSignals { get; set; }

    [Parameter("Log Replay T3 Signals", DefaultValue = false, Group = "Signals")]
    public bool LogReplayT3Signals { get; set; }

    [Parameter("Enable Visualization", DefaultValue = true, Group = "Visualization")]
    public bool EnableVisualization { get; set; }

    [Parameter("Visualization Mode", DefaultValue = VisualizationMode.TradingView, Group = "Visualization")]
    public VisualizationMode VisualizationMode { get; set; }

    [Parameter("Show Invalidated", DefaultValue = true, Group = "Visualization")]
    public bool ShowInvalidated { get; set; }

    [Parameter("Show Swings", DefaultValue = true, Group = "Visualization")]
    public bool ShowSwings { get; set; }

    [Parameter("Show Touches", DefaultValue = true, Group = "Visualization")]
    public bool ShowTouches { get; set; }

    [Parameter("Show Confluence", DefaultValue = true, Group = "Visualization")]
    public bool ShowConfluence { get; set; }

    [Parameter("Show Entry Candidates", DefaultValue = true, Group = "Visualization")]
    public bool ShowEntryCandidates { get; set; }

    private Bars _m15 = null!;
    private SupportResistanceEngine _engine = null!;
    private T3BreakoutSignalDetector _t3Signals = null!;
    private LevelChartVisualizer? _visualizer;
    private int _lastProcessedClosedIndex = -1;
    private int _visualRefreshCounter;
    private long[]? _lastTradingViewIds;
    private long[]? _lastEntryCandidateIds;

    protected override void OnStart()
    {
        _engine = new SupportResistanceEngine(
            swingStrength: SupportResistanceEngine.DefaultSwingStrength,
            lookbackBars: SupportResistanceEngine.DefaultLookbackBars,
            atrPeriod: SupportResistanceEngine.DefaultAtrPeriod);
        _t3Signals = new T3BreakoutSignalDetector();

        _m15 = MarketData.GetBars(TimeFrame.Minute15);
        _m15.BarOpened += OnM15BarOpened;

        if (EnableVisualization)
        {
            _visualizer = new LevelChartVisualizer(
                Chart,
                mode: VisualizationMode,
                showInvalidated: ShowInvalidated,
                showSwings: ShowSwings,
                showTouches: ShowTouches,
                showConfluence: ShowConfluence,
                showEntryCandidates: ShowEntryCandidates);
        }

        ReplayHistory();

        Print(
            "XauBreakoutBot {0} iniciado. Detección S/R M15 activa. Sin trading. T3Signals={1}, VizMode={2}, Symbol={3}, ChartTF={4}, M15Bars={5}",
            ModuleVersion, EnableT3BreakoutSignals, VisualizationMode, SymbolName, TimeFrame, _m15.Count);

        var warn = _engine.ConsumeHistoryWarningOnce();
        if (!string.IsNullOrEmpty(warn))
            Print("ADVERTENCIA historial: {0}", warn);

        RefreshVisualization(force: true);
    }

    protected override void OnTick()
    {
        // Invalidación Bid/Ask + candidatos en cada tick (sin órdenes).
        _engine.ProcessTick(Server.Time, Symbol.Bid, Symbol.Ask);

        _visualRefreshCounter++;
        var selectionChanged = VisualizationMode == VisualizationMode.TradingView && TradingViewSelectionChanged();
        var entryChanged = ShowEntryCandidates && EntryCandidateSelectionChanged();
        if (selectionChanged || entryChanged || _visualRefreshCounter >= 20)
        {
            _visualRefreshCounter = 0;
            RefreshVisualization(force: true);
        }
    }

    protected override void OnStop()
    {
        _m15.BarOpened -= OnM15BarOpened;
        _visualizer?.Clear();
        _lastTradingViewIds = null;
        _lastEntryCandidateIds = null;
        Print(
            "XauBreakoutBot detenido. Niveles Active={0}, Total={1}. Señales T3Breakout={2}. Sin operaciones ejecutadas por este módulo.",
            CountActive(), _engine.Levels.Count, _t3Signals.Signals.Count);
    }

    private void OnM15BarOpened(BarOpenedEventArgs args)
    {
        // La vela recién abierta es Count-1; la última cerrada es Count-2.
        var closedIndex = _m15.Count - 2;
        if (closedIndex < 0 || closedIndex <= _lastProcessedClosedIndex)
            return;

        for (var i = _lastProcessedClosedIndex + 1; i <= closedIndex; i++)
            ProcessM15ClosedIndex(i);

        RefreshVisualization(force: true);
    }

    private void ReplayHistory()
    {
        // Última barra (Count-1) está en formación → no incluirla.
        var lastClosed = _m15.Count - 2;
        if (lastClosed < 0)
        {
            Print("Sin velas M15 cerradas disponibles al arranque.");
            return;
        }

        // Misma ventana que ReplayClosedBars(enforceLookbackWindow:true): últimas MinimumPreferredBars.
        // Barra a barra para capturar señales T3 con snapshots de invalidación.
        _engine.Reset();
        _t3Signals.Reset();

        var preferred = _engine.MinimumPreferredBars;
        var count = lastClosed + 1;
        var start = count > preferred ? count - preferred : 0;

        var replaySignalCount = 0;
        for (var src = start; src <= lastClosed; src++)
        {
            var bar = new OhlcBar(
                src - start,
                _m15.OpenTimes[src],
                _m15.OpenPrices[src],
                _m15.HighPrices[src],
                _m15.LowPrices[src],
                _m15.ClosePrices[src]);
            _engine.ProcessClosedBar(bar);
            if (EnableT3BreakoutSignals)
            {
                var newly = _t3Signals.ProcessAfterClosedBar(_engine, bar);
                replaySignalCount += newly.Count;
                if (LogReplayT3Signals)
                {
                    foreach (var s in newly)
                        LogT3Signal(s, fromReplay: true);
                }
            }
        }

        _lastProcessedClosedIndex = lastClosed;

        Print(
            "Replay M15: cerradas={0}, ventana={1}, swings={2}, niveles={3}, Active={4}, ATR_ready={5}, ATR_current={6:0.####}, T3Signals={7}",
            count,
            lastClosed - start + 1,
            _engine.Swings.Count,
            _engine.Levels.Count,
            CountActive(),
            _engine.AtrReady,
            _engine.AtrReady ? _engine.AtrCurrent : 0,
            replaySignalCount);

        Print(
            "Limitación: el replay OHLC usa proxy de invalidación por Close; no reconstruye invalidaciones tick intradía.");
    }

    private void ProcessM15ClosedIndex(int index)
    {
        var bar = new OhlcBar(
            _engine.ClosedBarCount,
            _m15.OpenTimes[index],
            _m15.OpenPrices[index],
            _m15.HighPrices[index],
            _m15.LowPrices[index],
            _m15.ClosePrices[index]);

        _engine.ProcessClosedBar(bar);
        if (EnableT3BreakoutSignals)
        {
            var newly = _t3Signals.ProcessAfterClosedBar(_engine, bar);
            foreach (var s in newly)
                LogT3Signal(s, fromReplay: false);
        }

        _lastProcessedClosedIndex = index;

        // Tras cierre, actualizar candidatos con último Bid/Ask conocido.
        _engine.ProcessTick(Server.Time, Symbol.Bid, Symbol.Ask);
    }

    private void LogT3Signal(T3BreakoutSignal s, bool fromReplay)
    {
        Print(
            "T3BreakoutSignal{0}: {1} LevelId={2} Role={3} Geom={4} T3Bar={5} BreakBar={6} Close={7:0.####} Mid={8:0.####} Upper={9:0.####} Lower={10:0.####} ATR={11:0.####} Reason={12}",
            fromReplay ? " [replay]" : "",
            s.Direction,
            s.LevelId,
            s.LevelRole,
            s.Geometry,
            s.T3BarIndex,
            s.BreakoutBarIndex,
            s.ConfirmationClose,
            s.LevelMidAtBreakout,
            s.UpperBoundAtBreakout,
            s.LowerBoundAtBreakout,
            s.AtrAtBreakout,
            s.BreakoutReason);
    }

    private bool TradingViewSelectionChanged()
    {
        var barIndex = _engine.ClosedBarCount > 0 ? _engine.ClosedBarCount - 1 : 0;
        var selected = TradingViewLevelSelector.Select(
            _engine.Levels, Symbol.Bid, Symbol.Ask, barIndex);
        var ids = TradingViewLevelSelector.ToIdArray(selected);
        return !TradingViewLevelSelector.SameIdSet(_lastTradingViewIds, ids);
    }

    private bool EntryCandidateSelectionChanged()
    {
        var barIndex = _engine.ClosedBarCount > 0 ? _engine.ClosedBarCount - 1 : 0;
        var snapshot = EntryCandidateSelector.Select(
            _engine.Levels, Symbol.Bid, Symbol.Ask, barIndex);
        var ids = EntryCandidateSelector.ToIdArray(snapshot);
        return !EntryCandidateSelector.SameIdSet(_lastEntryCandidateIds, ids);
    }

    private void RefreshVisualization(bool force)
    {
        if (_visualizer == null || (!force && !EnableVisualization))
            return;
        if (!EnableVisualization)
        {
            _visualizer.Clear();
            _lastTradingViewIds = null;
            _lastEntryCandidateIds = null;
            return;
        }

        var extent = BuildExtentBars();
        var barIndex = _engine.ClosedBarCount > 0 ? _engine.ClosedBarCount - 1 : 0;
        var bid = Symbol.Bid;
        var ask = Symbol.Ask;

        if (VisualizationMode == VisualizationMode.TradingView)
        {
            var selected = TradingViewLevelSelector.Select(_engine.Levels, bid, ask, barIndex);
            var ids = TradingViewLevelSelector.ToIdArray(selected);
            if (!force && TradingViewLevelSelector.SameIdSet(_lastTradingViewIds, ids) &&
                (!ShowEntryCandidates || !EntryCandidateSelectionChanged()))
                return;
            _lastTradingViewIds = ids;
        }
        else
        {
            _lastTradingViewIds = null;
        }

        if (ShowEntryCandidates)
        {
            var entry = EntryCandidateSelector.Select(_engine.Levels, bid, ask, barIndex);
            _lastEntryCandidateIds = EntryCandidateSelector.ToIdArray(entry);
        }
        else
        {
            _lastEntryCandidateIds = null;
        }

        _visualizer.Render(
            _engine.Levels,
            _engine.Swings,
            extent,
            _engine.Candidates,
            bid,
            ask,
            barIndex);
    }

    private List<OhlcBar> BuildExtentBars()
    {
        var list = new List<OhlcBar>();
        var lastClosed = _m15.Count - 2;
        if (lastClosed < 0)
            return list;

        var start = Math.Max(0, lastClosed - SupportResistanceEngine.DefaultLookbackBars);
        for (var i = start; i <= lastClosed; i++)
        {
            list.Add(new OhlcBar(
                i,
                _m15.OpenTimes[i],
                _m15.OpenPrices[i],
                _m15.HighPrices[i],
                _m15.LowPrices[i],
                _m15.ClosePrices[i]));
        }

        return list;
    }

    private int CountActive()
    {
        var n = 0;
        foreach (var l in _engine.Levels)
        {
            if (l.State == LevelState.Active)
                n++;
        }

        return n;
    }
}

using cAlgo.Robots.Risk;
using cAlgo.Robots.SupportResistance;

namespace cAlgo.Robots;

/// <summary>
/// cBot XAUUSD — detección S/R M15 + señales T3 + plan de riesgo MVP.
/// Calcula SL/TP/volumen; no ejecuta órdenes.
/// </summary>
[Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
public partial class XauBreakoutBot : Robot
{
    private const string ModuleVersion = "1.6-risk-mvp";

    [Parameter("Enable T3 Breakout Signals", DefaultValue = true, Group = "Signals")]
    public bool EnableT3BreakoutSignals { get; set; }

    [Parameter("Log Replay T3 Signals", DefaultValue = false, Group = "Signals")]
    public bool LogReplayT3Signals { get; set; }

    [Parameter("Enable Risk Plans", DefaultValue = true, Group = "Risk")]
    public bool EnableRiskPlans { get; set; }

    [Parameter("Log Risk Plans", DefaultValue = true, Group = "Risk")]
    public bool LogRiskPlans { get; set; }

    [Parameter("Log Replay Risk Plans", DefaultValue = false, Group = "Risk")]
    public bool LogReplayRiskPlans { get; set; }

    [Parameter("Risk Percent", DefaultValue = RiskParameters.DefaultRiskPercent, Group = "Risk")]
    public double RiskPercent { get; set; }

    [Parameter("Stop Buffer ATR", DefaultValue = RiskParameters.DefaultStopBufferAtr, Group = "Risk")]
    public double StopBufferAtr { get; set; }

    [Parameter("Reward Risk Ratio", DefaultValue = RiskParameters.DefaultRewardRiskRatio, Group = "Risk")]
    public double RewardRiskRatio { get; set; }

    [Parameter("Max Open Positions", DefaultValue = RiskParameters.DefaultMaxOpenPositions, Group = "Risk")]
    public int MaxOpenPositions { get; set; }

    [Parameter("Bot Position Label", DefaultValue = RiskParameters.DefaultBotPositionLabel, Group = "Risk")]
    public string BotPositionLabel { get; set; } = RiskParameters.DefaultBotPositionLabel;

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
    private readonly List<RiskPlan> _riskPlans = new();
    private ISymbolRiskApi? _symbolRiskApi;
    private RiskParameters _riskParameters = null!;
    private LevelChartVisualizer? _visualizer;
    private int _lastProcessedClosedIndex = -1;
    private int _visualRefreshCounter;
    private long[]? _lastTradingViewIds;
    private long[]? _lastEntryCandidateIds;

    /// <summary>Planes de riesgo acumulados (live + replay). Solo lectura para diagnóstico.</summary>
    public IReadOnlyList<RiskPlan> RiskPlans => _riskPlans;

    protected override void OnStart()
    {
        _engine = new SupportResistanceEngine(
            swingStrength: SupportResistanceEngine.DefaultSwingStrength,
            lookbackBars: SupportResistanceEngine.DefaultLookbackBars,
            atrPeriod: SupportResistanceEngine.DefaultAtrPeriod);
        _t3Signals = new T3BreakoutSignalDetector();
        _riskPlans.Clear();
        _riskParameters = new RiskParameters
        {
            RiskPercent = RiskPercent,
            StopBufferAtr = StopBufferAtr,
            RewardRiskRatio = RewardRiskRatio,
            MaxOpenPositions = MaxOpenPositions,
            BotPositionLabel = string.IsNullOrWhiteSpace(BotPositionLabel)
                ? RiskParameters.DefaultBotPositionLabel
                : BotPositionLabel
        };
        _symbolRiskApi = new CTraderSymbolRiskApi(Symbol);

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
            "XauBreakoutBot {0} iniciado. Detección S/R M15 + RiskPlan MVP. Sin trading. T3Signals={1}, RiskPlans={2}, VizMode={3}, Symbol={4}, ChartTF={5}, M15Bars={6}",
            ModuleVersion, EnableT3BreakoutSignals, EnableRiskPlans, VisualizationMode, SymbolName, TimeFrame, _m15.Count);

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
            "XauBreakoutBot detenido. Niveles Active={0}, Total={1}. Señales T3Breakout={2}. RiskPlans={3}. Sin operaciones ejecutadas por este módulo.",
            CountActive(), _engine.Levels.Count, _t3Signals.Signals.Count, _riskPlans.Count);
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
        _riskPlans.Clear();

        var preferred = _engine.MinimumPreferredBars;
        var count = lastClosed + 1;
        var start = count > preferred ? count - preferred : 0;

        var replaySignalCount = 0;
        var replayPlanCount = 0;
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
                foreach (var s in newly)
                {
                    if (LogReplayT3Signals)
                        LogT3Signal(s, fromReplay: true);

                    if (EnableRiskPlans)
                    {
                        var plan = BuildRiskPlanForSignal(s);
                        _riskPlans.Add(plan);
                        replayPlanCount++;
                        if (LogReplayRiskPlans && LogRiskPlans)
                            LogRiskPlan(plan, fromReplay: true);
                    }
                }
            }
        }

        _lastProcessedClosedIndex = lastClosed;

        Print(
            "Replay M15: cerradas={0}, ventana={1}, swings={2}, niveles={3}, Active={4}, ATR_ready={5}, ATR_current={6:0.####}, T3Signals={7}, RiskPlans={8}",
            count,
            lastClosed - start + 1,
            _engine.Swings.Count,
            _engine.Levels.Count,
            CountActive(),
            _engine.AtrReady,
            _engine.AtrReady ? _engine.AtrCurrent : 0,
            replaySignalCount,
            replayPlanCount);

        Print(
            "Limitación: el replay OHLC usa proxy de invalidación por Close; no reconstruye invalidaciones tick intradía.");
        Print(
            "Limitación Risk: EntryReference={0}; riesgo real no garantizado; slippage/gaps no cuantificados.",
            RiskPlan.EntryReferenceKind);
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
            {
                LogT3Signal(s, fromReplay: false);
                if (EnableRiskPlans)
                {
                    var plan = BuildRiskPlanForSignal(s);
                    _riskPlans.Add(plan);
                    if (LogRiskPlans)
                        LogRiskPlan(plan, fromReplay: false);
                }
            }
        }

        _lastProcessedClosedIndex = index;

        // Tras cierre, actualizar candidatos con último Bid/Ask conocido.
        _engine.ProcessTick(Server.Time, Symbol.Bid, Symbol.Ask);
    }

    /// <summary>
    /// Construye y valida el plan de riesgo. Esta fase no envía ni coloca órdenes.
    /// </summary>
    private RiskPlan BuildRiskPlanForSignal(T3BreakoutSignal signal)
    {
        var eligibility = EvaluateBotPositionEligibility();
        return RiskPlanCalculator.Build(
            signal,
            _riskParameters,
            Account.Equity,
            _symbolRiskApi,
            eligibility);
    }

    private BotPositionGate.EligibilityResult EvaluateBotPositionEligibility()
    {
        var open = new List<BotPositionGate.OpenPositionRef>();
        foreach (var p in Positions)
            open.Add(new BotPositionGate.OpenPositionRef(p.Label, p.SymbolName));

        return BotPositionGate.Evaluate(
            open,
            _riskParameters.BotPositionLabel,
            SymbolName,
            _riskParameters.MaxOpenPositions);
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

    private void LogRiskPlan(RiskPlan plan, bool fromReplay)
    {
        Print(
            "RiskPlan{0}: Id={1} {2} Valid={3} Reason={4} EntryRef({5})={6:0.####} SL={7:0.####} TP={8:0.####} StopPips={9:0.####} RR={10:0.##} Equity={11:0.##} Budget={12:0.####} VolUnits={13} PriceRisk={14} TotalRisk={15} CostsPartial={16}",
            fromReplay ? " [replay]" : "",
            plan.SignalId,
            plan.Direction,
            plan.IsValid,
            plan.RejectionReason ?? "",
            plan.EntryPriceKind,
            plan.EntryReferencePrice,
            plan.StopLossPrice,
            plan.TakeProfitPrice,
            plan.StopDistancePips,
            plan.RewardRiskRatio,
            plan.Equity,
            plan.RiskBudget,
            plan.VolumeInUnits?.ToString("0.####") ?? "n/a",
            plan.EstimatedPriceRisk?.ToString("0.####") ?? "n/a",
            plan.EstimatedTotalRisk?.ToString("0.####") ?? "n/a",
            plan.CostsNotFullyModeled);
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

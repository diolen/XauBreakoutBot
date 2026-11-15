using System.Reflection;
using cAlgo.Robots.Execution;
using cAlgo.Robots.Risk;
using cAlgo.Robots.SupportResistance;
using Xunit;

namespace XauBreakoutBot.Tests;

public class ExecutionPlanBuilderTests
{
    private static readonly DateTime T0 = new(2025, 3, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly RiskParameters RiskDefaults = new();
    private static readonly ExecutionParameters ExecDefaults = new(); // OrdersEnabled=false

    private static PipValueSymbolRiskApi XauRiskApi(
        double min = 1, double max = 100, double step = 1) =>
        new(pipSize: 0.01, pipValue: 0.01, volumeInUnitsMin: min, volumeInUnitsMax: max, volumeInUnitsStep: step);

    private static T3BreakoutSignal BuySignal(
        double close = 2650,
        double upper = 2648,
        double lower = 2640,
        double atr = 10,
        int breakBar = 8) =>
        new()
        {
            LevelId = 10,
            LevelRole = LevelRole.Resistance,
            Direction = SignalDirection.Buy,
            T3BarIndex = 5,
            BreakoutBarIndex = breakBar,
            BreakoutTime = T0,
            ConfirmationClose = close,
            LevelMidAtBreakout = (upper + lower) / 2,
            UpperBoundAtBreakout = upper,
            LowerBoundAtBreakout = lower,
            HalfWidthAtBreakout = (upper - lower) / 2,
            AtrAtBreakout = atr,
            BreakoutReason = "ohlc-close-proxy-resistance",
            Geometry = LevelGeometry.Horizontal
        };

    private static T3BreakoutSignal SellSignal(
        double close = 2630,
        double upper = 2640,
        double lower = 2632,
        double atr = 10,
        int breakBar = 8) =>
        new()
        {
            LevelId = 20,
            LevelRole = LevelRole.Support,
            Direction = SignalDirection.Sell,
            T3BarIndex = 5,
            BreakoutBarIndex = breakBar,
            BreakoutTime = T0,
            ConfirmationClose = close,
            LevelMidAtBreakout = (upper + lower) / 2,
            UpperBoundAtBreakout = upper,
            LowerBoundAtBreakout = lower,
            HalfWidthAtBreakout = (upper - lower) / 2,
            AtrAtBreakout = atr,
            BreakoutReason = "ohlc-close-proxy-support",
            Geometry = LevelGeometry.Horizontal
        };

    private static ExecutionMarketContext Market(
        double bid = 2650.0,
        double ask = 2650.2,
        double equity = 10_000,
        bool isLive = false,
        int barIndex = 8,
        double minSl = 0,
        double minTp = 0,
        bool tradingEnabled = true,
        double pipSize = 0.01) =>
        new()
        {
            AccountIsLive = isLive,
            AccountAsset = "USD",
            Equity = equity,
            Bid = bid,
            Ask = ask,
            Spread = ask - bid,
            SymbolName = "XAUUSD",
            PipSize = pipSize,
            TickSize = 0.01,
            PipValue = 0.01,
            VolumeInUnitsMin = 1,
            VolumeInUnitsMax = 100,
            VolumeInUnitsStep = 1,
            LotSize = 100,
            Digits = 2,
            IsTradingEnabled = tradingEnabled,
            MinStopLossDistance = minSl,
            MinTakeProfitDistance = minTp,
            MinDistanceUnit = MinDistanceUnit.Pips,
            CurrentClosedBarIndex = barIndex
        };

    [Fact]
    public void Buy_UsesAskAsExecutableEntry()
    {
        // Spread 0.2 / 0.01 = 20 pips ≤ MaxSpreadPips 50
        var market = Market(bid: 2650.0, ask: 2650.2, barIndex: 8);
        var plan = ExecutionPlanBuilder.Build(BuySignal(), RiskDefaults, ExecDefaults, market, XauRiskApi());

        Assert.True(plan.IsEligible);
        Assert.Equal(2650.2, plan.ExecutableEntryPrice);
        Assert.Equal(ExecutionPlan.EntryPriceKind, plan.EntryKind);
        Assert.Equal(ExecutionRejectionReasons.OrdersDisabled, plan.RejectionReason);
        // SL estructural: 2640 - 1 = 2639
        Assert.Equal(2639, plan.StructuralStopLossPrice, 6);
        var r = plan.ExecutableEntryPrice - plan.StructuralStopLossPrice;
        Assert.Equal(plan.ExecutableEntryPrice + 2 * r, plan.TakeProfitPrice, 9);
        Assert.NotEqual(plan.SignalConfirmationClose, plan.ExecutableEntryPrice);
    }

    [Fact]
    public void Sell_UsesBidAsExecutableEntry()
    {
        var market = Market(bid: 2629.9, ask: 2630.1, barIndex: 8);
        var plan = ExecutionPlanBuilder.Build(SellSignal(), RiskDefaults, ExecDefaults, market, XauRiskApi());

        Assert.True(plan.IsEligible);
        Assert.Equal(2629.9, plan.ExecutableEntryPrice);
        Assert.Equal(2641, plan.StructuralStopLossPrice, 6); // 2640 + 1
        var r = plan.StructuralStopLossPrice - plan.ExecutableEntryPrice;
        Assert.Equal(plan.ExecutableEntryPrice - 2 * r, plan.TakeProfitPrice, 9);
    }

    [Fact]
    public void RecalculatesVolumeFromExecutableEntry_NotConfirmationClose()
    {
        // ConfirmationClose=2650, Ask=2655 → R mayor → volumen menor que RiskPlan teórico.
        // Spread 0.2 → 20 pips.
        var signal = BuySignal(close: 2650);
        var market = Market(bid: 2654.8, ask: 2655.0, barIndex: 8);
        var api = XauRiskApi();

        var risk = RiskPlanCalculator.Build(signal, RiskDefaults, 10_000, api);
        var exec = ExecutionPlanBuilder.Build(signal, RiskDefaults, ExecDefaults, market, api);

        Assert.True(risk.IsValid);
        Assert.True(exec.IsEligible);
        Assert.True(exec.VolumeInUnits < risk.VolumeInUnits);
        Assert.Equal(2655.0, exec.ExecutableEntryPrice);
        Assert.Equal(2639, exec.StructuralStopLossPrice, 6); // mismo SL estructural
        Assert.True(exec.EstimatedMonetaryRisk <= exec.RiskBudget + 1e-9);
    }

    [Fact]
    public void LiveAccount_Rejected()
    {
        var plan = ExecutionPlanBuilder.Build(
            BuySignal(), RiskDefaults, ExecDefaults, Market(isLive: true), XauRiskApi());
        Assert.False(plan.IsEligible);
        Assert.Equal(ExecutionRejectionReasons.LiveAccountForbidden, plan.RejectionReason);
    }

    [Fact]
    public void ExcessiveSpread_Rejected()
    {
        // Spread 1.0 / 0.01 = 100 pips > MaxSpreadPips 50
        var market = Market(bid: 2650, ask: 2651);
        var plan = ExecutionPlanBuilder.Build(BuySignal(), RiskDefaults, ExecDefaults, market, XauRiskApi());
        Assert.False(plan.IsEligible);
        Assert.Equal(ExecutionRejectionReasons.InvalidOrExcessiveSpread, plan.RejectionReason);
    }

    [Fact]
    public void InvalidQuotes_Rejected()
    {
        Assert.Equal(
            ExecutionRejectionReasons.InvalidQuote,
            ExecutionPlanBuilder.Build(BuySignal(), RiskDefaults, ExecDefaults,
                Market(bid: 0, ask: 2650), XauRiskApi()).RejectionReason);

        Assert.Equal(
            ExecutionRejectionReasons.InvalidQuote,
            ExecutionPlanBuilder.Build(BuySignal(), RiskDefaults, ExecDefaults,
                Market(bid: 2651, ask: 2650), XauRiskApi()).RejectionReason);
    }

    [Fact]
    public void SymbolMinDistance_Constraint_Rejected()
    {
        // Min SL distance 5000 pips → required 50 price units; actual R ~11.
        var market = Market(minSl: 5000);
        var plan = ExecutionPlanBuilder.Build(BuySignal(), RiskDefaults, ExecDefaults, market, XauRiskApi());
        Assert.False(plan.IsEligible);
        Assert.Equal(ExecutionRejectionReasons.SymbolConstraintsViolated, plan.RejectionReason);
    }

    [Fact]
    public void RiskAboveBudget_Rejected()
    {
        var api = XauRiskApi(min: 10, max: 100, step: 10);
        // stop large enough that min volume exceeds 0.5% of 10k
        var plan = ExecutionPlanBuilder.Build(BuySignal(), RiskDefaults, ExecDefaults, Market(), api);
        Assert.False(plan.IsEligible);
        Assert.Equal(ExecutionRejectionReasons.MinVolumeExceedsRiskBudget, plan.RejectionReason);
        Assert.Null(plan.VolumeInUnits);
    }

    [Fact]
    public void ExpiredSignal_Rejected()
    {
        var market = Market(barIndex: 8 + 3); // age 3 > MaxSignalAgeBars 2
        var plan = ExecutionPlanBuilder.Build(BuySignal(breakBar: 8), RiskDefaults, ExecDefaults, market, XauRiskApi());
        Assert.False(plan.IsEligible);
        Assert.Equal(ExecutionRejectionReasons.SignalExpired, plan.RejectionReason);
    }

    [Fact]
    public void ExistingBotPosition_Rejected()
    {
        var gate = BotPositionGate.Evaluate(
            new[] { new BotPositionGate.OpenPositionRef(RiskParameters.DefaultBotPositionLabel, "XAUUSD") },
            RiskParameters.DefaultBotPositionLabel,
            "XAUUSD",
            1);
        var plan = ExecutionPlanBuilder.Build(
            BuySignal(), RiskDefaults, ExecDefaults, Market(), XauRiskApi(), gate);
        Assert.False(plan.IsEligible);
        Assert.Equal(ExecutionRejectionReasons.MaxOpenPositionsReached, plan.RejectionReason);
    }

    [Fact]
    public void DuplicateSignal_Rejected()
    {
        var planned = new HashSet<string> { "L10-B8" };
        var plan = ExecutionPlanBuilder.Build(
            BuySignal(), RiskDefaults, ExecDefaults, Market(), XauRiskApi(),
            alreadyPlannedSignalIds: planned);
        Assert.False(plan.IsEligible);
        Assert.Equal(ExecutionRejectionReasons.SignalDuplicate, plan.RejectionReason);
    }

    [Fact]
    public void SymbolSpecSnapshot_OmitsAccountIdentifiers()
    {
        var snap = Market().ToSymbolSpecSnapshot();
        var line = snap.FormatSafeLogLine();
        Assert.Contains("AccountIsLive=False", line);
        Assert.Contains("AccountAsset=USD", line);
        Assert.DoesNotContain("AccountNumber", line);
        Assert.DoesNotContain("password", line, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", line, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExecutionModule_HasNoOrderExecutionApi()
    {
        foreach (var type in new[]
                 {
                     typeof(ExecutionPlanBuilder),
                     typeof(ExecutionPlan),
                     typeof(ExecutionParameters),
                     typeof(ExecutionMarketContext)
                 })
        {
            var names = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Select(m => m.Name)
                .ToArray();
            Assert.DoesNotContain(names, n => n.Contains("ExecuteMarketOrder", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(names, n => n.Contains("PlaceLimitOrder", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(names, n => n.Contains("PlaceStopOrder", StringComparison.OrdinalIgnoreCase));
        }

        var botSource = File.ReadAllText(Path.Combine(FindRepoRoot(), "XauBreakoutBot", "XauBreakoutBot.cs"));
        Assert.DoesNotContain("ExecuteMarketOrder(", botSource);
        Assert.DoesNotContain("PlaceLimitOrder(", botSource);
        Assert.DoesNotContain("PlaceStopOrder(", botSource);
        Assert.DoesNotContain("ModifyPosition(", botSource);
        Assert.DoesNotContain("ClosePosition(", botSource);

        foreach (var file in Directory.GetFiles(Path.Combine(FindRepoRoot(), "XauBreakoutBot", "Execution"), "*.cs"))
        {
            var src = File.ReadAllText(file);
            Assert.DoesNotContain("ExecuteMarketOrder(", src);
            Assert.DoesNotContain("PlaceLimitOrder(", src);
            Assert.DoesNotContain("PlaceStopOrder(", src);
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "XauBreakoutBot.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Repo root not found");
    }
}

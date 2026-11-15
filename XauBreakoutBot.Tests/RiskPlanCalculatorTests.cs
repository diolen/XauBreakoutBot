using System.Reflection;
using System.Text;
using cAlgo.Robots.Risk;
using cAlgo.Robots.SupportResistance;
using Xunit;

namespace XauBreakoutBot.Tests;

public class RiskPlanCalculatorTests
{
    private static readonly DateTime T0 = new(2025, 3, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly RiskParameters Defaults = new();

    /// <summary>XAUUSD-like: PipSize=0.01, PipValue=0.01 per unit → 1 unit risks 0.01 per pip.</summary>
    private static PipValueSymbolRiskApi XauSpec(
        double min = 1,
        double max = 100,
        double step = 1,
        double? spread = null,
        double? commissionPerLot = null) =>
        new(pipSize: 0.01, pipValue: 0.01, volumeInUnitsMin: min, volumeInUnitsMax: max, volumeInUnitsStep: step)
        {
            SpreadPrice = spread,
            RoundTripCommissionPerLot = commissionPerLot,
            LotSizeInUnits = 100
        };

    private static T3BreakoutSignal BuySignal(
        double close = 2650,
        double upper = 2648,
        double lower = 2640,
        double atr = 10) =>
        new()
        {
            LevelId = 10,
            LevelRole = LevelRole.Resistance,
            Direction = SignalDirection.Buy,
            T3BarIndex = 5,
            BreakoutBarIndex = 8,
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
        double atr = 10) =>
        new()
        {
            LevelId = 20,
            LevelRole = LevelRole.Support,
            Direction = SignalDirection.Sell,
            T3BarIndex = 5,
            BreakoutBarIndex = 8,
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

    [Fact]
    public void Buy_StopAndTakeProfit_Correct()
    {
        var signal = BuySignal();
        // SL = lower - 0.10*ATR = 2640 - 1 = 2639
        // Entry = 2650; R = 11; TP = 2650 + 22 = 2672
        var plan = RiskPlanCalculator.Build(signal, Defaults, equity: 10_000, XauSpec());

        Assert.True(plan.IsValid);
        Assert.Equal(SignalDirection.Buy, plan.Direction);
        Assert.Equal(2650, plan.EntryReferencePrice);
        Assert.Equal(2639, plan.StopLossPrice, 6);
        Assert.Equal(2672, plan.TakeProfitPrice, 6);
        Assert.Equal(RiskPlan.EntryReferenceKind, plan.EntryPriceKind);
        Assert.Equal("L10-B8", plan.SignalId);
    }

    [Fact]
    public void Sell_StopAndTakeProfit_Correct()
    {
        var signal = SellSignal();
        // SL = upper + 0.10*ATR = 2640 + 1 = 2641
        // Entry = 2630; R = 11; TP = 2630 - 22 = 2608
        var plan = RiskPlanCalculator.Build(signal, Defaults, equity: 10_000, XauSpec());

        Assert.True(plan.IsValid);
        Assert.Equal(SignalDirection.Sell, plan.Direction);
        Assert.Equal(2630, plan.EntryReferencePrice);
        Assert.Equal(2641, plan.StopLossPrice, 6);
        Assert.Equal(2608, plan.TakeProfitPrice, 6);
    }

    [Fact]
    public void RiskBudget_IsHalfPercentOfEquity()
    {
        var plan = RiskPlanCalculator.Build(BuySignal(), Defaults, equity: 20_000, XauSpec());
        Assert.True(plan.IsValid);
        Assert.Equal(100.0, plan.RiskBudget, 6); // 20000 * 0.5 / 100
        Assert.Equal(20_000, plan.Equity);
        Assert.True(plan.EstimatedPriceRisk <= plan.RiskBudget + 1e-9);
    }

    [Fact]
    public void TakeProfit_IsTwoR()
    {
        var plan = RiskPlanCalculator.Build(BuySignal(), Defaults, equity: 10_000, XauSpec());
        var r = Math.Abs(plan.EntryReferencePrice - plan.StopLossPrice);
        Assert.Equal(2.0, plan.RewardRiskRatio);
        Assert.Equal(plan.EntryReferencePrice + 2 * r, plan.TakeProfitPrice, 9);
    }

    [Fact]
    public void Volume_NormalizesDown_NeverUp()
    {
        // stopPips = 11 / 0.01 = 1100
        // riskBudget = 50; raw = 50 / (1100 * 0.01) = 4.545... → floor step 1 → 4
        var api = XauSpec(min: 1, max: 100, step: 1);
        var plan = RiskPlanCalculator.Build(BuySignal(), Defaults, equity: 10_000, api);
        Assert.True(plan.IsValid);
        Assert.Equal(4, plan.VolumeInUnits);
        Assert.True(plan.EstimatedPriceRisk <= 50 + 1e-9);

        // With step 2: floor(4.545/2)*2 = 4
        var api2 = XauSpec(min: 2, max: 100, step: 2);
        var plan2 = RiskPlanCalculator.Build(BuySignal(), Defaults, equity: 10_000, api2);
        Assert.True(plan2.IsValid);
        Assert.Equal(4, plan2.VolumeInUnits);
    }

    [Fact]
    public void MinVolume_Insufficient_Rejects()
    {
        // Min volume risks more than budget.
        // stopPips=1100; AmountRisked(10, 1100) = 10*1100*0.01 = 110 > budget 50
        var api = XauSpec(min: 10, max: 100, step: 10);
        var plan = RiskPlanCalculator.Build(BuySignal(), Defaults, equity: 10_000, api);
        Assert.False(plan.IsValid);
        Assert.Equal(RiskRejectionReasons.MinVolumeExceedsRiskBudget, plan.RejectionReason);
        Assert.Null(plan.VolumeInUnits);
    }

    [Fact]
    public void Volume_CapsAtMaximum()
    {
        // Huge equity → raw volume above max; should cap and still be valid if risk OK.
        var api = XauSpec(min: 1, max: 5, step: 1);
        var plan = RiskPlanCalculator.Build(BuySignal(), Defaults, equity: 1_000_000, api);
        Assert.True(plan.IsValid);
        Assert.Equal(5, plan.VolumeInUnits);
        Assert.True(plan.EstimatedPriceRisk <= plan.RiskBudget + 1e-9);
    }

    [Fact]
    public void InvalidEquity_Rejects()
    {
        Assert.Equal(RiskRejectionReasons.InvalidEquity,
            RiskPlanCalculator.Build(BuySignal(), Defaults, equity: null, XauSpec()).RejectionReason);
        Assert.Equal(RiskRejectionReasons.InvalidEquity,
            RiskPlanCalculator.Build(BuySignal(), Defaults, equity: 0, XauSpec()).RejectionReason);
        Assert.Equal(RiskRejectionReasons.InvalidEquity,
            RiskPlanCalculator.Build(BuySignal(), Defaults, equity: -100, XauSpec()).RejectionReason);
        Assert.Equal(RiskRejectionReasons.InvalidEquity,
            RiskPlanCalculator.Build(BuySignal(), Defaults, equity: double.NaN, XauSpec()).RejectionReason);
    }

    [Fact]
    public void InvalidAtr_Rejects()
    {
        var plan = RiskPlanCalculator.Build(BuySignal(atr: 0), Defaults, equity: 10_000, XauSpec());
        Assert.False(plan.IsValid);
        Assert.Equal(RiskRejectionReasons.InvalidAtr, plan.RejectionReason);

        var nan = RiskPlanCalculator.Build(BuySignal(atr: double.NaN), Defaults, equity: 10_000, XauSpec());
        Assert.Equal(RiskRejectionReasons.InvalidAtr, nan.RejectionReason);
    }

    [Fact]
    public void StopLossWrongSide_Rejects()
    {
        // BUY but close already below SL geometry → SL ends above entry.
        var plan = RiskPlanCalculator.Build(
            BuySignal(close: 2635, upper: 2648, lower: 2640, atr: 10),
            Defaults, equity: 10_000, XauSpec());
        Assert.False(plan.IsValid);
        Assert.Equal(RiskRejectionReasons.StopLossWrongSide, plan.RejectionReason);
    }

    [Fact]
    public void InvalidSymbolData_Rejects()
    {
        var bad = new PipValueSymbolRiskApi(0, 0.01, 1, 100, 1);
        var plan = RiskPlanCalculator.Build(BuySignal(), Defaults, equity: 10_000, bad);
        Assert.False(plan.IsValid);
        Assert.Equal(RiskRejectionReasons.InvalidSymbolData, plan.RejectionReason);

        var missing = RiskPlanCalculator.Build(BuySignal(), Defaults, equity: 10_000, symbol: null);
        Assert.False(missing.IsValid);
        Assert.Equal(RiskRejectionReasons.InvalidSymbolData, missing.RejectionReason);
        // Plan parcial: SL/TP geométricos presentes.
        Assert.True(double.IsFinite(missing.StopLossPrice));
        Assert.True(double.IsFinite(missing.TakeProfitPrice));
        Assert.Null(missing.VolumeInUnits);
    }

    [Fact]
    public void BotPosition_Existing_BlocksEligibility()
    {
        var positions = new[]
        {
            new BotPositionGate.OpenPositionRef(RiskParameters.DefaultBotPositionLabel, "XAUUSD")
        };
        var gate = BotPositionGate.Evaluate(positions, RiskParameters.DefaultBotPositionLabel, "XAUUSD", 1);
        Assert.False(gate.IsEligible);
        Assert.Equal(RiskRejectionReasons.MaxOpenPositionsReached, gate.Reason);

        var plan = RiskPlanCalculator.Build(BuySignal(), Defaults, 10_000, XauSpec(), gate);
        Assert.False(plan.IsValid);
        Assert.Equal(RiskRejectionReasons.MaxOpenPositionsReached, plan.RejectionReason);
    }

    [Fact]
    public void ManualPosition_Unrelated_DoesNotBlock()
    {
        var positions = new[]
        {
            new BotPositionGate.OpenPositionRef("ManualTrade", "XAUUSD"),
            new BotPositionGate.OpenPositionRef("OtherBot", "XAUUSD"),
            new BotPositionGate.OpenPositionRef(null, "XAUUSD")
        };
        var gate = BotPositionGate.Evaluate(positions, RiskParameters.DefaultBotPositionLabel, "XAUUSD", 1);
        Assert.True(gate.IsEligible);
        Assert.Equal(0, gate.BotOpenPositionCount);

        var plan = RiskPlanCalculator.Build(BuySignal(), Defaults, 10_000, XauSpec(), gate);
        Assert.True(plan.IsValid);
    }

    [Fact]
    public void UnknownCosts_SetsCostsNotFullyModeled()
    {
        var plan = RiskPlanCalculator.Build(BuySignal(), Defaults, 10_000, XauSpec(spread: null, commissionPerLot: null));
        Assert.True(plan.IsValid);
        Assert.True(plan.CostsNotFullyModeled);
        Assert.Null(plan.EstimatedTotalRisk);
        Assert.NotNull(plan.EstimatedPriceRisk);
    }

    [Fact]
    public void KnownCosts_PopulateEstimatedTotalRisk()
    {
        var api = XauSpec(spread: 0.05, commissionPerLot: 7); // 5 pips spread; $7 RT per 100 units
        var plan = RiskPlanCalculator.Build(BuySignal(), Defaults, 10_000, api);
        Assert.True(plan.IsValid);
        Assert.False(plan.CostsNotFullyModeled);
        Assert.NotNull(plan.EstimatedTotalRisk);
        Assert.True(plan.EstimatedTotalRisk > plan.EstimatedPriceRisk);
    }

    [Fact]
    public void ExcessiveRisk_RejectedWithoutFallbackVolume()
    {
        // Crafted so raw volume floors below min after budget enforcement.
        // Use tiny budget relative to step.
        var api = XauSpec(min: 5, max: 100, step: 5);
        var plan = RiskPlanCalculator.Build(BuySignal(), Defaults, equity: 1_000, api); // budget=5
        // min risk = 5 * 1100 * 0.01 = 55 > 5
        Assert.False(plan.IsValid);
        Assert.Equal(RiskRejectionReasons.MinVolumeExceedsRiskBudget, plan.RejectionReason);
        Assert.Null(plan.VolumeInUnits);
    }

    [Fact]
    public void RiskModule_HasNoOrderExecutionApi()
    {
        foreach (var type in new[]
                 {
                     typeof(RiskPlanCalculator),
                     typeof(RiskPlan),
                     typeof(BotPositionGate),
                     typeof(PipValueSymbolRiskApi),
                     typeof(RiskParameters)
                 })
        {
            var names = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Select(m => m.Name)
                .ToArray();
            Assert.DoesNotContain(names, n => n.Contains("ExecuteMarketOrder", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(names, n => n.Contains("PlaceLimitOrder", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(names, n => n.Contains("PlaceStopOrder", StringComparison.OrdinalIgnoreCase));
        }

        // Código fuente del cBot: no debe invocar APIs de ejecución en esta fase.
        var botSource = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "XauBreakoutBot", "XauBreakoutBot.cs"));
        Assert.DoesNotContain("ExecuteMarketOrder(", botSource);
        Assert.DoesNotContain("PlaceLimitOrder(", botSource);
        Assert.DoesNotContain("PlaceStopOrder(", botSource);
    }

    [Fact]
    public void Parameters_Invalid_RejectedBeforePlan()
    {
        var bad = new RiskParameters { RiskPercent = 0 };
        var plan = RiskPlanCalculator.Build(BuySignal(), bad, 10_000, XauSpec());
        Assert.False(plan.IsValid);
        Assert.Equal(RiskRejectionReasons.InvalidParameters, plan.RejectionReason);
    }

    [Fact]
    public void HistoricalReplayCsv_ProducesPartialPlansWithoutInventedBrokerData()
    {
        var csvPath = Path.Combine(FindRepoRoot(), "artifacts", "t3-breakout-signal-mvp", "signals_replay.csv");
        Assert.True(File.Exists(csvPath));

        var lines = File.ReadAllLines(csvPath);
        Assert.True(lines.Length > 1);

        var outDir = Path.Combine(FindRepoRoot(), "artifacts", "risk-management-mvp");
        Directory.CreateDirectory(outDir);
        var outPath = Path.Combine(outDir, "risk_plans_replay_partial.csv");

        var sb = new StringBuilder();
        sb.AppendLine(
            "SignalId,Direction,EntryRef,StopLoss,TakeProfit,R,IsValid,RejectionReason,VolumeInUnits,Note");

        var count = 0;
        var validGeom = 0;
        for (var i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
                continue;
            var signal = ParseReplaySignal(lines[i]);
            // Sin Equity ni specs de broker inventados → plan parcial / volumen rechazado.
            var plan = RiskPlanCalculator.Build(signal, Defaults, equity: null, symbol: null);
            count++;
            Assert.False(plan.IsValid);
            Assert.Equal(RiskRejectionReasons.InvalidEquity, plan.RejectionReason);
            Assert.Null(plan.VolumeInUnits);
            Assert.True(double.IsFinite(plan.StopLossPrice));
            Assert.True(double.IsFinite(plan.TakeProfitPrice));
            validGeom++;

            var r = Math.Abs(plan.EntryReferencePrice - plan.StopLossPrice);
            sb.AppendLine(string.Join(',',
                plan.SignalId,
                plan.Direction,
                plan.EntryReferencePrice.ToString("0.####"),
                plan.StopLossPrice.ToString("0.####"),
                plan.TakeProfitPrice.ToString("0.####"),
                r.ToString("0.####"),
                plan.IsValid,
                plan.RejectionReason,
                "",
                "NoEquityNoSymbolSpecs"));
        }

        File.WriteAllText(outPath, sb.ToString());
        Assert.True(count > 0);
        Assert.Equal(count, validGeom);
        Assert.True(File.Exists(outPath));
    }

    private static T3BreakoutSignal ParseReplaySignal(string csvLine)
    {
        // LevelId,Role,Direction,Geometry,T3BarIndex,BreakoutBarIndex,BreakoutTime,ConfirmationClose,Mid,Upper,Lower,Atr,Reason
        var p = csvLine.Split(',');
        var role = Enum.Parse<LevelRole>(p[1]);
        var direction = Enum.Parse<SignalDirection>(p[2]);
        var geometry = Enum.Parse<LevelGeometry>(p[3]);
        return new T3BreakoutSignal
        {
            LevelId = long.Parse(p[0]),
            LevelRole = role,
            Direction = direction,
            Geometry = geometry,
            T3BarIndex = int.Parse(p[4]),
            BreakoutBarIndex = int.Parse(p[5]),
            BreakoutTime = DateTime.Parse(p[6], null, System.Globalization.DateTimeStyles.RoundtripKind),
            ConfirmationClose = double.Parse(p[7], System.Globalization.CultureInfo.InvariantCulture),
            LevelMidAtBreakout = double.Parse(p[8], System.Globalization.CultureInfo.InvariantCulture),
            UpperBoundAtBreakout = double.Parse(p[9], System.Globalization.CultureInfo.InvariantCulture),
            LowerBoundAtBreakout = double.Parse(p[10], System.Globalization.CultureInfo.InvariantCulture),
            HalfWidthAtBreakout = (double.Parse(p[9], System.Globalization.CultureInfo.InvariantCulture) -
                                   double.Parse(p[10], System.Globalization.CultureInfo.InvariantCulture)) / 2,
            AtrAtBreakout = double.Parse(p[11], System.Globalization.CultureInfo.InvariantCulture),
            BreakoutReason = p[12]
        };
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

        throw new InvalidOperationException("Repo root not found from " + AppContext.BaseDirectory);
    }
}

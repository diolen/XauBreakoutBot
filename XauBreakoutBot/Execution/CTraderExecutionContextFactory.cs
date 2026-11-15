using System;
using cAlgo.API;
using cAlgo.API.Internals;

namespace cAlgo.Robots.Execution;

/// <summary>
/// Lee Symbol/Account reales de cTrader para DEMO prep.
/// No registra números de cuenta ni credenciales.
/// </summary>
public static class CTraderExecutionContextFactory
{
    public static ExecutionMarketContext Create(
        Symbol symbol,
        IAccount account,
        string symbolName,
        int currentClosedBarIndex)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        ArgumentNullException.ThrowIfNull(account);

        var spread = symbol.Spread;
        if (!double.IsFinite(spread) || spread < 0)
            spread = symbol.Ask - symbol.Bid;

        return new ExecutionMarketContext
        {
            AccountIsLive = account.IsLive,
            AccountAsset = account.Asset?.Name ?? "",
            Equity = account.Equity,
            Bid = symbol.Bid,
            Ask = symbol.Ask,
            Spread = spread,
            SymbolName = symbolName,
            PipSize = symbol.PipSize,
            TickSize = symbol.TickSize,
            PipValue = symbol.PipValue,
            VolumeInUnitsMin = symbol.VolumeInUnitsMin,
            VolumeInUnitsMax = symbol.VolumeInUnitsMax,
            VolumeInUnitsStep = symbol.VolumeInUnitsStep,
            LotSize = symbol.LotSize,
            Digits = symbol.Digits,
            IsTradingEnabled = symbol.IsTradingEnabled,
            MinStopLossDistance = symbol.MinStopLossDistance,
            MinTakeProfitDistance = symbol.MinTakeProfitDistance,
            MinDistanceUnit = MapMinDistance(symbol.MinDistanceType),
            CurrentClosedBarIndex = currentClosedBarIndex
        };
    }

    public static SymbolSpecSnapshot CaptureSpec(
        Symbol symbol,
        IAccount account,
        string symbolName) =>
        Create(symbol, account, symbolName, currentClosedBarIndex: 0).ToSymbolSpecSnapshot();

    private static MinDistanceUnit MapMinDistance(SymbolMinDistanceType type) =>
        type == SymbolMinDistanceType.Percentage
            ? MinDistanceUnit.Percentage
            : MinDistanceUnit.Pips;
}

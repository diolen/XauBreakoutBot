using cAlgo.Robots.SupportResistance;

namespace XauTradeSimulator.Simulation;

/// <summary>
/// Convención Bid/Ask sintética sobre OHLC histórico.
/// OHLC se trata como <b>precio de referencia / mid</b> (no se conoce Bid/Ask del export).
/// Bid = Mid − Spread/2, Ask = Mid + Spread/2. Spread=0 ⇒ Bid=Ask=Mid.
/// </summary>
public readonly record struct BidAskQuote(double Bid, double Ask, double Mid, double Spread)
{
    public static BidAskQuote FromMid(double mid, double spread)
    {
        if (!double.IsFinite(mid))
            throw new ArgumentOutOfRangeException(nameof(mid));
        if (!double.IsFinite(spread) || spread < 0)
            throw new ArgumentOutOfRangeException(nameof(spread));

        var half = spread / 2.0;
        return new BidAskQuote(mid - half, mid + half, mid, spread);
    }

    public static BidAskBar FromBar(OhlcBar bar, double spread) =>
        new(
            FromMid(bar.Open, spread),
            FromMid(bar.High, spread),
            FromMid(bar.Low, spread),
            FromMid(bar.Close, spread));
}

public readonly record struct BidAskBar(
    BidAskQuote Open,
    BidAskQuote High,
    BidAskQuote Low,
    BidAskQuote Close);

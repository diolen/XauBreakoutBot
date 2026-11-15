namespace cAlgo.Robots;

/// <summary>
/// cBot auxiliar aislado: exporta OHLC histórico XAUUSD M15 a CSV y se detiene.
/// No opera, no usa SupportResistanceEngine ni indicadores.
/// </summary>
[Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
public partial class XauM15HistoryExporter : Robot
{
    private const string ModuleVersion = "1.0-history-exporter";
    private const int MaxLoadMoreIterations = 500;

    [Parameter("Symbol Override", DefaultValue = "XAUUSD", Group = "Market")]
    public string SymbolOverride { get; set; } = "XAUUSD";

    [Parameter("From Date UTC (yyyy-MM-dd)", DefaultValue = "2025-11-14", Group = "Range")]
    public string FromDateUtcText { get; set; } = "2025-11-14";

    [Parameter("To Date UTC (yyyy-MM-dd)", DefaultValue = "2026-11-14", Group = "Range")]
    public string ToDateUtcText { get; set; } = "2026-11-14";

    [Parameter("Output File Name", DefaultValue = "", Group = "Output")]
    public string OutputFileName { get; set; } = string.Empty;

    [Parameter("Max Bars (0=unlimited)", DefaultValue = 0, MinValue = 0, Group = "Safety")]
    public int MaxBars { get; set; }

    protected override void OnStart()
    {
        Print("XauM15HistoryExporter {0} — inicio. Sin trading.", ModuleVersion);

        try
        {
            RunExport();
        }
        catch (Exception ex)
        {
            Print("ERROR exportación: {0}", ex.Message);
            Print("Detalle: {0}", ex);
        }
        finally
        {
            Print("XauM15HistoryExporter finalizando (Stop).");
            Stop();
        }
    }

    private void RunExport()
    {
        if (!TryParseUtcDate(FromDateUtcText, out var fromUtc))
        {
            Print("ERROR: From Date UTC inválida: '{0}'. Use yyyy-MM-dd o ISO 8601.", FromDateUtcText);
            return;
        }

        if (!TryParseUtcDate(ToDateUtcText, out var toUtc))
        {
            Print("ERROR: To Date UTC inválida: '{0}'. Use yyyy-MM-dd o ISO 8601.", ToDateUtcText);
            return;
        }

        // Fin del día UTC inclusive para fechas solo-día.
        if (IsDateOnly(ToDateUtcText))
            toUtc = toUtc.Date.AddDays(1).AddTicks(-1);

        if (toUtc < fromUtc)
        {
            Print("ERROR: To Date ({0:O}) < From Date ({1:O}).", toUtc, fromUtc);
            return;
        }

        var effectiveSymbol = ResolveSymbolName();
        Print("Símbolo efectivo: {0}", effectiveSymbol);
        Print("Broker: {0}", SafeBrokerName());
        Print("Timeframe solicitado: M15");
        Print("Fecha inicial solicitada (UTC): {0:O}", fromUtc);
        Print("Fecha final solicitada (UTC): {0:O}", toUtc);
        Print("IsLive (solo contexto; no operar): {0}", Account.IsLive);

        if (Account.IsLive)
            Print("ADVERTENCIA: cuenta Live detectada. Este cBot no envía órdenes, pero confirme que no desea ejecutarlo en real.");

        Bars bars;
        try
        {
            bars = MarketData.GetBars(TimeFrame.Minute15, effectiveSymbol);
        }
        catch (Exception ex)
        {
            Print("ERROR GetBars(M15, {0}): {1}", effectiveSymbol, ex.Message);
            return;
        }

        if (bars.TimeFrame != TimeFrame.Minute15)
        {
            Print("ERROR: timeframe efectivo {0} != Minute15.", bars.TimeFrame);
            return;
        }

        LoadHistoryUntil(bars, fromUtc);

        var lastClosed = bars.Count - 2;
        if (lastClosed < 0)
        {
            Print("ERROR: no hay velas M15 cerradas en Bars (Count={0}).", bars.Count);
            return;
        }

        Print(
            "Bars cargadas: Count={0}, primeraOpen={1:O}, últimaCerradaOpen={2:O}, serverFirst={3:O}",
            bars.Count,
            HistoryBarCollector.ToUtc(bars.OpenTimes[0]),
            HistoryBarCollector.ToUtc(bars.OpenTimes[lastClosed]),
            SafeServerFirstBarOpenTime(bars));

        var raw = HistoryBarCollector.CollectClosedInRange(
            CopyTimes(bars, lastClosed),
            CopySeries(bars.OpenPrices, lastClosed),
            CopySeries(bars.HighPrices, lastClosed),
            CopySeries(bars.LowPrices, lastClosed),
            CopySeries(bars.ClosePrices, lastClosed),
            lastClosed,
            fromUtc,
            toUtc);

        Print("Velas cerradas en rango solicitado (pre-validación): {0}", raw.Count);

        var validation = HistoryExportValidator.Validate(
            raw,
            fromUtc,
            toUtc,
            HistoryExportValidator.M15Period,
            MaxBars);

        Print("Duplicados detectados (omitidos): {0}", validation.DuplicateCount);
        Print("Saltos temporales (> M15): {0}", validation.TemporalGapCount);

        if (!validation.IsValid)
        {
            foreach (var err in validation.Errors)
                Print("VALIDACIÓN ERROR: {0}", err);
            Print("Exportación abortada: no se escribió CSV.");
            return;
        }

        foreach (var err in validation.Errors)
            Print("VALIDACIÓN WARN: {0}", err);

        var exportBars = validation.BarsForExport;
        var first = validation.FirstOpenTimeUtc!.Value;
        var last = validation.LastOpenTimeUtc!.Value;

        var fileName = string.IsNullOrWhiteSpace(OutputFileName)
            ? HistoryCsvFormatter.SuggestFileName(effectiveSymbol, first, last)
            : OutputFileName.Trim();

        if (!fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            fileName += ".csv";

        // AccessRights.None: ruta relativa → Documents\cAlgo\Data\cBots\XauM15HistoryExporter\
        var relativePath = fileName;
        var csv = HistoryCsvFormatter.FormatDocument(exportBars);
        var bytes = Encoding.UTF8.GetBytes(csv);

        try
        {
            // AccessRights.None: System.IO con ruta relativa escribe en la carpeta designada del cBot.
            System.IO.File.WriteAllBytes(relativePath, bytes);
        }
        catch (Exception ex)
        {
            Print("ERROR escritura CSV '{0}': {1}", relativePath, ex.Message);
            return;
        }

        string? sha = null;
        try
        {
            sha = HistoryCsvFormatter.ComputeSha256Hex(bytes);
        }
        catch (Exception ex)
        {
            Print("ADVERTENCIA: no se pudo calcular SHA-256: {0}", ex.Message);
        }

        var absoluteHint = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "cAlgo", "Data", "cBots", nameof(XauM15HistoryExporter), relativePath);

        Print("--- Resumen exportación ---");
        Print("Símbolo efectivo: {0}", effectiveSymbol);
        Print("Broker: {0}", SafeBrokerName());
        Print("Timeframe: M15");
        Print("From solicitado: {0:O}", fromUtc);
        Print("To solicitado: {0:O}", toUtc);
        Print("Primera vela exportada: {0:O}", first);
        Print("Última vela exportada: {0:O}", last);
        Print("Total velas: {0}", exportBars.Count);
        Print("Duplicados omitidos: {0}", validation.DuplicateCount);
        Print("Saltos temporales: {0}", validation.TemporalGapCount);
        Print("Ruta relativa CSV: {0}", relativePath);
        Print("Ruta esperada Windows: {0}", absoluteHint);
        if (sha != null)
            Print("SHA-256: {0}", sha);
        Print("Exportación completada sin órdenes ni posiciones.");
    }

    private void LoadHistoryUntil(Bars bars, DateTime fromUtc)
    {
        DateTime? serverFirst = null;
        try
        {
            serverFirst = HistoryBarCollector.ToUtc(bars.GetServerFirstBarOpenTime());
            Print("GetServerFirstBarOpenTime: {0:O}", serverFirst);
        }
        catch (Exception ex)
        {
            Print("ADVERTENCIA GetServerFirstBarOpenTime: {0}", ex.Message);
        }

        var iterations = 0;
        while (iterations < MaxLoadMoreIterations)
        {
            if (bars.Count == 0)
                break;

            var oldest = HistoryBarCollector.ToUtc(bars.OpenTimes[0]);
            if (oldest <= fromUtc)
            {
                Print("Historial suficiente: oldest={0:O} <= From={1:O}", oldest, fromUtc);
                break;
            }

            if (serverFirst.HasValue && oldest <= serverFirst.Value)
            {
                Print("Límite de servidor alcanzado (oldest≈serverFirst={0:O}).", serverFirst);
                break;
            }

            if (MaxBars > 0 && bars.Count >= MaxBars + 1)
            {
                Print("Tope MaxBars alcanzado durante carga (Bars.Count={0}).", bars.Count);
                break;
            }

            int loaded;
            try
            {
                loaded = bars.LoadMoreHistory();
            }
            catch (Exception ex)
            {
                Print("ERROR LoadMoreHistory: {0}", ex.Message);
                break;
            }

            iterations++;
            if (loaded <= 0)
            {
                Print("LoadMoreHistory devolvió {0} tras {1} iteraciones. oldest={2:O}", loaded, iterations, oldest);
                break;
            }

            if (iterations % 10 == 0)
                Print("LoadMoreHistory progreso: +{0}, Count={1}, oldest={2:O}", loaded, bars.Count, HistoryBarCollector.ToUtc(bars.OpenTimes[0]));
        }

        if (iterations >= MaxLoadMoreIterations)
            Print("ADVERTENCIA: se alcanzó MaxLoadMoreIterations={0}.", MaxLoadMoreIterations);

        if (bars.Count > 0)
        {
            var oldestFinal = HistoryBarCollector.ToUtc(bars.OpenTimes[0]);
            if (oldestFinal > fromUtc)
                Print(
                    "ADVERTENCIA: broker/API no entregó historia hasta FromDate. oldest={0:O}, From={1:O}. Se exportará solo lo disponible.",
                    oldestFinal, fromUtc);
        }
    }

    private string ResolveSymbolName()
    {
        if (!string.IsNullOrWhiteSpace(SymbolOverride))
            return SymbolOverride.Trim();
        return SymbolName;
    }

    private string SafeBrokerName()
    {
        try
        {
            return string.IsNullOrWhiteSpace(Account.BrokerName) ? "(desconocido)" : Account.BrokerName;
        }
        catch
        {
            return "(no disponible)";
        }
    }

    private static DateTime? SafeServerFirstBarOpenTime(Bars bars)
    {
        try
        {
            return HistoryBarCollector.ToUtc(bars.GetServerFirstBarOpenTime());
        }
        catch
        {
            return null;
        }
    }

    private static List<DateTime> CopyTimes(Bars bars, int lastClosedInclusive)
    {
        var list = new List<DateTime>(lastClosedInclusive + 1);
        for (var i = 0; i <= lastClosedInclusive; i++)
            list.Add(bars.OpenTimes[i]);
        return list;
    }

    private static List<double> CopySeries(DataSeries series, int lastClosedInclusive)
    {
        var list = new List<double>(lastClosedInclusive + 1);
        for (var i = 0; i <= lastClosedInclusive; i++)
            list.Add(series[i]);
        return list;
    }

    private static bool TryParseUtcDate(string text, out DateTime utc)
    {
        utc = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.Trim();
        if (DateTime.TryParseExact(
                trimmed,
                new[] { "yyyy-MM-dd", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ssK" },
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out utc))
        {
            utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            return true;
        }

        if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out utc))
        {
            utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            return true;
        }

        return false;
    }

    private static bool IsDateOnly(string text) =>
        text.Trim().Length == 10 && text.Trim().Count(c => c == '-') == 2;
}

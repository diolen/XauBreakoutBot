using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using XauSwingAbHarness.Dataset;
using XauTradeSimulator.Reporting;
using XauTradeSimulator.Simulation;

namespace XauTradeSimulator;

public static class Program
{
    public static int Main(string[] args)
    {
        // Guardrail: este ensamblado no debe referenciar APIs de envío de órdenes.
        AssertNoOrderApis();

        var csvPath = args.Length > 0 ? args[0] : SimulatorConfig.DefaultCsvPath;
        var outputDir = args.Length > 1
            ? args[1]
            : Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "artifacts", "trade-simulator-mvp"));

        Console.WriteLine("XauTradeSimulator — Historical Trade Simulator MVP");
        Console.WriteLine("CSV: {0}", csvPath);
        Console.WriteLine("Out: {0}", outputDir);
        Directory.CreateDirectory(outputDir);

        if (!File.Exists(csvPath))
        {
            Console.Error.WriteLine("ERROR: CSV no encontrado.");
            return 2;
        }

        var bars = CsvHistoryLoader.Load(csvPath);
        var dataset = DatasetValidator.Validate(csvPath, bars);
        File.WriteAllText(
            Path.Combine(outputDir, "dataset_validation.txt"),
            DatasetValidator.FormatSummary(dataset));

        if (!string.Equals(dataset.Sha256Hex, SimulatorConfig.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine(
                "ABORT: SHA-256 del CSV no coincide.\nEsperado: {0}\nActual:   {1}",
                SimulatorConfig.ExpectedSha256, dataset.Sha256Hex);
            return 4;
        }

        if (!dataset.IsStructurallyValid)
        {
            Console.Error.WriteLine("ABORT: dataset inválido (orden/OHLC/duplicados/non-finite).");
            return 3;
        }

        Console.WriteLine("Dataset OK | bars={0} gaps={1}", dataset.BarCount, dataset.Gaps.Count);

        var sw = Stopwatch.StartNew();
        Console.WriteLine("Replay señales T3 (K=3)...");
        var replay = SignalReplay.Run(bars);
        sw.Stop();
        Console.WriteLine(
            string.Format(
                CultureInfo.InvariantCulture,
                "Replay en {0:F1}s | señales={1} (esperado {2})",
                sw.Elapsed.TotalSeconds, replay.Signals.Count, SimulatorConfig.ExpectedSignalCount));

        if (replay.Signals.Count != SimulatorConfig.ExpectedSignalCount)
        {
            Console.Error.WriteLine(
                "ABORT: el motor no reproduce exactamente las {0} señales conocidas (obtuvo {1}). " +
                "No se simulan operaciones ni se interpretan métricas de PnL. " +
                "No se ajustará la estrategia para forzar el resultado.",
                SimulatorConfig.ExpectedSignalCount, replay.Signals.Count);
            File.WriteAllText(
                Path.Combine(outputDir, "signal_count_mismatch.txt"),
                $"Expected={SimulatorConfig.ExpectedSignalCount}\nActual={replay.Signals.Count}\n");
            return 5;
        }

        Console.WriteLine("Scenario A — Frictionless...");
        var a = TradeSimulator.Run(bars, replay.Signals, SimulatorConfig.Frictionless());
        Console.WriteLine("Scenario B — Spread 0.40...");
        var b = TradeSimulator.Run(bars, replay.Signals, SimulatorConfig.Spread040());

        ArtifactWriter.WriteAll(
            outputDir, a, b,
            dataset.Sha256Hex, dataset.BarCount,
            dataset.FirstOpenUtc, dataset.LastOpenUtc);

        PrintScenario("A Frictionless", a.Metrics);
        PrintScenario("B Spread 0.40", b.Metrics);

        Console.WriteLine("Artefactos en: {0}", outputDir);
        Console.WriteLine("DONE — sin órdenes reales/DEMO. Esperando revisión humana.");
        return 0;
    }

    private static void PrintScenario(string label, SimulationMetrics m)
    {
        Console.WriteLine("--- {0} ---", label);
        Console.WriteLine(
            string.Format(
                CultureInfo.InvariantCulture,
                "signals={0} opened={1} rejected={2} winRate={3:F2}% netPnL={4:F2} PF={5} avgR={6:F3} maxDD={7:F2} ({8:F2}%) equity={9:F2} ambig={10} gaps={11}",
                m.TotalSignals, m.TradesOpened, m.TradesRejected, m.WinRate, m.NetPnL,
                double.IsInfinity(m.ProfitFactor) ? "+Inf" : m.ProfitFactor.ToString("F3", CultureInfo.InvariantCulture),
                m.AverageR, m.MaxDrawdownUsd, m.MaxDrawdownPct, m.EndingEquity,
                m.AmbiguousSameBarCount, m.GapExitCount));
    }

    private static void AssertNoOrderApis()
    {
        var asm = Assembly.GetExecutingAssembly();
        foreach (var type in asm.GetTypes())
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                var name = method.Name;
                if (name.Contains("ExecuteMarketOrder", StringComparison.Ordinal) ||
                    name.Contains("PlaceLimitOrder", StringComparison.Ordinal) ||
                    name.Contains("PlaceStopOrder", StringComparison.Ordinal) ||
                    name.Contains("PlaceMarketOrder", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Guardrail: método de órdenes detectado: {type.FullName}.{name}");
                }
            }
        }
    }
}

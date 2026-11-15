using System.Diagnostics;
using System.Globalization;
using System.Text;
using XauSwingAbHarness.Dataset;
using XauSwingAbHarness.Metrics;

namespace XauSwingAbHarness.Structural;

public static class StructuralBreakoutProgram
{
    public static int Run(string[] args)
    {
        var config = new StructuralBreakoutConfig();
        var csvPath = args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal)
            ? args[0]
            : config.DefaultCsvPath;
        var outputDir = args.Length > 1 && !args[1].StartsWith("--", StringComparison.Ordinal)
            ? args[1]
            : Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "artifacts", "swing-ab-structural-breakout"));

        Console.WriteLine("XauSwingAbHarness — Structural (A) vs Experimental (C) Breakout");
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
        if (!string.Equals(dataset.Sha256Hex, StructuralBreakoutConfig.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("ABORT: SHA-256 distinto.\nEsperado: {0}\nActual: {1}",
                StructuralBreakoutConfig.ExpectedSha256, dataset.Sha256Hex);
            return 4;
        }

        if (!dataset.IsStructurallyValid)
        {
            Console.Error.WriteLine("ABORT: dataset inválido.");
            return 3;
        }

        var openTimes = bars.Select(b => b.OpenTime).ToList();
        var sw = Stopwatch.StartNew();
        Console.WriteLine("Replay A_K2...");
        var runA = StructuralBreakoutRunner.Run("A_K2", 2, bars, config);
        Console.WriteLine("Replay B_K3...");
        var runB = StructuralBreakoutRunner.Run("B_K3", 3, bars, config);
        sw.Stop();
        Console.WriteLine("Replay {0:F1}s | T3 A={1} B={2} | snaps A={3} B={4}",
            sw.Elapsed.TotalSeconds, runA.T3Count, runB.T3Count, runA.Snapshots.Count, runB.Snapshots.Count);

        var allMetrics = new List<DefinitionMetricsRow>();
        foreach (var run in new[] { runA, runB })
        {
            allMetrics.AddRange(StructuralBreakoutEvaluator.EvaluateDefinitions(
                run, bars, config, "baseline", run.Classified));

            var s1 = StructuralBreakoutEvaluator.FilterByGap(
                run.Classified, dataset.Gaps, openTimes, config.ReactionHorizonBars, restrictedOnly: false);
            allMetrics.AddRange(StructuralBreakoutEvaluator.EvaluateDefinitions(
                run, bars, config, "gap_S1_all", s1));

            var s2 = StructuralBreakoutEvaluator.FilterByGap(
                run.Classified, dataset.Gaps, openTimes, config.ReactionHorizonBars, restrictedOnly: true);
            allMetrics.AddRange(StructuralBreakoutEvaluator.EvaluateDefinitions(
                run, bars, config, "gap_S2_weekend_multiday", s2));

            var dec = StructuralBreakoutEvaluator.Decimate(run.Classified, config.DecimationMinGapBars);
            File.WriteAllText(
                Path.Combine(outputDir, $"decimation_{run.VariantId}.txt"),
                $"raw={run.Classified.Count} decimated={dec.Count} minGap={config.DecimationMinGapBars}\n");
            allMetrics.AddRange(StructuralBreakoutEvaluator.EvaluateDefinitions(
                run, bars, config, "decimated_16", dec));
        }

        StructuralBreakoutReport.WriteAll(outputDir, dataset, config, runA, runB, allMetrics);

        var docs = Path.GetFullPath(Path.Combine(outputDir, "..", "..", "docs", "experiments"));
        Directory.CreateDirectory(docs);
        File.Copy(
            Path.Combine(outputDir, "report_structural_vs_experimental.md"),
            Path.Combine(docs, "report_structural_vs_experimental_latest.md"),
            overwrite: true);
        File.WriteAllText(Path.Combine(docs, "structural-breakout-t3.md"),
            "# Structural vs Experimental Breakout\n\n" +
            "```bash\ndotnet run --project XauSwingAbHarness/XauSwingAbHarness.csproj -c Release -- \\\n" +
            "  /home/emsetec/cAlgo/Data/cBots/XauM15HistoryExporter/xauusd_m15_2025-11_2026-11.csv \\\n" +
            "  artifacts/swing-ab-structural-breakout --structural\n```\n",
            Encoding.UTF8);

        Console.WriteLine("Informe: {0}", Path.Combine(outputDir, "report_structural_vs_experimental.md"));
        PrintQuick(allMetrics, runA, runB);
        return 0;
    }

    private static void PrintQuick(
        List<DefinitionMetricsRow> metrics,
        StructuralBreakoutRunResult a,
        StructuralBreakoutRunResult b)
    {
        Console.WriteLine("\n--- Class counts ---");
        foreach (var run in new[] { a, b })
        {
            Console.WriteLine("{0}:", run.VariantId);
            foreach (BreakoutClass c in Enum.GetValues(typeof(BreakoutClass)))
                Console.WriteLine("  {0}={1}", c, run.Classified.Count(x => x.Class == c));
        }

        Console.WriteLine("\n--- OOS ExcludeAmbiguous ---");
        foreach (var r in metrics.Where(m =>
                     m.Filter == "baseline" && m.Segment == "oos" &&
                     m.Policy == OhlcAmbiguityPolicy.ExcludeAmbiguous))
        {
            Console.WriteLine(
                "  {0} def={1} confirm={2:P1} (n={3}) hit={4:P1}",
                r.VariantId, r.Definition, r.ConfirmationRate, r.Confirmed, r.HitRateReported);
        }
    }
}

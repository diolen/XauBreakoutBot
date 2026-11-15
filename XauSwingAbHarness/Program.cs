using System.Diagnostics;
using System.Globalization;
using System.Text;
using XauSwingAbHarness.Breakout;
using XauSwingAbHarness.Dataset;
using XauSwingAbHarness.Experiment;
using XauSwingAbHarness.Metrics;
using XauSwingAbHarness.Reporting;
using XauSwingAbHarness.Structural;

namespace XauSwingAbHarness;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Any(a => string.Equals(a, "--structural", StringComparison.OrdinalIgnoreCase)))
            return StructuralBreakoutProgram.Run(args.Where(a =>
                !string.Equals(a, "--structural", StringComparison.OrdinalIgnoreCase)).ToArray());

        var config = new BreakoutExperimentConfig();
        var csvPath = args.Length > 0 ? args[0] : config.DefaultCsvPath;
        var outputDir = args.Length > 1
            ? args[1]
            : Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "artifacts", "swing-ab-breakout-t3"));

        Console.WriteLine("XauSwingAbHarness — Breakout T=3 + HitRate corregido");
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
        File.WriteAllText(Path.Combine(outputDir, "dataset_validation.txt"), DatasetValidator.FormatSummary(dataset));

        if (!string.Equals(dataset.Sha256Hex, BreakoutExperimentConfig.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine(
                "ABORT: SHA-256 del CSV no coincide.\nEsperado: {0}\nActual:   {1}",
                BreakoutExperimentConfig.ExpectedSha256, dataset.Sha256Hex);
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
        var richA = RichVariantRunner.Run("A_K2", 2, bars, config);
        Console.WriteLine("Replay B_K3...");
        var richB = RichVariantRunner.Run("B_K3", 3, bars, config);
        sw.Stop();
        Console.WriteLine("Replay en {0:F1}s | T3 A={1} B={2}", sw.Elapsed.TotalSeconds, richA.T3Events.Count, richB.T3Events.Count);

        var allPolicy = new List<PolicyAggregate>();
        var allConfirms = new List<BreakoutConfirmResult>();
        var postPolicies = new List<PolicyAggregate>();

        foreach (var rich in new[] { richA, richB })
        {
            // --- Baseline reaction from T3 ---
            var baselineRows = rich.T3Events
                .Select(e => ReactionStudy.EvaluateFromT3(e, bars, config, "baseline"))
                .ToList();
            allPolicy.AddRange(ReactionStudy.Aggregate(baselineRows, "baseline_t3_close", config.ReactionHorizonBars));

            // --- Gap S1 / S2 ---
            foreach (var (tag, restricted) in new[] { ("gap_S1_all", false), ("gap_S2_weekend_multiday", true) })
            {
                var filtered = rich.T3Events
                    .Where(e => !EventFilters.IsExcludedByGap(
                        e, dataset.Gaps, openTimes, config.ReactionHorizonBars, restricted))
                    .Select(e => ReactionStudy.EvaluateFromT3(e, bars, config, tag))
                    .ToList();
                allPolicy.AddRange(ReactionStudy.Aggregate(filtered, tag, config.ReactionHorizonBars));
            }

            // --- Decimation ---
            var decimated = EventFilters.DecimateByMinGap(rich.T3Events, config.DecimationMinGapBars);
            File.WriteAllText(
                Path.Combine(outputDir, $"decimation_{rich.Base.VariantId}.txt"),
                $"Variant={rich.Base.VariantId} raw={rich.T3Events.Count} decimated={decimated.Count} minGap={config.DecimationMinGapBars}\n");
            var decRows = decimated
                .Select(e => ReactionStudy.EvaluateFromT3(e, bars, config, "decimated_16"))
                .ToList();
            allPolicy.AddRange(ReactionStudy.Aggregate(decRows, "decimated_16", config.ReactionHorizonBars));

            // --- Breakout confirmation ---
            var confirms = rich.T3Events
                .Select(e => BreakoutConfirmation.TryConfirm(e, bars, config, rich.AtrByBar))
                .ToList();
            allConfirms.AddRange(confirms);
            BreakoutReportWriter.WriteConfirmCsv(
                Path.Combine(outputDir, $"breakout_confirm_{rich.Base.VariantId}.csv"), confirms);

            var postRows = new List<ReactionStudyRow>();
            foreach (var c in confirms.Where(x => x.Status == BreakoutConfirmStatus.Confirmed))
            {
                var post = BreakoutConfirmation.EvaluatePostBreakout(c, bars, config, bars.Count);
                // Reutilizar ReactionStudyRow shape
                postRows.Add(new ReactionStudyRow
                {
                    T3 = c.T3,
                    FilterTag = "post_breakout",
                    HorizonTruncated = post.HorizonTruncated,
                    ByPolicy = post.Reactions
                });
            }

            postPolicies.AddRange(ReactionStudy.Aggregate(postRows, "post_breakout", config.ReactionHorizonBars));

            // Segment confirmation rates
            WriteConfirmSummary(outputDir, rich.Base.VariantId, confirms);
        }

        BreakoutReportWriter.WritePolicyCsv(Path.Combine(outputDir, "metrics_reaction_policies.csv"), allPolicy);
        BreakoutReportWriter.WritePolicyCsv(Path.Combine(outputDir, "metrics_post_breakout_policies.csv"), postPolicies);

        var limitations = BuildLimitations();
        File.WriteAllText(Path.Combine(outputDir, "limitations.md"), limitations, Encoding.UTF8);

        BreakoutReportWriter.WriteMarkdown(
            Path.Combine(outputDir, "report_breakout_t3.md"),
            dataset,
            config,
            allPolicy,
            allConfirms.Where(c => c.T3.VariantId == "A_K2").ToList(),
            allConfirms.Where(c => c.T3.VariantId == "B_K3").ToList(),
            postPolicies,
            limitations);

        // Copia documentación experimento
        var docsDir = Path.GetFullPath(Path.Combine(outputDir, "..", "..", "docs", "experiments"));
        Directory.CreateDirectory(docsDir);
        File.Copy(
            Path.Combine(outputDir, "report_breakout_t3.md"),
            Path.Combine(docsDir, "report_breakout_t3_latest.md"),
            overwrite: true);

        Console.WriteLine("Informe: {0}", Path.Combine(outputDir, "report_breakout_t3.md"));
        PrintQuick(allPolicy, allConfirms, postPolicies);
        return 0;
    }

    private static void WriteConfirmSummary(string dir, string variant, IReadOnlyList<BreakoutConfirmResult> confirms)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Variant={variant}");
        sb.AppendLine($"T3={confirms.Count}");
        foreach (BreakoutConfirmStatus st in Enum.GetValues(typeof(BreakoutConfirmStatus)))
            sb.AppendLine($"{st}={confirms.Count(c => c.Status == st)}");
        var conf = confirms.Where(c => c.Status == BreakoutConfirmStatus.Confirmed).ToList();
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"ConfirmRate={(confirms.Count == 0 ? 0 : conf.Count / (double)confirms.Count):F4}");
        if (conf.Count > 0)
            sb.AppendLine(CultureInfo.InvariantCulture, $"MeanBarsToConfirm={conf.Average(c => c.BarsToConfirm!.Value):F2}");
        foreach (var seg in new[] { SegmentSplitter.Develop, SegmentSplitter.Validate, SegmentSplitter.Oos })
        {
            var sub = confirms.Where(c => c.T3.Segment == seg).ToList();
            var ok = sub.Count(c => c.Status == BreakoutConfirmStatus.Confirmed);
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"{seg}: T3={sub.Count} Confirmed={ok} Rate={(sub.Count == 0 ? 0 : ok / (double)sub.Count):F4}");
        }

        File.WriteAllText(Path.Combine(dir, $"breakout_summary_{variant}.txt"), sb.ToString());
    }

    private static void PrintQuick(
        List<PolicyAggregate> reaction,
        List<BreakoutConfirmResult> confirms,
        List<PolicyAggregate> post)
    {
        Console.WriteLine("\n--- Baseline T3 Close ExcludeAmbiguous OOS ---");
        foreach (var r in reaction.Where(x =>
                     x.Study == "baseline_t3_close" && x.Segment == "oos" &&
                     x.Policy == OhlcAmbiguityPolicy.ExcludeAmbiguous))
            Console.WriteLine("  {0}: N={1} HitRate={2:P1} Amb={3} Unres={4}",
                r.VariantId, r.Evaluable, r.HitRateReported, r.AmbiguousSameBar, r.Unresolved);

        Console.WriteLine("\n--- Breakout confirm rates ---");
        foreach (var v in new[] { "A_K2", "B_K3" })
        {
            var sub = confirms.Where(c => c.T3.VariantId == v).ToList();
            var ok = sub.Count(c => c.Status == BreakoutConfirmStatus.Confirmed);
            Console.WriteLine("  {0}: {1}/{2} = {3:P1}", v, ok, sub.Count, sub.Count == 0 ? 0 : ok / (double)sub.Count);
        }

        Console.WriteLine("\n--- Post-breakout ExcludeAmbiguous OOS ---");
        foreach (var r in post.Where(x =>
                     x.Segment == "oos" && x.Policy == OhlcAmbiguityPolicy.ExcludeAmbiguous))
            Console.WriteLine("  {0}: N={1} HitRate={2:P1}", r.VariantId, r.Evaluable, r.HitRateReported);
    }

    private static string BuildLimitations() =>
        """
        # Limitaciones — Breakout T=3

        1. Confirmación con buffer 0.15×ATR es hipótesis experimental, distinta de BreakoutBufferAtr productivo (0.10).
        2. OHLC no revela orden intrabar; políticas Optimistic/Conservative/Exclude son cotas, no verdad.
        3. Invalidación productiva (proxy Close) puede correlacionarse con la confirmación experimental.
        4. ATR del buffer de cruce usa snapshot en T=3; ATR post-confirmación usa barra de confirmación.
        5. Horizonte y gaps en barras, no en tiempo de reloj.
        6. Decimación reduce solape pero no implica independencia i.i.d.
        7. Diagonales incluidas vía ProjectPriceAtBar causal; si faltaran anclas se excluirían.
        8. No hay órdenes, spreads ni PnL.
        9. Resultados originales del A/B previo están en artifacts/swing-ab-original-k2k3/ sin sobrescribir.
        """;
}

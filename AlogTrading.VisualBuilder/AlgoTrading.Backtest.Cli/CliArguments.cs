using System.Globalization;

namespace AlgoTrading.Backtest.Cli;

public sealed class CliArguments
{
    public bool Daemon { get; init; }
    public bool ShowHelp { get; init; }
    public string? RulePath { get; init; }
    public uint Instrument { get; init; }
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }
    public string? ConfigPath { get; init; }
    public string? OutFolder { get; init; }
    public bool Sweep { get; init; }
    public bool WalkForward { get; init; }
    public bool Quiet { get; init; }
    public bool Verbose { get; init; }
    public string? Error { get; init; }
    public bool HadIgnoredFlagsInDaemonMode { get; init; }

    public const string UsageText =
        "AlgoTrading.Backtest.Cli\n" +
        "\n" +
        "Usage:\n" +
        "  AlgoTrading.Backtest.Cli --rule <path> --instrument <token>\n" +
        "      --from <yyyy-MM-dd> --to <yyyy-MM-dd>\n" +
        "      [--config <appsettings.json>] [--out <folder>]\n" +
        "      [--sweep | --walk-forward] [--quiet | --verbose]\n" +
        "\n" +
        "  AlgoTrading.Backtest.Cli --daemon [--config <appsettings.json>]\n" +
        "\n" +
        "Flags:\n" +
        "  --rule <path>          Path to a strategy JSON file\n" +
        "  --instrument <token>   Non-zero instrument token\n" +
        "  --from <yyyy-MM-dd>    Backtest start (inclusive)\n" +
        "  --to <yyyy-MM-dd>      Backtest end (inclusive)\n" +
        "  --config <path>        Path to appsettings.json\n" +
        "  --out <folder>         Output folder for results\n" +
        "  --sweep                Run parameter sweep\n" +
        "  --walk-forward         Run walk-forward analysis\n" +
        "  --quiet                Suppress console progress\n" +
        "  --verbose              Debug-level output\n" +
        "  --daemon               Run as background file watcher\n" +
        "  --help                 Show this help and exit\n" +
        "\n" +
        "Exit codes: 0 success | 1 error | 2 config error | 130 cancelled\n";

    public static CliArguments Parse(string[] args)
    {
        bool daemon = false, help = false, sweep = false, wf = false, quiet = false, verbose = false;
        string? rulePath = null, configPath = null, outFolder = null;
        uint? instrument = null;
        DateOnly? from = null, to = null;

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (string.IsNullOrEmpty(arg)) continue;

            switch (arg)
            {
                case "--daemon": daemon = true; break;
                case "--help": case "-h": case "/?": help = true; break;
                case "--sweep": sweep = true; break;
                case "--walk-forward": wf = true; break;
                case "--quiet": quiet = true; break;
                case "--verbose": verbose = true; break;

                case "--rule":
                    if (!TryTakeValue(args, ref i, out rulePath, out var e1)) return Fail(e1);
                    break;
                case "--config":
                    if (!TryTakeValue(args, ref i, out configPath, out var e2)) return Fail(e2);
                    break;
                case "--out":
                    if (!TryTakeValue(args, ref i, out outFolder, out var e3)) return Fail(e3);
                    break;

                case "--instrument":
                    if (!TryTakeValue(args, ref i, out var instStr, out var e4)) return Fail(e4);
                    if (!uint.TryParse(instStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var inst) || inst == 0)
                        return Fail("--instrument must be a non-zero unsigned integer (got: " + instStr + ").");
                    instrument = inst;
                    break;

                case "--from":
                    if (!TryTakeValue(args, ref i, out var fromStr, out var e5)) return Fail(e5);
                    if (!DateOnly.TryParseExact(fromStr, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out var fromV))
                        return Fail("--from must be yyyy-MM-dd (got: " + fromStr + ").");
                    from = fromV;
                    break;

                case "--to":
                    if (!TryTakeValue(args, ref i, out var toStr, out var e6)) return Fail(e6);
                    if (!DateOnly.TryParseExact(toStr, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out var toV))
                        return Fail("--to must be yyyy-MM-dd (got: " + toStr + ").");
                    to = toV;
                    break;

                default:
                    return Fail("Unknown argument: " + arg);
            }
        }

        if (help) return new CliArguments { ShowHelp = true };

        if (daemon)
        {
            bool ignored =
                rulePath is not null || instrument is not null || from is not null || to is not null ||
                outFolder is not null || sweep || wf || quiet || verbose;
            return new CliArguments
            {
                Daemon = true,
                ConfigPath = configPath,
                HadIgnoredFlagsInDaemonMode = ignored
            };
        }

        if (sweep && wf) return Fail("--sweep and --walk-forward cannot both be set.");
        if (quiet && verbose) return Fail("--quiet and --verbose cannot both be set.");
        if (rulePath is null) return Fail("--rule is required.");
        if (!File.Exists(rulePath)) return Fail("--rule file not found: " + rulePath);
        if (!rulePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return Fail("--rule must point to a .json file.");
        if (instrument is null) return Fail("--instrument is required.");
        if (from is null) return Fail("--from is required.");
        if (to is null) return Fail("--to is required.");
        if (from.Value.CompareTo(to.Value) > 0)
            return Fail("--from (" + from.Value + ") must be <= --to (" + to.Value + ").");

        return new CliArguments
        {
            RulePath = Path.GetFullPath(rulePath),
            Instrument = instrument.Value,
            From = from.Value,
            To = to.Value,
            ConfigPath = configPath,
            OutFolder = outFolder,
            Sweep = sweep,
            WalkForward = wf,
            Quiet = quiet,
            Verbose = verbose
        };
    }

    private static bool TryTakeValue(string[] args, ref int i, out string? value, out string error)
    {
        value = null;
        error = "";
        if (i + 1 >= args.Length)
        {
            error = "Missing value for " + args[i];
            return false;
        }
        value = args[++i];
        return true;
    }

    private static CliArguments Fail(string error) => new() { Error = error };
}
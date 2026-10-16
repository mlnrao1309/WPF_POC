using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AlgoTrading.Screener.Storage;

public sealed class ScreenerPlanStore
{
    private readonly string folder;
    private readonly SemaphoreSlim gate = new(1, 1);
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public ScreenerPlanStore(string? folder = null) => this.folder = folder ?? ScreenerPaths.BuildPlansFolder;

    public static string ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        string value = name.Trim();
        if (value is "." or ".." || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || value.EndsWith('.') || value.EndsWith(' ')
            || value.Contains(Path.DirectorySeparatorChar) || value.Contains(Path.AltDirectorySeparatorChar)
            || IsReservedWindowsName(value))
            throw new ArgumentException("Screener Name contains invalid filename or path characters.", nameof(name));
        return value;
    }

    private static bool IsReservedWindowsName(string value)
    {
        string stem = Path.GetFileNameWithoutExtension(value);
        if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)) return true;
        return stem.Length == 4
            && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
            && stem[3] is >= '1' and <= '9';
    }

    public async Task<string> SaveAsync(ScreenerExecutionPlan plan, bool overwriteDifferent, CancellationToken ct = default)
    {
        string name = ValidateName(plan.Name);
        string path = Path.Combine(folder, name + ".json");
        string fullFolder = Path.GetFullPath(folder);
        string fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(fullFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Execution-plan path escapes the BuildPlans folder.");

        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(folder);
            if (File.Exists(path))
            {
                await using FileStream existingStream = File.OpenRead(path);
                ScreenerExecutionPlan? existing = await JsonSerializer.DeserializeAsync<ScreenerExecutionPlan>(existingStream, Options, ct).ConfigureAwait(false);
                if (existing?.ExpressionFingerprint != plan.ExpressionFingerprint && !overwriteDifferent)
                    throw new ScreenerPlanCollisionException(path);
            }
            string temp = path + ".tmp";
            await using (FileStream stream = File.Create(temp))
                await JsonSerializer.SerializeAsync(stream, plan, Options, ct).ConfigureAwait(false);
            File.Move(temp, path, true);
            return path;
        }
        finally { gate.Release(); }
    }
}

public sealed class ScreenerPlanCollisionException(string path)
    : IOException($"A different execution plan already exists at '{path}'.")
{
    public string PlanPath { get; } = path;
}

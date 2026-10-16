using AlgoTrading.Models;
using System.Globalization;

internal sealed class TimeframeNormalizationTests
{
    private int assertionCount;

    internal void Run()
    {
        TimeframeNormalizer normalizer = new TimeframeNormalizer();
        VerifyFixture(normalizer);
        VerifyInvalidInputs(normalizer);
        VerifyDefinitionIdentity(normalizer);
        Console.WriteLine("Passed " + assertionCount + " timeframe normalization assertions.");
    }

    private void Assert(bool condition, string message)
    {
        assertionCount++;
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private void VerifyFixture(TimeframeNormalizer normalizer)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "timeframe-normalization.csv");
        string[] lines = File.ReadAllLines(path);
        for (int index = 1; index < lines.Length; index++)
        {
            string[] cells = lines[index].Split(',');
            TimeframeDefinition definition = normalizer.Normalize(cells[0]);
            Assert(definition.CanonicalName == cells[1], "Canonical name mismatch on row " + index);
            Assert(definition.Kind.ToString() == cells[2], "Timeframe kind mismatch on row " + index);
            if (cells[3].Length == 0)
            {
                Assert(!definition.Duration.HasValue, "Calendar period received an intraday duration on row " + index);
            }
            else
            {
                int minutes = int.Parse(cells[3], CultureInfo.InvariantCulture);
                Assert(definition.Duration == TimeSpan.FromMinutes(minutes), "Duration mismatch on row " + index);
            }
        }
    }

    private void VerifyInvalidInputs(TimeframeNormalizer normalizer)
    {
        List<string> invalid = new List<string>();
        invalid.Add("");
        invalid.Add(" ");
        invalid.Add("0");
        invalid.Add("-5m");
        invalid.Add("20seconds");
        invalid.Add("hour");
        invalid.Add("1.5h");
        invalid.Add("D2");
        invalid.Add("15 minute extra");
        foreach (string value in invalid)
        {
            bool rejected = false;
            try
            {
                normalizer.Normalize(value);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is FormatException)
            {
                rejected = true;
            }
            Assert(rejected, "Invalid timeframe was accepted: " + value);
        }
        TimeframeDefinition daily = normalizer.Normalize("D");
        TimeframeDefinition intraday = normalizer.Normalize("15m");
        bool rejectedCalendar = false;
        try
        {
            TimeframeValidation.RequireIntraday(daily);
        }
        catch (ArgumentException)
        {
            rejectedCalendar = true;
        }
        Assert(rejectedCalendar, "Daily timeframe passed intraday validation.");
        bool rejectedIntraday = false;
        try
        {
            TimeframeValidation.RequireCalendar(intraday);
        }
        catch (ArgumentException)
        {
            rejectedIntraday = true;
        }
        Assert(rejectedIntraday, "Intraday timeframe passed calendar validation.");
        TimeframeValidation.RequireCalendar(daily);
        TimeframeValidation.RequireIntraday(intraday);
        Assert(true, "Valid timeframe validation completed.");
    }

    private void VerifyDefinitionIdentity(TimeframeNormalizer normalizer)
    {
        TimeframeDefinition first = normalizer.Normalize("20 minutes");
        TimeframeDefinition second = normalizer.Normalize("20m");
        TimeframeDefinition different = normalizer.Normalize("45m");
        Assert(first.Equals(second), "Equivalent aliases must have equal definitions.");
        Assert(first == second, "Equivalent aliases must compare equal.");
        Assert(first.GetHashCode() == second.GetHashCode(), "Equivalent aliases must have equal hashes.");
        Assert(first != different, "Different durations must remain distinct.");
        Assert(first.IsCalendarBased == false && normalizer.Normalize("W").IsCalendarBased, "Calendar classification is wrong.");
    }
}

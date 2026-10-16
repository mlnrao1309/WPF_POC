namespace Stage1Scanner.Core.Rules;

public enum RuleTruth
{
    False,
    True,
    Unknown
}

public static class RuleTruthLogic
{
    public static RuleTruth And(IEnumerable<RuleTruth> values)
    {
        var sawUnknown = false;

        foreach (var value in values)
        {
            if (value == RuleTruth.False)
            {
                return RuleTruth.False;
            }

            if (value == RuleTruth.Unknown)
            {
                sawUnknown = true;
            }
        }

        return sawUnknown ? RuleTruth.Unknown : RuleTruth.True;
    }

    public static RuleTruth Or(IEnumerable<RuleTruth> values)
    {
        var sawUnknown = false;

        foreach (var value in values)
        {
            if (value == RuleTruth.True)
            {
                return RuleTruth.True;
            }

            if (value == RuleTruth.Unknown)
            {
                sawUnknown = true;
            }
        }

        return sawUnknown ? RuleTruth.Unknown : RuleTruth.False;
    }
}

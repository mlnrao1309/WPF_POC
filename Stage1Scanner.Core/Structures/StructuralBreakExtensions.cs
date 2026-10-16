using Stage1Scanner.Core.Market;

namespace Stage1Scanner.Core.Structures;

public enum StructuralEventType
{
    None,
    AnchorCreated,
    BullishContinuation,
    BearishContinuation,
    BullishReversal,
    BearishReversal
}

public sealed record StructuralEvent(
    StructuralEventType Type,
    string StructureId,
    Candle Candle,
    Anchor? Anchor);

public static class StructuralBreakExtensions
{
    public static StructuralEvent EvaluateBreak(
        this Instruments instruments,
        string structureId,
        Candle candle)
    {
        if (instruments.Structures.TryGetLevel(structureId, out var level))
        {
            return EvaluateLevel(instruments, level, candle);
        }

        if (instruments.Structures.TryGetZone(structureId, out var zone))
        {
            return EvaluateZone(instruments, zone, candle);
        }

        return new StructuralEvent(
            StructuralEventType.None,
            structureId,
            candle,
            null);
    }

    public static StructuralEvent EvaluateAnchor(
        this Instruments instruments,
        string structureId,
        Candle candle)
    {
        if (!instruments.Structures.TryGetAnchor(structureId, out var anchor))
        {
            return new StructuralEvent(
                StructuralEventType.None,
                structureId,
                candle,
                null);
        }

        if (anchor.BreakDirection == BreakDirection.Up)
        {
            if (candle.Close > anchor.High)
            {
                return new StructuralEvent(
                    StructuralEventType.BullishContinuation,
                    structureId,
                    candle,
                    anchor);
            }

            if (candle.Close < anchor.Low)
            {
                return new StructuralEvent(
                    StructuralEventType.BearishReversal,
                    structureId,
                    candle,
                    anchor);
            }
        }
        else
        {
            if (candle.Close < anchor.Low)
            {
                return new StructuralEvent(
                    StructuralEventType.BearishContinuation,
                    structureId,
                    candle,
                    anchor);
            }

            if (candle.Close > anchor.High)
            {
                return new StructuralEvent(
                    StructuralEventType.BullishReversal,
                    structureId,
                    candle,
                    anchor);
            }
        }

        return new StructuralEvent(
            StructuralEventType.None,
            structureId,
            candle,
            anchor);
    }

    //private static StructuralEvent EvaluateLevel(
    //    Instruments instruments,
    //    CriticalLevel level,
    //    Candle candle)
    //{
    //    if (candle.Close > level.Price)
    //    {
    //        var anchor = new Anchor(
    //            level.Id,
    //            BreakDirection.Up,
    //            candle.CloseTime,
    //            candle.High,
    //            candle.Low);

    //        instruments.Structures.SetAnchor(anchor);

    //        return new StructuralEvent(
    //            StructuralEventType.AnchorCreated,
    //            level.Id,
    //            candle,
    //            anchor);
    //    }

    //    if (candle.Close < level.Price)
    //    {
    //        var anchor = new Anchor(
    //            level.Id,
    //            BreakDirection.Down,
    //            candle.CloseTime,
    //            candle.High,
    //            candle.Low);

    //        instruments.Structures.SetAnchor(anchor);

    //        return new StructuralEvent(
    //            StructuralEventType.AnchorCreated,
    //            level.Id,
    //            candle,
    //            anchor);
    //    }

    //    return new StructuralEvent(
    //        StructuralEventType.None,
    //        level.Id,
    //        candle,
    //        null);
    //}

    private static StructuralEvent EvaluateLevel(
    Instruments instruments,
    CriticalLevel level,
    Candle candle)
    {
        // SCN-BRK-004: the anchor is created by the FIRST confirming candle.
        // Once an anchor exists, subsequent closes beyond the level are
        // continuation/reversal candidates, not new breaks.
        if (instruments.Structures.TryGetAnchor(level.Id, out _))
        {
            return new StructuralEvent(
                StructuralEventType.None,
                level.Id,
                candle,
                null);
        }

        if (candle.Close > level.Price)
        {
            var anchor = new Anchor(
                level.Id,
                BreakDirection.Up,
                candle.CloseTime,
                candle.High,
                candle.Low);

            instruments.Structures.SetAnchor(anchor);

            return new StructuralEvent(
                StructuralEventType.AnchorCreated,
                level.Id,
                candle,
                anchor);
        }

        if (candle.Close < level.Price)
        {
            var anchor = new Anchor(
                level.Id,
                BreakDirection.Down,
                candle.CloseTime,
                candle.High,
                candle.Low);

            instruments.Structures.SetAnchor(anchor);

            return new StructuralEvent(
                StructuralEventType.AnchorCreated,
                level.Id,
                candle,
                anchor);
        }

        return new StructuralEvent(
            StructuralEventType.None,
            level.Id,
            candle,
            null);
    }

    private static StructuralEvent EvaluateZone(
        Instruments instruments,
        CriticalZone zone,
        Candle candle)
    {
        if (instruments.Structures.TryGetAnchor(zone.Id, out _))
        {
            return new StructuralEvent(
                StructuralEventType.None,
                zone.Id,
                candle,
                null);
        }

        if (candle.Close > zone.High)
        {
            var anchor = new Anchor(
                zone.Id,
                BreakDirection.Up,
                candle.CloseTime,
                candle.High,
                candle.Low);

            instruments.Structures.SetAnchor(anchor);

            return new StructuralEvent(
                StructuralEventType.AnchorCreated,
                zone.Id,
                candle,
                anchor);
        }

        if (candle.Close < zone.Low)
        {
            var anchor = new Anchor(
                zone.Id,
                BreakDirection.Down,
                candle.CloseTime,
                candle.High,
                candle.Low);

            instruments.Structures.SetAnchor(anchor);

            return new StructuralEvent(
                StructuralEventType.AnchorCreated,
                zone.Id,
                candle,
                anchor);
        }

        return new StructuralEvent(
            StructuralEventType.None,
            zone.Id,
            candle,
            null);
    }
}

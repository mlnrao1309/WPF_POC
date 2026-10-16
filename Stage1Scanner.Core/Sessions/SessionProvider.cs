using Stage1Scanner.Core.Market;
using System;
using System.Collections.Generic;
using System.Text;

namespace Stage1Scanner.Core.Sessions
{
    public interface ISessionProvider
    {
        SessionDefinition GetSession(Instruments instrument, DateOnly date);
    }

    public sealed record SessionDefinition(
        DateOnly Date,
        TimeOnly Open,
        TimeOnly Close,
        bool IsTradingDay);
}

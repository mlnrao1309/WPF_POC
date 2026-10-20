using Stage1Scanner.Core.Market;
using System;
using System.Collections.Generic;
using System.Text;

namespace AlgoTrading.DataAccess.Repositories
{
    public interface IInstrumentRegistry
    {
        void Initialize(IEnumerable<Instruments> instruments);
        Instruments? GetByToken(long token);
        IReadOnlyCollection<Instruments> GetAll();
    }

    public class InstrumentRegistry : IInstrumentRegistry
    {
        private Dictionary<long, Instruments> _instruments = new();

        public void Initialize(IEnumerable<Instruments> instruments)
        {
            _instruments = instruments.ToDictionary(i => i.InstrumentToken);
        }

        public Instruments? GetByToken(long token)
        {
            return _instruments.TryGetValue(token, out var inst) ? inst : null;
        }

        public IReadOnlyCollection<Instruments> GetAll()
        {
            return _instruments.Values.ToList();
        }
    }
}

namespace AlgoTrading.Data;

public interface IInstrumentLookup
{
    bool TryGet(uint token, out InstrumentInfo info);
}
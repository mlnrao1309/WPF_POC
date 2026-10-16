using AlgoTrading.DataAccess.Infrastructure.Repositories;
using AlgoTrading.Models.MarketData.Entities;
using System.Text.Json;

namespace AlgoTrading.Services
{
    public interface IInstrumentImportService
    {
        Task ProcessInstrumentsJsonAsync(string jsonArrayContent);
        Task ProcessInstrumentsJsonAsync(JsonElement jsonArray);
    }

    public class InstrumentImportService : IInstrumentImportService
    {
        private readonly IUnitOfWork _uow;

        public InstrumentImportService(IUnitOfWork uow)
        {
            _uow = uow;
        }

        public async Task ProcessInstrumentsJsonAsync(string jsonArrayContent)
        {
            using var doc = JsonDocument.Parse(jsonArrayContent);
            await ProcessInstrumentsJsonAsync(doc.RootElement);
        }

        public async Task ProcessInstrumentsJsonAsync(JsonElement jsonArray)
        {
            if (jsonArray.ValueKind != JsonValueKind.Array)
            {
                throw new ArgumentException("Provided JSON element must be an array.", nameof(jsonArray));
            }

            // 1. Separate incoming items into EQ and FO lists
            var eqItems = new List<InstrumentEq>();
            var foItems = new List<InstrumentFo>();

            foreach (var inst in jsonArray.EnumerateArray())
            {
                string segment = inst.GetProperty("segment").GetString() ?? string.Empty;

                if (segment == "NFO-FUT" || segment == "NFO-OPT")
                {
                    foItems.Add(new InstrumentFo
                    {
                        Id = inst.GetProperty("id").GetString() ?? string.Empty,
                        InstrumentToken = inst.GetProperty("instrument_token").GetInt64(),
                        TradingSymbol = inst.GetProperty("tradingsymbol").GetString() ?? string.Empty,
                        Segment = segment,
                        Exchange = inst.GetProperty("exchange").GetString() ?? string.Empty,
                        LotSize = inst.TryGetProperty("lot_size", out var lotProp) && lotProp.ValueKind != JsonValueKind.Null
                            ? lotProp.GetInt64()
                            : 1,
                        Expiry = inst.GetProperty("expiry").GetDateTime()
                    });
                }
                else
                {
                    eqItems.Add(new InstrumentEq
                    {
                        Id = inst.GetProperty("id").GetString() ?? string.Empty,
                        InstrumentToken = inst.GetProperty("instrument_token").GetInt64(),
                        TradingSymbol = inst.GetProperty("tradingsymbol").GetString() ?? string.Empty,
                        Segment = segment,
                        Exchange = inst.GetProperty("exchange").GetString() ?? string.Empty,
                        LotSize = inst.TryGetProperty("lot_size", out var lotProp) && lotProp.ValueKind != JsonValueKind.Null
                            ? lotProp.GetInt64()
                            : 1
                    });
                }
            }

            // 2. Perform Upsert on EQ Instruments (Key: InstrumentToken + Exchange)
            if (eqItems.Count > 0)
            {
                await UpsertEqInstrumentsAsync(eqItems);
            }

            // 3. Perform Upsert on FO Instruments (Key: InstrumentToken)
            if (foItems.Count > 0)
            {
                await UpsertFoInstrumentsAsync(foItems);
            }

            // 4. Commit unit of work
            await _uow.CompleteAsync();
        }

        private async Task UpsertEqInstrumentsAsync(List<InstrumentEq> incomingEqs)
        {
            // Get unique tokens from incoming list
            var incomingTokens = incomingEqs.Select(x => x.InstrumentToken).Distinct().ToList();

            // Fetch existing entities from DB matching incoming tokens
            var existingEqList = await _uow.InstrumentsEq
                .FindAsync(x => incomingTokens.Contains(x.InstrumentToken));

            // Create dictionary with composite key (Token, Exchange) for fast O(1) lookups
            var existingEqDict = existingEqList
                .ToDictionary(x => (x.InstrumentToken, x.Exchange));

            foreach (var incoming in incomingEqs)
            {
                var key = (incoming.InstrumentToken, incoming.Exchange);

                if (existingEqDict.TryGetValue(key, out var existing))
                {
                    // Record exists -> Update properties
                    existing.Id = incoming.Id;
                    existing.TradingSymbol = incoming.TradingSymbol;
                    existing.Segment = incoming.Segment;
                    existing.LotSize = incoming.LotSize;

                    _uow.InstrumentsEq.Update(existing);
                }
                else
                {
                    // New record -> Add
                    await _uow.InstrumentsEq.AddAsync(incoming);
                }
            }
        }

        private async Task UpsertFoInstrumentsAsync(List<InstrumentFo> incomingFos)
        {
            // Get unique tokens from incoming list
            var incomingTokens = incomingFos.Select(x => x.InstrumentToken).Distinct().ToList();

            // Fetch existing entities from DB matching incoming tokens
            var existingFoList = await _uow.InstrumentsFo
                .FindAsync(x => incomingTokens.Contains(x.InstrumentToken));

            // Create dictionary with key (InstrumentToken) for fast O(1) lookups
            var existingFoDict = existingFoList
                .ToDictionary(x => x.InstrumentToken);

            foreach (var incoming in incomingFos)
            {
                if (existingFoDict.TryGetValue(incoming.InstrumentToken, out var existing))
                {
                    // Record exists -> Update properties
                    existing.Id = incoming.Id;
                    existing.TradingSymbol = incoming.TradingSymbol;
                    existing.Segment = incoming.Segment;
                    existing.Exchange = incoming.Exchange;
                    existing.LotSize = incoming.LotSize;
                    existing.Expiry = incoming.Expiry;

                    _uow.InstrumentsFo.Update(existing);
                }
                else
                {
                    // New record -> Add
                    await _uow.InstrumentsFo.AddAsync(incoming);
                }
            }
        }
    }
}
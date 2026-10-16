using AlgoTrading.Models;
using System.Globalization;
using System.Text.Json;

namespace AlgoTrading.DataAccess.Calendar
{
    /// <summary>Retrieves holiday observations only. It never infers or publishes trading sessions.</summary>
    public sealed class NseHolidaySourceClient
    {
        private readonly HttpClient client;
        private readonly Uri endpoint;

        public NseHolidaySourceClient(HttpClient client, Uri endpoint)
        {
            ArgumentNullException.ThrowIfNull(client);
            ArgumentNullException.ThrowIfNull(endpoint);
            if (!endpoint.IsAbsoluteUri || endpoint.Scheme != Uri.UriSchemeHttps || endpoint.Host != "www.nseindia.com")
            {
                throw new ArgumentException("An official HTTPS NSE endpoint is required.", nameof(endpoint));
            }
            this.client = client;
            this.endpoint = endpoint;
        }

        public async Task<HolidaySourceReport> FetchAsync(string segmentCode, int year, CancellationToken cancellationToken = default)
        {
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, this.endpoint);
            request.Headers.Accept.ParseAdd("application/json");
            request.Headers.AcceptLanguage.ParseAdd("en-IN,en;q=0.9");
            request.Headers.Referrer = new Uri("https://www.nseindia.com/");
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 AlgoTrading/1.0");
            using HttpResponseMessage response = await this.client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await response.Content.LoadIntoBufferAsync(8000000, cancellationToken);
            string content = await response.Content.ReadAsStringAsync(cancellationToken);
            string contentType = response.Content.Headers.ContentType?.MediaType ?? "application/json";
            CalendarSourceDocument source = new CalendarSourceDocument(Guid.NewGuid(), "NSE", this.endpoint,
                DateTimeOffset.UtcNow, contentType, content);
            return Parse(source, segmentCode, year);
        }

        public HolidaySourceReport Parse(CalendarSourceDocument source, string segmentCode, int year)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentException.ThrowIfNullOrWhiteSpace(segmentCode);
            if (source.ExchangeCode != "NSE" || source.SourceUri.Host != "www.nseindia.com")
            {
                throw new ArgumentException("NSE observations require NSE source provenance.");
            }
            using JsonDocument document = JsonDocument.Parse(source.Content);
            JsonElement rows;
            if (!document.RootElement.TryGetProperty(segmentCode, out rows) || rows.ValueKind != JsonValueKind.Array || rows.GetArrayLength() == 0)
            {
                throw new InvalidDataException("The requested NSE segment is missing or empty; it is not a complete calendar.");
            }
            HashSet<DateOnly> dates = new HashSet<DateOnly>();
            List<ExchangeHoliday> holidays = new List<ExchangeHoliday>();
            foreach (JsonElement row in rows.EnumerateArray())
            {
                string? rawDate = row.GetProperty("tradingDate").GetString();
                string? description = row.GetProperty("description").GetString();
                DateOnly date;
                if (!DateOnly.TryParseExact(rawDate, "dd-MMM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
                    || date.Year != year || !dates.Add(date) || string.IsNullOrWhiteSpace(description))
                {
                    throw new InvalidDataException("NSE holiday data contains an invalid, duplicate or wrong-year entry.");
                }
                holidays.Add(new ExchangeHoliday(date, description));
            }
            return new HolidaySourceReport(source, segmentCode, year, holidays);
        }
    }
}

using System;

namespace AlgoTrading.Services;

/// <summary>
/// A payload pushed from the WebView when a response of interest is captured.
/// Keep this DTO dumb — no WPF, no WebView2 types — so consumers stay decoupled.
/// </summary>
public sealed record MarketWatchPayload(
    string Url,
    string Body,
    DateTimeOffset ReceivedAt);

public interface IMarketWatchDataStream
{
    event EventHandler<MarketWatchPayload>? DataReceived;
}
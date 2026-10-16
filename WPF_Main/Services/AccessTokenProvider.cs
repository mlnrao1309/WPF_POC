namespace AlgoTrading.Services;

public interface IAccessTokenProvider
{
    string? AccessToken { get; }
}

public sealed class AccessTokenStore : IAccessTokenProvider
{
    private string? accessToken;

    public string? AccessToken => Volatile.Read(ref this.accessToken);

    public void Update(string? value) => Interlocked.Exchange(ref this.accessToken, value);
}

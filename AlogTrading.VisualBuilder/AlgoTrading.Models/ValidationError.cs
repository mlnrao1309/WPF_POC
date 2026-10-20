namespace AlgoTrading.Models;

public sealed class ValidationError
{
    public string Code { get; }
    public string Message { get; }
    public string Path { get; }

    public ValidationError(string code, string message, string path)
    {
        Code = code;
        Message = message;
        Path = path;
    }

    public override string ToString() => $"{Code} @ {Path}: {Message}";
}
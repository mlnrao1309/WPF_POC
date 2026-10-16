namespace AlgoTrading.DataAccess.Infrastructure
{
    public sealed class PlatformConfigurationException : InvalidOperationException
    {
        public PlatformConfigurationException(string message)
            : base(message)
        {
        }

        public PlatformConfigurationException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}

namespace AlgoTrading.Models.Configuration
{
    /// <summary>An immutable, validated calculator rule that is safe to expose to background processing.</summary>
    public sealed class ActiveCriticalLevelRule
    {
        public ActiveCriticalLevelRule(string methodCode, string appliedTimeframe, string? referenceTimeframe,
            string parametersJson, int minimumBarsRequired)
        {
            this.MethodCode = methodCode;
            this.AppliedTimeframe = appliedTimeframe;
            this.ReferenceTimeframe = referenceTimeframe;
            this.ParametersJson = parametersJson;
            this.MinimumBarsRequired = minimumBarsRequired;
        }

        public string MethodCode { get; }

        public string AppliedTimeframe { get; }

        public string? ReferenceTimeframe { get; }

        public string ParametersJson { get; }

        public int MinimumBarsRequired { get; }
    }
}

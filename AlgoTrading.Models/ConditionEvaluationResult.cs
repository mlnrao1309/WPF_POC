namespace AlgoTrading.Models.Rules
{
    internal sealed class ConditionEvaluationResult
    {
        private readonly RuleStatus status;
        private readonly string explanation;

        internal ConditionEvaluationResult(RuleStatus status, string explanation)
        {
            this.status = status;
            this.explanation = explanation;
        }

        internal RuleStatus Status
        {
            get
            {
                return status;
            }
        }

        internal string Explanation
        {
            get
            {
                return explanation;
            }
        }
    }
}

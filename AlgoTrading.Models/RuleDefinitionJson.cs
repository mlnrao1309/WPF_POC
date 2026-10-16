using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AlgoTrading.Models.Rules
{
    public static class RuleDefinitionJson
    {
        private static readonly JsonSerializerOptions Options = CreateOptions();

        private static JsonSerializerOptions CreateOptions()
        {
            JsonSerializerOptions options = new JsonSerializerOptions();
            options.WriteIndented = true;
            options.MaxDepth = 128;
            options.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
            options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
            return options;
        }

        public static string Serialize(RuleDefinition definition)
        {
            return JsonSerializer.Serialize(definition, Options);
        }

        public static RuleDefinition Deserialize(string document)
        {
            RuleDefinition? definition = JsonSerializer.Deserialize<RuleDefinition>(document, Options);
            if (definition == null)
            {
                throw new ArgumentException("The rule document is empty.", nameof(document));
            }
            return definition;
        }
    }
}

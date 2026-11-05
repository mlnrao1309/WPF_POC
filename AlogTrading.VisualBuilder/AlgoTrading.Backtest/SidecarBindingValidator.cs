using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using AlgoTrading.Models;

namespace AlgoTrading.Backtest;

public sealed record SweepBindingSpec
{
    public required string Name { get; init; }
    public IReadOnlyList<string>? JsonPointers { get; init; } // M3-04: Supports linked multi-pointers
    public string? JsonPointer => JsonPointers?.FirstOrDefault();
    public required IReadOnlyList<decimal> Values { get; init; }
    public bool IsLinked => JsonPointers is { Count: > 1 };
}

public static class SidecarBindingValidator
{
    /// <summary>
    /// Validates sidecar sweep bindings against the root rule JSON object.
    /// Enforces RFC 6901 rules, leaf-only targets, and type legality.
    /// </summary>
    public static void ValidateBindings(JsonNode rootRule, IReadOnlyList<SweepBindingSpec> bindings)
    {
        ArgumentNullException.ThrowIfNull(rootRule);
        ArgumentNullException.ThrowIfNull(bindings);

        var seenPointers = new HashSet<string>(StringComparer.Ordinal);

        foreach (var binding in bindings)
        {
            if (string.IsNullOrWhiteSpace(binding.Name))
                throw new ArgumentException("Sweep binding name cannot be null or whitespace.");
            if (binding.Values is null || binding.Values.Count == 0)
                throw new ArgumentException($"Sweep binding '{binding.Name}' has no values to sweep.");

            var pointers = binding.JsonPointers ?? (binding.JsonPointer != null ? new[] { binding.JsonPointer } : Array.Empty<string>());
            if (pointers.Count == 0)
                throw new ArgumentException($"Sweep binding '{binding.Name}' specifies no JSON pointers.");

            foreach (var pointer in pointers)
            {
                if (!pointer.StartsWith('/'))
                    throw new ArgumentException($"JSON pointer '{pointer}' must start with '/' (RFC 6901).");

                if (!seenPointers.Add(pointer))
                    throw new ArgumentException($"Duplicate JSON pointer target '{pointer}' detected across bindings.");

                ValidatePointerTargetExistsAsLeaf(rootRule, pointer);
            }
        }
    }

    private static void ValidatePointerTargetExistsAsLeaf(JsonNode root, string pointer)
    {
        var tokens = DecodePointer(pointer);
        JsonNode? current = root;

        for (int i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];

            if (current is JsonObject obj)
            {
                if (!obj.TryGetPropertyValue(token, out var next) || next is null)
                    throw new InvalidOperationException($"JSON pointer '{pointer}' targets missing property '{token}'.");
                current = next;
            }
            else if (current is JsonArray arr && int.TryParse(token, out int idx))
            {
                if (idx < 0 || idx >= arr.Count)
                    throw new InvalidOperationException($"JSON pointer '{pointer}' array index '{token}' out of range.");
                current = arr[idx];
            }
            else
            {
                throw new InvalidOperationException($"JSON pointer '{pointer}' cannot traverse non-container node at '{token}'.");
            }
        }

        // M3-03 Guard: Ensure target is a leaf (Value), not a container (Object or Array)
        if (current is JsonObject || current is JsonArray)
        {
            throw new InvalidOperationException($"JSON pointer '{pointer}' targets a container object/array instead of a scalar leaf node.");
        }
    }

    private static string[] DecodePointer(string pointer)
    {
        return pointer.TrimStart('/')
            .Split('/')
            .Select(seg => seg.Replace("~1", "/").Replace("~0", "~"))
            .ToArray();
    }
}

public static class SidecarBindingApplicator
{
    /// <summary>
    /// Applies a tuple of binding values atomically to a rule JSON object clone.
    /// Supports both independent bindings and linked multi-pointer bindings.
    /// </summary>
    public static void ApplyTuple(JsonObject cloneObj, IReadOnlyList<SweepBindingSpec> bindings, decimal[] tupleValues)
    {
        if (bindings.Count != tupleValues.Length)
            throw new ArgumentException("Bindings count must match tuple values length.");

        for (int i = 0; i < bindings.Count; i++)
        {
            var binding = bindings[i];
            var val = tupleValues[i];

            if (binding.JsonPointers is { Count: > 0 } pointers)
            {
                // M3-04: Atomic Linked Binding application
                foreach (var pointer in pointers)
                {
                    ApplyJsonPointerValue(cloneObj, pointer, val);
                }
            }
            else
            {
                // Fallback for declared parameter name binding
                ApplyDeclaredParameter(cloneObj, binding.Name, val);
            }
        }
    }

    private static void ApplyJsonPointerValue(JsonObject root, string pointer, decimal value)
    {
        var tokens = DecodePointer(pointer);
        JsonNode? current = root;

        for (int i = 0; i < tokens.Length - 1; i++)
        {
            var token = tokens[i];
            if (current is JsonObject obj)
            {
                if (!obj.TryGetPropertyValue(token, out var next) || next is null)
                    throw new InvalidOperationException($"Pointer '{pointer}' missing property '{token}'.");
                current = next;
            }
            else if (current is JsonArray arr && int.TryParse(token, out int idx))
            {
                if (idx < 0 || idx >= arr.Count)
                    throw new InvalidOperationException($"Pointer '{pointer}' index '{token}' out of range.");
                current = arr[idx];
            }
        }

        var lastToken = tokens[^1];
        if (current is JsonObject parentObj)
        {
            if (!parentObj.ContainsKey(lastToken))
                throw new InvalidOperationException($"Pointer '{pointer}' target property '{lastToken}' does not exist.");
            parentObj[lastToken] = JsonValue.Create(value);
        }
        else if (current is JsonArray parentArr && int.TryParse(lastToken, out int arrIdx))
        {
            if (arrIdx < 0 || arrIdx >= parentArr.Count)
                throw new InvalidOperationException($"Pointer '{pointer}' array index '{lastToken}' out of range.");
            parentArr[arrIdx] = JsonValue.Create(value);
        }
    }

    private static void ApplyDeclaredParameter(JsonObject cloneObj, string paramName, decimal value)
    {
        if (cloneObj["parameters"] is JsonArray pArr)
        {
            bool found = false;
            foreach (var item in pArr)
            {
                if (item?["name"]?.GetValue<string>() == paramName)
                {
                    item["default"] = value;
                    found = true;
                    break;
                }
            }
            if (!found)
                throw new InvalidOperationException($"Declared parameter '{paramName}' not found in JSON.");
        }
        else
        {
            throw new InvalidOperationException($"JSON has no parameters array, cannot bind '{paramName}'.");
        }
    }

    private static string[] DecodePointer(string pointer)
    {
        return pointer.TrimStart('/')
            .Split('/')
            .Select(seg => seg.Replace("~1", "/").Replace("~0", "~"))
            .ToArray();
    }
}
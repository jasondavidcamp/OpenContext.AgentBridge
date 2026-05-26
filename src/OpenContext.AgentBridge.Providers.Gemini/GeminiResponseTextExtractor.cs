using System.Text.Json;

namespace OpenContext.AgentBridge.Providers.Gemini;

public static class GeminiResponseTextExtractor
{
    public static string Extract(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var root = document.RootElement;

            return TryExtractGeminiCandidates(root)
                ?? TryExtractOpenAiCompatibleChoice(root)
                ?? TryExtractSimpleText(root)
                ?? root.GetRawText();
        }
        catch (JsonException)
        {
            return responseBody;
        }
    }

    private static string? TryExtractGeminiCandidates(JsonElement root)
    {
        if (!root.TryGetProperty("candidates", out var candidates)
            || candidates.ValueKind != JsonValueKind.Array
            || candidates.GetArrayLength() == 0)
        {
            return null;
        }

        var candidate = candidates[0];
        if (!candidate.TryGetProperty("content", out var content))
        {
            return null;
        }

        if (content.ValueKind == JsonValueKind.String)
        {
            return content.GetString();
        }

        if (!content.TryGetProperty("parts", out var parts)
            || parts.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var textParts = parts
            .EnumerateArray()
            .Select(ExtractTextPart)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray();

        return textParts.Length == 0
            ? null
            : string.Join(Environment.NewLine, textParts);
    }

    private static string? TryExtractOpenAiCompatibleChoice(JsonElement root)
    {
        if (!root.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0)
        {
            return null;
        }

        var choice = choices[0];
        if (choice.TryGetProperty("message", out var message)
            && message.TryGetProperty("content", out var content))
        {
            return content.ValueKind == JsonValueKind.String
                ? content.GetString()
                : content.GetRawText();
        }

        if (choice.TryGetProperty("text", out var text)
            && text.ValueKind == JsonValueKind.String)
        {
            return text.GetString();
        }

        return null;
    }

    private static string? TryExtractSimpleText(JsonElement root)
    {
        foreach (var propertyName in new[] { "text", "output", "content" })
        {
            if (root.TryGetProperty(propertyName, out var value)
                && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }

    private static string? ExtractTextPart(JsonElement part)
    {
        if (part.ValueKind == JsonValueKind.String)
        {
            return part.GetString();
        }

        return part.TryGetProperty("text", out var text)
            ? text.GetString()
            : null;
    }
}

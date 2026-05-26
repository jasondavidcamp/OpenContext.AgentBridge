using System.Text;
using System.Text.Json;

namespace OpenContext.AgentBridge.Providers.OpenAiCompatible;

public static class OpenAiCompatibleResponseTextExtractor
{
    public static string Extract(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (TryExtractChoiceMessageContent(root, out var content)
                || TryExtractChoiceText(root, out content)
                || TryExtractSimpleText(root, out content))
            {
                return content;
            }
        }
        catch (JsonException)
        {
            return json.Trim();
        }

        throw new InvalidOperationException("OpenAI-compatible response did not contain choices[0].message.content.");
    }

    private static bool TryExtractChoiceMessageContent(JsonElement root, out string content)
    {
        content = string.Empty;
        if (!TryGetFirstChoice(root, out var choice)
            || !choice.TryGetProperty("message", out var message)
            || !message.TryGetProperty("content", out var contentElement))
        {
            return false;
        }

        return TryReadContent(contentElement, out content);
    }

    private static bool TryExtractChoiceText(JsonElement root, out string content)
    {
        content = string.Empty;
        if (!TryGetFirstChoice(root, out var choice)
            || !choice.TryGetProperty("text", out var text)
            || text.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        content = text.GetString() ?? string.Empty;
        return true;
    }

    private static bool TryExtractSimpleText(JsonElement root, out string content)
    {
        content = string.Empty;
        if (!root.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        content = text.GetString() ?? string.Empty;
        return true;
    }

    private static bool TryReadContent(JsonElement contentElement, out string content)
    {
        content = string.Empty;
        if (contentElement.ValueKind == JsonValueKind.String)
        {
            content = contentElement.GetString() ?? string.Empty;
            return true;
        }

        if (contentElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var parts = new List<string>();
        foreach (var part in contentElement.EnumerateArray())
        {
            if (part.ValueKind == JsonValueKind.String)
            {
                parts.Add(part.GetString() ?? string.Empty);
            }
            else if (part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            {
                parts.Add(text.GetString() ?? string.Empty);
            }
        }

        content = JoinNonEmpty(parts);
        return parts.Count > 0;
    }

    private static bool TryGetFirstChoice(JsonElement root, out JsonElement choice)
    {
        choice = default;
        if (!root.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0)
        {
            return false;
        }

        choice = choices[0];
        return true;
    }

    private static string JoinNonEmpty(IEnumerable<string> parts)
    {
        var builder = new StringBuilder();
        foreach (var part in parts.Where(part => !string.IsNullOrEmpty(part)))
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(part);
        }

        return builder.ToString();
    }
}

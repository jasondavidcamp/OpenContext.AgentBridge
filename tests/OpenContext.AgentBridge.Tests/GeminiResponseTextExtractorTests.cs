using OpenContext.AgentBridge.Providers.Gemini;

namespace OpenContext.AgentBridge.Tests;

public sealed class GeminiResponseTextExtractorTests
{
    [Fact]
    public void Extract_reads_gemini_candidates_parts()
    {
        var text = GeminiResponseTextExtractor.Extract(
            """
            {
              "candidates": [
                {
                  "content": {
                    "parts": [
                      { "text": "hello" },
                      { "text": "world" }
                    ]
                  }
                }
              ]
            }
            """);

        Assert.Equal($"hello{Environment.NewLine}world", text);
    }

    [Fact]
    public void Extract_reads_openai_compatible_choices()
    {
        var text = GeminiResponseTextExtractor.Extract(
            """
            {
              "choices": [
                {
                  "message": {
                    "content": "hi from wrapper"
                  }
                }
              ]
            }
            """);

        Assert.Equal("hi from wrapper", text);
    }

    [Fact]
    public void Extract_reads_simple_text_shape()
    {
        var text = GeminiResponseTextExtractor.Extract("""{"text":"simple"}""");

        Assert.Equal("simple", text);
    }

    [Fact]
    public void GetRedactedEndpoint_hides_public_api_key()
    {
        var options = new GeminiOptions
        {
            ApiKey = "secret",
            Model = "gemini-test"
        };

        var endpoint = options.GetRedactedEndpoint();

        Assert.Contains("key=<redacted>", endpoint);
        Assert.DoesNotContain("secret", endpoint);
    }
}

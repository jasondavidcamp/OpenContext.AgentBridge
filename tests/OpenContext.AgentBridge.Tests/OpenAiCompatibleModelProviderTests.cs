using System.Net;
using System.Text;
using System.Text.Json;
using OpenContext.AgentBridge.Core.Conversation;
using OpenContext.AgentBridge.Core.Models;
using OpenContext.AgentBridge.Core.Skills;
using OpenContext.AgentBridge.Core.Tools;
using OpenContext.AgentBridge.Providers.OpenAiCompatible;

namespace OpenContext.AgentBridge.Tests;

public sealed class OpenAiCompatibleModelProviderTests
{
    [Fact]
    public async Task CompleteAsync_posts_chat_completion_request()
    {
        var handler = new CaptureHandler(
            """
            {
              "id": "chatcmpl-test",
              "object": "chat.completion",
              "created": 1,
              "model": "gemini-stark",
              "choices": [
                {
                  "index": 0,
                  "message": {
                    "role": "assistant",
                    "content": "{\"type\":\"final\",\"message\":\"ok\"}"
                  },
                  "finish_reason": "stop"
                }
              ]
            }
            """);
        using var httpClient = new HttpClient(handler);
        var provider = new OpenAiCompatibleModelProvider(
            httpClient,
            new OpenAiCompatibleOptions
            {
                Endpoint = "https://stark.test/v1",
                Model = "gemini-stark",
                ApiKey = "secret"
            });

        var response = await provider.CompleteAsync(
            new AgentTurnRequest(
                "C:\\work",
                new[]
                {
                    new AgentMessage("user", "hello", DateTimeOffset.UtcNow)
                },
                Array.Empty<Skill>(),
                Array.Empty<ToolDefinition>()));

        Assert.Equal("""{"type":"final","message":"ok"}""", response.Content);
        Assert.Equal(HttpMethod.Post, handler.RequestMethod);
        Assert.Equal("https://stark.test/v1/chat/completions", handler.RequestUri?.ToString());
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("secret", handler.AuthorizationParameter);

        using var document = JsonDocument.Parse(handler.RequestBody);
        var root = document.RootElement;
        Assert.Equal("gemini-stark", root.GetProperty("model").GetString());
        Assert.False(root.GetProperty("stream").GetBoolean());

        var messages = root.GetProperty("messages");
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Contains("You are AgentBridge", messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("hello", messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task CompleteAsync_supports_custom_api_key_header_without_prefix()
    {
        var handler = new CaptureHandler(
            """
            {
              "choices": [
                {
                  "message": {
                    "content": "done"
                  }
                }
              ]
            }
            """);
        using var httpClient = new HttpClient(handler);
        var provider = new OpenAiCompatibleModelProvider(
            httpClient,
            new OpenAiCompatibleOptions
            {
                Endpoint = "https://stark.test/v1/chat/completions",
                Model = "gemini-stark",
                ApiKey = "secret",
                ApiKeyHeader = "X-STARK-Key",
                ApiKeyPrefix = string.Empty
            });

        await provider.CompleteAsync(
            new AgentTurnRequest(
                "C:\\work",
                new[]
                {
                    new AgentMessage("assistant", "previous", DateTimeOffset.UtcNow)
                },
                Array.Empty<Skill>(),
                Array.Empty<ToolDefinition>()));

        Assert.Equal("https://stark.test/v1/chat/completions", handler.RequestUri?.ToString());
        Assert.True(handler.Headers.TryGetValue("X-STARK-Key", out var value));
        Assert.Equal("secret", value);

        using var document = JsonDocument.Parse(handler.RequestBody);
        var messages = document.RootElement.GetProperty("messages");
        Assert.Equal("assistant", messages[1].GetProperty("role").GetString());
    }

    [Fact]
    public void Options_redacts_query_secrets()
    {
        var endpoint = new OpenAiCompatibleOptions
        {
            Endpoint = "https://stark.test/v1?api_key=secret-token",
            Model = "model"
        }.GetRedactedEndpoint();

        Assert.Contains("api_key=<redacted>", endpoint);
        Assert.DoesNotContain("secret-token", endpoint);
    }

    [Fact]
    public async Task ModelCatalogClient_reads_models_endpoint()
    {
        var handler = new CaptureHandler(
            """
            {
              "object": "list",
              "data": [
                {
                  "id": "gemini-stark",
                  "object": "model",
                  "created": 1686935002,
                  "owned_by": "stark-proxy"
                }
              ]
            }
            """);
        using var httpClient = new HttpClient(handler);
        var client = new OpenAiCompatibleModelCatalogClient(
            httpClient,
            new OpenAiCompatibleOptions
            {
                Endpoint = "https://stark.test/v1/chat/completions",
                Model = "ignored",
                ApiKey = "secret"
            });

        var models = await client.ListAsync();

        Assert.Equal("https://stark.test/v1/models", handler.RequestUri?.ToString());
        var model = Assert.Single(models);
        Assert.Equal("gemini-stark", model.Id);
        Assert.Equal("stark-proxy", model.OwnedBy);
        Assert.Equal(1686935002, model.Created);
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        private readonly string _responseBody;

        public CaptureHandler(string responseBody)
        {
            _responseBody = responseBody;
        }

        public HttpMethod? RequestMethod { get; private set; }

        public Uri? RequestUri { get; private set; }

        public string RequestBody { get; private set; } = string.Empty;

        public string? AuthorizationScheme { get; private set; }

        public string? AuthorizationParameter { get; private set; }

        public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestMethod = request.Method;
            RequestUri = request.RequestUri;
            RequestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;

            foreach (var header in request.Headers)
            {
                Headers[header.Key] = string.Join(",", header.Value);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}

using OpenContext.AgentBridge.SimulatedStark;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var app = builder.Build();
var responder = new SimulatedStarkResponder();

app.MapGet("/", () => Results.Json(new
{
    name = "OpenContext.AgentBridge.SimulatedStark",
    description = "Local OpenAI-compatible simulator for AgentBridge regression runs.",
    endpoints = new[] { "/v1/models", "/v1/chat/completions" }
}));

app.MapGet("/v1/models", (HttpRequest request) =>
{
    if (!HasAuthorization(request))
    {
        return Results.Unauthorized();
    }

    return Results.Json(SimulatedModelsResponse.Create());
});

app.MapPost("/v1/chat/completions", async (HttpRequest request, CancellationToken cancellationToken) =>
{
    if (!HasAuthorization(request))
    {
        return Results.Unauthorized();
    }

    var body = await new StreamReader(request.Body).ReadToEndAsync(cancellationToken);
    var response = responder.CreateChatCompletion(body);

    return Results.Json(response);
});

app.Run();

static bool HasAuthorization(HttpRequest request)
{
    return request.Headers.TryGetValue("Authorization", out var authorization)
        && authorization.Any(value => !string.IsNullOrWhiteSpace(value));
}

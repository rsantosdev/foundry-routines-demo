using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;

Console.OutputEncoding = Encoding.UTF8;

if (args.Contains("--help") || args.Contains("-h"))
{
    PrintUsage();
    return 0;
}

string? responseId = null;
var agentName = Environment.GetEnvironmentVariable("AGENT_NAME");
var showJson = false;
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--json")
        showJson = true;
    else if (args[i] == "--agent" && i + 1 < args.Length && !args[i + 1].StartsWith("--"))
        agentName = args[++i];
    else if (args[i].StartsWith("--") || responseId is not null)
    {
        PrintUsage();
        return 1;
    }
    else
        responseId = args[i];
}

var endpoint = Environment.GetEnvironmentVariable("PROJECT_ENDPOINT");
if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var projectUri) ||
    projectUri.Scheme != Uri.UriSchemeHttps)
{
    Console.Error.WriteLine("Set PROJECT_ENDPOINT to https://<account>.services.ai.azure.com/api/projects/<project>");
    return 1;
}

var endpointParts = projectUri.AbsolutePath.Trim('/').Split('/');
if (endpointParts.Length != 3 || endpointParts[0] != "api" || endpointParts[1] != "projects" ||
    !string.IsNullOrEmpty(projectUri.Query) || !string.IsNullOrEmpty(projectUri.Fragment))
{
    Console.Error.WriteLine("PROJECT_ENDPOINT must be the project URL, without /openai/v1 or a query string.");
    return 1;
}

responseId = responseId?.Trim();
if (string.IsNullOrWhiteSpace(responseId))
{
    Console.Error.Write("Response ID: ");
    responseId = Console.ReadLine()?.Trim();
}
if (string.IsNullOrWhiteSpace(responseId))
{
    Console.Error.WriteLine("A response ID from the routine's run history is required.");
    return 1;
}

// Routine responses are served by the agent endpoint, which has a different
// response/conversation scope from the project's /openai/v1 endpoint.
agentName = agentName?.Trim();
var projectBase = projectUri.AbsoluteUri.TrimEnd('/');
var escapedId = Uri.EscapeDataString(responseId);
var responseUri = string.IsNullOrWhiteSpace(agentName)
    ? new Uri($"{projectBase}/openai/v1/responses/{escapedId}")
    : new Uri($"{projectBase}/agents/{Uri.EscapeDataString(agentName)}/endpoint/protocols/openai/responses/{escapedId}?api-version=v1");

try
{
    var credential = new AzureCliCredential();
    var token = await credential.GetTokenAsync(new TokenRequestContext(["https://ai.azure.com/.default"]));
    using var http = new HttpClient();
    using var request = new HttpRequestMessage(HttpMethod.Get, responseUri);
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
    using var result = await http.SendAsync(request);
    var body = await result.Content.ReadAsStringAsync();

    if (!result.IsSuccessStatusCode)
    {
        Console.Error.WriteLine($"Lookup failed: HTTP {(int)result.StatusCode} ({result.ReasonPhrase}).");
        Console.Error.WriteLine($"Request: GET {responseUri}");
        Console.Error.WriteLine(body);
        foreach (var name in new[] { "apim-request-id", "x-request-id", "x-ms-request-id" })
            if (result.Headers.TryGetValues(name, out var values))
                Console.Error.WriteLine($"{name}: {string.Join(", ", values)}");
        if ((int)result.StatusCode == 404)
            Console.Error.WriteLine("For routine responses, pass --agent <agent-name>. Use the identity that created a creator-identity routine.");
        return 1;
    }

    using var document = JsonDocument.Parse(body);
    var response = document.RootElement;
    var status = response.GetProperty("status").GetString();
    if (showJson)
        Console.WriteLine(JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true }));
    else
    {
        Console.WriteLine($"Response: {response.GetProperty("id").GetString()}");
        Console.WriteLine($"Status:   {status}");
        Console.WriteLine($"Scope:    {(string.IsNullOrWhiteSpace(agentName) ? "project" : $"agent {agentName}")}");
        Console.WriteLine();
        var textParts = new List<string>();
        if (response.TryGetProperty("output", out var output))
            foreach (var item in output.EnumerateArray())
                if (item.TryGetProperty("content", out var content))
                    foreach (var part in content.EnumerateArray())
                        if (part.TryGetProperty("type", out var type) && type.GetString() == "output_text" &&
                            part.TryGetProperty("text", out var text))
                            textParts.Add(text.GetString() ?? "");
        Console.WriteLine(textParts.Count > 0
            ? string.Join(Environment.NewLine, textParts)
            : "No text output is available. Use --json to inspect all output items and error details.");
    }

    if (status != "completed")
    {
        Console.Error.WriteLine("Response is not completed. If queued or in progress, run again later. Use --json for details.");
        return 2;
    }
    return 0;
}
catch (AuthenticationFailedException)
{
    Console.Error.WriteLine("Azure authentication failed. Run az login for the tenant containing your Foundry project.");
    return 1;
}
catch (HttpRequestException ex)
{
    Console.Error.WriteLine($"Request failed: {ex.Message}");
    return 1;
}

static void PrintUsage()
{
    Console.Error.WriteLine("Usage: dotnet run -- [response-id] [--agent <agent-name>] [--json]");
    Console.Error.WriteLine("Set PROJECT_ENDPOINT and sign in with az login. AGENT_NAME can replace --agent.");
    Console.Error.WriteLine("For routine responses, specify the agent. Omit response-id to paste it at a prompt.");
}

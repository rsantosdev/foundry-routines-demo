# Foundry routine response viewer

A small .NET console app for a video demo: copy a response ID from a routine run, pass it with the agent name, and display the agent's answer.

## Setup and run

Install the [.NET 8 SDK or later](https://dotnet.microsoft.com/download) and [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli). Sign in using an identity with access to the Foundry project.

From this directory:

```bash
az login
export PROJECT_ENDPOINT="https://<account>.services.ai.azure.com/api/projects/<project>"
export AGENT_NAME="agent-demo-classification"
dotnet run -- "<response-id-from-the-routine>"
```

Or pass the agent explicitly:

```bash
dotnet run -- "<response-id>" --agent agent-demo-classification
```

PowerShell:

```powershell
$env:PROJECT_ENDPOINT = "https://<account>.services.ai.azure.com/api/projects/<project>"
$env:AGENT_NAME = "agent-demo-classification"
dotnet run -- "<response-id>"
```

Omit the response ID to paste it at a prompt. Add `--json` to print the complete response object, including metadata, tool calls, annotations, and error details:

```bash
dotnet run -- "<response-id>" --agent agent-demo-classification --json
```

`--agent` overrides `AGENT_NAME`. Use the project URL from Foundry Overview, without appending `/openai/v1`. The app uses `AzureCliCredential`; for a creator-identity routine, sign in as its creator in the appropriate tenant (`az login --tenant <tenant-id>`).

## Why the agent name matters

The project and agent endpoints have different response and conversation scopes:

| Mode | GET route, relative to PROJECT_ENDPOINT |
|---|---|
| Routine response, with an agent name | `/agents/<agent-name>/endpoint/protocols/openai/responses/<response-id>?api-version=v1` |
| Project response, without an agent name | `/openai/v1/responses/<response-id>` |

A routine response can exist at the agent endpoint and return HTTP 404 at the project endpoint. This behavior was verified live for a creator-identity routine. Its retrieved response metadata identifies the routine by name.

The app uses an explicit REST request so the target route is visible in the code. Its only NuGet dependency is `Azure.Identity` for keyless authentication. The successful response is parsed with `System.Text.Json`.

This app only retrieves existing responses. It doesn't create or dispatch routines or invoke an agent. It supports routines using the Responses API action (`invoke_agent_responses_api`).

## Conversations and identity

To continue a routine on a known conversation, create that conversation through the same agent endpoint and identity context:

```text
POST <project-endpoint>/agents/<agent-name>/endpoint/protocols/openai/conversations?api-version=v1
```

The returned conversation ID can be selected or pasted into the routine's Conversation field. A conversation created under the project's `/openai/v1/conversations` route may be unavailable to the agent endpoint. Selecting such a conversation caused `conversation_not_found` in the live diagnostic test.

The tested agent uses Entra-based isolation. An old response from an agent-identity routine still returned 404 under the human CLI identity, including at the agent endpoint. The creator-identity routine's response was successfully retrieved under the CLI identity. Don't assume changing the route alone makes responses created under another identity accessible.

## Video demo

1. Use the existing creator-identity routine and its response ID from run history.
2. Set `PROJECT_ENDPOINT` and `AGENT_NAME` as shown above.
3. Run `dotnet run` and paste the response ID.
4. The app prints the response ID, status, scope, and generated text.

The displayed text is the actual stored agent output. Check it before recording: a completed response can explain that a requested tool is unavailable, or contain illustrative data rather than tool results.

## Errors and validation

HTTP errors print the exact request URL, service error, and available request IDs. For a 404, check the agent name and signed-in identity as well as the project and response ID.

Exit codes: `0` for a completed response, `2` for a retrieved response in another state, and `1` for input, authentication, HTTP, or network errors. If queued or in progress, run again later. `--json` doesn't fetch linked files or tool artifacts.

The revised app builds with no warnings or errors. Live checks retrieved the creator-identity routine response through the agent endpoint and verified both text and JSON output. Project-level retrieval remains available when neither `--agent` nor `AGENT_NAME` is supplied.

## References

- [Routines guide](https://learn.microsoft.com/en-us/azure/foundry/agents/how-to/use-routines)
- [Foundry SDK quickstart, including agent endpoint REST routes](https://learn.microsoft.com/en-us/azure/foundry/quickstarts/get-started-code)
- [Agent endpoint isolation](https://learn.microsoft.com/en-us/azure/foundry/agents/how-to/manage-hosted-sessions#isolation-keys)

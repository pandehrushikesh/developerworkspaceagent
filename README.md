# ProjectLens

ProjectLens is a .NET 8 console-based AI agent solution scaffolded with clean architecture boundaries and an optional OpenAI-backed model client.

## Project Structure

- `ProjectLens.Host`: console entry point and composition root for running the agent application.
- `ProjectLens.Application`: application-layer coordination and use-case logic.
- `ProjectLens.Domain`: core agent contracts and domain models, kept free of infrastructure concerns.
- `ProjectLens.Infrastructure`: external system implementations and adapters.
- `ProjectLens.Tests`: lightweight automated coverage for tools and orchestration flows.

## Notes

- Dependency flow is `Host -> Application -> Domain` and `Host -> Infrastructure -> Application -> Domain`.
- The `Domain` project contains the core agent request/response models plus tool and orchestrator interfaces.
- `ProjectLens.Application` contains the agent orchestrator plus the `IModelClient` seam used for model-driven tool orchestration.
- `ProjectLens.Infrastructure` contains filesystem tools and the OpenAI `IModelClient` implementation.

## Model Configuration

Set model settings in [ProjectLens.Host/appsettings.json](C:/Users/admin/source/repos/developerworkspaceagent/ProjectLens.Host/appsettings.json):

- `OpenAI:ApiKey`: required to enable model-driven orchestration.
- `OpenAI:Model`: required model name.
- `OpenAI:BaseUrl`: optional override for the API base URL.
- `OpenAI:MaxIterations`: maximum model/tool loop iterations before the agent stops.

If `ApiKey` or `Model` is not configured, ProjectLens automatically falls back to the existing rule-based repository summary flow.

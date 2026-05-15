# StrandsAgents.Models.Bedrock

Amazon Bedrock model provider for [Strands Agents .NET](https://github.com/apncodes/StrandsAgents.net).

```bash
dotnet add package StrandsAgents.Core
dotnet add package StrandsAgents.Models.Bedrock
```

```csharp
using StrandsAgents.Core;
using StrandsAgents.Models.Bedrock;

IModel model = new BedrockModel(
    region: "us-east-1",
    modelId: "us.anthropic.claude-sonnet-4-5-v1:0");

var agent = new Agent(model, systemPrompt: "You are a helpful assistant.");
var result = await agent.InvokeAsync("Hello!");
```

Supports Claude, Nova, Llama, and all other Bedrock-hosted models. Requires AWS credentials
configured via environment variables, `~/.aws/credentials`, or IAM role.

# CodeInterpreterSample

A data analysis assistant that executes code in a managed sandbox provided by Amazon Bedrock AgentCore Code Interpreter. The session is stateful — variables and imports persist across turns, so you can build on previous results interactively.

## SDK concepts demonstrated

**`AgentCoreCodeInterpreterTool`** — backed by the official `AWSSDK.BedrockAgentCore` SDK client. The tool manages its own session lifecycle:
- Session is created lazily on the first `InvokeAsync` call via `StartCodeInterpreterSessionAsync`
- The same session is reused across all subsequent calls — state persists
- Session is stopped automatically when the tool is disposed (`DisposeAsync`)

**Stateful execution** — because the session persists, you can define a variable in one turn and reference it in the next. This enables iterative data analysis workflows.

**`clear_context` parameter** — the LLM can pass `clear_context: true` in a tool call to reset the session state when starting a fresh computation.

**Supported languages** — `python`, `javascript`, `typescript`. The tool validates the language before making any SDK call and returns a clear error for unsupported languages.

**Tool call visibility** — the REPL prints `ToolCallStartEvent` and `ToolCallResultEvent` so you can see when code is being executed and what the result was.

## Prerequisites

- .NET 10 SDK
- AWS credentials configured for Bedrock and AgentCore
- An AgentCore Code Interpreter resource in your AWS account (optional — uses `"default"` if not set)

## How to run

```bash
# Optional: set your code interpreter resource ID
export AGENTCORE_CODE_INTERPRETER_ID=ci-abc123

dotnet run --project samples/CodeInterpreterSample
```

## Example session

```
You: Calculate the first 20 Fibonacci numbers in Python
  [executing code]... → Exit code: 0  Execution time: 0.123s  Stdout: [0, 1, 1, 2, 3, 5, 8...
Assistant: Here are the first 20 Fibonacci numbers: [0, 1, 1, 2, 3, 5, 8, 13, 21, 34, ...]

You: What is the sum of all even ones?
  [executing code]... → Exit code: 0  Execution time: 0.045s  Stdout: 3382
Assistant: The sum of all even Fibonacci numbers in that list is 3382.
           (The variable `fibs` from the previous turn was still in scope.)
```

## Where you'd use these patterns

- **Data analysis assistants** — let the agent write and run pandas/numpy code rather than computing manually.
- **Code review tools** — execute code snippets to verify correctness before suggesting them.
- **Math tutors** — show step-by-step computation with real execution results.
- **DevOps agents** — run diagnostic scripts and act on the output.

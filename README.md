# YOLO LLM Module (TextGen + Summarization)

Experiment to ship a Voxta module that brings its own OpenAI-compatible LLM client instead of relying on the server’s built-in chat LLM. It registers both `TextGen` and `Summarization` services.

## What it does
- Calls a configurable chat-completions endpoint (`BaseUrl`, `Model`, `ApiKey`) with a simple OpenAI-like payload.
- Implements `ITextGenService` and `ISummarizationService`:
  - Reply/story generation uses Voxta prompt builder requests and returns a single token chunk.
  - Summarization/memory extraction uses prompt builder requests; memory extraction output is parsed from JSON array, `<memories>...</memories>`, or newline list.
  - Lines starting with `GRAPH_JSON:` are ignored during memory extraction parsing.
  - Memory merge is currently a no-op (returns `MemoryMergeResult.Empty`).
- Tokenization uses `NullTokenizer`; streaming is emulated by returning one `LLMOutputToken` with the full response.

## Configuration fields
- `ApiKey` (secret), `BaseUrl`, `Model`
- `Temperature`, `MaxNewTokens`, `MaxWindowTokens`, `MaxMemoryTokens`
- `MaxSummaryTokens`, `SummarizationDigestRatio`, `SummarizationTriggerMessagesBuffer`, `KeepLastMessages`
- `LogLifecycleEvents` (default `true`) — logs when summarization/memory extraction run
- Optional prompt overrides (prepended as system messages):
  - `ReplySystemPromptPath`
  - `SummaryPromptPath`
  - `MemoryExtractionPromptPath`

## Usage
1) `dotnet build -c Release`
2) Copy `bin/Release/net10.0/Voxta.Modules.YoloLLM.dll` to `Voxta.Server.../Modules/`.
3) In Voxta UI, enable the module and select `YoloLLM` for TextGen and/or Summarization. Provide API key, base URL, model.

## Limitations / TODO
- No true streaming; outputs are single-chunk.
- No retry/backoff or rate-limit handling.
- Memory merge not implemented; only extraction parsing is provided.
- Expects OpenAI-compatible responses (`choices[0].message.content`). Other providers may need payload/response tweaks.
- No preset dropdowns; use the prompt-path overrides if you need custom system prompts.

# YOLO LLM Module (TextGen + Summarization)

Experiment to ship a Voxta module that brings its own OpenAI-compatible LLM client instead of relying on the server’s built-in chat LLM. It registers both `TextGen` and `Summarization` services.

## What it does
  - Calls a configurable chat-completions endpoint (`BaseUrl`, `Model`, optional `ApiKey`) with a simple OpenAI-like payload.
  - Implements `ITextGenService` and `ISummarizationService`:
  - Reply/story generation uses Voxta prompt builder requests and returns a single token chunk.
  - Summarization/memory extraction uses prompt builder requests; memory extraction output is parsed from JSON array, `<memories>...</memories>`, or newline list.
  - Graph extraction (separate call): during summarization, runs an additional prompt to produce JSON `{entities, relations}` and writes it to the GraphMemory inbox (`Data/GraphMemory/Inbox`) for ingestion (includes `meta.chatId/sessionId/user/characters` for scoping).
  - Memory merge is currently a no-op (returns `MemoryMergeResult.Empty`).
- Tokenization uses `NullTokenizer`; streaming is emulated by returning one `LLMOutputToken` with the full response.

## Configuration fields
These are module-wide defaults (connection + fallback values). Service settings presets (below) override them when set.

- `ApiKey` (secret, optional for local/unauthed endpoints), `BaseUrl`, `Model`
- `Temperature`, `MaxNewTokens`, `MaxWindowTokens`, `MaxMemoryTokens`
- `MaxSummaryTokens`, `SummarizationDigestRatio`, `SummarizationTriggerMessagesBuffer`, `KeepLastMessages`
- `LogLifecycleEvents` (default `true`) — logs when summarization/memory extraction run
- Optional prompt overrides (prepended as system messages):
  - `ReplySystemPromptPath`
  - `SummaryPromptPath`
  - `MemoryExtractionPromptPath`
- Graph extraction:
  - `EnableGraphExtraction` (default `true`)
  - `GraphExtractionPromptPath` (default `Resources/Prompts/Default/en/YoloLLM/GraphExtraction.graph.scriban`)

## Prompt templates
Editable prompt add-ons are kept in `YoloLLMArtifacts/` and can be deployed to:
- `Voxta.Server.Win.v1.2.0/Resources/Prompts/Default/en/YoloLLM/`

Suggested paths:
- `Resources/Prompts/Default/en/YoloLLM/ReplySystemAddon.scriban`
- `Resources/Prompts/Default/en/YoloLLM/SummarizationAddon.scriban`
- `Resources/Prompts/Default/en/YoloLLM/MemoryExtractionAddon.scriban`
- `Resources/Prompts/Default/en/YoloLLM/GraphExtraction.graph.scriban`
- (Legacy) `Resources/Prompts/Default/en/YoloLLM/MemoryExtractionAddon.GraphMemory.scriban` (old approach that embedded `GRAPH_JSON:` into memory extraction output; no longer needed with inbox ingestion).

## Settings presets (Service Settings)
This module now supports Voxta **Service Settings** presets. In Voxta, summarization/action-inference share the same preset bucket as TextGen, so a single preset controls both reply generation and summarization behavior for this module.

Preset overrides include:
- `Model`, `Temperature`
- `MaxNewTokens`, `MaxSummaryTokens`, `MaxWindowTokens`, `MaxMemoryTokens`
- `SummarizationDigestRatio`, `SummarizationTriggerMessagesBuffer`, `KeepLastMessages`
- `ReplySystemPromptPath`, `SummaryPromptPath`, `MemoryExtractionPromptPath`
- `LogLifecycleEvents`

## Usage
1) `dotnet build -c Release`
2) Copy `bin/Release/net10.0/Voxta.Modules.YoloLLM.dll` to `Voxta.Server.../Modules/`.
3) In Voxta UI, enable the module and select `YoloLLM` for TextGen and/or Summarization. Provide base URL + model (API key only if the endpoint requires auth).

### OpenRouter quick setup
- `BaseUrl`: `https://openrouter.ai/api/v1/chat/completions`
- `ApiKey`: paste the raw key (do NOT include the `Bearer ` prefix)
- `Model`: e.g. `google/gemini-2.5-flash`

## Limitations / TODO
- No true streaming; outputs are single-chunk.
- No retry/backoff or rate-limit handling.
- Memory merge not implemented; only extraction parsing is provided.
- Expects OpenAI-compatible responses (`choices[0].message.content`). Other providers may need payload/response tweaks.
- No model dropdowns; `Model` is free-text (use presets to try different models/prompts quickly).

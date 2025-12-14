# YOLO LLM Module — Agent Notes

## What it is
- A module that brings its own OpenAI-compatible client for both TextGen and Summarization. It does NOT reuse the server’s configured LLM.
- Registers as `YoloLLM` for `ServiceTypes.TextGen` and `ServiceTypes.Summarization`.
- Uses the Voxta prompt builder to assemble messages; optional prompt overrides can be prepended as system messages.

## Prompts / Overrides
- Reply/system for chat replies & story: `ReplySystemPromptPath` (optional file path).
- Summarization system: `SummaryPromptPath` (optional file path).
- Memory extraction system: `MemoryExtractionPromptPath` (optional file path).
- If an override path is empty or missing, it uses the server’s default templates (whatever prompt builder resolves).
- Prompt add-on templates are kept in `YoloLLMArtifacts/` and are meant to be copied to `Resources/Prompts/Default/en/YoloLLM/` in the server bundle.

## Configuration Fields (UI)
- Auth/endpoint: `ApiKey` (secret), `BaseUrl`, `Model`
- Generation caps: `Temperature`, `MaxNewTokens`, `MaxWindowTokens`, `MaxMemoryTokens`
- Summaries: `MaxSummaryTokens`, `SummarizationDigestRatio`, `SummarizationTriggerMessagesBuffer`, `KeepLastMessages`
- Observability: `LogLifecycleEvents` (default true) logs start/end for summarization + memory extraction
- Prompt overrides: `ReplySystemPromptPath`, `SummaryPromptPath`, `MemoryExtractionPromptPath`

## Service Settings Presets (Now Supported)
- `YoloLLM` now provides a `PresetsProviderType` (`IServiceSettingsProvider`) so you can create multiple **Service Settings** presets in Voxta.
- Voxta groups Summarization/ActionInference presets under `ServiceTypes.TextGen` (`ServiceTypesExtensions.ForServiceSettings()`), so a single preset applies to both reply generation and summarization for this module.
- Presets override module defaults when present: model/temperature, token budgets, prompt paths, and summarization knobs.

## Behavior
- TextGen: single-shot generation (no true streaming) via chat-completions; returns one `LLMOutputToken` chunk.
- Summarization: uses builder requests; extraction output parsed from JSON array or newline list; merge is a no-op.
- Memory extraction parsing supports JSON arrays, default Voxta `<memories>...</memories>` blocks, and newline lists.
- If the model emits a line starting with `GRAPH_JSON:`, it is passed through as a memory item (intended for the `GraphMemory` provider to ingest and discard from lore).
- Tokenizer: `NullTokenizer`.

## Limitations / TODO
- No retries/backoff; assumes OpenAI-compatible response shape (`choices[0].message.content`).
- Model list is free-text; use presets to switch models/prompts quickly.
- Memory merge not implemented; just returns `MemoryMergeResult.Empty`.
- No attachment handling; no multimodal.

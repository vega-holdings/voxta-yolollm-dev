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

## Configuration Fields (UI)
- Auth/endpoint: `ApiKey` (secret), `BaseUrl`, `Model`
- Generation caps: `Temperature`, `MaxNewTokens`, `MaxWindowTokens`, `MaxMemoryTokens`
- Summaries: `MaxSummaryTokens`, `SummarizationDigestRatio`, `SummarizationTriggerMessagesBuffer`, `KeepLastMessages`
- Observability: `LogLifecycleEvents` (default true) logs start/end for summarization + memory extraction
- Prompt overrides: `ReplySystemPromptPath`, `SummaryPromptPath`, `MemoryExtractionPromptPath`

## Behavior
- TextGen: single-shot generation (no true streaming) via chat-completions; returns one `LLMOutputToken` chunk.
- Summarization: uses builder requests; extraction output parsed from JSON array or newline list; merge is a no-op.
- Memory extraction parsing also supports default Voxta `<memories>...</memories>` blocks and ignores `GRAPH_JSON:` lines.
- Tokenizer: `NullTokenizer`.

## Limitations / TODO
- No retries/backoff; assumes OpenAI-compatible response shape (`choices[0].message.content`).
- No per-model preset selection (use prompt override paths instead).
- Memory merge not implemented; just returns `MemoryMergeResult.Empty`.
- No attachment handling; no multimodal.

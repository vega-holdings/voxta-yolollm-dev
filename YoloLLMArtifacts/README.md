# YOLOLLM Prompt Add-ons

This folder keeps editable copies of optional **system-prompt add-ons** for the `Voxta.Modules.YoloLLM` module.

These files are **read as plain text** and are **prepended as an extra `system` message** before Voxta’s normal prompt-builder messages. They are not rendered/evaluated as Scriban templates by this module (the `.scriban` extension is just to match Voxta’s prompt conventions).

## Files
- `ReplySystemAddon.scriban` — Optional add-on for chat replies/story generation.
- `SummarizationAddon.scriban` — Optional add-on for summarization.
- `MemoryExtractionAddon.scriban` — Optional add-on for memory extraction.
- `MemoryExtractionAddon.GraphMemory.scriban` — Optional add-on that asks the model to also emit a `GRAPH_JSON:` line that the `GraphMemory` module can ingest.

## Deployment
Copy these files to the Voxta server bundle:
- `Voxta.Server.Win.v1.2.0/Resources/Prompts/Default/en/YoloLLM/`

Then point the module settings to them (relative to the server root), e.g.:
- `Resources/Prompts/Default/en/YoloLLM/ReplySystemAddon.scriban`
- `Resources/Prompts/Default/en/YoloLLM/SummarizationAddon.scriban`
- `Resources/Prompts/Default/en/YoloLLM/MemoryExtractionAddon.scriban`


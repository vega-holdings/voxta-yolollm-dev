using System.Collections.Generic;
using Voxta.Abstractions.Diagnostics;
using Voxta.Model.Shared;

namespace Voxta.Modules.YoloLLM.Services;

internal sealed class SimpleMessagesDisplayable(SimpleMessageData[] messages) : IDisplayable
{
    public SimpleMessageData[] GetMessages() => messages;

    public Dictionary<string, string> GetDisplayDict() => new()
    {
        ["Messages"] = messages.Length.ToString(),
    };
}


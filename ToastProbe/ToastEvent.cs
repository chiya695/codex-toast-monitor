namespace CodexToastProbe;

internal sealed record ToastEvent(
    uint Id,
    DateTimeOffset CreationTime,
    string AppUserModelId,
    string DisplayName,
    string[] Texts,
    DateTimeOffset ObservedAtUtc);

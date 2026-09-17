namespace MinecraftTopo.Core;

/// <summary>A progress snapshot reported by long-running operations.</summary>
public sealed record ProgressInfo(
    string Phase,
    double Percent,
    string? Message = null,
    int? Done = null,
    int? Total = null)
{
    public override string ToString() =>
        Total is { } t ? $"{Phase} {Percent:0}% ({Done}/{t}) {Message}" : $"{Phase} {Percent:0}% {Message}";
}

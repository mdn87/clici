namespace Clici.App.Processes;

internal interface IForegroundProcessProvider
{
    ForegroundProcessResult TryGetForegroundProcess();
}

internal sealed record ForegroundProcessResult(
    bool Succeeded,
    string? ProcessName,
    string? ExceptionType);

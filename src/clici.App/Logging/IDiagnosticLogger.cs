using Clici.Core.MarginNormalization;

namespace Clici.App.Logging;

internal interface IDiagnosticLogger
{
    void Decision(
        string? processName,
        MarginNormalizationResult result);

    void Failure(
        string operation,
        string? processName,
        string? exceptionType);

    void Event(string eventName);
}

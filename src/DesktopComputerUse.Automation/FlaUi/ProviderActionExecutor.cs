using DesktopComputerUse.Contracts.Automation;

namespace DesktopComputerUse.Automation.FlaUi;

internal static class ProviderActionExecutor
{
    internal static void Execute(string operation, string pattern, Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception) when (exception is InvalidOperationException ||
            AutomationExceptionResultMapper.IsExpectedProviderException(exception))
        {
            var code = exception is InvalidOperationException
                ? AutomationErrorCode.UnsupportedPattern
                : AutomationExceptionResultMapper.Classify(exception);
            var message = exception is InvalidOperationException
                ? $"The resolved control advertises {pattern} but the provider rejected the operation. Use a supported semantic action or explicitly enabled native input."
                : $"The control resolved successfully, but its {pattern} provider failed during execution. Inspect application state before retrying.";
            var diagnostic = AutomationExceptionResultMapper.CreateDiagnostic(exception,
                new AutomationDiagnostic { Operation = operation, Phase = "executePattern", Property = pattern });
            throw new AutomationOperationException(code, message, diagnostic: diagnostic with
            {
                Code = code,
                Retryable = false,
                InputMayHaveOccurred = true
            });
        }
    }
}

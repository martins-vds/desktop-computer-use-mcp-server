namespace DesktopComputerUse.Contracts.Configuration;

public sealed class ProfileValidationException : Exception
{
    public ProfileValidationException(string message)
        : base(message)
    {
    }
}

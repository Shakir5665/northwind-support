namespace SupportDesk.Application.Exceptions;

/// <summary>
/// Thrown when authentication fails (unknown email, incorrect password, or inactive agent).
/// </summary>
public class AuthenticationException : Exception
{
    public const string DefaultMessage = "Invalid email or password.";

    public AuthenticationException(string message = DefaultMessage) : base(message)
    {
    }
}

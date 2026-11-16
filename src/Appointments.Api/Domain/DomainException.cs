namespace Appointments.Api.Domain;

/// <summary>
/// A business rule was broken. Carries a stable machine-readable code and the HTTP status to answer with;
/// the API turns it into an RFC 9457 problem response.
/// </summary>
public sealed class DomainException(string code, string message, int statusCode) : Exception(message)
{
    public string Code { get; } = code;

    public int StatusCode { get; } = statusCode;

    public static DomainException Invalid(string code, string message) => new(code, message, StatusCodes.Status400BadRequest);

    public static DomainException NotFound(string code, string message) => new(code, message, StatusCodes.Status404NotFound);

    public static DomainException Conflict(string code, string message) => new(code, message, StatusCodes.Status409Conflict);
}

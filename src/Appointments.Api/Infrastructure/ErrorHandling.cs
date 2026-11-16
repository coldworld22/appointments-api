using Appointments.Api.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace Appointments.Api.Infrastructure;

/// <summary>
/// Turns broken business rules into RFC 9457 problem responses with a stable <c>code</c>,
/// so clients branch on <c>slot_full</c> rather than on message text.
/// </summary>
public sealed class DomainExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, code, title) = exception switch
        {
            DomainException e => (e.StatusCode, e.Code, e.Message),
            // Still conflicting after the retries in WithConcurrencyRetry: heavy contention on one slot.
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "busy", "The slot changed while you were booking. Please try again."),
            BadHttpRequestException e => (e.StatusCode, "bad_request", "The request could not be read."),
            _ => (0, "", ""),
        };

        if (status == 0)
        {
            return false;
        }

        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails =
            {
                Status = status,
                Title = title,
                Extensions = { ["code"] = code },
            },
        });
    }
}

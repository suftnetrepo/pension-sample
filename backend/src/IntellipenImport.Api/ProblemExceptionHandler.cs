using IntellipenImport.Application.Abstractions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace IntellipenImport.Api;

/// <summary>
/// Turns <see cref="ImportProblem"/> into problem details with its plain-English message (and, for a schema mismatch,
/// the differences under "mismatch"). Anything unexpected becomes a 500 with no internal detail.
/// </summary>
public sealed class ProblemExceptionHandler(IProblemDetailsService problems, ILogger<ProblemExceptionHandler> log) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        ProblemDetails details;
        if (exception is ImportProblem p)
        {
            details = new ProblemDetails { Status = StatusFor(p.Kind), Title = p.Title, Detail = p.Message };
            if (p.Mismatch is not null) details.Extensions["mismatch"] = p.Mismatch;
        }
        else if (exception is OperationCanceledException && http.RequestAborted.IsCancellationRequested)
        {
            return true; // the user cancelled the upload: nothing to report
        }
        else
        {
            log.LogError(exception, "Unhandled error on {Path}", http.Request.Path);
            details = new ProblemDetails { Status = 500, Title = "Something went wrong", Detail = "The error has been logged. Please try again or contact support." };
        }
        http.Response.StatusCode = details.Status!.Value;
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = http, ProblemDetails = details, Exception = exception });
    }

    private static int StatusFor(ProblemKind kind) => kind switch
    {
        ProblemKind.Invalid => StatusCodes.Status400BadRequest,
        ProblemKind.NotFound => StatusCodes.Status404NotFound,
        ProblemKind.Conflict => StatusCodes.Status409Conflict,
        ProblemKind.Forbidden => StatusCodes.Status403Forbidden,
        ProblemKind.Gone => StatusCodes.Status410Gone,
        ProblemKind.TooLarge => StatusCodes.Status413PayloadTooLarge,
        ProblemKind.Unprocessable => StatusCodes.Status422UnprocessableEntity,
        _ => StatusCodes.Status500InternalServerError,
    };
}

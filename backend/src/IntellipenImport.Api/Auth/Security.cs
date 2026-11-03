using System.Security.Claims;
using System.Text.Encodings.Web;
using IntellipenImport.Application.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace IntellipenImport.Api.Auth;

public static class Roles
{
    public const string Uploader = "Import.Uploader";
    public const string Committer = "Import.Committer";
    /// <summary>Read-only: may view and export committed batches, nothing else.</summary>
    public const string Viewer = "Import.Viewer";
    /// <summary>May ask to see one member's unmasked NI number, date of birth and salaries. Committers may too.</summary>
    public const string RevealSensitive = "Import.RevealSensitive";
}

public static class Policies
{
    /// <summary>Upload, review and discard.</summary>
    public const string Upload = "Import.Upload";
    /// <summary>Commit (save, produce the file, send the report).</summary>
    public const string Commit = "Import.Commit";
    /// <summary>View and export committed batches (read-only).</summary>
    public const string View = "Import.View";
    /// <summary>Show one member's unmasked values (audited).</summary>
    public const string Reveal = "Import.Reveal";
    /// <summary>Download the complete IntelliPen file. Today the same people as Commit; named separately so the two can diverge.</summary>
    public const string DownloadFile = "Import.DownloadFile";
}

/// <summary>The signed-in user, from the request's claims.</summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal User => accessor.HttpContext?.User ?? new ClaimsPrincipal();

    public string UserId =>
        User.FindFirstValue("preferred_username") ?? User.FindFirstValue(ClaimTypes.Name) ?? User.Identity?.Name ?? "unknown";

    public bool IsCommitter => User.IsInRole(Roles.Committer);

    public bool CanReveal => IsCommitter || User.IsInRole(Roles.RevealSensitive);
}

/// <summary>
/// DEVELOPMENT ONLY. Signs every request in as a configured user (or the X-Dev-User header) with configured roles,
/// so the API can be run and tested without Entra ID. X-Dev-Roles overrides the roles (comma-separated) for testing. Never registered outside the Development environment.
/// </summary>
public sealed class DevAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, IConfiguration config)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Development";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var user = Request.Headers["X-Dev-User"].FirstOrDefault() ?? config["DevAuth:User"] ?? "dev.user";
        var roles = (Request.Headers["X-Dev-Roles"].FirstOrDefault() ?? config["DevAuth:Roles"] ?? $"{Roles.Uploader},{Roles.Committer}")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var claims = new List<Claim> { new(ClaimTypes.Name, user) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}

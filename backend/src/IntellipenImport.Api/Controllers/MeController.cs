using System.Security.Claims;
using IntellipenImport.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IntellipenImport.Api.Controllers;

public sealed record MeCan(bool Upload, bool Commit, bool Reveal, bool DownloadFile, bool ViewCommitted);

public sealed record MeDto(string UserId, IReadOnlyList<string> Roles, MeCan Can);

/// <summary>
/// Who the signed-in user is and what they may do, so the UI can hide buttons that would only be refused. Each answer is the
/// result of the same named policy the matching endpoint uses (defined once, in Program.cs), so there is no second list of roles.
/// The API still enforces everything.
/// </summary>
[ApiController]
[Route("api/v1/me")]
public sealed class MeController(IAuthorizationService authorization, IntellipenImport.Application.Abstractions.ICurrentUser user) : ControllerBase
{
    [HttpGet]
    public async Task<MeDto> Get()
    {
        async Task<bool> Allowed(string policy) => (await authorization.AuthorizeAsync(User, policy)).Succeeded;
        return new MeDto(
            user.UserId,
            User.FindAll(ClaimTypes.Role).Select(c => c.Value).Order().ToList(),
            new MeCan(
                Upload: await Allowed(Policies.Upload),
                Commit: await Allowed(Policies.Commit),
                Reveal: await Allowed(Policies.Reveal),
                DownloadFile: await Allowed(Policies.DownloadFile),
                ViewCommitted: await Allowed(Policies.View)));
    }
}

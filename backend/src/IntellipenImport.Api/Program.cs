using System.Text.Json.Serialization;
using IntellipenImport.Api;
using IntellipenImport.Api.Auth;
using IntellipenImport.Application.Abstractions;
using IntellipenImport.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);
var limits = builder.Configuration.GetSection(ImportOptions.Section).Get<ImportOptions>() ?? new ImportOptions();

// Application + Infrastructure (use cases, config catalogs, temporary store, SQLite, delivery)
builder.Services.AddIntellipenImport(builder.Configuration);

// Web layer
builder.Services.AddControllers().AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ProblemExceptionHandler>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();

// Upload size: the transport limit sits just above the file limit, so the friendly "file too large" message is reached.
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = limits.MaxFileBytes + 1024 * 1024);
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = limits.MaxFileBytes + 1024 * 1024);

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddAuthentication(DevAuthenticationHandler.SchemeName)
        .AddScheme<AuthenticationSchemeOptions, DevAuthenticationHandler>(DevAuthenticationHandler.SchemeName, null);
}
else
{
    // Production sign-in uses the team's Entra ID: add Microsoft.AspNetCore.Authentication.JwtBearer and
    // AddAuthentication().AddJwtBearer(...) here (see README). Refuse to start without it rather than run open.
    throw new InvalidOperationException("Production authentication is not configured. Add Entra ID (JwtBearer) in Program.cs before deploying; see README.");
}

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.Upload, p => p.RequireRole(Roles.Uploader, Roles.Committer))
    .AddPolicy(Policies.Commit, p => p.RequireRole(Roles.Committer))
    .AddPolicy(Policies.View, p => p.RequireRole(Roles.Viewer, Roles.Uploader, Roles.Committer))
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

var app = builder.Build();

// Check every schema and the mapping, and create the database tables, before taking any request.
await app.Services.InitialiseIntellipenImportAsync();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

await app.RunAsync();

/// <summary>Visible to integration tests.</summary>
public partial class Program;

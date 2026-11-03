using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using IntellipenImport.Application.Abstractions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace IntellipenImport.Api.Tests;

/// <summary>Where ASP.NET would put a buffered request body. It does not exist, so any attempt to write a temporary file throws.</summary>
internal static class TempArea
{
    public static readonly string Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "intellipen-api-tests-" + Guid.NewGuid().ToString("N"));

    [ModuleInitializer]
    internal static void Init()
    {
        // Deliberately NOT created: if anything tries to buffer a request body to a temporary file, it fails loudly.
        Environment.SetEnvironmentVariable("ASPNETCORE_TEMP", Path); // ASP.NET's temp folder for buffered request bodies
    }
}

/// <summary>The real API (Development sign-in, in-memory store) with a capturing audit log and a throwaway output folder.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public CapturingAudit Audit { get; } = new();
    private readonly string _output = Path.Combine(Path.GetTempPath(), "intellipen-api-out-" + Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Infrastructure:Persistence", "InMemory");
        builder.UseSetting("Infrastructure:OutputPath", _output);
        builder.ConfigureTestServices(s => s.AddSingleton<IAuditLog>(Audit));
    }

    /// <summary>A client signed in (Development headers) as this user with these roles.</summary>
    public HttpClient As(string user, string roles)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Dev-User", user);
        client.DefaultRequestHeaders.Add("X-Dev-Roles", roles);
        return client;
    }

    public static HttpClient WithoutBody(HttpClient c) { c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*")); return c; }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_output)) Directory.Delete(_output, recursive: true);
    }
}

public sealed class CapturingAudit : IAuditLog
{
    private readonly List<string> _lines = [];
    public IReadOnlyList<string> Lines { get { lock (_lines) return [.. _lines]; } }
    public void Record(string action, string batchId, string user, string detail = "") { lock (_lines) _lines.Add($"{action}|{batchId}|{user}|{detail}"); }
    public void Failure(string action, string batchId, Exception error) { }
}

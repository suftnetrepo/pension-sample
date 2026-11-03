using System.Net.Mail;
using System.Text;
using IntellipenImport.Application.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IntellipenImport.Infrastructure.Delivery;

/// <summary>First implementation: writes the IntelliPen file to {OutputPath}/intellipen. Replace with IntelliPen's import folder or API.</summary>
public sealed class FolderFileTarget(IOptions<InfrastructureOptions> options, IHostEnvironment env) : IIntellipenFileTarget
{
    public async Task<string> WriteAsync(string fileName, byte[] content, CancellationToken ct)
    {
        var path = Path.Combine(OutputFolder(options.Value, env, "intellipen"), Path.GetFileName(fileName));
        await File.WriteAllBytesAsync(path, content, ct);
        return $"Written to the IntelliPen folder as {Path.GetFileName(path)}";
    }

    internal static string OutputFolder(InfrastructureOptions o, IHostEnvironment env, string sub)
    {
        var folder = Path.GetFullPath(Path.Combine(env.ContentRootPath, o.OutputPath, sub));
        Directory.CreateDirectory(folder);
        return folder;
    }
}

/// <summary>
/// First implementation: always saves the report to {OutputPath}/reports and, when Email:Enabled, also emails it
/// using .NET's built-in SMTP client. Replace with the team's email service if needed.
/// </summary>
public sealed class FolderAndEmailReportSender(IOptions<InfrastructureOptions> options, IHostEnvironment env) : IReportSender
{
    public async Task<string> SendAsync(string batchId, string subject, string reportHtml, CancellationToken ct)
    {
        var path = Path.Combine(FolderFileTarget.OutputFolder(options.Value, env, "reports"), batchId + "_reconciliation.html");
        await File.WriteAllTextAsync(path, reportHtml, ct);

        var email = options.Value.Email;
        if (!email.Enabled) return $"Saved to the reports folder as {Path.GetFileName(path)} (email not switched on yet)";
        if (email.To.Count == 0) throw new InvalidOperationException("Email is enabled but Infrastructure:Email:To has no recipients.");

        using var message = new MailMessage { From = new MailAddress(email.From), Subject = subject, Body = reportHtml, IsBodyHtml = true };
        foreach (var to in email.To) message.To.Add(to);
        message.Attachments.Add(new Attachment(new MemoryStream(Encoding.UTF8.GetBytes(reportHtml)), batchId + "_reconciliation.html", "text/html"));
        using var smtp = new SmtpClient(email.Host, email.Port) { EnableSsl = email.EnableSsl };
        if (!string.IsNullOrEmpty(email.UserName)) smtp.Credentials = new System.Net.NetworkCredential(email.UserName, email.Password);
        await smtp.SendMailAsync(message, ct);
        return "Emailed to " + string.Join(", ", email.To);
    }
}

/// <summary>Audit events to the application log: batch ID, user and counts only, never member data.</summary>
public sealed class LoggingAuditLog(ILogger<LoggingAuditLog> log) : IAuditLog
{
    public void Record(string action, string batchId, string user, string detail = "") =>
        log.LogInformation("Audit: {Action} {BatchId} by {User}. {Detail}", action, batchId, user, detail);

    public void Failure(string action, string batchId, Exception error) =>
        log.LogError(error, "Audit: {Action} {BatchId}", action, batchId);
}

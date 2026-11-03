namespace IntellipenImport.Infrastructure;

/// <summary>Configuration section "Infrastructure". Relative paths are from the API's content root.</summary>
public sealed class InfrastructureOptions
{
    public const string Section = "Infrastructure";

    /// <summary>Holds schemas/{source}/{version}.json and intellipen/*.json.</summary>
    public string ConfigPath { get; set; } = "../../config";
    /// <summary>File name in config/intellipen to use. Optional when there is only one.</summary>
    public string? IntellipenMapping { get; set; }

    /// <summary>"Sqlite" (default) or "InMemory" (demos and tests: committed batches are lost on restart).</summary>
    public string Persistence { get; set; } = "Sqlite";
    public string SqliteConnectionString { get; set; } = "Data Source=../../data/intellipen-import.db";

    /// <summary>Where the IntelliPen file and the report are written (first implementations, see specification section 9).</summary>
    public string OutputPath { get; set; } = "../../output";

    public EmailOptions Email { get; set; } = new();
}

public sealed class EmailOptions
{
    public bool Enabled { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public string From { get; set; } = string.Empty;
    public List<string> To { get; set; } = [];
}

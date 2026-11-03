using IntellipenImport.Application.Processing;
using IntellipenImport.Domain.Mapping;
using IntellipenImport.Domain.Processing;
using IntellipenImport.Domain.Schemas;
using IntellipenImport.Infrastructure.Catalogs;

namespace IntellipenImport.Tests;

/// <summary>The real config, samples and expected files, found from wherever the tests run.</summary>
internal static class TestFiles
{
    public static readonly ImportContext Context = new("IMP-20261102-TEST01", new DateOnly(2026, 11, 2));

    public static string Backend
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "config", "schemas"))) dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException("Could not find the backend folder (with config/schemas).");
        }
    }

    private static readonly Lazy<List<SourceSchema>> Schemas = new(() => FileSourceSchemaCatalog.LoadFolder(Path.Combine(Backend, "config", "schemas")));
    private static readonly Lazy<TargetMapping> TheMapping = new(() => FileTargetMappingCatalog.Load(Path.Combine(Backend, "config", "intellipen", "member-import-v0.1.json")));

    public static IReadOnlyList<SourceSchema> AllSchemas => Schemas.Value;
    public static SourceSchema Schema(string source, string version) => Schemas.Value.Single(s => s.Source == source && s.Version == version);
    public static SourceSchema Ashwood => Schema("ashwood-payroll", "v1");
    public static SourceSchema Legacy => Schema("legacy-admin", "v1");
    public static TargetMapping Mapping => TheMapping.Value;

    public static string Sample(string name) => File.ReadAllText(Path.Combine(Backend, "samples", name));
    public static string Expected(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Expected", name));

    public static ImportResult Run(string csv, SourceSchema? schema = null, ImportContext? context = null) =>
        ImportPipeline.Process(csv, schema ?? Ashwood, Mapping, context ?? Context);

    /// <summary>Ashwood payroll v1 headings plus the given data lines.</summary>
    public static string WithLines(params string[] lines) =>
        "Member ID,Name,Sex,Date of birth,NI number,Joined scheme,Left service,Status,Salary Y-3,Salary Y-2,Salary Y-1,Part-time fraction,Married\r\n"
        + string.Join("\r\n", lines) + "\r\n";

    public const string GoodLine = "DB001,David Okafor,M,01/02/1967,AB123456C,01/09/1995,31/08/2016,Deferred,46200,47800,50000,1,Y";
}

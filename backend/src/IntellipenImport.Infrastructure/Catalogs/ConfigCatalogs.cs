using System.Text.Json;
using System.Text.Json.Serialization;
using IntellipenImport.Application.Abstractions;
using IntellipenImport.Application.Processing;
using IntellipenImport.Domain.Mapping;
using IntellipenImport.Domain.Schemas;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace IntellipenImport.Infrastructure.Catalogs;

internal static class ConfigJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Folder(InfrastructureOptions o, IHostEnvironment env, string sub) =>
        Path.GetFullPath(Path.Combine(env.ContentRootPath, o.ConfigPath, sub));

    public static T Read<T>(string path) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? throw new ConfigException($"'{path}' is empty.");
}

public sealed class ConfigException(string message) : Exception(message);

/// <summary>
/// The agreed schemas: config/schemas/{source}/{version}.json. Loaded and checked once at start-up;
/// a broken file stops the API starting, naming the file and every problem in it.
/// </summary>
public sealed class FileSourceSchemaCatalog : ISourceSchemaCatalog
{
    private readonly List<SourceSchema> _schemas;

    public FileSourceSchemaCatalog(IOptions<InfrastructureOptions> options, IHostEnvironment env)
    {
        _schemas = LoadFolder(ConfigJson.Folder(options.Value, env, "schemas"));
    }

    /// <summary>Loads and checks every schema under a folder. Also used by the tests.</summary>
    public static List<SourceSchema> LoadFolder(string root)
    {
        if (!Directory.Exists(root)) throw new ConfigException($"Schemas folder '{root}' does not exist.");
        var schemas = new List<SourceSchema>();
        foreach (var file in Directory.GetFiles(root, "*.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var schema = ConfigJson.Read<SourceSchema>(file);
            schema.Source = Path.GetFileName(Path.GetDirectoryName(file)!);
            schema.Version = Path.GetFileNameWithoutExtension(file);
            var problems = ConfigRules.CheckSchema(schema);
            if (problems.Count > 0) throw new ConfigException($"Schema '{schema.Source}/{schema.Version}' is not valid: {string.Join("; ", problems)}.");
            schemas.Add(schema);
        }
        if (schemas.Count == 0) throw new ConfigException($"No schema files found under '{root}'.");
        return schemas;
    }

    public IReadOnlyList<SourceSchema> All => _schemas;

    public SourceSchema? Find(string source, string version) =>
        _schemas.FirstOrDefault(s => string.Equals(s.Source, source, StringComparison.OrdinalIgnoreCase)
                                  && string.Equals(s.Version, version, StringComparison.OrdinalIgnoreCase));
}

/// <summary>The single IntelliPen mapping: config/intellipen/*.json (or the one named in Infrastructure:IntellipenMapping).</summary>
public sealed class FileTargetMappingCatalog : ITargetMappingCatalog
{
    public FileTargetMappingCatalog(IOptions<InfrastructureOptions> options, IHostEnvironment env)
    {
        var folder = ConfigJson.Folder(options.Value, env, "intellipen");
        var files = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.json") : [];
        var chosen = options.Value.IntellipenMapping is { Length: > 0 } name
            ? files.FirstOrDefault(f => string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase))
              ?? throw new ConfigException($"IntelliPen mapping '{name}' was not found in '{folder}'.")
            : files.Length == 1 ? files[0]
            : throw new ConfigException(files.Length == 0
                ? $"No IntelliPen mapping found in '{folder}'."
                : $"'{folder}' has {files.Length} mappings: set Infrastructure:IntellipenMapping to the one to use.");

        var mapping = Load(chosen);
        Current = mapping;
    }

    public TargetMapping Current { get; }

    /// <summary>Loads and checks one mapping file. Also used by the tests.</summary>
    public static TargetMapping Load(string path)
    {
        var mapping = ConfigJson.Read<TargetMapping>(path);
        mapping.Key = Path.GetFileNameWithoutExtension(path);
        var problems = ConfigRules.CheckMapping(mapping);
        if (problems.Count > 0) throw new ConfigException($"IntelliPen mapping '{mapping.Key}' is not valid: {string.Join("; ", problems)}.");
        return mapping;
    }
}

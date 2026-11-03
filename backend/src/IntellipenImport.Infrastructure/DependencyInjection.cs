using IntellipenImport.Application.Abstractions;
using IntellipenImport.Application.Imports;
using IntellipenImport.Infrastructure.Catalogs;
using IntellipenImport.Infrastructure.Delivery;
using IntellipenImport.Infrastructure.Persistence;
using IntellipenImport.Infrastructure.Storage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IntellipenImport.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers the use cases and every adapter. The Api only adds the web layer and the current user.</summary>
    public static IServiceCollection AddIntellipenImport(this IServiceCollection services, IConfiguration config)
    {
        var importOptions = config.GetSection(ImportOptions.Section).Get<ImportOptions>() ?? new ImportOptions();
        services.AddSingleton(importOptions);
        services.Configure<InfrastructureOptions>(config.GetSection(InfrastructureOptions.Section));
        services.AddSingleton(TimeProvider.System);

        // Application
        services.AddScoped<BatchAccess>();
        services.AddScoped<ImportCommands>();
        services.AddScoped<ImportQueries>();
        services.AddScoped<CommittedQueries>();

        // Config: loaded once; a broken file stops start-up (see InitialiseIntellipenImportAsync).
        services.AddSingleton<ISourceSchemaCatalog, FileSourceSchemaCatalog>();
        services.AddSingleton<ITargetMappingCatalog, FileTargetMappingCatalog>();

        // Temporary store: in-memory cache here. With more than one server replace this line with
        // AddStackExchangeRedisCache(...) or AddDistributedSqlServerCache(...), and persist Data Protection keys.
        services.AddDistributedMemoryCache();
        services.AddDataProtection().SetApplicationName("IntellipenImport");
        services.AddSingleton<IHeldBatchStore, DistributedCacheHeldBatchStore>();

        // Committed batches
        var persistence = config[$"{InfrastructureOptions.Section}:Persistence"] ?? "Sqlite";
        if (persistence.Equals("InMemory", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<InMemoryBatchRepository>();
            services.AddSingleton<IBatchRepository>(sp => sp.GetRequiredService<InMemoryBatchRepository>());
            services.AddSingleton<ICommittedBatchReader>(sp => sp.GetRequiredService<InMemoryBatchRepository>());
        }
        else
        {
            services.AddSingleton<IDbConnectionFactory, SqliteConnectionFactory>();
            services.AddSingleton<SqlBatchRepository>();
            services.AddSingleton<IBatchRepository>(sp => sp.GetRequiredService<SqlBatchRepository>());
            services.AddSingleton<ICommittedBatchReader>(sp => sp.GetRequiredService<SqlBatchRepository>());
        }

        // Delivery (first implementations: folders, optional email)
        services.AddSingleton<IIntellipenFileTarget, FolderFileTarget>();
        services.AddSingleton<IReportSender, FolderAndEmailReportSender>();
        services.AddSingleton<IAuditLog, LoggingAuditLog>();
        return services;
    }

    /// <summary>Loads and checks every schema and the mapping, and creates the database tables. Call once at start-up.</summary>
    public static async Task InitialiseIntellipenImportAsync(this IServiceProvider services)
    {
        _ = services.GetRequiredService<ISourceSchemaCatalog>();
        _ = services.GetRequiredService<ITargetMappingCatalog>();
        if (services.GetService<SqlBatchRepository>() is { } sql) await sql.InitialiseAsync();
    }
}

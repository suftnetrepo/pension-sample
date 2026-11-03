using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace IntellipenImport.Infrastructure.Persistence;

/// <summary>SQLite connections. The only file that references the SQLite package.</summary>
public sealed class SqliteConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;

    public SqliteConnectionFactory(IOptions<InfrastructureOptions> options, IHostEnvironment env)
    {
        var builder = new SqliteConnectionStringBuilder(options.Value.SqliteConnectionString);
        if (!Path.IsPathRooted(builder.DataSource) && builder.DataSource != ":memory:")
            builder.DataSource = Path.GetFullPath(Path.Combine(env.ContentRootPath, builder.DataSource));
        Directory.CreateDirectory(Path.GetDirectoryName(builder.DataSource)!);
        builder.ForeignKeys = true;
        _connectionString = builder.ToString();
    }

    public DbConnection Create() => new SqliteConnection(_connectionString);
}

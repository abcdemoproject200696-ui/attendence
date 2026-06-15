using Attendance.Infrastructure;
using Npgsql;
using Xunit;

namespace Attendance.Tests;

/// <summary>
/// Verifies provider selection and Render DATABASE_URL parsing in <see cref="DbConnectionHelper"/>.
/// </summary>
public class DbConnectionHelperTests
{
    [Fact]
    public void Resolve_NoEnv_FallsBackToSqlite()
    {
        var cfg = DbConnectionHelper.Resolve(databaseUrl: null, connectionString: null);

        Assert.Equal(DbProvider.Sqlite, cfg.Provider);
        Assert.Equal("Data Source=attendance.db", cfg.ConnectionString);
    }

    [Fact]
    public void Resolve_DatabaseUrl_SelectsPostgres()
    {
        var cfg = DbConnectionHelper.Resolve(
            databaseUrl: "postgres://user:pass@db.example.com:5432/mydb",
            connectionString: null);

        Assert.Equal(DbProvider.PostgreSql, cfg.Provider);
        var b = new NpgsqlConnectionStringBuilder(cfg.ConnectionString);
        Assert.Equal("db.example.com", b.Host);
        Assert.Equal(5432, b.Port);
        Assert.Equal("user", b.Username);
        Assert.Equal("pass", b.Password);
        Assert.Equal("mydb", b.Database);
        Assert.Equal(SslMode.Require, b.SslMode);
    }

    [Theory]
    [InlineData("postgres://u:p@host:6543/d")]
    [InlineData("postgresql://u:p@host:6543/d")]
    public void BuildNpgsql_HonorsScheme_AndCustomPort(string url)
    {
        var conn = DbConnectionHelper.BuildNpgsqlConnectionString(url);
        var b = new NpgsqlConnectionStringBuilder(conn);

        Assert.Equal("host", b.Host);
        Assert.Equal(6543, b.Port);
        Assert.Equal("d", b.Database);
    }

    [Fact]
    public void BuildNpgsql_DefaultsPortTo5432_WhenMissing()
    {
        var conn = DbConnectionHelper.BuildNpgsqlConnectionString("postgres://u:p@host/d");
        var b = new NpgsqlConnectionStringBuilder(conn);

        Assert.Equal(5432, b.Port);
    }

    [Fact]
    public void BuildNpgsql_DecodesUrlEncodedPassword()
    {
        // Password "p@ss w/rd" URL-encoded.
        var conn = DbConnectionHelper.BuildNpgsqlConnectionString(
            "postgres://user:p%40ss%20w%2Frd@host:5432/mydb");
        var b = new NpgsqlConnectionStringBuilder(conn);

        Assert.Equal("p@ss w/rd", b.Password);
        Assert.Equal("user", b.Username);
    }

    [Fact]
    public void Resolve_PostgresUrlInConnectionString_IsParsed()
    {
        var cfg = DbConnectionHelper.Resolve(
            databaseUrl: null,
            connectionString: "postgresql://u:p@host:5432/d");

        Assert.Equal(DbProvider.PostgreSql, cfg.Provider);
        var b = new NpgsqlConnectionStringBuilder(cfg.ConnectionString);
        Assert.Equal("host", b.Host);
    }

    [Fact]
    public void Resolve_KeyValueConnectionString_PassedThroughAsPostgres()
    {
        const string raw = "Host=h;Port=5432;Username=u;Password=p;Database=d";
        var cfg = DbConnectionHelper.Resolve(databaseUrl: null, connectionString: raw);

        Assert.Equal(DbProvider.PostgreSql, cfg.Provider);
        Assert.Equal(raw, cfg.ConnectionString);
    }
}

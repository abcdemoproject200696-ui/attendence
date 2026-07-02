namespace Attendance.Infrastructure;

/// <summary>
/// Resolves the database provider + connection string at startup.
///
/// Selection rules:
///   - If env var <c>DATABASE_URL</c> is set (Render Postgres gives a URL like
///     <c>postgres://user:pass@host:port/dbname</c>) OR
///     <c>ConnectionStrings__DefaultConnection</c> is set → PostgreSQL (Npgsql).
///   - Otherwise → SQLite <c>Data Source=attendance.db</c> (no-setup local fallback).
/// </summary>
public enum DbProvider
{
    Sqlite,
    PostgreSql
}

public static class DbConnectionHelper
{
    public sealed record DbConfig(DbProvider Provider, string ConnectionString);

    /// <summary>
    /// Decide which provider to use and produce a ready-to-use connection string.
    /// </summary>
    /// <param name="databaseUrl">Value of env var DATABASE_URL (Render style URL), or null.</param>
    /// <param name="connectionString">
    /// Value of ConnectionStrings__DefaultConnection / GetConnectionString("DefaultConnection"), or null.
    /// May be either a Render-style URL or an already-formed Npgsql key/value string.
    /// </param>
    public static DbConfig Resolve(string? databaseUrl, string? connectionString)
    {
        // 1) DATABASE_URL (Render Postgres) wins.
        if (!string.IsNullOrWhiteSpace(databaseUrl))
        {
            return new DbConfig(DbProvider.PostgreSql, BuildNpgsqlConnectionString(databaseUrl));
        }

        // 2) Explicit connection string. Could be a postgres:// URL or a key/value string.
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            var npgsql = IsPostgresUrl(connectionString)
                ? BuildNpgsqlConnectionString(connectionString)
                : connectionString;
            return new DbConfig(DbProvider.PostgreSql, npgsql);
        }

        // 3) Local fallback: SQLite, no install required.
        return new DbConfig(DbProvider.Sqlite, "Data Source=attendance.db");
    }

    private static bool IsPostgresUrl(string value) =>
        value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Parse a Render-style <c>postgres://user:pass@host:port/dbname</c> (or <c>postgresql://</c>)
    /// URL into an Npgsql key/value connection string. Handles URL-encoded passwords/usernames,
    /// a missing port (defaults to 5432), and an optional query string. Forces SSL for Render.
    /// </summary>
    public static string BuildNpgsqlConnectionString(string databaseUrl)
    {
        if (string.IsNullOrWhiteSpace(databaseUrl))
            throw new ArgumentException("DATABASE_URL is empty.", nameof(databaseUrl));

        var uri = new Uri(databaseUrl);

        var userInfo = uri.UserInfo.Split(':', 2);
        var username = Uri.UnescapeDataString(userInfo[0]);
        var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty;

        var host = uri.Host;
        var port = uri.Port > 0 ? uri.Port : 5432;

        // AbsolutePath is "/dbname"; trim the leading slash.
        var database = uri.AbsolutePath.TrimStart('/');
        if (string.IsNullOrEmpty(database))
            throw new ArgumentException("DATABASE_URL has no database name.", nameof(databaseUrl));

        // A local Postgres (e.g. a dev machine holding a copy of prod data) usually has SSL
        // turned off, so forcing Require there fails to connect. Use Prefer for localhost —
        // it uses TLS when the server offers it and falls back to plaintext otherwise. Remote
        // hosts (Render) still Require SSL.
        var isLocal = host is "localhost" or "127.0.0.1" or "::1";

        var builder = new Npgsql.NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = port,
            Username = username,
            Password = password,
            Database = database,
            // Render-managed Postgres requires SSL. In Npgsql 10, SslMode.Require connects
            // over TLS without validating the server cert chain (suitable for Render's
            // self-managed certs), so no separate TrustServerCertificate flag is needed.
            SslMode = isLocal ? Npgsql.SslMode.Prefer : Npgsql.SslMode.Require
        };

        return builder.ConnectionString;
    }
}

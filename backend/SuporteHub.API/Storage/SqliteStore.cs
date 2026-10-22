using Microsoft.Data.Sqlite;
using SuporteHub.API.Models;
using System.Text;
using System.Text.Json;

namespace SuporteHub.API.Storage;

/// <summary>Persistent local storage. The native SQLite library ships with the NuGet package.</summary>
public sealed class SqliteStore : IAppStore
{
    readonly string databasePath;
    readonly string connectionString;
    public string ProviderName => "SQLite";
    public bool IsLocal { get; }

    public SqliteStore(IConfiguration config, IWebHostEnvironment environment)
    {
        databasePath = Path.GetFullPath(config["Storage:DatabasePath"] ?? ".local/suportehub.db");
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath, Mode = SqliteOpenMode.ReadWriteCreate, DefaultTimeout = 30
        }.ToString();
        // First-account support provisioning is for explicit development mode only.
        IsLocal = environment.IsDevelopment();
    }

    async Task<SqliteConnection> Open()
    {
        var connection = new SqliteConnection(connectionString);
        try { await connection.OpenAsync(); return connection; }
        catch { await connection.DisposeAsync(); throw; }
    }

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        await using var connection = await Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS Records (
                PK TEXT NOT NULL,
                SK TEXT NOT NULL,
                Data TEXT NOT NULL,
                Version INTEGER NOT NULL,
                PRIMARY KEY (PK, SK)
            ) WITHOUT ROWID;
            CREATE TABLE IF NOT EXISTS Counters (
                Name TEXT PRIMARY KEY NOT NULL,
                Value INTEGER NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync();
    }

    public async Task<bool> NeedsInitialAgent()
    {
        if (!IsLocal) return false;
        await using var connection = await Open();
        return await EmptyForSetup(connection, null);
    }

    static async Task<bool> EmptyForSetup(SqliteConnection connection, SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT NOT EXISTS(SELECT 1 FROM Records WHERE PK = 'USERS')
               AND NOT EXISTS(SELECT 1 FROM Records WHERE PK = 'META' AND SK = 'INITIAL_AGENT');
            """;
        return Convert.ToInt64(await command.ExecuteScalarAsync()) == 1;
    }

    public async Task Register(Conta account)
    {
        await using var connection = await Open();
        // Acquire the write lock before deciding who gets the initial support role.
        using var transaction = connection.BeginTransaction(deferred: false);
        account.Perfil = "user";
        if (IsLocal && await EmptyForSetup(connection, transaction))
        {
            account.Perfil = "admin";
            await Insert(connection, transaction, "META", "INITIAL_AGENT", "{}");
        }
        await Insert(connection, transaction, "USERS", account.Email, Serialize(account));
        await transaction.CommitAsync();
    }

    static string Serialize<T>(T value)
    {
        var json = JsonSerializer.Serialize(value);
        if (Encoding.UTF8.GetByteCount(json) > 300_000)
            throw new InvalidOperationException("Este chamado atingiu o limite de histórico e anexos.");
        return json;
    }

    static async Task Insert(SqliteConnection connection, SqliteTransaction? transaction, string pk, string sk, string json)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO Records (PK, SK, Data, Version) VALUES ($pk, $sk, $data, 1);";
        command.Parameters.AddWithValue("$pk", pk);
        command.Parameters.AddWithValue("$sk", sk);
        command.Parameters.AddWithValue("$data", json);
        try { await command.ExecuteNonQueryAsync(); }
        catch (SqliteException e) when (e.SqliteErrorCode == 19)
        {
            throw new StorageConflictException("O registro já existe.", e);
        }
    }

    public async Task<(T Value, int Version)?> Get<T>(string pk, string sk)
    {
        await using var connection = await Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Data, Version FROM Records WHERE PK = $pk AND SK = $sk;";
        command.Parameters.AddWithValue("$pk", pk); command.Parameters.AddWithValue("$sk", sk);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync()
            ? (JsonSerializer.Deserialize<T>(reader.GetString(0))!, reader.GetInt32(1)) : null;
    }

    public async Task<List<T>> Query<T>(string pk)
    {
        await using var connection = await Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Data FROM Records WHERE PK = $pk ORDER BY SK;";
        command.Parameters.AddWithValue("$pk", pk);
        await using var reader = await command.ExecuteReaderAsync();
        List<T> values = [];
        while (await reader.ReadAsync()) values.Add(JsonSerializer.Deserialize<T>(reader.GetString(0))!);
        return values;
    }

    public async Task Put<T>(string pk, string sk, T value, int? previousVersion = null)
    {
        var json = Serialize(value);
        await using var connection = await Open();
        if (previousVersion is null)
        {
            await Insert(connection, null, pk, sk, json);
            return;
        }
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE Records SET Data = $data, Version = Version + 1
            WHERE PK = $pk AND SK = $sk AND Version = $version;
            """;
        command.Parameters.AddWithValue("$pk", pk); command.Parameters.AddWithValue("$sk", sk);
        command.Parameters.AddWithValue("$data", json); command.Parameters.AddWithValue("$version", previousVersion.Value);
        if (await command.ExecuteNonQueryAsync() != 1)
            throw new StorageConflictException("O registro foi alterado durante a operação.");
    }

    public async Task<int> NextId()
    {
        await using var connection = await Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Counters (Name, Value) VALUES ('TICKETS', 1)
            ON CONFLICT (Name) DO UPDATE SET Value = Value + 1
            RETURNING Value;
            """;
        return checked((int)Convert.ToInt64(await command.ExecuteScalarAsync()));
    }
}

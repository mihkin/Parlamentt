using System.Data;
using Microsoft.Data.SqlClient;
using ParlamB.Api.Contracts;

namespace ParlamB.Api.Services;

public sealed class LobbyRegistryService
{
    private static readonly TimeSpan EntryLifetime = TimeSpan.FromSeconds(35);
    private readonly IConfiguration configuration;

    public LobbyRegistryService(IConfiguration configuration)
    {
        this.configuration = configuration;
    }

    private string ConnectionString =>
        configuration.GetConnectionString("ParlamBDatabase")
        ?? throw new InvalidOperationException("Connection string 'ParlamBDatabase' is missing.");

    public async Task InitializeAsync()
    {
        await using SqlConnection connection = new(ConnectionString);
        await connection.OpenAsync();
        await EnsureSchemaAsync(connection);
    }

    public async Task<IReadOnlyList<PublicLobbyDto>> GetActiveLobbiesAsync()
    {
        await using SqlConnection connection = new(ConnectionString);
        await connection.OpenAsync();

        await ExecuteAsync(connection, @"
DELETE FROM dbo.public_lobbies
WHERE updated_at_utc < @cutoffUtc;",
            new SqlParameter("@cutoffUtc", SqlDbType.DateTime2) { Value = DateTime.UtcNow - EntryLifetime });

        await using SqlCommand command = new(@"
SELECT room_code, host_nickname, host_address, player_count, max_players, started
FROM dbo.public_lobbies
WHERE started = 0
ORDER BY player_count DESC, host_nickname ASC;", connection);

        await using SqlDataReader reader = await command.ExecuteReaderAsync();
        List<PublicLobbyDto> lobbies = [];
        while (await reader.ReadAsync())
        {
            lobbies.Add(new PublicLobbyDto
            {
                RoomCode = reader.GetString(0),
                HostNickname = reader.GetString(1),
                HostAddress = reader.GetString(2),
                PlayerCount = reader.GetInt32(3),
                MaxPlayers = reader.GetInt32(4),
                Started = reader.GetBoolean(5)
            });
        }

        return lobbies;
    }

    public async Task UpsertAsync(PublicLobbyDto lobby, string remoteIpAddress)
    {
        if (lobby == null || string.IsNullOrWhiteSpace(lobby.RoomCode))
            return;

        string normalizedHostAddress = NormalizeHostAddress(lobby.HostAddress, remoteIpAddress);
        if (string.IsNullOrWhiteSpace(normalizedHostAddress))
            return;

        await using SqlConnection connection = new(ConnectionString);
        await connection.OpenAsync();

        await ExecuteAsync(connection, @"
MERGE dbo.public_lobbies AS target
USING (
    SELECT
        @roomCode AS room_code,
        @hostAddress AS host_address
) AS source
ON target.room_code = source.room_code AND target.host_address = source.host_address
WHEN MATCHED THEN
    UPDATE SET
        host_nickname = @hostNickname,
        player_count = @playerCount,
        max_players = @maxPlayers,
        started = @started,
        updated_at_utc = SYSUTCDATETIME()
WHEN NOT MATCHED THEN
    INSERT (room_code, host_nickname, host_address, player_count, max_players, started, updated_at_utc)
    VALUES (@roomCode, @hostNickname, @hostAddress, @playerCount, @maxPlayers, @started, SYSUTCDATETIME());",
            new SqlParameter("@roomCode", SqlDbType.NVarChar, 16) { Value = lobby.RoomCode.Trim().ToUpperInvariant() },
            new SqlParameter("@hostNickname", SqlDbType.NVarChar, 64) { Value = string.IsNullOrWhiteSpace(lobby.HostNickname) ? "Host" : lobby.HostNickname.Trim() },
            new SqlParameter("@hostAddress", SqlDbType.NVarChar, 128) { Value = normalizedHostAddress },
            new SqlParameter("@playerCount", SqlDbType.Int) { Value = Math.Max(0, lobby.PlayerCount) },
            new SqlParameter("@maxPlayers", SqlDbType.Int) { Value = Math.Clamp(lobby.MaxPlayers, 2, 8) },
            new SqlParameter("@started", SqlDbType.Bit) { Value = lobby.Started });
    }

    public async Task RemoveAsync(string roomCode, string hostAddress, string remoteIpAddress)
    {
        if (string.IsNullOrWhiteSpace(roomCode))
            return;

        string normalizedHostAddress = NormalizeHostAddress(hostAddress, remoteIpAddress);
        if (string.IsNullOrWhiteSpace(normalizedHostAddress))
            return;

        await using SqlConnection connection = new(ConnectionString);
        await connection.OpenAsync();

        await ExecuteAsync(connection, @"
DELETE FROM dbo.public_lobbies
WHERE room_code = @roomCode AND host_address = @hostAddress;",
            new SqlParameter("@roomCode", SqlDbType.NVarChar, 16) { Value = roomCode.Trim().ToUpperInvariant() },
            new SqlParameter("@hostAddress", SqlDbType.NVarChar, 128) { Value = normalizedHostAddress });
    }

    private static string NormalizeHostAddress(string hostAddress, string remoteIpAddress)
    {
        string port = "7777";
        if (!string.IsNullOrWhiteSpace(hostAddress))
        {
            int separatorIndex = hostAddress.LastIndexOf(':');
            if (separatorIndex > 0 && separatorIndex < hostAddress.Length - 1)
            {
                string parsedPort = hostAddress[(separatorIndex + 1)..].Trim();
                if (int.TryParse(parsedPort, out int portNumber) && portNumber > 0 && portNumber <= 65535)
                    port = parsedPort;
            }
        }

        string normalizedIp = string.IsNullOrWhiteSpace(remoteIpAddress)
            ? string.Empty
            : remoteIpAddress.Trim();

        if (normalizedIp.StartsWith("::ffff:", StringComparison.OrdinalIgnoreCase))
            normalizedIp = normalizedIp["::ffff:".Length..];

        if (string.IsNullOrWhiteSpace(normalizedIp))
            return string.Empty;

        return $"{normalizedIp}:{port}";
    }

    private static async Task EnsureSchemaAsync(SqlConnection connection)
    {
        const string sql = """
IF OBJECT_ID(N'dbo.public_lobbies', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.public_lobbies (
        room_code NVARCHAR(16) NOT NULL,
        host_nickname NVARCHAR(64) NOT NULL,
        host_address NVARCHAR(128) NOT NULL,
        player_count INT NOT NULL CONSTRAINT DF_public_lobbies_player_count DEFAULT 0,
        max_players INT NOT NULL CONSTRAINT DF_public_lobbies_max_players DEFAULT 4,
        started BIT NOT NULL CONSTRAINT DF_public_lobbies_started DEFAULT 0,
        updated_at_utc DATETIME2 NOT NULL CONSTRAINT DF_public_lobbies_updated_at_utc DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_public_lobbies PRIMARY KEY (room_code, host_address)
    );
END;
""";

        await ExecuteAsync(connection, sql);
    }

    private static async Task<int> ExecuteAsync(SqlConnection connection, string sql, params SqlParameter[] parameters)
    {
        await using SqlCommand command = new(sql, connection);
        command.Parameters.AddRange(parameters);
        return await command.ExecuteNonQueryAsync();
    }
}

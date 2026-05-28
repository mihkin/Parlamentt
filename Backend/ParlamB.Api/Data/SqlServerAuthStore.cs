using System.Data;
using Microsoft.Data.SqlClient;
using ParlamB.Api.Contracts;
using ParlamB.Api.Services;

namespace ParlamB.Api.Data;

public sealed class SqlServerAuthStore
{
    private readonly IConfiguration configuration;
    private readonly PasswordHashService passwordHashService;
    private readonly SqlServerProfileStore profileStore;

    public SqlServerAuthStore(
        IConfiguration configuration,
        PasswordHashService passwordHashService,
        SqlServerProfileStore profileStore)
    {
        this.configuration = configuration;
        this.passwordHashService = passwordHashService;
        this.profileStore = profileStore;
    }

    private string ConnectionString =>
        configuration.GetConnectionString("ParlamBDatabase")
        ?? throw new InvalidOperationException("Connection string 'ParlamBDatabase' is missing.");

    public async Task InitializeAsync()
    {
        await using SqlConnection connection = new(ConnectionString);
        await connection.OpenAsync();
        await EnsureAuthSchemaAsync(connection);
    }

    public async Task<AuthenticatedUser?> RegisterAsync(RegisterRequest request)
    {
        string login = NormalizeLogin(request.Login);
        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(request.Password))
            return null;

        await using SqlConnection connection = new(ConnectionString);
        await connection.OpenAsync();
        await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        bool loginExists = await ExistsAsync(connection, transaction,
            "SELECT 1 FROM dbo.auth_users WHERE login = @login;",
            new SqlParameter("@login", SqlDbType.NVarChar, 64) { Value = login });

        if (loginExists)
            return null;

        Guid playerId = Guid.NewGuid();
        string nickname = string.IsNullOrWhiteSpace(request.Nickname) ? login : request.Nickname.Trim();
        bool nicknameExists = await ExistsAsync(connection, transaction,
            "SELECT 1 FROM dbo.players WHERE nickname = @nickname;",
            new SqlParameter("@nickname", SqlDbType.NVarChar, 64) { Value = nickname });

        if (nicknameExists)
            return null;

        (byte[] hash, byte[] salt) = passwordHashService.HashPassword(request.Password);

        await ExecuteAsync(connection, transaction, @"
INSERT INTO dbo.players (id, nickname, rank_name, avatar, level, experience, coins)
VALUES (@playerId, @nickname, @rankName, @avatar, @level, @experience, @coins);",
            new SqlParameter("@playerId", SqlDbType.UniqueIdentifier) { Value = playerId },
            new SqlParameter("@nickname", SqlDbType.NVarChar, 64) { Value = nickname },
            new SqlParameter("@rankName", SqlDbType.NVarChar, 32) { Value = "Bronze" },
            new SqlParameter("@avatar", SqlDbType.NVarChar, 64) { Value = "default" },
            new SqlParameter("@level", SqlDbType.Int) { Value = 1 },
            new SqlParameter("@experience", SqlDbType.Int) { Value = 0 },
            new SqlParameter("@coins", SqlDbType.Int) { Value = 500 });

        await ExecuteAsync(connection, transaction, @"
INSERT INTO dbo.player_statistics (player_id, total_matches, wins, losses, online_matches, offline_matches, cards_played, turns_played)
VALUES (@playerId, 0, 0, 0, 0, 0, 0, 0);",
            new SqlParameter("@playerId", SqlDbType.UniqueIdentifier) { Value = playerId });

        for (int cardId = 1; cardId <= 8; cardId++)
        {
            await ExecuteAsync(connection, transaction, @"
INSERT INTO dbo.player_owned_cards (player_id, card_id, source)
VALUES (@playerId, @cardId, @source);",
                new SqlParameter("@playerId", SqlDbType.UniqueIdentifier) { Value = playerId },
                new SqlParameter("@cardId", SqlDbType.Int) { Value = cardId },
                new SqlParameter("@source", SqlDbType.NVarChar, 32) { Value = "register" });

            await ExecuteAsync(connection, transaction, @"
INSERT INTO dbo.player_selected_deck (player_id, slot_index, card_id)
VALUES (@playerId, @slotIndex, @cardId);",
                new SqlParameter("@playerId", SqlDbType.UniqueIdentifier) { Value = playerId },
                new SqlParameter("@slotIndex", SqlDbType.Int) { Value = cardId - 1 },
                new SqlParameter("@cardId", SqlDbType.Int) { Value = cardId });
        }

        await ExecuteAsync(connection, transaction, @"
INSERT INTO dbo.auth_users (player_id, login, password_hash, password_salt)
VALUES (@playerId, @login, @passwordHash, @passwordSalt);",
            new SqlParameter("@playerId", SqlDbType.UniqueIdentifier) { Value = playerId },
            new SqlParameter("@login", SqlDbType.NVarChar, 64) { Value = login },
            new SqlParameter("@passwordHash", SqlDbType.VarBinary, hash.Length) { Value = hash },
            new SqlParameter("@passwordSalt", SqlDbType.VarBinary, salt.Length) { Value = salt });

        await transaction.CommitAsync();
        PlayerProfileDto profile = (await profileStore.GetByIdAsync(playerId))!;
        return new AuthenticatedUser(login, profile);
    }

    public async Task<AuthenticatedUser?> LoginAsync(LoginRequest request)
    {
        string login = NormalizeLogin(request.Login);
        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(request.Password))
            return null;

        await using SqlConnection connection = new(ConnectionString);
        await connection.OpenAsync();

        await using SqlCommand command = new(@"
SELECT player_id, login, password_hash, password_salt
FROM dbo.auth_users
WHERE login = @login;", connection);
        command.Parameters.Add(new SqlParameter("@login", SqlDbType.NVarChar, 64) { Value = login });

        await using SqlDataReader reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        Guid playerId = reader.GetGuid(0);
        string actualLogin = reader.GetString(1);
        byte[] passwordHash = (byte[])reader["password_hash"];
        byte[] passwordSalt = (byte[])reader["password_salt"];
        await reader.CloseAsync();

        bool validPassword = passwordHashService.VerifyPassword(request.Password, passwordHash, passwordSalt);
        if (!validPassword)
            return null;

        PlayerProfileDto? profile = await profileStore.GetByIdAsync(playerId);
        return profile is null ? null : new AuthenticatedUser(actualLogin, profile);
    }

    private static string NormalizeLogin(string login) => string.IsNullOrWhiteSpace(login) ? string.Empty : login.Trim();

    private static async Task EnsureAuthSchemaAsync(SqlConnection connection)
    {
        const string sql = """
IF OBJECT_ID(N'dbo.auth_users', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.auth_users (
        player_id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        login NVARCHAR(64) NOT NULL UNIQUE,
        password_hash VARBINARY(64) NOT NULL,
        password_salt VARBINARY(32) NOT NULL,
        created_at DATETIME2 NOT NULL CONSTRAINT DF_auth_users_created_at DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_auth_users_players FOREIGN KEY (player_id) REFERENCES dbo.players(id) ON DELETE CASCADE
    );
END;
""";

        await using SqlCommand command = new(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<bool> ExistsAsync(SqlConnection connection, SqlTransaction transaction, string sql, params SqlParameter[] parameters)
    {
        await using SqlCommand command = new(sql, connection, transaction);
        command.Parameters.AddRange(parameters);
        object? result = await command.ExecuteScalarAsync();
        return result is not null && result != DBNull.Value;
    }

    private static async Task<int> ExecuteAsync(SqlConnection connection, SqlTransaction transaction, string sql, params SqlParameter[] parameters)
    {
        await using SqlCommand command = new(sql, connection, transaction);
        command.Parameters.AddRange(parameters);
        return await command.ExecuteNonQueryAsync();
    }
}

public sealed record AuthenticatedUser(string Login, PlayerProfileDto Profile);

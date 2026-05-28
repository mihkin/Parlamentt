using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.SqlClient;
using ParlamB.Api.Contracts;

namespace ParlamB.Api.Data;

public sealed class SqlServerProfileStore
{
    private const int MinimumDeckCards = 3;
    private const int MaximumDeckCards = 20;

    private readonly IConfiguration configuration;
    private readonly IWebHostEnvironment environment;

    public SqlServerProfileStore(IConfiguration configuration, IWebHostEnvironment environment)
    {
        this.configuration = configuration;
        this.environment = environment;
    }

    private string ConnectionString =>
        configuration.GetConnectionString("ParlamBDatabase")
        ?? throw new InvalidOperationException("Connection string 'ParlamBDatabase' is missing.");

    public async Task InitializeAsync()
    {
        await using SqlConnection connection = new(ConnectionString);
        await connection.OpenAsync();
        await EnsureSchemaAsync(connection);
        await SeedCardsAsync(connection);
    }

    public async Task<PlayerProfileDto?> GetByIdAsync(Guid playerId)
    {
        await using SqlConnection connection = new(ConnectionString);
        await connection.OpenAsync();
        return await LoadProfileByQueryAsync(connection,
            "SELECT TOP 1 id FROM dbo.players WHERE id = @playerId;",
            new SqlParameter("@playerId", SqlDbType.UniqueIdentifier) { Value = playerId });
    }

    public async Task<PlayerProfileDto?> GetByNicknameAsync(string nickname)
    {
        await using SqlConnection connection = new(ConnectionString);
        await connection.OpenAsync();
        return await LoadProfileByQueryAsync(connection,
            "SELECT TOP 1 id FROM dbo.players WHERE nickname = @nickname;",
            new SqlParameter("@nickname", SqlDbType.NVarChar, 64) { Value = nickname.Trim() });
    }

    public async Task<PlayerProfileDto> UpsertProfileAsync(CreateOrUpdateProfileRequest request)
    {
        PlayerProfileDto normalized = NormalizeProfile(request);

        await using SqlConnection connection = new(ConnectionString);
        await connection.OpenAsync();
        await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        bool exists = await ExistsAsync(
            connection,
            transaction,
            "SELECT 1 FROM dbo.players WHERE id = @playerId;",
            new SqlParameter("@playerId", SqlDbType.UniqueIdentifier) { Value = normalized.PlayerId });

        if (exists)
        {
            await ExecuteAsync(connection, transaction, @"
UPDATE dbo.players
SET nickname = @nickname,
    rank_name = @rankName,
    avatar = @avatar,
    level = @level,
    experience = @experience,
    coins = @coins,
    updated_at = SYSUTCDATETIME()
WHERE id = @playerId;",
                PlayerParameters(normalized));
        }
        else
        {
            await ExecuteAsync(connection, transaction, @"
INSERT INTO dbo.players (id, nickname, rank_name, avatar, level, experience, coins)
VALUES (@playerId, @nickname, @rankName, @avatar, @level, @experience, @coins);",
                PlayerParameters(normalized));
        }

        bool statisticsExists = await ExistsAsync(
            connection,
            transaction,
            "SELECT 1 FROM dbo.player_statistics WHERE player_id = @playerId;",
            new SqlParameter("@playerId", SqlDbType.UniqueIdentifier) { Value = normalized.PlayerId });

        if (statisticsExists)
        {
            await ExecuteAsync(connection, transaction, @"
UPDATE dbo.player_statistics
SET total_matches = @totalMatches,
    wins = @wins,
    losses = @losses,
    online_matches = @onlineMatches,
    offline_matches = @offlineMatches,
    cards_played = @cardsPlayed,
    turns_played = @turnsPlayed
WHERE player_id = @playerId;",
                StatisticsParameters(normalized));
        }
        else
        {
            await ExecuteAsync(connection, transaction, @"
INSERT INTO dbo.player_statistics (player_id, total_matches, wins, losses, online_matches, offline_matches, cards_played, turns_played)
VALUES (@playerId, @totalMatches, @wins, @losses, @onlineMatches, @offlineMatches, @cardsPlayed, @turnsPlayed);",
                StatisticsParameters(normalized));
        }

        await ReplaceOwnedCardsAsync(connection, transaction, normalized.PlayerId, normalized.OwnedCards);
        await ReplaceSelectedDeckAsync(connection, transaction, normalized.PlayerId, normalized.SelectedDeck);

        await transaction.CommitAsync();
        return (await GetByIdAsync(normalized.PlayerId))!;
    }

    public Task<PlayerProfileDto> UpdateProfileAsync(Guid playerId, CreateOrUpdateProfileRequest request)
    {
        request.PlayerId = playerId;
        return UpsertProfileAsync(request);
    }

    public async Task<PlayerProfileDto?> UpdateDeckAsync(Guid playerId, IReadOnlyCollection<int> selectedDeck)
    {
        await using SqlConnection connection = new(ConnectionString);
        await connection.OpenAsync();
        PlayerProfileDto? profile = await GetByIdAsync(playerId);
        if (profile is null)
            return null;

        profile.SelectedDeck = SanitizeSelectedDeck(profile.OwnedCards, selectedDeck);

        await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        await ReplaceSelectedDeckAsync(connection, transaction, playerId, profile.SelectedDeck);
        await transaction.CommitAsync();

        return await GetByIdAsync(playerId);
    }

    public async Task<PlayerProfileDto?> ChangeCoinsAsync(Guid playerId, int amount)
    {
        await using SqlConnection connection = new(ConnectionString);
        await connection.OpenAsync();

        int affected = await ExecuteAsync(connection, null, @"
UPDATE dbo.players
SET coins = CASE WHEN coins + @amount < 0 THEN 0 ELSE coins + @amount END,
    updated_at = SYSUTCDATETIME()
WHERE id = @playerId;",
            new SqlParameter("@amount", SqlDbType.Int) { Value = amount },
            new SqlParameter("@playerId", SqlDbType.UniqueIdentifier) { Value = playerId });

        if (affected == 0)
            return null;

        return await GetByIdAsync(playerId);
    }

    public async Task<PlayerProfileDto?> UnlockCardAsync(Guid playerId, int cardId)
    {
        await using SqlConnection connection = new(ConnectionString);
        await connection.OpenAsync();
        PlayerProfileDto? profile = await GetByIdAsync(playerId);
        if (profile is null)
            return null;

        if (!profile.OwnedCards.Contains(cardId))
            profile.OwnedCards.Add(cardId);

        profile.OwnedCards = profile.OwnedCards.Distinct().OrderBy(id => id).ToList();
        profile.SelectedDeck = SanitizeSelectedDeck(profile.OwnedCards, profile.SelectedDeck);

        await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        await ReplaceOwnedCardsAsync(connection, transaction, playerId, profile.OwnedCards);
        await ReplaceSelectedDeckAsync(connection, transaction, playerId, profile.SelectedDeck);
        await transaction.CommitAsync();

        return await GetByIdAsync(playerId);
    }

    public async Task<PlayerProfileDto?> RecordMatchResultAsync(Guid playerId, MatchResultRequest request)
    {
        await using SqlConnection connection = new(ConnectionString);
        await connection.OpenAsync();
        PlayerProfileDto? profile = await GetByIdAsync(playerId);
        if (profile is null)
            return null;

        profile.Statistics.TotalMatches++;
        if (request.Online)
            profile.Statistics.OnlineMatches++;
        else
            profile.Statistics.OfflineMatches++;

        if (request.Won)
            profile.Statistics.Wins++;
        else
            profile.Statistics.Losses++;

        profile.Coins = Math.Max(0, profile.Coins + request.CoinsReward);

        await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        await ExecuteAsync(connection, transaction, @"
UPDATE dbo.players
SET coins = @coins,
    updated_at = SYSUTCDATETIME()
WHERE id = @playerId;",
            new SqlParameter("@coins", SqlDbType.Int) { Value = profile.Coins },
            new SqlParameter("@playerId", SqlDbType.UniqueIdentifier) { Value = playerId });

        await ExecuteAsync(connection, transaction, @"
UPDATE dbo.player_statistics
SET total_matches = @totalMatches,
    wins = @wins,
    losses = @losses,
    online_matches = @onlineMatches,
    offline_matches = @offlineMatches,
    cards_played = @cardsPlayed,
    turns_played = @turnsPlayed
WHERE player_id = @playerId;",
            StatisticsParameters(profile));

        await transaction.CommitAsync();
        return await GetByIdAsync(playerId);
    }

    private async Task<PlayerProfileDto?> LoadProfileByQueryAsync(SqlConnection connection, string sql, params SqlParameter[] parameters)
    {
        await using SqlCommand command = new(sql, connection);
        command.Parameters.AddRange(parameters);

        object? result = await command.ExecuteScalarAsync();
        if (result is null || result == DBNull.Value)
            return null;

        Guid playerId = (Guid)result;
        return await LoadProfileAsync(connection, playerId);
    }

    private async Task<PlayerProfileDto?> LoadProfileAsync(SqlConnection connection, Guid playerId)
    {
        await using SqlCommand playerCommand = new(@"
SELECT id, nickname, rank_name, avatar, level, experience, coins
FROM dbo.players
WHERE id = @playerId;", connection);
        playerCommand.Parameters.Add(new SqlParameter("@playerId", SqlDbType.UniqueIdentifier) { Value = playerId });

        await using SqlDataReader reader = await playerCommand.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        PlayerProfileDto profile = new()
        {
            PlayerId = reader.GetGuid(0),
            Nickname = reader.GetString(1),
            Rank = reader.GetString(2),
            Avatar = reader.GetString(3),
            Level = reader.GetInt32(4),
            Experience = reader.GetInt32(5),
            Coins = reader.GetInt32(6)
        };

        await reader.CloseAsync();

        profile.Statistics = await LoadStatisticsAsync(connection, playerId);
        profile.OwnedCards = await LoadCardIdsAsync(connection, playerId, fromDeck: false);
        profile.SelectedDeck = await LoadCardIdsAsync(connection, playerId, fromDeck: true);
        profile.OwnedCards = profile.OwnedCards.Distinct().OrderBy(id => id).ToList();
        profile.SelectedDeck = SanitizeSelectedDeck(profile.OwnedCards, profile.SelectedDeck);

        return profile;
    }

    private static async Task<PlayerStatisticsDto> LoadStatisticsAsync(SqlConnection connection, Guid playerId)
    {
        await using SqlCommand command = new(@"
SELECT total_matches, wins, losses, online_matches, offline_matches, cards_played, turns_played
FROM dbo.player_statistics
WHERE player_id = @playerId;", connection);
        command.Parameters.Add(new SqlParameter("@playerId", SqlDbType.UniqueIdentifier) { Value = playerId });

        await using SqlDataReader reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return new PlayerStatisticsDto();

        return new PlayerStatisticsDto
        {
            TotalMatches = reader.GetInt32(0),
            Wins = reader.GetInt32(1),
            Losses = reader.GetInt32(2),
            OnlineMatches = reader.GetInt32(3),
            OfflineMatches = reader.GetInt32(4),
            CardsPlayed = reader.GetInt32(5),
            TurnsPlayed = reader.GetInt32(6)
        };
    }

    private static async Task<List<int>> LoadCardIdsAsync(SqlConnection connection, Guid playerId, bool fromDeck)
    {
        string sql = fromDeck
            ? "SELECT card_id FROM dbo.player_selected_deck WHERE player_id = @playerId ORDER BY slot_index;"
            : "SELECT card_id FROM dbo.player_owned_cards WHERE player_id = @playerId ORDER BY card_id;";

        await using SqlCommand command = new(sql, connection);
        command.Parameters.Add(new SqlParameter("@playerId", SqlDbType.UniqueIdentifier) { Value = playerId });

        await using SqlDataReader reader = await command.ExecuteReaderAsync();
        List<int> cardIds = [];
        while (await reader.ReadAsync())
            cardIds.Add(reader.GetInt32(0));

        return cardIds;
    }

    private static PlayerProfileDto NormalizeProfile(CreateOrUpdateProfileRequest request)
    {
        List<int> ownedCards = request.OwnedCards.Distinct().OrderBy(id => id).ToList();
        if (ownedCards.Count == 0)
            ownedCards = Enumerable.Range(1, 8).ToList();

        List<int> selectedDeck = SanitizeSelectedDeck(ownedCards, request.SelectedDeck);

        return new PlayerProfileDto
        {
            PlayerId = request.PlayerId ?? Guid.NewGuid(),
            Nickname = string.IsNullOrWhiteSpace(request.Nickname) ? "Senator" : request.Nickname.Trim(),
            Level = Math.Max(1, request.Level),
            Experience = Math.Max(0, request.Experience),
            Coins = Math.Max(0, request.Coins),
            OwnedCards = ownedCards,
            SelectedDeck = selectedDeck,
            Statistics = new PlayerStatisticsDto
            {
                TotalMatches = Math.Max(0, request.Statistics.TotalMatches),
                Wins = Math.Max(0, request.Statistics.Wins),
                Losses = Math.Max(0, request.Statistics.Losses),
                OnlineMatches = Math.Max(0, request.Statistics.OnlineMatches),
                OfflineMatches = Math.Max(0, request.Statistics.OfflineMatches),
                CardsPlayed = Math.Max(0, request.Statistics.CardsPlayed),
                TurnsPlayed = Math.Max(0, request.Statistics.TurnsPlayed)
            },
            Rank = string.IsNullOrWhiteSpace(request.Rank) ? "Bronze" : request.Rank.Trim(),
            Avatar = string.IsNullOrWhiteSpace(request.Avatar) ? "default" : request.Avatar.Trim()
        };
    }

    private static List<int> SanitizeSelectedDeck(IEnumerable<int> ownedCards, IEnumerable<int> selectedDeck)
    {
        List<int> owned = ownedCards.Distinct().OrderBy(id => id).ToList();
        List<int> deck = selectedDeck
            .Where(id => owned.Contains(id))
            .Distinct()
            .Take(MaximumDeckCards)
            .ToList();

        foreach (int cardId in owned)
        {
            if (deck.Count >= MinimumDeckCards)
                break;

            if (!deck.Contains(cardId))
                deck.Add(cardId);
        }

        return deck;
    }

    private static SqlParameter[] PlayerParameters(PlayerProfileDto profile) =>
    [
        new("@playerId", SqlDbType.UniqueIdentifier) { Value = profile.PlayerId },
        new("@nickname", SqlDbType.NVarChar, 64) { Value = profile.Nickname },
        new("@rankName", SqlDbType.NVarChar, 32) { Value = profile.Rank },
        new("@avatar", SqlDbType.NVarChar, 64) { Value = profile.Avatar },
        new("@level", SqlDbType.Int) { Value = profile.Level },
        new("@experience", SqlDbType.Int) { Value = profile.Experience },
        new("@coins", SqlDbType.Int) { Value = profile.Coins }
    ];

    private static SqlParameter[] StatisticsParameters(PlayerProfileDto profile) =>
    [
        new("@playerId", SqlDbType.UniqueIdentifier) { Value = profile.PlayerId },
        new("@totalMatches", SqlDbType.Int) { Value = profile.Statistics.TotalMatches },
        new("@wins", SqlDbType.Int) { Value = profile.Statistics.Wins },
        new("@losses", SqlDbType.Int) { Value = profile.Statistics.Losses },
        new("@onlineMatches", SqlDbType.Int) { Value = profile.Statistics.OnlineMatches },
        new("@offlineMatches", SqlDbType.Int) { Value = profile.Statistics.OfflineMatches },
        new("@cardsPlayed", SqlDbType.Int) { Value = profile.Statistics.CardsPlayed },
        new("@turnsPlayed", SqlDbType.Int) { Value = profile.Statistics.TurnsPlayed }
    ];

    private static async Task ReplaceOwnedCardsAsync(SqlConnection connection, SqlTransaction transaction, Guid playerId, IReadOnlyCollection<int> ownedCards)
    {
        await ExecuteAsync(connection, transaction, "DELETE FROM dbo.player_owned_cards WHERE player_id = @playerId;",
            new SqlParameter("@playerId", SqlDbType.UniqueIdentifier) { Value = playerId });

        foreach (int cardId in ownedCards)
        {
            await ExecuteAsync(connection, transaction, @"
INSERT INTO dbo.player_owned_cards (player_id, card_id, source)
VALUES (@playerId, @cardId, @source);",
                new SqlParameter("@playerId", SqlDbType.UniqueIdentifier) { Value = playerId },
                new SqlParameter("@cardId", SqlDbType.Int) { Value = cardId },
                new SqlParameter("@source", SqlDbType.NVarChar, 32) { Value = "api" });
        }
    }

    private static async Task ReplaceSelectedDeckAsync(SqlConnection connection, SqlTransaction transaction, Guid playerId, IReadOnlyList<int> selectedDeck)
    {
        await ExecuteAsync(connection, transaction, "DELETE FROM dbo.player_selected_deck WHERE player_id = @playerId;",
            new SqlParameter("@playerId", SqlDbType.UniqueIdentifier) { Value = playerId });

        for (int i = 0; i < selectedDeck.Count; i++)
        {
            await ExecuteAsync(connection, transaction, @"
INSERT INTO dbo.player_selected_deck (player_id, slot_index, card_id)
VALUES (@playerId, @slotIndex, @cardId);",
                new SqlParameter("@playerId", SqlDbType.UniqueIdentifier) { Value = playerId },
                new SqlParameter("@slotIndex", SqlDbType.Int) { Value = i },
                new SqlParameter("@cardId", SqlDbType.Int) { Value = selectedDeck[i] });
        }
    }

    private async Task EnsureSchemaAsync(SqlConnection connection)
    {
        const string sql = """
IF OBJECT_ID(N'dbo.players', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.players (
        id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        nickname NVARCHAR(64) NOT NULL UNIQUE,
        rank_name NVARCHAR(32) NOT NULL CONSTRAINT DF_players_rank_name DEFAULT N'Bronze',
        avatar NVARCHAR(64) NOT NULL CONSTRAINT DF_players_avatar DEFAULT N'default',
        level INT NOT NULL CONSTRAINT DF_players_level DEFAULT 1,
        experience INT NOT NULL CONSTRAINT DF_players_experience DEFAULT 0,
        coins INT NOT NULL CONSTRAINT DF_players_coins DEFAULT 500,
        created_at DATETIME2 NOT NULL CONSTRAINT DF_players_created_at DEFAULT SYSUTCDATETIME(),
        updated_at DATETIME2 NOT NULL CONSTRAINT DF_players_updated_at DEFAULT SYSUTCDATETIME()
    );
END;

IF OBJECT_ID(N'dbo.player_statistics', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.player_statistics (
        player_id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        total_matches INT NOT NULL CONSTRAINT DF_player_statistics_total_matches DEFAULT 0,
        wins INT NOT NULL CONSTRAINT DF_player_statistics_wins DEFAULT 0,
        losses INT NOT NULL CONSTRAINT DF_player_statistics_losses DEFAULT 0,
        online_matches INT NOT NULL CONSTRAINT DF_player_statistics_online_matches DEFAULT 0,
        offline_matches INT NOT NULL CONSTRAINT DF_player_statistics_offline_matches DEFAULT 0,
        cards_played INT NOT NULL CONSTRAINT DF_player_statistics_cards_played DEFAULT 0,
        turns_played INT NOT NULL CONSTRAINT DF_player_statistics_turns_played DEFAULT 0,
        CONSTRAINT FK_player_statistics_players FOREIGN KEY (player_id) REFERENCES dbo.players(id) ON DELETE CASCADE
    );
END;

IF OBJECT_ID(N'dbo.cards', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.cards (
        id INT NOT NULL PRIMARY KEY,
        code NVARCHAR(128) NULL UNIQUE,
        name NVARCHAR(128) NOT NULL,
        rarity NVARCHAR(32) NOT NULL,
        card_type NVARCHAR(32) NOT NULL,
        cost INT NOT NULL CONSTRAINT DF_cards_cost DEFAULT 0,
        is_active BIT NOT NULL CONSTRAINT DF_cards_is_active DEFAULT 1
    );
END;

IF OBJECT_ID(N'dbo.player_owned_cards', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.player_owned_cards (
        player_id UNIQUEIDENTIFIER NOT NULL,
        card_id INT NOT NULL,
        obtained_at DATETIME2 NOT NULL CONSTRAINT DF_player_owned_cards_obtained_at DEFAULT SYSUTCDATETIME(),
        source NVARCHAR(32) NULL,
        CONSTRAINT PK_player_owned_cards PRIMARY KEY (player_id, card_id),
        CONSTRAINT FK_player_owned_cards_players FOREIGN KEY (player_id) REFERENCES dbo.players(id) ON DELETE CASCADE,
        CONSTRAINT FK_player_owned_cards_cards FOREIGN KEY (card_id) REFERENCES dbo.cards(id)
    );
END;

IF OBJECT_ID(N'dbo.player_selected_deck', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.player_selected_deck (
        player_id UNIQUEIDENTIFIER NOT NULL,
        slot_index INT NOT NULL,
        card_id INT NOT NULL,
        CONSTRAINT PK_player_selected_deck PRIMARY KEY (player_id, slot_index),
        CONSTRAINT UQ_player_selected_deck_player_card UNIQUE (player_id, card_id),
        CONSTRAINT FK_player_selected_deck_players FOREIGN KEY (player_id) REFERENCES dbo.players(id) ON DELETE CASCADE,
        CONSTRAINT FK_player_selected_deck_cards FOREIGN KEY (card_id) REFERENCES dbo.cards(id)
    );
END;
""";

        await ExecuteAsync(connection, null, sql);
    }

    private async Task SeedCardsAsync(SqlConnection connection)
    {
        string cardsPath = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", "..", "Assets", "StreamingAssets", "cards.json"));
        if (!File.Exists(cardsPath))
            return;

        using FileStream stream = File.OpenRead(cardsPath);
        CardFileRoot? root = await JsonSerializer.DeserializeAsync<CardFileRoot>(stream);
        if (root?.Cards is null)
            return;

        foreach (CardSeedModel card in root.Cards.Where(card => card is not null))
        {
            bool exists = await ExistsAsync(connection, null,
                "SELECT 1 FROM dbo.cards WHERE id = @cardId;",
                new SqlParameter("@cardId", SqlDbType.Int) { Value = card.Id });

            if (exists)
                continue;

            await ExecuteAsync(connection, null, @"
INSERT INTO dbo.cards (id, code, name, rarity, card_type, cost, is_active)
VALUES (@cardId, @code, @name, @rarity, @cardType, @cost, @isActive);",
                new SqlParameter("@cardId", SqlDbType.Int) { Value = card.Id },
                new SqlParameter("@code", SqlDbType.NVarChar, 128) { Value = $"card_{card.Id}" },
                new SqlParameter("@name", SqlDbType.NVarChar, 128) { Value = card.Name ?? $"Card {card.Id}" },
                new SqlParameter("@rarity", SqlDbType.NVarChar, 32) { Value = string.IsNullOrWhiteSpace(card.Rarity) ? "Common" : card.Rarity },
                new SqlParameter("@cardType", SqlDbType.NVarChar, 32) { Value = string.IsNullOrWhiteSpace(card.Type) ? "General" : card.Type },
                new SqlParameter("@cost", SqlDbType.Int) { Value = Math.Max(0, card.Cost) },
                new SqlParameter("@isActive", SqlDbType.Bit) { Value = true });
        }
    }

    private static async Task<bool> ExistsAsync(SqlConnection connection, SqlTransaction? transaction, string sql, params SqlParameter[] parameters)
    {
        await using SqlCommand command = new(sql, connection, transaction);
        command.Parameters.AddRange(parameters);
        object? result = await command.ExecuteScalarAsync();
        return result is not null && result != DBNull.Value;
    }

    private static async Task<int> ExecuteAsync(SqlConnection connection, SqlTransaction? transaction, string sql, params SqlParameter[] parameters)
    {
        await using SqlCommand command = new(sql, connection, transaction);
        command.Parameters.AddRange(parameters);
        return await command.ExecuteNonQueryAsync();
    }

    private sealed class CardFileRoot
    {
        [JsonPropertyName("cards")]
        public List<CardSeedModel> Cards { get; set; } = [];
    }

    private sealed class CardSeedModel
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }
        [JsonPropertyName("name")]
        public string? Name { get; set; }
        [JsonPropertyName("type")]
        public string? Type { get; set; }
        [JsonPropertyName("cost")]
        public int Cost { get; set; }
        [JsonPropertyName("rarity")]
        public string? Rarity { get; set; }
    }
}

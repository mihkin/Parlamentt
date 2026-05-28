using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using ParlamB.Api.Contracts;

namespace ParlamB.Api.Services;

public sealed class OnlineSessionStore
{
    private readonly IConfiguration configuration;

    public OnlineSessionStore(IConfiguration configuration)
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

    public async Task<IReadOnlyList<PublicLobbyDto>> ListLobbySummariesAsync()
    {
        await using SqlConnection connection = new(ConnectionString);
        await connection.OpenAsync();

        await using SqlCommand command = new(@"
SELECT l.room_code,
       l.host_nickname,
       COUNT(p.player_id) AS player_count,
       l.max_players,
       l.started
FROM dbo.public_lobbies l
LEFT JOIN dbo.lobby_players p ON p.room_code = l.room_code
GROUP BY l.room_code, l.host_nickname, l.max_players, l.started
ORDER BY l.started ASC, player_count DESC, l.host_nickname ASC;", connection);

        await using SqlDataReader reader = await command.ExecuteReaderAsync();
        List<PublicLobbyDto> lobbies = [];
        while (await reader.ReadAsync())
        {
            lobbies.Add(new PublicLobbyDto
            {
                RoomCode = reader.GetString(0),
                HostNickname = reader.GetString(1),
                HostAddress = string.Empty,
                PlayerCount = reader.GetInt32(2),
                MaxPlayers = reader.GetInt32(3),
                Started = reader.GetBoolean(4)
            });
        }

        return lobbies;
    }

    public async Task<OnlineLobbyStateDto> HostLobbyAsync(Guid playerId, HostOnlineLobbyRequest request)
    {
        string roomCode = GenerateRoomCode();
        await using SqlConnection connection = new(ConnectionString);
        await connection.OpenAsync();
        await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        await ExecuteAsync(connection, transaction, @"
DELETE FROM dbo.match_commands WHERE room_code IN (SELECT room_code FROM dbo.public_lobbies WHERE host_player_id = @playerId);
DELETE FROM dbo.match_players WHERE room_code IN (SELECT room_code FROM dbo.public_lobbies WHERE host_player_id = @playerId);
DELETE FROM dbo.match_state WHERE room_code IN (SELECT room_code FROM dbo.public_lobbies WHERE host_player_id = @playerId);
DELETE FROM dbo.lobby_players WHERE room_code IN (SELECT room_code FROM dbo.public_lobbies WHERE host_player_id = @playerId);
DELETE FROM dbo.public_lobbies WHERE host_player_id = @playerId;",
            new SqlParameter("@playerId", SqlDbType.UniqueIdentifier) { Value = playerId });

        await ExecuteAsync(connection, transaction, @"
INSERT INTO dbo.public_lobbies (room_code, host_player_id, host_nickname, max_players, started, created_at_utc, updated_at_utc)
VALUES (@roomCode, @playerId, @hostNickname, @maxPlayers, 0, SYSUTCDATETIME(), SYSUTCDATETIME());",
            new SqlParameter("@roomCode", SqlDbType.NVarChar, 16) { Value = roomCode },
            new SqlParameter("@playerId", SqlDbType.UniqueIdentifier) { Value = playerId },
            new SqlParameter("@hostNickname", SqlDbType.NVarChar, 64) { Value = NormalizeNickname(request.Nickname) },
            new SqlParameter("@maxPlayers", SqlDbType.Int) { Value = Math.Clamp(request.MaxPlayers, 2, 4) });

        await UpsertLobbyPlayerAsync(connection, transaction, roomCode, playerId, request.Nickname, request.Level, request.Rank, request.Avatar, request.SelectedDeckCardIds, seatIndex: 0, isHost: true, isReady: false, "Connected");

        await transaction.CommitAsync();
        return (await GetLobbyAsync(roomCode))!;
    }

    public async Task<OnlineLobbyStateDto?> JoinLobbyAsync(Guid playerId, JoinOnlineLobbyRequest request)
    {
        string roomCode = NormalizeRoomCode(request.RoomCode);
        if (string.IsNullOrWhiteSpace(roomCode))
            return null;

        await using SqlConnection connection = new(ConnectionString);
        await connection.OpenAsync();
        await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        (bool exists, bool started, int maxPlayers, int playerCount) = await GetLobbyMetaAsync(connection, transaction, roomCode);
        if (!exists || started || playerCount >= maxPlayers)
            return null;

        int seatIndex = await GetNextSeatIndexAsync(connection, transaction, roomCode, maxPlayers);
        await UpsertLobbyPlayerAsync(connection, transaction, roomCode, playerId, request.Nickname, request.Level, request.Rank, request.Avatar, request.SelectedDeckCardIds, seatIndex, isHost: false, isReady: false, "Connected");
        await TouchLobbyAsync(connection, transaction, roomCode);

        await transaction.CommitAsync();
        return await GetLobbyAsync(roomCode);
    }

    public async Task<OnlineLobbyStateDto?> GetLobbyAsync(string roomCode)
    {
        roomCode = NormalizeRoomCode(roomCode);
        if (string.IsNullOrWhiteSpace(roomCode))
            return null;

        await using SqlConnection connection = new(ConnectionString);
        await connection.OpenAsync();

        await using SqlCommand lobbyCommand = new(@"
SELECT room_code, host_player_id, max_players, started
FROM dbo.public_lobbies
WHERE room_code = @roomCode;", connection);
        lobbyCommand.Parameters.Add(new SqlParameter("@roomCode", SqlDbType.NVarChar, 16) { Value = roomCode });

        await using SqlDataReader lobbyReader = await lobbyCommand.ExecuteReaderAsync();
        if (!await lobbyReader.ReadAsync())
            return null;

        OnlineLobbyStateDto lobby = new()
        {
            RoomCode = lobbyReader.GetString(0),
            HostPlayerId = lobbyReader.GetGuid(1).ToString(),
            MaxPlayers = lobbyReader.GetInt32(2),
            Started = lobbyReader.GetBoolean(3)
        };
        await lobbyReader.CloseAsync();

        await using SqlCommand playerCommand = new(@"
SELECT player_id, nickname, seat_index, is_host, is_ready, level, rank_name, avatar, selected_deck_json, connection_state
FROM dbo.lobby_players
WHERE room_code = @roomCode
ORDER BY seat_index;", connection);
        playerCommand.Parameters.Add(new SqlParameter("@roomCode", SqlDbType.NVarChar, 16) { Value = roomCode });

        await using SqlDataReader playerReader = await playerCommand.ExecuteReaderAsync();
        while (await playerReader.ReadAsync())
        {
            lobby.Players.Add(new OnlineLobbyPlayerDto
            {
                PlayerId = playerReader.GetGuid(0).ToString(),
                Nickname = playerReader.GetString(1),
                SeatIndex = playerReader.GetInt32(2),
                IsHost = playerReader.GetBoolean(3),
                IsReady = playerReader.GetBoolean(4),
                Level = playerReader.GetInt32(5),
                Rank = playerReader.GetString(6),
                Avatar = playerReader.GetString(7),
                SelectedDeckCardIds = DeserializeIntList(playerReader.IsDBNull(8) ? string.Empty : playerReader.GetString(8)),
                ConnectionState = playerReader.GetString(9)
            });
        }

        return lobby;
    }

    public async Task<OnlineLobbyStateDto?> SetReadyAsync(Guid playerId, string roomCode, bool ready)
    {
        roomCode = NormalizeRoomCode(roomCode);
        await using SqlConnection connection = new(ConnectionString);
        await connection.OpenAsync();
        await ExecuteAsync(connection, null, @"
UPDATE dbo.lobby_players
SET is_ready = @ready,
    updated_at_utc = SYSUTCDATETIME()
WHERE room_code = @roomCode AND player_id = @playerId;",
            new SqlParameter("@ready", SqlDbType.Bit) { Value = ready },
            new SqlParameter("@roomCode", SqlDbType.NVarChar, 16) { Value = roomCode },
            new SqlParameter("@playerId", SqlDbType.UniqueIdentifier) { Value = playerId });

        await ExecuteAsync(connection, null, @"
UPDATE dbo.public_lobbies SET updated_at_utc = SYSUTCDATETIME() WHERE room_code = @roomCode;",
            new SqlParameter("@roomCode", SqlDbType.NVarChar, 16) { Value = roomCode });

        return await GetLobbyAsync(roomCode);
    }

    public async Task<OnlineLobbyStateDto?> StartMatchAsync(Guid playerId, string roomCode)
    {
        roomCode = NormalizeRoomCode(roomCode);
        await using SqlConnection connection = new(ConnectionString);
        await connection.OpenAsync();
        await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        await ExecuteAsync(connection, transaction, @"
UPDATE dbo.public_lobbies
SET started = 1,
    updated_at_utc = SYSUTCDATETIME()
WHERE room_code = @roomCode AND host_player_id = @playerId;",
            new SqlParameter("@roomCode", SqlDbType.NVarChar, 16) { Value = roomCode },
            new SqlParameter("@playerId", SqlDbType.UniqueIdentifier) { Value = playerId });

        await ExecuteAsync(connection, transaction, @"
IF NOT EXISTS (SELECT 1 FROM dbo.match_state WHERE room_code = @roomCode)
INSERT INTO dbo.match_state (room_code, host_player_id, revision, snapshot_json, created_at_utc, updated_at_utc)
VALUES (@roomCode, @playerId, 0, N'', SYSUTCDATETIME(), SYSUTCDATETIME());",
            new SqlParameter("@roomCode", SqlDbType.NVarChar, 16) { Value = roomCode },
            new SqlParameter("@playerId", SqlDbType.UniqueIdentifier) { Value = playerId });

        await transaction.CommitAsync();
        return await GetLobbyAsync(roomCode);
    }

    public async Task LeaveLobbyAsync(Guid playerId, string roomCode)
    {
        roomCode = NormalizeRoomCode(roomCode);
        await using SqlConnection connection = new(ConnectionString);
        await connection.OpenAsync();
        await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        bool isHost = await ExistsAsync(connection, transaction,
            "SELECT 1 FROM dbo.public_lobbies WHERE room_code = @roomCode AND host_player_id = @playerId;",
            new SqlParameter("@roomCode", SqlDbType.NVarChar, 16) { Value = roomCode },
            new SqlParameter("@playerId", SqlDbType.UniqueIdentifier) { Value = playerId });

        await ExecuteAsync(connection, transaction, @"
DELETE FROM dbo.lobby_players
WHERE room_code = @roomCode AND player_id = @playerId;",
            new SqlParameter("@roomCode", SqlDbType.NVarChar, 16) { Value = roomCode },
            new SqlParameter("@playerId", SqlDbType.UniqueIdentifier) { Value = playerId });

        int remainingPlayers = await CountAsync(connection, transaction,
            "SELECT COUNT(*) FROM dbo.lobby_players WHERE room_code = @roomCode;",
            new SqlParameter("@roomCode", SqlDbType.NVarChar, 16) { Value = roomCode });

        if (isHost || remainingPlayers == 0)
        {
            await DeleteLobbyTreeAsync(connection, transaction, roomCode);
        }
        else
        {
            await TouchLobbyAsync(connection, transaction, roomCode);
        }

        await transaction.CommitAsync();
    }

    public async Task<MatchSnapshotDto?> GetSnapshotAsync(Guid playerId, string roomCode)
    {
        roomCode = NormalizeRoomCode(roomCode);
        await using SqlConnection connection = new(ConnectionString);
        await connection.OpenAsync();

        if (!await IsLobbyMemberAsync(connection, null, roomCode, playerId))
            return null;

        await using SqlCommand command = new(@"
SELECT room_code, revision, snapshot_json, updated_at_utc
FROM dbo.match_state
WHERE room_code = @roomCode;", connection);
        command.Parameters.Add(new SqlParameter("@roomCode", SqlDbType.NVarChar, 16) { Value = roomCode });

        await using SqlDataReader reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return new MatchSnapshotDto
        {
            RoomCode = reader.GetString(0),
            Revision = reader.GetInt32(1),
            SnapshotJson = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
            UpdatedAtUtc = reader.GetDateTime(3)
        };
    }

    public async Task<MatchSnapshotDto?> UpdateSnapshotAsync(Guid playerId, string roomCode, string snapshotJson)
    {
        roomCode = NormalizeRoomCode(roomCode);
        await using SqlConnection connection = new(ConnectionString);
        await connection.OpenAsync();
        await using SqlTransaction transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        bool isHost = await ExistsAsync(connection, transaction,
            "SELECT 1 FROM dbo.public_lobbies WHERE room_code = @roomCode AND host_player_id = @playerId;",
            new SqlParameter("@roomCode", SqlDbType.NVarChar, 16) { Value = roomCode },
            new SqlParameter("@playerId", SqlDbType.UniqueIdentifier) { Value = playerId });
        if (!isHost)
            return null;

        ParsedSnapshot parsed = ParseSnapshot(snapshotJson);

        await ExecuteAsync(connection, transaction, @"
UPDATE dbo.match_state
SET revision = revision + 1,
    snapshot_json = @snapshotJson,
    current_round = @currentRound,
    current_participant_index = @currentParticipantIndex,
    neutral_influence = @neutralInfluence,
    phase = @phase,
    result = @result,
    result_reason = @resultReason,
    current_turn_time_left = @turnTimeLeft,
    current_turn_duration = @turnDuration,
    current_action_points_remaining = @actionPoints,
    winner_participant_id = @winnerParticipantId,
    updated_at_utc = SYSUTCDATETIME()
WHERE room_code = @roomCode;",
            new SqlParameter("@snapshotJson", SqlDbType.NVarChar) { Value = snapshotJson ?? string.Empty },
            new SqlParameter("@currentRound", SqlDbType.Int) { Value = parsed.CurrentRound },
            new SqlParameter("@currentParticipantIndex", SqlDbType.Int) { Value = parsed.CurrentParticipantIndex },
            new SqlParameter("@neutralInfluence", SqlDbType.Int) { Value = parsed.NeutralInfluence },
            new SqlParameter("@phase", SqlDbType.NVarChar, 32) { Value = parsed.Phase },
            new SqlParameter("@result", SqlDbType.NVarChar, 32) { Value = parsed.Result },
            new SqlParameter("@resultReason", SqlDbType.NVarChar) { Value = parsed.ResultReason },
            new SqlParameter("@turnTimeLeft", SqlDbType.Float) { Value = parsed.CurrentTurnTimeLeft },
            new SqlParameter("@turnDuration", SqlDbType.Float) { Value = parsed.CurrentTurnDuration },
            new SqlParameter("@actionPoints", SqlDbType.Int) { Value = parsed.CurrentActionPointsRemaining },
            new SqlParameter("@winnerParticipantId", SqlDbType.Int) { Value = parsed.WinnerParticipantId.HasValue ? parsed.WinnerParticipantId.Value : DBNull.Value },
            new SqlParameter("@roomCode", SqlDbType.NVarChar, 16) { Value = roomCode });

        await ExecuteAsync(connection, transaction, "DELETE FROM dbo.match_players WHERE room_code = @roomCode;",
            new SqlParameter("@roomCode", SqlDbType.NVarChar, 16) { Value = roomCode });

        foreach (ParsedSnapshotPlayer player in parsed.Players)
        {
            await ExecuteAsync(connection, transaction, @"
INSERT INTO dbo.match_players
(room_code, player_id, seat_index, participant_id, nickname, political_points, influence, hand_card_ids_json, deck_count, is_current_turn, disconnected, status, state_json, updated_at_utc)
VALUES
(@roomCode, @playerId, @seatIndex, @participantId, @nickname, @politicalPoints, @influence, @handJson, @deckCount, @isCurrentTurn, @disconnected, @status, @stateJson, SYSUTCDATETIME());",
                new SqlParameter("@roomCode", SqlDbType.NVarChar, 16) { Value = roomCode },
                new SqlParameter("@playerId", SqlDbType.UniqueIdentifier) { Value = player.PlayerId },
                new SqlParameter("@seatIndex", SqlDbType.Int) { Value = player.SeatIndex },
                new SqlParameter("@participantId", SqlDbType.Int) { Value = player.ParticipantId },
                new SqlParameter("@nickname", SqlDbType.NVarChar, 64) { Value = player.Nickname },
                new SqlParameter("@politicalPoints", SqlDbType.Int) { Value = player.PoliticalPoints },
                new SqlParameter("@influence", SqlDbType.Int) { Value = player.Influence },
                new SqlParameter("@handJson", SqlDbType.NVarChar) { Value = SerializeIntList(player.HandCardIds) },
                new SqlParameter("@deckCount", SqlDbType.Int) { Value = player.DeckCount },
                new SqlParameter("@isCurrentTurn", SqlDbType.Bit) { Value = player.IsCurrentTurn },
                new SqlParameter("@disconnected", SqlDbType.Bit) { Value = player.Disconnected },
                new SqlParameter("@status", SqlDbType.NVarChar, 32) { Value = player.Status },
                new SqlParameter("@stateJson", SqlDbType.NVarChar) { Value = player.StateJson });
        }

        await transaction.CommitAsync();
        return await GetSnapshotAsync(playerId, roomCode);
    }

    public async Task<long?> AddCommandAsync(Guid playerId, string roomCode, string payload)
    {
        roomCode = NormalizeRoomCode(roomCode);
        await using SqlConnection connection = new(ConnectionString);
        await connection.OpenAsync();

        if (!await IsLobbyMemberAsync(connection, null, roomCode, playerId))
            return null;

        await using SqlCommand command = new(@"
INSERT INTO dbo.match_commands (room_code, player_id, payload, created_at_utc)
OUTPUT INSERTED.command_id
VALUES (@roomCode, @playerId, @payload, SYSUTCDATETIME());", connection);
        command.Parameters.Add(new SqlParameter("@roomCode", SqlDbType.NVarChar, 16) { Value = roomCode });
        command.Parameters.Add(new SqlParameter("@playerId", SqlDbType.UniqueIdentifier) { Value = playerId });
        command.Parameters.Add(new SqlParameter("@payload", SqlDbType.NVarChar) { Value = payload ?? string.Empty });

        object? result = await command.ExecuteScalarAsync();
        return result == null || result == DBNull.Value ? null : Convert.ToInt64(result);
    }

    public async Task<IReadOnlyList<MatchCommandDto>> GetCommandsAsync(Guid playerId, string roomCode, long afterId)
    {
        roomCode = NormalizeRoomCode(roomCode);
        await using SqlConnection connection = new(ConnectionString);
        await connection.OpenAsync();

        if (!await IsLobbyMemberAsync(connection, null, roomCode, playerId))
            return [];

        await using SqlCommand command = new(@"
SELECT command_id, room_code, player_id, payload, created_at_utc
FROM dbo.match_commands
WHERE room_code = @roomCode AND command_id > @afterId
ORDER BY command_id ASC;", connection);
        command.Parameters.Add(new SqlParameter("@roomCode", SqlDbType.NVarChar, 16) { Value = roomCode });
        command.Parameters.Add(new SqlParameter("@afterId", SqlDbType.BigInt) { Value = Math.Max(0, afterId) });

        await using SqlDataReader reader = await command.ExecuteReaderAsync();
        List<MatchCommandDto> commands = [];
        while (await reader.ReadAsync())
        {
            commands.Add(new MatchCommandDto
            {
                CommandId = reader.GetInt64(0),
                RoomCode = reader.GetString(1),
                PlayerId = reader.GetGuid(2).ToString(),
                Payload = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                CreatedAtUtc = reader.GetDateTime(4)
            });
        }

        return commands;
    }

    private static string NormalizeRoomCode(string roomCode) =>
        string.IsNullOrWhiteSpace(roomCode) ? string.Empty : roomCode.Trim().ToUpperInvariant();

    private static string NormalizeNickname(string nickname) =>
        string.IsNullOrWhiteSpace(nickname) ? "Host" : nickname.Trim();

    private static string GenerateRoomCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        Random random = new();
        char[] buffer = new char[6];
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] = alphabet[random.Next(alphabet.Length)];

        return new string(buffer);
    }

    private static async Task UpsertLobbyPlayerAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string roomCode,
        Guid playerId,
        string nickname,
        int level,
        string rank,
        string avatar,
        IReadOnlyList<int> selectedDeckCardIds,
        int seatIndex,
        bool isHost,
        bool isReady,
        string connectionState)
    {
        await ExecuteAsync(connection, transaction, @"
MERGE dbo.lobby_players AS target
USING (SELECT @roomCode AS room_code, @playerId AS player_id) AS source
ON target.room_code = source.room_code AND target.player_id = source.player_id
WHEN MATCHED THEN
    UPDATE SET
        nickname = @nickname,
        seat_index = @seatIndex,
        is_host = @isHost,
        is_ready = @isReady,
        level = @level,
        rank_name = @rankName,
        avatar = @avatar,
        selected_deck_json = @selectedDeckJson,
        connection_state = @connectionState,
        updated_at_utc = SYSUTCDATETIME()
WHEN NOT MATCHED THEN
    INSERT (room_code, player_id, nickname, seat_index, is_host, is_ready, level, rank_name, avatar, selected_deck_json, connection_state, joined_at_utc, updated_at_utc)
    VALUES (@roomCode, @playerId, @nickname, @seatIndex, @isHost, @isReady, @level, @rankName, @avatar, @selectedDeckJson, @connectionState, SYSUTCDATETIME(), SYSUTCDATETIME());",
            new SqlParameter("@roomCode", SqlDbType.NVarChar, 16) { Value = roomCode },
            new SqlParameter("@playerId", SqlDbType.UniqueIdentifier) { Value = playerId },
            new SqlParameter("@nickname", SqlDbType.NVarChar, 64) { Value = NormalizeNickname(nickname) },
            new SqlParameter("@seatIndex", SqlDbType.Int) { Value = seatIndex },
            new SqlParameter("@isHost", SqlDbType.Bit) { Value = isHost },
            new SqlParameter("@isReady", SqlDbType.Bit) { Value = isReady },
            new SqlParameter("@level", SqlDbType.Int) { Value = Math.Max(1, level) },
            new SqlParameter("@rankName", SqlDbType.NVarChar, 32) { Value = string.IsNullOrWhiteSpace(rank) ? "Bronze" : rank.Trim() },
            new SqlParameter("@avatar", SqlDbType.NVarChar, 64) { Value = string.IsNullOrWhiteSpace(avatar) ? "default" : avatar.Trim() },
            new SqlParameter("@selectedDeckJson", SqlDbType.NVarChar) { Value = SerializeIntList(selectedDeckCardIds) },
            new SqlParameter("@connectionState", SqlDbType.NVarChar, 32) { Value = string.IsNullOrWhiteSpace(connectionState) ? "Connected" : connectionState.Trim() });
    }

    private static async Task<(bool Exists, bool Started, int MaxPlayers, int PlayerCount)> GetLobbyMetaAsync(SqlConnection connection, SqlTransaction transaction, string roomCode)
    {
        await using SqlCommand command = new(@"
SELECT l.started, l.max_players, COUNT(p.player_id)
FROM dbo.public_lobbies l
LEFT JOIN dbo.lobby_players p ON p.room_code = l.room_code
WHERE l.room_code = @roomCode
GROUP BY l.started, l.max_players;", connection, transaction);
        command.Parameters.Add(new SqlParameter("@roomCode", SqlDbType.NVarChar, 16) { Value = roomCode });

        await using SqlDataReader reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return (false, false, 0, 0);

        return (true, reader.GetBoolean(0), reader.GetInt32(1), reader.GetInt32(2));
    }

    private static async Task<int> GetNextSeatIndexAsync(SqlConnection connection, SqlTransaction transaction, string roomCode, int maxPlayers)
    {
        HashSet<int> used = [];
        await using SqlCommand command = new("SELECT seat_index FROM dbo.lobby_players WHERE room_code = @roomCode;", connection, transaction);
        command.Parameters.Add(new SqlParameter("@roomCode", SqlDbType.NVarChar, 16) { Value = roomCode });
        await using SqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            used.Add(reader.GetInt32(0));

        for (int index = 0; index < maxPlayers; index++)
        {
            if (!used.Contains(index))
                return index;
        }

        return used.Count;
    }

    private static async Task TouchLobbyAsync(SqlConnection connection, SqlTransaction transaction, string roomCode)
    {
        await ExecuteAsync(connection, transaction, "UPDATE dbo.public_lobbies SET updated_at_utc = SYSUTCDATETIME() WHERE room_code = @roomCode;",
            new SqlParameter("@roomCode", SqlDbType.NVarChar, 16) { Value = roomCode });
    }

    private static async Task DeleteLobbyTreeAsync(SqlConnection connection, SqlTransaction transaction, string roomCode)
    {
        await ExecuteAsync(connection, transaction, "DELETE FROM dbo.match_commands WHERE room_code = @roomCode;",
            new SqlParameter("@roomCode", SqlDbType.NVarChar, 16) { Value = roomCode });
        await ExecuteAsync(connection, transaction, "DELETE FROM dbo.match_players WHERE room_code = @roomCode;",
            new SqlParameter("@roomCode", SqlDbType.NVarChar, 16) { Value = roomCode });
        await ExecuteAsync(connection, transaction, "DELETE FROM dbo.match_state WHERE room_code = @roomCode;",
            new SqlParameter("@roomCode", SqlDbType.NVarChar, 16) { Value = roomCode });
        await ExecuteAsync(connection, transaction, "DELETE FROM dbo.lobby_players WHERE room_code = @roomCode;",
            new SqlParameter("@roomCode", SqlDbType.NVarChar, 16) { Value = roomCode });
        await ExecuteAsync(connection, transaction, "DELETE FROM dbo.public_lobbies WHERE room_code = @roomCode;",
            new SqlParameter("@roomCode", SqlDbType.NVarChar, 16) { Value = roomCode });
    }

    private static async Task<bool> IsLobbyMemberAsync(SqlConnection connection, SqlTransaction? transaction, string roomCode, Guid playerId)
    {
        return await ExistsAsync(connection, transaction,
            "SELECT 1 FROM dbo.lobby_players WHERE room_code = @roomCode AND player_id = @playerId;",
            new SqlParameter("@roomCode", SqlDbType.NVarChar, 16) { Value = roomCode },
            new SqlParameter("@playerId", SqlDbType.UniqueIdentifier) { Value = playerId });
    }

    private static List<int> DeserializeIntList(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<int>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static string SerializeIntList(IReadOnlyList<int> values)
    {
        return JsonSerializer.Serialize(values ?? Array.Empty<int>());
    }

    private static ParsedSnapshot ParseSnapshot(string snapshotJson)
    {
        ParsedSnapshot snapshot = new();
        if (string.IsNullOrWhiteSpace(snapshotJson))
            return snapshot;

        using JsonDocument document = JsonDocument.Parse(snapshotJson);
        JsonElement root = document.RootElement;
        snapshot.CurrentTurnTimeLeft = ReadSingle(root, "currentTurnTimeLeft");
        snapshot.CurrentTurnDuration = ReadSingle(root, "currentTurnDuration");
        snapshot.CurrentActionPointsRemaining = ReadInt(root, "currentActionPointsRemaining");
        snapshot.WinnerParticipantId = TryReadNullableInt(root, "winnerParticipantId");
        snapshot.ResultReason = ReadString(root, "resultReason");

        if (!root.TryGetProperty("state", out JsonElement state))
            return snapshot;

        snapshot.CurrentRound = ReadInt(state, "currentRound");
        snapshot.CurrentParticipantIndex = ReadInt(state, "currentParticipantIndex");
        snapshot.NeutralInfluence = ReadInt(state, "neutralInfluence");
        snapshot.Phase = ReadRawProperty(state, "phase");
        snapshot.Result = ReadRawProperty(state, "result");

        if (!state.TryGetProperty("participants", out JsonElement participants) || participants.ValueKind != JsonValueKind.Array)
            return snapshot;

        int index = 0;
        foreach (JsonElement participant in participants.EnumerateArray())
        {
            string playerIdRaw = ReadString(participant, "networkPlayerId");
            if (!Guid.TryParse(playerIdRaw, out Guid playerId))
            {
                index++;
                continue;
            }

            List<int> handIds = [];
            if (participant.TryGetProperty("hand", out JsonElement hand) && hand.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement card in hand.EnumerateArray())
                    handIds.Add(ReadInt(card, "id"));
            }

            int deckCount = 0;
            if (participant.TryGetProperty("deck", out JsonElement deck) && deck.ValueKind == JsonValueKind.Array)
                deckCount = deck.GetArrayLength();

            snapshot.Players.Add(new ParsedSnapshotPlayer
            {
                PlayerId = playerId,
                SeatIndex = ReadInt(participant, "seatIndex"),
                ParticipantId = ReadInt(participant, "id"),
                Nickname = ReadString(participant, "displayName"),
                PoliticalPoints = ReadInt(participant, "politicalPoints"),
                Influence = ReadInt(participant, "influence"),
                HandCardIds = handIds,
                DeckCount = deckCount,
                IsCurrentTurn = index == snapshot.CurrentParticipantIndex,
                Disconnected = false,
                Status = ReadRawProperty(participant, "status"),
                StateJson = participant.GetRawText()
            });
            index++;
        }

        return snapshot;
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private static int ReadInt(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement property) && property.TryGetInt32(out int value)
            ? value
            : 0;
    }

    private static float ReadSingle(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement property) && property.TryGetSingle(out float value)
            ? value
            : 0f;
    }

    private static int? TryReadNullableInt(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement property) && property.TryGetInt32(out int value)
            ? value
            : null;
    }

    private static string ReadRawProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement property)
            ? property.ToString()
            : string.Empty;
    }

    private async Task EnsureSchemaAsync(SqlConnection connection)
    {
        const string sql = """
IF OBJECT_ID(N'dbo.public_lobbies', N'U') IS NOT NULL AND COL_LENGTH(N'dbo.public_lobbies', N'host_player_id') IS NULL
BEGIN
    DROP TABLE dbo.public_lobbies;
END;

IF OBJECT_ID(N'dbo.public_lobbies', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.public_lobbies (
        room_code NVARCHAR(16) NOT NULL PRIMARY KEY,
        host_player_id UNIQUEIDENTIFIER NOT NULL,
        host_nickname NVARCHAR(64) NOT NULL,
        max_players INT NOT NULL CONSTRAINT DF_public_lobbies_max_players DEFAULT 4,
        started BIT NOT NULL CONSTRAINT DF_public_lobbies_started DEFAULT 0,
        created_at_utc DATETIME2 NOT NULL CONSTRAINT DF_public_lobbies_created_at DEFAULT SYSUTCDATETIME(),
        updated_at_utc DATETIME2 NOT NULL CONSTRAINT DF_public_lobbies_updated_at DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_public_lobbies_players FOREIGN KEY (host_player_id) REFERENCES dbo.players(id)
    );
END;

IF OBJECT_ID(N'dbo.lobby_players', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.lobby_players (
        room_code NVARCHAR(16) NOT NULL,
        player_id UNIQUEIDENTIFIER NOT NULL,
        nickname NVARCHAR(64) NOT NULL,
        seat_index INT NOT NULL,
        is_host BIT NOT NULL CONSTRAINT DF_lobby_players_is_host DEFAULT 0,
        is_ready BIT NOT NULL CONSTRAINT DF_lobby_players_is_ready DEFAULT 0,
        level INT NOT NULL CONSTRAINT DF_lobby_players_level DEFAULT 1,
        rank_name NVARCHAR(32) NOT NULL CONSTRAINT DF_lobby_players_rank_name DEFAULT N'Bronze',
        avatar NVARCHAR(64) NOT NULL CONSTRAINT DF_lobby_players_avatar DEFAULT N'default',
        selected_deck_json NVARCHAR(MAX) NOT NULL CONSTRAINT DF_lobby_players_selected_deck DEFAULT N'[]',
        connection_state NVARCHAR(32) NOT NULL CONSTRAINT DF_lobby_players_connection_state DEFAULT N'Connected',
        joined_at_utc DATETIME2 NOT NULL CONSTRAINT DF_lobby_players_joined_at DEFAULT SYSUTCDATETIME(),
        updated_at_utc DATETIME2 NOT NULL CONSTRAINT DF_lobby_players_updated_at DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_lobby_players PRIMARY KEY (room_code, player_id),
        CONSTRAINT UQ_lobby_players_room_seat UNIQUE (room_code, seat_index),
        CONSTRAINT FK_lobby_players_public_lobbies FOREIGN KEY (room_code) REFERENCES dbo.public_lobbies(room_code) ON DELETE CASCADE,
        CONSTRAINT FK_lobby_players_players FOREIGN KEY (player_id) REFERENCES dbo.players(id) ON DELETE CASCADE
    );
END;

IF OBJECT_ID(N'dbo.match_state', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.match_state (
        room_code NVARCHAR(16) NOT NULL PRIMARY KEY,
        host_player_id UNIQUEIDENTIFIER NOT NULL,
        revision INT NOT NULL CONSTRAINT DF_match_state_revision DEFAULT 0,
        snapshot_json NVARCHAR(MAX) NOT NULL CONSTRAINT DF_match_state_snapshot DEFAULT N'',
        current_round INT NOT NULL CONSTRAINT DF_match_state_current_round DEFAULT 0,
        current_participant_index INT NOT NULL CONSTRAINT DF_match_state_current_participant_index DEFAULT 0,
        neutral_influence INT NOT NULL CONSTRAINT DF_match_state_neutral_influence DEFAULT 0,
        phase NVARCHAR(32) NOT NULL CONSTRAINT DF_match_state_phase DEFAULT N'',
        result NVARCHAR(32) NOT NULL CONSTRAINT DF_match_state_result DEFAULT N'',
        result_reason NVARCHAR(MAX) NOT NULL CONSTRAINT DF_match_state_result_reason DEFAULT N'',
        current_turn_time_left FLOAT NOT NULL CONSTRAINT DF_match_state_turn_time_left DEFAULT 0,
        current_turn_duration FLOAT NOT NULL CONSTRAINT DF_match_state_turn_duration DEFAULT 0,
        current_action_points_remaining INT NOT NULL CONSTRAINT DF_match_state_action_points DEFAULT 0,
        winner_participant_id INT NULL,
        created_at_utc DATETIME2 NOT NULL CONSTRAINT DF_match_state_created_at DEFAULT SYSUTCDATETIME(),
        updated_at_utc DATETIME2 NOT NULL CONSTRAINT DF_match_state_updated_at DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_match_state_public_lobbies FOREIGN KEY (room_code) REFERENCES dbo.public_lobbies(room_code) ON DELETE CASCADE,
        CONSTRAINT FK_match_state_players FOREIGN KEY (host_player_id) REFERENCES dbo.players(id)
    );
END;

IF OBJECT_ID(N'dbo.match_players', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.match_players (
        room_code NVARCHAR(16) NOT NULL,
        player_id UNIQUEIDENTIFIER NOT NULL,
        seat_index INT NOT NULL,
        participant_id INT NOT NULL,
        nickname NVARCHAR(64) NOT NULL,
        political_points INT NOT NULL CONSTRAINT DF_match_players_political_points DEFAULT 0,
        influence INT NOT NULL CONSTRAINT DF_match_players_influence DEFAULT 0,
        hand_card_ids_json NVARCHAR(MAX) NOT NULL CONSTRAINT DF_match_players_hand DEFAULT N'[]',
        deck_count INT NOT NULL CONSTRAINT DF_match_players_deck_count DEFAULT 0,
        is_current_turn BIT NOT NULL CONSTRAINT DF_match_players_is_current_turn DEFAULT 0,
        disconnected BIT NOT NULL CONSTRAINT DF_match_players_disconnected DEFAULT 0,
        status NVARCHAR(32) NOT NULL CONSTRAINT DF_match_players_status DEFAULT N'',
        state_json NVARCHAR(MAX) NOT NULL CONSTRAINT DF_match_players_state_json DEFAULT N'',
        updated_at_utc DATETIME2 NOT NULL CONSTRAINT DF_match_players_updated_at DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_match_players PRIMARY KEY (room_code, player_id),
        CONSTRAINT FK_match_players_public_lobbies FOREIGN KEY (room_code) REFERENCES dbo.public_lobbies(room_code) ON DELETE CASCADE,
        CONSTRAINT FK_match_players_players FOREIGN KEY (player_id) REFERENCES dbo.players(id) ON DELETE CASCADE
    );
END;

IF OBJECT_ID(N'dbo.match_commands', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.match_commands (
        command_id BIGINT NOT NULL IDENTITY(1,1) PRIMARY KEY,
        room_code NVARCHAR(16) NOT NULL,
        player_id UNIQUEIDENTIFIER NOT NULL,
        payload NVARCHAR(MAX) NOT NULL CONSTRAINT DF_match_commands_payload DEFAULT N'',
        created_at_utc DATETIME2 NOT NULL CONSTRAINT DF_match_commands_created_at DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_match_commands_public_lobbies FOREIGN KEY (room_code) REFERENCES dbo.public_lobbies(room_code) ON DELETE CASCADE,
        CONSTRAINT FK_match_commands_players FOREIGN KEY (player_id) REFERENCES dbo.players(id) ON DELETE CASCADE
    );
END;
""";

        await ExecuteAsync(connection, null, sql);
    }

    private static async Task<bool> ExistsAsync(SqlConnection connection, SqlTransaction? transaction, string sql, params SqlParameter[] parameters)
    {
        await using SqlCommand command = new(sql, connection, transaction);
        command.Parameters.AddRange(parameters);
        object? result = await command.ExecuteScalarAsync();
        return result is not null && result != DBNull.Value;
    }

    private static async Task<int> CountAsync(SqlConnection connection, SqlTransaction? transaction, string sql, params SqlParameter[] parameters)
    {
        await using SqlCommand command = new(sql, connection, transaction);
        command.Parameters.AddRange(parameters);
        object? result = await command.ExecuteScalarAsync();
        return result == null || result == DBNull.Value ? 0 : Convert.ToInt32(result);
    }

    private static async Task<int> ExecuteAsync(SqlConnection connection, SqlTransaction? transaction, string sql, params SqlParameter[] parameters)
    {
        await using SqlCommand command = new(sql, connection, transaction);
        command.Parameters.AddRange(parameters);
        return await command.ExecuteNonQueryAsync();
    }

    private sealed class ParsedSnapshot
    {
        public int CurrentRound { get; set; }
        public int CurrentParticipantIndex { get; set; }
        public int NeutralInfluence { get; set; }
        public string Phase { get; set; } = string.Empty;
        public string Result { get; set; } = string.Empty;
        public string ResultReason { get; set; } = string.Empty;
        public float CurrentTurnTimeLeft { get; set; }
        public float CurrentTurnDuration { get; set; }
        public int CurrentActionPointsRemaining { get; set; }
        public int? WinnerParticipantId { get; set; }
        public List<ParsedSnapshotPlayer> Players { get; } = [];
    }

    private sealed class ParsedSnapshotPlayer
    {
        public Guid PlayerId { get; set; }
        public int SeatIndex { get; set; }
        public int ParticipantId { get; set; }
        public string Nickname { get; set; } = string.Empty;
        public int PoliticalPoints { get; set; }
        public int Influence { get; set; }
        public List<int> HandCardIds { get; set; } = [];
        public int DeckCount { get; set; }
        public bool IsCurrentTurn { get; set; }
        public bool Disconnected { get; set; }
        public string Status { get; set; } = string.Empty;
        public string StateJson { get; set; } = string.Empty;
    }
}

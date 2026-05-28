namespace ParlamB.Api.Contracts;

public sealed class OnlineLobbyPlayerDto
{
    public string PlayerId { get; set; } = string.Empty;
    public string Nickname { get; set; } = string.Empty;
    public int SeatIndex { get; set; }
    public bool IsHost { get; set; }
    public bool IsReady { get; set; }
    public int Level { get; set; } = 1;
    public string Rank { get; set; } = "Bronze";
    public string Avatar { get; set; } = "default";
    public List<int> SelectedDeckCardIds { get; set; } = new();
    public string ConnectionState { get; set; } = "Connected";
}

public sealed class OnlineLobbyStateDto
{
    public string RoomCode { get; set; } = string.Empty;
    public int MaxPlayers { get; set; } = 4;
    public bool Started { get; set; }
    public string HostPlayerId { get; set; } = string.Empty;
    public List<OnlineLobbyPlayerDto> Players { get; set; } = new();
}

public sealed class HostOnlineLobbyRequest
{
    public string Nickname { get; set; } = string.Empty;
    public int Level { get; set; } = 1;
    public string Rank { get; set; } = "Bronze";
    public string Avatar { get; set; } = "default";
    public List<int> SelectedDeckCardIds { get; set; } = new();
    public int MaxPlayers { get; set; } = 4;
}

public sealed class JoinOnlineLobbyRequest
{
    public string RoomCode { get; set; } = string.Empty;
    public string Nickname { get; set; } = string.Empty;
    public int Level { get; set; } = 1;
    public string Rank { get; set; } = "Bronze";
    public string Avatar { get; set; } = "default";
    public List<int> SelectedDeckCardIds { get; set; } = new();
}

public sealed class SetLobbyReadyRequest
{
    public bool Ready { get; set; }
}

public sealed class SubmitMatchCommandRequest
{
    public string Payload { get; set; } = string.Empty;
}

public sealed class MatchCommandDto
{
    public long CommandId { get; set; }
    public string RoomCode { get; set; } = string.Empty;
    public string PlayerId { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class MatchCommandListResponse
{
    public List<MatchCommandDto> Commands { get; set; } = new();
}

public sealed class UpdateMatchSnapshotRequest
{
    public string SnapshotJson { get; set; } = string.Empty;
}

public sealed class MatchSnapshotDto
{
    public string RoomCode { get; set; } = string.Empty;
    public int Revision { get; set; }
    public string SnapshotJson { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; }
}

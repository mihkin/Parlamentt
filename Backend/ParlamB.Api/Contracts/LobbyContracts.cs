namespace ParlamB.Api.Contracts;

public sealed class PublicLobbyDto
{
    public string RoomCode { get; set; } = string.Empty;
    public string HostNickname { get; set; } = string.Empty;
    public string HostAddress { get; set; } = string.Empty;
    public int PlayerCount { get; set; }
    public int MaxPlayers { get; set; }
    public bool Started { get; set; }
}

public sealed class PublicLobbyListResponse
{
    public List<PublicLobbyDto> Lobbies { get; set; } = new();
}

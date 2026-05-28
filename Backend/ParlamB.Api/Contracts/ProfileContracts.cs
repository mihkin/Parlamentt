namespace ParlamB.Api.Contracts;

public sealed class PlayerStatisticsDto
{
    public int TotalMatches { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int OnlineMatches { get; set; }
    public int OfflineMatches { get; set; }
    public int CardsPlayed { get; set; }
    public int TurnsPlayed { get; set; }
}

public sealed class PlayerProfileDto
{
    public Guid PlayerId { get; set; }
    public string Nickname { get; set; } = "Senator";
    public int Level { get; set; } = 1;
    public int Experience { get; set; }
    public int Coins { get; set; } = 500;
    public List<int> OwnedCards { get; set; } = new();
    public List<int> SelectedDeck { get; set; } = new();
    public PlayerStatisticsDto Statistics { get; set; } = new();
    public string Rank { get; set; } = "Bronze";
    public string Avatar { get; set; } = "default";
}

public sealed class CreateOrUpdateProfileRequest
{
    public Guid? PlayerId { get; set; }
    public string Nickname { get; set; } = "Senator";
    public int Level { get; set; } = 1;
    public int Experience { get; set; }
    public int Coins { get; set; } = 500;
    public List<int> OwnedCards { get; set; } = new();
    public List<int> SelectedDeck { get; set; } = new();
    public PlayerStatisticsDto Statistics { get; set; } = new();
    public string Rank { get; set; } = "Bronze";
    public string Avatar { get; set; } = "default";
}

public sealed class UpdateDeckRequest
{
    public List<int> SelectedDeck { get; set; } = new();
}

public sealed class ChangeCoinsRequest
{
    public int Amount { get; set; }
}

public sealed class UnlockCardRequest
{
    public int CardId { get; set; }
}

public sealed class MatchResultRequest
{
    public bool Won { get; set; }
    public bool Online { get; set; }
    public int CoinsReward { get; set; }
}

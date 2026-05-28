namespace ParlamB.Api.Contracts;

public sealed class RegisterRequest
{
    public string Login { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Nickname { get; set; } = "Senator";
}

public sealed class LoginRequest
{
    public string Login { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public sealed class AuthResponseDto
{
    public string Token { get; set; } = string.Empty;
    public Guid PlayerId { get; set; }
    public string Login { get; set; } = string.Empty;
    public PlayerProfileDto Profile { get; set; } = new();
}

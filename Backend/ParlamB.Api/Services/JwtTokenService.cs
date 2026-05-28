using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace ParlamB.Api.Services;

public sealed class JwtTokenService
{
    private readonly IConfiguration configuration;

    public JwtTokenService(IConfiguration configuration)
    {
        this.configuration = configuration;
    }

    public string CreateToken(Guid playerId, string login)
    {
        string issuer = configuration["Jwt:Issuer"] ?? "ParlamB.Api";
        string audience = configuration["Jwt:Audience"] ?? "ParlamB.Client";
        string key = configuration["Jwt:Key"] ?? throw new InvalidOperationException("JWT signing key is missing.");

        SigningCredentials credentials = new(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            SecurityAlgorithms.HmacSha256);

        List<Claim> claims =
        [
            new(JwtRegisteredClaimNames.Sub, playerId.ToString()),
            new(ClaimTypes.NameIdentifier, playerId.ToString()),
            new(ClaimTypes.Name, login),
            new(JwtRegisteredClaimNames.UniqueName, login)
        ];

        JwtSecurityToken token = new(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddDays(7),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

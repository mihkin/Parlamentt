using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.IdentityModel.Tokens;
using ParlamB.Api.Contracts;
using ParlamB.Api.Data;
using ParlamB.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddSingleton<SqlServerProfileStore>();
builder.Services.AddSingleton<SqlServerAuthStore>();
builder.Services.AddSingleton<PasswordHashService>();
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddSingleton<LobbyRegistryService>();
builder.Services.AddSingleton<OnlineSessionStore>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        string issuer = builder.Configuration["Jwt:Issuer"] ?? "ParlamB.Api";
        string audience = builder.Configuration["Jwt:Audience"] ?? "ParlamB.Client";
        string key = builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("JWT signing key is missing.");

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

using (IServiceScope scope = app.Services.CreateScope())
{
    SqlServerProfileStore profileStore = scope.ServiceProvider.GetRequiredService<SqlServerProfileStore>();
    SqlServerAuthStore authStore = scope.ServiceProvider.GetRequiredService<SqlServerAuthStore>();
    LobbyRegistryService lobbyRegistry = scope.ServiceProvider.GetRequiredService<LobbyRegistryService>();
    OnlineSessionStore onlineSessionStore = scope.ServiceProvider.GetRequiredService<OnlineSessionStore>();
    await profileStore.InitializeAsync();
    await authStore.InitializeAsync();
    await lobbyRegistry.InitializeAsync();
    await onlineSessionStore.InitializeAsync();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }))
    .WithName("HealthCheck");

app.MapGet("/api/lobbies", async (OnlineSessionStore onlineSessionStore) =>
    Results.Ok(new PublicLobbyListResponse { Lobbies = (await onlineSessionStore.ListLobbySummariesAsync()).ToList() }))
    .WithName("GetPublicLobbies");

app.MapPost("/api/lobbies/upsert", async (HttpContext httpContext, PublicLobbyDto lobby, LobbyRegistryService lobbyRegistry) =>
{
    string remoteIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
    await lobbyRegistry.UpsertAsync(lobby, remoteIp);
    return Results.Ok();
})
.WithName("UpsertPublicLobby");

app.MapDelete("/api/lobbies", async (HttpContext httpContext, string roomCode, string hostAddress, LobbyRegistryService lobbyRegistry) =>
{
    string remoteIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
    await lobbyRegistry.RemoveAsync(roomCode, hostAddress, remoteIp);
    return Results.Ok();
})
.WithName("RemovePublicLobby");

app.MapPost("/api/auth/register", async Task<Results<Ok<AuthResponseDto>, Conflict<string>, BadRequest<string>>> (
    RegisterRequest request,
    SqlServerAuthStore authStore,
    JwtTokenService jwtTokenService) =>
{
    if (string.IsNullOrWhiteSpace(request.Login) || string.IsNullOrWhiteSpace(request.Password))
        return TypedResults.BadRequest("Login and password are required.");

    AuthenticatedUser? user = await authStore.RegisterAsync(request);
    if (user is null)
        return TypedResults.Conflict("Login already exists or request is invalid.");

    return TypedResults.Ok(new AuthResponseDto
    {
        Token = jwtTokenService.CreateToken(user.Profile.PlayerId, user.Login),
        PlayerId = user.Profile.PlayerId,
        Login = user.Login,
        Profile = user.Profile
    });
})
.WithName("Register");

app.MapPost("/api/auth/login", async Task<Results<Ok<AuthResponseDto>, UnauthorizedHttpResult, BadRequest<string>>> (
    LoginRequest request,
    SqlServerAuthStore authStore,
    JwtTokenService jwtTokenService) =>
{
    if (string.IsNullOrWhiteSpace(request.Login) || string.IsNullOrWhiteSpace(request.Password))
        return TypedResults.BadRequest("Login and password are required.");

    AuthenticatedUser? user = await authStore.LoginAsync(request);
    if (user is null)
        return TypedResults.Unauthorized();

    return TypedResults.Ok(new AuthResponseDto
    {
        Token = jwtTokenService.CreateToken(user.Profile.PlayerId, user.Login),
        PlayerId = user.Profile.PlayerId,
        Login = user.Login,
        Profile = user.Profile
    });
})
.WithName("Login");

RouteGroupBuilder profileGroup = app.MapGroup("/api/profile").RequireAuthorization();
RouteGroupBuilder onlineGroup = app.MapGroup("/api/online").RequireAuthorization();

profileGroup.MapGet("/me", async Task<Results<Ok<PlayerProfileDto>, UnauthorizedHttpResult, NotFound>> (
    ClaimsPrincipal user,
    SqlServerProfileStore store) =>
{
    Guid? playerId = GetPlayerId(user);
    if (playerId is null)
        return TypedResults.Unauthorized();

    PlayerProfileDto? profile = await store.GetByIdAsync(playerId.Value);
    return profile is null ? TypedResults.NotFound() : TypedResults.Ok(profile);
})
.WithName("GetMyProfile");

profileGroup.MapPut("/me", async Task<Results<Ok<PlayerProfileDto>, UnauthorizedHttpResult, BadRequest<string>>> (
    ClaimsPrincipal user,
    CreateOrUpdateProfileRequest request,
    SqlServerProfileStore store) =>
{
    Guid? playerId = GetPlayerId(user);
    if (playerId is null)
        return TypedResults.Unauthorized();

    if (string.IsNullOrWhiteSpace(request.Nickname))
        return TypedResults.BadRequest("Nickname is required.");

    PlayerProfileDto profile = await store.UpdateProfileAsync(playerId.Value, request);
    return TypedResults.Ok(profile);
})
.WithName("UpdateMyProfile");

profileGroup.MapPut("/me/deck", async Task<Results<Ok<PlayerProfileDto>, UnauthorizedHttpResult, NotFound>> (
    ClaimsPrincipal user,
    UpdateDeckRequest request,
    SqlServerProfileStore store) =>
{
    Guid? playerId = GetPlayerId(user);
    if (playerId is null)
        return TypedResults.Unauthorized();

    PlayerProfileDto? profile = await store.UpdateDeckAsync(playerId.Value, request.SelectedDeck);
    return profile is null ? TypedResults.NotFound() : TypedResults.Ok(profile);
})
.WithName("UpdateMyDeck");

profileGroup.MapPost("/me/coins", async Task<Results<Ok<PlayerProfileDto>, UnauthorizedHttpResult, NotFound>> (
    ClaimsPrincipal user,
    ChangeCoinsRequest request,
    SqlServerProfileStore store) =>
{
    Guid? playerId = GetPlayerId(user);
    if (playerId is null)
        return TypedResults.Unauthorized();

    PlayerProfileDto? profile = await store.ChangeCoinsAsync(playerId.Value, request.Amount);
    return profile is null ? TypedResults.NotFound() : TypedResults.Ok(profile);
})
.WithName("ChangeMyCoins");

profileGroup.MapPost("/me/unlock-card", async Task<Results<Ok<PlayerProfileDto>, UnauthorizedHttpResult, NotFound>> (
    ClaimsPrincipal user,
    UnlockCardRequest request,
    SqlServerProfileStore store) =>
{
    Guid? playerId = GetPlayerId(user);
    if (playerId is null)
        return TypedResults.Unauthorized();

    PlayerProfileDto? profile = await store.UnlockCardAsync(playerId.Value, request.CardId);
    return profile is null ? TypedResults.NotFound() : TypedResults.Ok(profile);
})
.WithName("UnlockMyCard");

profileGroup.MapPost("/me/match-result", async Task<Results<Ok<PlayerProfileDto>, UnauthorizedHttpResult, NotFound>> (
    ClaimsPrincipal user,
    MatchResultRequest request,
    SqlServerProfileStore store) =>
{
    Guid? playerId = GetPlayerId(user);
    if (playerId is null)
        return TypedResults.Unauthorized();

    PlayerProfileDto? profile = await store.RecordMatchResultAsync(playerId.Value, request);
    return profile is null ? TypedResults.NotFound() : TypedResults.Ok(profile);
})
.WithName("RecordMyMatchResult");

onlineGroup.MapPost("/lobbies/host", async Task<Results<Ok<OnlineLobbyStateDto>, UnauthorizedHttpResult, BadRequest<string>>> (
    ClaimsPrincipal user,
    HostOnlineLobbyRequest request,
    OnlineSessionStore store) =>
{
    Guid? playerId = GetPlayerId(user);
    if (playerId is null)
        return TypedResults.Unauthorized();

    if (string.IsNullOrWhiteSpace(request.Nickname))
        return TypedResults.BadRequest("Nickname is required.");

    OnlineLobbyStateDto lobby = await store.HostLobbyAsync(playerId.Value, request);
    return TypedResults.Ok(lobby);
});

onlineGroup.MapGet("/lobbies", async Task<Ok<PublicLobbyListResponse>> (OnlineSessionStore store) =>
    TypedResults.Ok(new PublicLobbyListResponse { Lobbies = (await store.ListLobbySummariesAsync()).ToList() }));

onlineGroup.MapGet("/lobbies/{roomCode}", async Task<Results<Ok<OnlineLobbyStateDto>, UnauthorizedHttpResult, NotFound>> (
    ClaimsPrincipal user,
    string roomCode,
    OnlineSessionStore store) =>
{
    Guid? playerId = GetPlayerId(user);
    if (playerId is null)
        return TypedResults.Unauthorized();

    OnlineLobbyStateDto? lobby = await store.GetLobbyAsync(roomCode);
    return lobby is null ? TypedResults.NotFound() : TypedResults.Ok(lobby);
});

onlineGroup.MapPost("/lobbies/join", async Task<Results<Ok<OnlineLobbyStateDto>, UnauthorizedHttpResult, BadRequest<string>, NotFound>> (
    ClaimsPrincipal user,
    JoinOnlineLobbyRequest request,
    OnlineSessionStore store) =>
{
    Guid? playerId = GetPlayerId(user);
    if (playerId is null)
        return TypedResults.Unauthorized();

    if (string.IsNullOrWhiteSpace(request.RoomCode))
        return TypedResults.BadRequest("Room code is required.");

    OnlineLobbyStateDto? lobby = await store.JoinLobbyAsync(playerId.Value, request);
    return lobby is null ? TypedResults.NotFound() : TypedResults.Ok(lobby);
});

onlineGroup.MapPost("/lobbies/{roomCode}/ready", async Task<Results<Ok<OnlineLobbyStateDto>, UnauthorizedHttpResult, NotFound>> (
    ClaimsPrincipal user,
    string roomCode,
    SetLobbyReadyRequest request,
    OnlineSessionStore store) =>
{
    Guid? playerId = GetPlayerId(user);
    if (playerId is null)
        return TypedResults.Unauthorized();

    OnlineLobbyStateDto? lobby = await store.SetReadyAsync(playerId.Value, roomCode, request.Ready);
    return lobby is null ? TypedResults.NotFound() : TypedResults.Ok(lobby);
});

onlineGroup.MapPost("/lobbies/{roomCode}/start", async Task<Results<Ok<OnlineLobbyStateDto>, UnauthorizedHttpResult, NotFound>> (
    ClaimsPrincipal user,
    string roomCode,
    OnlineSessionStore store) =>
{
    Guid? playerId = GetPlayerId(user);
    if (playerId is null)
        return TypedResults.Unauthorized();

    OnlineLobbyStateDto? lobby = await store.StartMatchAsync(playerId.Value, roomCode);
    return lobby is null ? TypedResults.NotFound() : TypedResults.Ok(lobby);
});

onlineGroup.MapDelete("/lobbies/{roomCode}/leave", async Task<Results<Ok, UnauthorizedHttpResult>> (
    ClaimsPrincipal user,
    string roomCode,
    OnlineSessionStore store) =>
{
    Guid? playerId = GetPlayerId(user);
    if (playerId is null)
        return TypedResults.Unauthorized();

    await store.LeaveLobbyAsync(playerId.Value, roomCode);
    return TypedResults.Ok();
});

onlineGroup.MapGet("/matches/{roomCode}/snapshot", async Task<Results<Ok<MatchSnapshotDto>, UnauthorizedHttpResult, NotFound>> (
    ClaimsPrincipal user,
    string roomCode,
    OnlineSessionStore store) =>
{
    Guid? playerId = GetPlayerId(user);
    if (playerId is null)
        return TypedResults.Unauthorized();

    MatchSnapshotDto? snapshot = await store.GetSnapshotAsync(playerId.Value, roomCode);
    return snapshot is null ? TypedResults.NotFound() : TypedResults.Ok(snapshot);
});

onlineGroup.MapPut("/matches/{roomCode}/snapshot", async Task<Results<Ok<MatchSnapshotDto>, UnauthorizedHttpResult, NotFound>> (
    ClaimsPrincipal user,
    string roomCode,
    UpdateMatchSnapshotRequest request,
    OnlineSessionStore store) =>
{
    Guid? playerId = GetPlayerId(user);
    if (playerId is null)
        return TypedResults.Unauthorized();

    MatchSnapshotDto? snapshot = await store.UpdateSnapshotAsync(playerId.Value, roomCode, request.SnapshotJson);
    return snapshot is null ? TypedResults.NotFound() : TypedResults.Ok(snapshot);
});

onlineGroup.MapGet("/matches/{roomCode}/commands", async Task<Results<Ok<MatchCommandListResponse>, UnauthorizedHttpResult>> (
    ClaimsPrincipal user,
    string roomCode,
    long? afterId,
    OnlineSessionStore store) =>
{
    Guid? playerId = GetPlayerId(user);
    if (playerId is null)
        return TypedResults.Unauthorized();

    return TypedResults.Ok(new MatchCommandListResponse
    {
        Commands = (await store.GetCommandsAsync(playerId.Value, roomCode, afterId ?? 0)).ToList()
    });
});

onlineGroup.MapPost("/matches/{roomCode}/commands", async Task<Results<Ok<long>, UnauthorizedHttpResult, NotFound>> (
    ClaimsPrincipal user,
    string roomCode,
    SubmitMatchCommandRequest request,
    OnlineSessionStore store) =>
{
    Guid? playerId = GetPlayerId(user);
    if (playerId is null)
        return TypedResults.Unauthorized();

    long? commandId = await store.AddCommandAsync(playerId.Value, roomCode, request.Payload);
    return commandId.HasValue ? TypedResults.Ok(commandId.Value) : TypedResults.NotFound();
});

app.Run();

static Guid? GetPlayerId(ClaimsPrincipal user)
{
    string? raw = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
    return Guid.TryParse(raw, out Guid playerId) ? playerId : null;
}

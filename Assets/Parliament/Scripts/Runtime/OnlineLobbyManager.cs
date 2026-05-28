using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using UnityEngine;

namespace ParliamentGame
{
    [Serializable]
    public sealed class LobbyRoomState
    {
        [SerializeField] private string roomCode;
        [SerializeField] private int maxPlayers = 4;
        [SerializeField] private List<PlayerNetworkData> players = new List<PlayerNetworkData>();

        public string RoomCode => roomCode;
        public int MaxPlayers => maxPlayers;
        public IReadOnlyList<PlayerNetworkData> Players => players;

        public LobbyRoomState(string roomCode, int maxPlayers)
        {
            this.roomCode = roomCode;
            this.maxPlayers = Mathf.Clamp(maxPlayers, 2, 4);
        }

        public bool IsFull => players.Count >= maxPlayers;

        public bool AddPlayer(PlayerNetworkData player)
        {
            if (player == null || IsFull || players.Any(item => item.PlayerId == player.PlayerId))
                return false;

            players.Add(player);
            players.Sort((left, right) => left.SeatIndex.CompareTo(right.SeatIndex));
            return true;
        }

        public void RemovePlayer(string playerId)
        {
            players.RemoveAll(item => item.PlayerId == playerId);
        }

        public PlayerNetworkData FindPlayer(string playerId)
        {
            return players.FirstOrDefault(item => item.PlayerId == playerId);
        }
    }

    public sealed class OnlineLobbyManager : MonoBehaviour
    {
        [Serializable]
        private sealed class ApiSettingsData
        {
            public bool enabled = true;
            public string baseUrl = string.Empty;
        }

        [Serializable]
        private sealed class OnlineLobbyPlayerDto
        {
            public string playerId = string.Empty;
            public string nickname = string.Empty;
            public int seatIndex;
            public bool isHost;
            public bool isReady;
            public int level = 1;
            public string rank = "Bronze";
            public string avatar = "default";
            public int[] selectedDeckCardIds;
            public string connectionState = "Connected";
        }

        [Serializable]
        private sealed class OnlineLobbyStateDto
        {
            public string roomCode = string.Empty;
            public int maxPlayers = 4;
            public bool started;
            public string hostPlayerId = string.Empty;
            public OnlineLobbyPlayerDto[] players;
        }

        [Serializable]
        private sealed class PublicLobbyInfoDto
        {
            public string roomCode = string.Empty;
            public string hostNickname = string.Empty;
            public string hostAddress = string.Empty;
            public int playerCount;
            public int maxPlayers;
            public bool started;
        }

        [Serializable]
        private sealed class HostLobbyRequestDto
        {
            public string nickname = string.Empty;
            public int level = 1;
            public string rank = "Bronze";
            public string avatar = "default";
            public int[] selectedDeckCardIds;
            public int maxPlayers = 4;
        }

        [Serializable]
        private sealed class JoinLobbyRequestDto
        {
            public string roomCode = string.Empty;
            public string nickname = string.Empty;
            public int level = 1;
            public string rank = "Bronze";
            public string avatar = "default";
            public int[] selectedDeckCardIds;
        }

        [Serializable]
        private sealed class ReadyRequestDto
        {
            public bool ready;
        }

        [Serializable]
        private sealed class MatchSnapshotDto
        {
            public string roomCode = string.Empty;
            public int revision;
            public string snapshotJson = string.Empty;
        }

        [Serializable]
        private sealed class UpdateMatchSnapshotRequestDto
        {
            public string snapshotJson = string.Empty;
        }

        [Serializable]
        private sealed class SubmitMatchCommandRequestDto
        {
            public string payload = string.Empty;
        }

        [Serializable]
        private sealed class MatchCommandDto
        {
            public long commandId;
            public string roomCode = string.Empty;
            public string playerId = string.Empty;
            public string payload = string.Empty;
        }

        [Serializable]
        private sealed class MatchCommandListResponseDto
        {
            public MatchCommandDto[] commands;
        }

        private static readonly HttpClient HttpClient = CreateHttpClient();

        [SerializeField] private float lobbyPollIntervalSeconds = 1f;
        [SerializeField] private float matchPollIntervalSeconds = 0.2f;

        private ApiAuthenticationService authService;
        private string apiBaseUrl = string.Empty;
        private Coroutine lobbyPollCoroutine;
        private Coroutine hostCommandPollCoroutine;
        private Coroutine clientSnapshotPollCoroutine;
        private bool currentLobbyStarted;
        private int lastSnapshotRevision;
        private long lastCommandId;

        public event Action<LobbyRoomState> LobbyChanged;
        public event Action<LobbyRoomState> LobbyStarted;
        public event Action<string> LobbyError;
        public event Action<PlayerNetworkData> PlayerDisconnected;
        public event Action<string> GameplayCommandReceived;
        public event Action<string> GameplaySnapshotReceived;

        public LobbyRoomState CurrentRoom { get; private set; }
        public PlayerNetworkData LocalPlayer { get; private set; }
        public string LocalPlayerId => authService == null ? string.Empty : authService.CurrentPlayerId;
        public bool IsHost => LocalPlayer != null && LocalPlayer.IsHost;
        public string HostJoinAddress => CurrentRoom == null ? string.Empty : CurrentRoom.RoomCode;

        private void Awake()
        {
            LoadApiBaseUrl();
            EnsureAuthService();
        }

        private void OnDestroy()
        {
            StopAllPolling();
        }

        public void HostLobby(string nickname, int level, string rank, string avatar, IReadOnlyList<int> selectedDeckCardIds, int maxPlayers)
        {
            if (!EnsureReadyForOnline())
                return;

            HostLobbyRequestDto request = new HostLobbyRequestDto
            {
                nickname = nickname ?? string.Empty,
                level = level,
                rank = rank ?? "Bronze",
                avatar = avatar ?? "default",
                selectedDeckCardIds = selectedDeckCardIds == null ? Array.Empty<int>() : selectedDeckCardIds.ToArray(),
                maxPlayers = maxPlayers
            };

            OnlineLobbyStateDto response = SendJson<OnlineLobbyStateDto>(HttpMethod.Post, "/api/online/lobbies/host", JsonUtility.ToJson(request));
            ApplyLobbyState(response, raiseStartedEvent: false);
        }

        public bool JoinLobby(string roomCodeOrAddress, string nickname, int level, string rank, string avatar, IReadOnlyList<int> selectedDeckCardIds)
        {
            if (!EnsureReadyForOnline())
                return false;

            JoinLobbyRequestDto request = new JoinLobbyRequestDto
            {
                roomCode = NormalizeRoomCode(roomCodeOrAddress),
                nickname = nickname ?? string.Empty,
                level = level,
                rank = rank ?? "Bronze",
                avatar = avatar ?? "default",
                selectedDeckCardIds = selectedDeckCardIds == null ? Array.Empty<int>() : selectedDeckCardIds.ToArray()
            };

            if (string.IsNullOrWhiteSpace(request.roomCode))
            {
                LobbyError?.Invoke("Введите код комнаты.");
                return false;
            }

            OnlineLobbyStateDto response = SendJson<OnlineLobbyStateDto>(HttpMethod.Post, "/api/online/lobbies/join", JsonUtility.ToJson(request));
            ApplyLobbyState(response, raiseStartedEvent: false);
            return response != null;
        }

        public void SetReady(bool ready)
        {
            if (!EnsureReadyForOnline() || CurrentRoom == null)
                return;

            ReadyRequestDto request = new ReadyRequestDto { ready = ready };
            OnlineLobbyStateDto response = SendJson<OnlineLobbyStateDto>(HttpMethod.Post, $"/api/online/lobbies/{CurrentRoom.RoomCode}/ready", JsonUtility.ToJson(request));
            ApplyLobbyState(response, raiseStartedEvent: false);
        }

        public void LeaveLobby()
        {
            if (!EnsureReadyForOnline() || CurrentRoom == null)
            {
                ClearLobbyState();
                return;
            }

            try
            {
                SendWithoutResponse(HttpMethod.Delete, $"/api/online/lobbies/{CurrentRoom.RoomCode}/leave");
            }
            catch (Exception exception)
            {
                LobbyError?.Invoke(string.IsNullOrWhiteSpace(exception.Message) ? "Не удалось покинуть лобби." : exception.Message);
            }

            ClearLobbyState();
        }

        public void SendGameplayCommandToHost(string payload)
        {
            if (!EnsureReadyForOnline() || CurrentRoom == null || !currentLobbyStarted || string.IsNullOrWhiteSpace(payload))
                return;

            SubmitMatchCommandRequestDto request = new SubmitMatchCommandRequestDto { payload = payload };
            try
            {
                SendJson<long>(HttpMethod.Post, $"/api/online/matches/{CurrentRoom.RoomCode}/commands", JsonUtility.ToJson(request));
            }
            catch (Exception exception)
            {
                LobbyError?.Invoke(string.IsNullOrWhiteSpace(exception.Message) ? "Не удалось отправить команду матча." : exception.Message);
            }
        }

        public void BroadcastGameplaySnapshot(string payload)
        {
            if (!EnsureReadyForOnline() || CurrentRoom == null || !IsHost)
                return;

            UpdateMatchSnapshotRequestDto request = new UpdateMatchSnapshotRequestDto { snapshotJson = payload ?? string.Empty };
            try
            {
                MatchSnapshotDto snapshot = SendJson<MatchSnapshotDto>(HttpMethod.Put, $"/api/online/matches/{CurrentRoom.RoomCode}/snapshot", JsonUtility.ToJson(request));
                if (snapshot != null)
                    lastSnapshotRevision = Mathf.Max(lastSnapshotRevision, snapshot.revision);
            }
            catch (Exception exception)
            {
                LobbyError?.Invoke(string.IsNullOrWhiteSpace(exception.Message) ? "Не удалось сохранить состояние матча." : exception.Message);
            }
        }

        public bool TryStartMatch()
        {
            if (!EnsureReadyForOnline() || CurrentRoom == null || !IsHost)
                return false;

            OnlineLobbyStateDto response = SendJson<OnlineLobbyStateDto>(HttpMethod.Post, $"/api/online/lobbies/{CurrentRoom.RoomCode}/start", null);
            ApplyLobbyState(response, raiseStartedEvent: true);
            return response != null;
        }

        private void EnsureAuthService()
        {
            if (authService != null)
                return;

            authService = FindObjectOfType<ApiAuthenticationService>();
        }

        private bool EnsureReadyForOnline()
        {
            EnsureAuthService();

            if (string.IsNullOrWhiteSpace(apiBaseUrl))
            {
                LobbyError?.Invoke("API онлайна не настроен.");
                return false;
            }

            if (authService == null || !authService.IsAuthenticated || string.IsNullOrWhiteSpace(authService.CurrentAccessToken))
            {
                LobbyError?.Invoke("Сначала войдите в аккаунт.");
                return false;
            }

            return true;
        }

        private void ApplyLobbyState(OnlineLobbyStateDto dto, bool raiseStartedEvent)
        {
            if (dto == null)
                return;

            bool wasStarted = currentLobbyStarted;
            CurrentRoom = CreateRoomState(dto);
            LocalPlayer = CurrentRoom == null ? null : CurrentRoom.FindPlayer(LocalPlayerId);
            currentLobbyStarted = dto.started;

            RestartPolling();
            LobbyChanged?.Invoke(CurrentRoom);

            if (raiseStartedEvent && currentLobbyStarted)
                LobbyStarted?.Invoke(CurrentRoom);
            else if (!wasStarted && currentLobbyStarted)
                LobbyStarted?.Invoke(CurrentRoom);
        }

        private void ClearLobbyState()
        {
            StopAllPolling();
            CurrentRoom = null;
            LocalPlayer = null;
            currentLobbyStarted = false;
            lastSnapshotRevision = 0;
            lastCommandId = 0;
            LobbyChanged?.Invoke(null);
        }

        private LobbyRoomState CreateRoomState(OnlineLobbyStateDto dto)
        {
            if (dto == null)
                return null;

            LobbyRoomState room = new LobbyRoomState(dto.roomCode ?? string.Empty, dto.maxPlayers);
            if (dto.players != null)
            {
                foreach (OnlineLobbyPlayerDto player in dto.players.OrderBy(item => item.seatIndex))
                {
                    if (player == null)
                        continue;

                    room.AddPlayer(new PlayerNetworkData(
                        player.playerId ?? string.Empty,
                        string.IsNullOrWhiteSpace(player.nickname) ? "Senator" : player.nickname,
                        player.seatIndex,
                        player.isHost,
                        player.level,
                        player.rank ?? "Bronze",
                        player.avatar ?? "default",
                        player.selectedDeckCardIds ?? Array.Empty<int>()));

                    PlayerNetworkData added = room.FindPlayer(player.playerId ?? string.Empty);
                    if (added != null)
                    {
                        added.SetReady(player.isReady);
                        added.SetConnectionState(ParseConnectionState(player.connectionState));
                    }
                }
            }

            return room;
        }

        private void RestartPolling()
        {
            StopAllPolling();
            if (CurrentRoom == null)
                return;

            lobbyPollCoroutine = StartCoroutine(LobbyPollCoroutine());
            if (currentLobbyStarted)
            {
                if (IsHost)
                    hostCommandPollCoroutine = StartCoroutine(HostCommandPollCoroutine());
                else
                    clientSnapshotPollCoroutine = StartCoroutine(ClientSnapshotPollCoroutine());
            }
        }

        private void StopAllPolling()
        {
            if (lobbyPollCoroutine != null)
                StopCoroutine(lobbyPollCoroutine);
            if (hostCommandPollCoroutine != null)
                StopCoroutine(hostCommandPollCoroutine);
            if (clientSnapshotPollCoroutine != null)
                StopCoroutine(clientSnapshotPollCoroutine);

            lobbyPollCoroutine = null;
            hostCommandPollCoroutine = null;
            clientSnapshotPollCoroutine = null;
        }

        private IEnumerator LobbyPollCoroutine()
        {
            while (CurrentRoom != null)
            {
                yield return new WaitForSecondsRealtime(Mathf.Max(0.25f, lobbyPollIntervalSeconds));

                OnlineLobbyStateDto lobby = null;
                try
                {
                    lobby = SendJson<OnlineLobbyStateDto>(HttpMethod.Get, $"/api/online/lobbies/{CurrentRoom.RoomCode}", null, suppressErrors: true);
                }
                catch
                {
                }

                if (lobby == null)
                {
                    LobbyError?.Invoke("Лобби больше недоступно.");
                    ClearLobbyState();
                    yield break;
                }

                ApplyLobbyState(lobby, raiseStartedEvent: false);
            }
        }

        private IEnumerator HostCommandPollCoroutine()
        {
            while (CurrentRoom != null && currentLobbyStarted && IsHost)
            {
                yield return new WaitForSecondsRealtime(Mathf.Max(0.1f, matchPollIntervalSeconds));

                MatchCommandListResponseDto response = null;
                try
                {
                    response = SendJson<MatchCommandListResponseDto>(HttpMethod.Get, $"/api/online/matches/{CurrentRoom.RoomCode}/commands?afterId={lastCommandId}", null, suppressErrors: true);
                }
                catch
                {
                }

                if (response?.commands == null)
                    continue;

                foreach (MatchCommandDto command in response.commands.OrderBy(item => item.commandId))
                {
                    lastCommandId = Math.Max(lastCommandId, command.commandId);
                    if (!string.Equals(command.playerId, LocalPlayerId, StringComparison.Ordinal))
                        GameplayCommandReceived?.Invoke(command.payload ?? string.Empty);
                }
            }
        }

        private IEnumerator ClientSnapshotPollCoroutine()
        {
            while (CurrentRoom != null && currentLobbyStarted && !IsHost)
            {
                yield return new WaitForSecondsRealtime(Mathf.Max(0.1f, matchPollIntervalSeconds));

                MatchSnapshotDto snapshot = null;
                try
                {
                    snapshot = SendJson<MatchSnapshotDto>(HttpMethod.Get, $"/api/online/matches/{CurrentRoom.RoomCode}/snapshot", null, suppressErrors: true);
                }
                catch
                {
                }

                if (snapshot == null || snapshot.revision <= lastSnapshotRevision || string.IsNullOrWhiteSpace(snapshot.snapshotJson))
                    continue;

                lastSnapshotRevision = snapshot.revision;
                GameplaySnapshotReceived?.Invoke(snapshot.snapshotJson);
            }
        }

        private TResponse SendJson<TResponse>(HttpMethod method, string relativePath, string json, bool suppressErrors = false)
        {
            using HttpRequestMessage request = CreateAuthorizedRequest(method, relativePath, json);
            using HttpResponseMessage response = HttpClient.SendAsync(request).GetAwaiter().GetResult();
            string responseJson = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode)
            {
                if (!suppressErrors)
                    LobbyError?.Invoke(string.IsNullOrWhiteSpace(responseJson) ? $"HTTP {(int)response.StatusCode}" : responseJson.Trim('"'));
                return default;
            }

            if (typeof(TResponse) == typeof(long))
            {
                object numeric = long.TryParse(responseJson, out long value) ? value : 0L;
                return (TResponse)numeric;
            }

            if (string.IsNullOrWhiteSpace(responseJson))
                return default;

            return JsonUtility.FromJson<TResponse>(responseJson);
        }

        private void SendWithoutResponse(HttpMethod method, string relativePath)
        {
            using HttpRequestMessage request = CreateAuthorizedRequest(method, relativePath, null);
            using HttpResponseMessage response = HttpClient.SendAsync(request).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                string responseJson = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(responseJson) ? $"HTTP {(int)response.StatusCode}" : responseJson.Trim('"'));
            }
        }

        private HttpRequestMessage CreateAuthorizedRequest(HttpMethod method, string relativePath, string json)
        {
            HttpRequestMessage request = new HttpRequestMessage(method, BuildUrl(relativePath));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authService.CurrentAccessToken);
            if (json != null)
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            return request;
        }

        private void LoadApiBaseUrl()
        {
            string settingsPath = Path.Combine(Application.streamingAssetsPath, ApiAuthenticationService.DefaultSettingsFileName);
            if (!File.Exists(settingsPath))
                return;

            try
            {
                ApiSettingsData settings = JsonUtility.FromJson<ApiSettingsData>(File.ReadAllText(settingsPath));
                if (settings != null && settings.enabled && !string.IsNullOrWhiteSpace(settings.baseUrl))
                    apiBaseUrl = settings.baseUrl.Trim().TrimEnd('/');
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Failed to load online API settings: {exception.Message}");
            }
        }

        private string BuildUrl(string relativePath)
        {
            string path = string.IsNullOrWhiteSpace(relativePath) ? string.Empty : relativePath.Trim();
            if (!path.StartsWith("/"))
                path = "/" + path;

            return apiBaseUrl + path;
        }

        private static string NormalizeRoomCode(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string trimmed = value.Trim();
            int separatorIndex = trimmed.IndexOf(':');
            if (separatorIndex > 0 && trimmed.Length <= 16)
                trimmed = trimmed.Substring(0, separatorIndex);

            return trimmed.ToUpperInvariant();
        }

        private static NetworkPlayerConnectionState ParseConnectionState(string value)
        {
            if (string.Equals(value, "Disconnected", StringComparison.OrdinalIgnoreCase))
                return NetworkPlayerConnectionState.Disconnected;
            if (string.Equals(value, "Connecting", StringComparison.OrdinalIgnoreCase))
                return NetworkPlayerConnectionState.Connecting;

            return NetworkPlayerConnectionState.Connected;
        }

        private static HttpClient CreateHttpClient()
        {
            HttpClient client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(3);
            return client;
        }
    }
}

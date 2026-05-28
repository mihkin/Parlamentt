using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using UnityEngine;

namespace ParliamentGame
{
    public sealed class ApiAuthenticationService : MonoBehaviour
    {
        public const string DefaultSettingsFileName = "api-profile-settings.json";

        private const string SessionCacheFileName = "api_auth_session_cache.json";
        private static readonly HttpClient HttpClient = CreateHttpClient();

        [Serializable]
        private sealed class ApiSettingsData
        {
            public bool enabled = true;
            public string baseUrl = "http://localhost:5268";
            public bool autoLoginWithSavedToken = true;
        }

        [Serializable]
        private sealed class ApiSessionCacheData
        {
            public string token = string.Empty;
            public string playerId = string.Empty;
            public string login = string.Empty;
            public PlayerProfileData profile;
        }

        [Serializable]
        private sealed class AuthRequestDto
        {
            public string login = string.Empty;
            public string password = string.Empty;
            public string nickname = string.Empty;
        }

        [Serializable]
        private sealed class AuthResponseDto
        {
            public string token = string.Empty;
            public string playerId = string.Empty;
            public string login = string.Empty;
            public ApiProfileDto profile;
        }

        [Serializable]
        internal sealed class ApiStatisticsDto
        {
            public int totalMatches;
            public int wins;
            public int losses;
            public int onlineMatches;
            public int offlineMatches;
            public int cardsPlayed;
            public int turnsPlayed;
        }

        [Serializable]
        internal sealed class ApiProfileDto
        {
            public string playerId = string.Empty;
            public string nickname = "Senator";
            public int level = 1;
            public int experience;
            public int coins = 500;
            public int[] ownedCards;
            public int[] selectedDeck;
            public ApiStatisticsDto statistics;
            public string rank = "Bronze";
            public string avatar = "default";
        }

        private ApiSettingsData settings = new ApiSettingsData();
        private PlayerProfileData cachedProfile;
        private string token = string.Empty;
        private string currentPlayerId = string.Empty;
        private string currentLogin = string.Empty;

        public event Action AuthenticationChanged;

        public bool IsEnabled => settings != null && settings.enabled && !string.IsNullOrWhiteSpace(settings.baseUrl);
        public bool IsAuthenticated => !string.IsNullOrWhiteSpace(token);
        public bool HasCachedProfile => cachedProfile != null;
        public string CurrentLogin => currentLogin ?? string.Empty;
        public string CurrentPlayerId => currentPlayerId ?? string.Empty;
        public string CurrentAccessToken => token ?? string.Empty;

        private void Awake()
        {
            LoadSettings();
            RestoreSession();
        }

        public ApiAuthOperationResult Login(string login, string password)
        {
            if (!IsEnabled)
                return ApiAuthOperationResult.Fail("API авторизации отключен.");

            AuthRequestDto payload = new AuthRequestDto
            {
                login = Normalize(login),
                password = password ?? string.Empty
            };

            return Authenticate("/api/auth/login", payload, "Не удалось выполнить вход.");
        }

        public ApiAuthOperationResult Register(string login, string password, string nickname)
        {
            if (!IsEnabled)
                return ApiAuthOperationResult.Fail("API авторизации отключен.");

            AuthRequestDto payload = new AuthRequestDto
            {
                login = Normalize(login),
                password = password ?? string.Empty,
                nickname = NormalizeNickname(nickname, login)
            };

            return Authenticate("/api/auth/register", payload, "Не удалось зарегистрировать аккаунт.");
        }

        public void Logout()
        {
            ClearSession(notify: true, deleteCacheFile: true);
        }

        internal bool TryLoadProfile(out PlayerProfileData profile)
        {
            profile = cachedProfile == null ? null : PlayerProfileDatabase.CloneProfile(cachedProfile);
            return profile != null;
        }

        internal bool SaveProfile(PlayerProfileData profile)
        {
            if (!IsEnabled || !IsAuthenticated || profile == null)
                return false;

            try
            {
                using HttpRequestMessage request = CreateAuthorizedRequest(HttpMethod.Put, "/api/profile/me", JsonUtility.ToJson(ApiProfileMapper.ToUpdateRequest(profile)));
                using HttpResponseMessage response = HttpClient.SendAsync(request).GetAwaiter().GetResult();
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    Debug.LogWarning("API profile save returned 401. Preserving cached session.");
                    return false;
                }

                if (!response.IsSuccessStatusCode)
                    return false;

                string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                ApiProfileDto dto = JsonUtility.FromJson<ApiProfileDto>(json);
                if (dto == null)
                    return false;

                cachedProfile = ApiProfileMapper.ToProfileData(dto);
                WriteSessionCache();
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"API profile save failed: {exception.Message}");
                return false;
            }
        }

        private ApiAuthOperationResult Authenticate(string relativePath, AuthRequestDto payload, string fallbackError)
        {
            if (string.IsNullOrWhiteSpace(payload.login) || string.IsNullOrWhiteSpace(payload.password))
                return ApiAuthOperationResult.Fail("Логин и пароль обязательны.");

            try
            {
                using HttpRequestMessage request = CreateJsonRequest(HttpMethod.Post, relativePath, JsonUtility.ToJson(payload));
                using HttpResponseMessage response = HttpClient.SendAsync(request).GetAwaiter().GetResult();
                string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                if (!response.IsSuccessStatusCode)
                    return ApiAuthOperationResult.Fail(ExtractApiError(response.StatusCode, json, fallbackError));

                AuthResponseDto authResponse = JsonUtility.FromJson<AuthResponseDto>(json);
                if (authResponse == null || string.IsNullOrWhiteSpace(authResponse.token))
                    return ApiAuthOperationResult.Fail(fallbackError);

                ApplyAuthResponse(authResponse, notify: true);
                return ApiAuthOperationResult.SuccessResult();
            }
            catch (Exception exception)
            {
                return ApiAuthOperationResult.Fail(string.IsNullOrWhiteSpace(exception.Message) ? fallbackError : exception.Message);
            }
        }

        private void LoadSettings()
        {
            settings = new ApiSettingsData();
            string settingsPath = Path.Combine(Application.streamingAssetsPath, DefaultSettingsFileName);
            if (!File.Exists(settingsPath))
                return;

            try
            {
                ApiSettingsData loaded = JsonUtility.FromJson<ApiSettingsData>(File.ReadAllText(settingsPath));
                if (loaded != null)
                    settings = loaded;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Failed to load API settings: {exception.Message}");
            }
        }

        private void RestoreSession()
        {
            string cachePath = GetSessionCachePath();
            if (!File.Exists(cachePath))
                return;

            try
            {
                ApiSessionCacheData cache = JsonUtility.FromJson<ApiSessionCacheData>(File.ReadAllText(cachePath));
                if (cache == null)
                    return;

                token = cache.token ?? string.Empty;
                currentPlayerId = cache.playerId ?? string.Empty;
                currentLogin = cache.login ?? string.Empty;
                cachedProfile = cache.profile == null ? null : PlayerProfileDatabase.CloneProfile(cache.profile);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Failed to restore API session: {exception.Message}");
            }
        }

        private bool TryRefreshProfileFromApi(bool clearSessionOnUnauthorized)
        {
            if (!IsEnabled || !IsAuthenticated)
                return false;

            try
            {
                using HttpRequestMessage request = CreateAuthorizedRequest(HttpMethod.Get, "/api/profile/me");
                using HttpResponseMessage response = HttpClient.SendAsync(request).GetAwaiter().GetResult();
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    if (clearSessionOnUnauthorized || cachedProfile == null)
                    {
                        ClearSession(notify: true, deleteCacheFile: true);
                    }
                    else
                    {
                        Debug.LogWarning("API profile refresh returned 401. Preserving cached session.");
                    }

                    return false;
                }

                if (!response.IsSuccessStatusCode)
                    return false;

                string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                ApiProfileDto dto = JsonUtility.FromJson<ApiProfileDto>(json);
                if (dto == null)
                    return false;

                cachedProfile = ApiProfileMapper.ToProfileData(dto);
                if (string.IsNullOrWhiteSpace(currentPlayerId) && cachedProfile != null)
                    currentPlayerId = cachedProfile.playerId;

                WriteSessionCache();
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Failed to refresh API profile: {exception.Message}");
                return false;
            }
        }

        private void ApplyAuthResponse(AuthResponseDto response, bool notify)
        {
            token = response.token ?? string.Empty;
            currentPlayerId = response.playerId ?? string.Empty;
            currentLogin = response.login ?? string.Empty;
            cachedProfile = response.profile == null ? null : ApiProfileMapper.ToProfileData(response.profile);
            WriteSessionCache();

            if (notify)
                AuthenticationChanged?.Invoke();
        }

        private void ClearSession(bool notify, bool deleteCacheFile)
        {
            token = string.Empty;
            currentPlayerId = string.Empty;
            currentLogin = string.Empty;
            cachedProfile = null;

            if (deleteCacheFile)
            {
                string cachePath = GetSessionCachePath();
                if (File.Exists(cachePath))
                    File.Delete(cachePath);
            }

            if (notify)
                AuthenticationChanged?.Invoke();
        }

        private void WriteSessionCache()
        {
            if (!IsAuthenticated)
                return;

            try
            {
                ApiSessionCacheData cache = new ApiSessionCacheData
                {
                    token = token,
                    playerId = currentPlayerId,
                    login = currentLogin,
                    profile = cachedProfile == null ? null : PlayerProfileDatabase.CloneProfile(cachedProfile)
                };

                string path = GetSessionCachePath();
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Application.persistentDataPath);
                File.WriteAllText(path, JsonUtility.ToJson(cache, true));
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Failed to cache API session: {exception.Message}");
            }
        }

        private string GetSessionCachePath()
        {
            return Path.Combine(Application.persistentDataPath, SessionCacheFileName);
        }

        private string BuildUrl(string relativePath)
        {
            string baseUrl = (settings.baseUrl ?? string.Empty).Trim().TrimEnd('/');
            string path = string.IsNullOrWhiteSpace(relativePath) ? string.Empty : relativePath.Trim();
            if (!path.StartsWith("/"))
                path = "/" + path;

            return baseUrl + path;
        }

        private HttpRequestMessage CreateJsonRequest(HttpMethod method, string relativePath, string json)
        {
            HttpRequestMessage request = new HttpRequestMessage(method, BuildUrl(relativePath));
            if (json != null)
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            return request;
        }

        private HttpRequestMessage CreateAuthorizedRequest(HttpMethod method, string relativePath, string json = null)
        {
            HttpRequestMessage request = CreateJsonRequest(method, relativePath, json);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return request;
        }

        private static HttpClient CreateHttpClient()
        {
            HttpClient client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(3);
            return client;
        }

        private static string ExtractApiError(HttpStatusCode statusCode, string responseBody, string fallbackError)
        {
            if (!string.IsNullOrWhiteSpace(responseBody))
                return responseBody.Trim('"', ' ', '\r', '\n', '\t');

            switch (statusCode)
            {
                case HttpStatusCode.BadRequest:
                    return "Некорректный запрос к API.";
                case HttpStatusCode.Conflict:
                    return "Аккаунт с таким логином или ником уже существует.";
                case HttpStatusCode.Unauthorized:
                    return "Неверный логин или пароль.";
                default:
                    return fallbackError;
            }
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static string NormalizeNickname(string nickname, string login)
        {
            string normalizedNickname = Normalize(nickname);
            if (!string.IsNullOrWhiteSpace(normalizedNickname))
                return normalizedNickname;

            string normalizedLogin = Normalize(login);
            return string.IsNullOrWhiteSpace(normalizedLogin) ? "Senator" : normalizedLogin;
        }
    }

    public sealed class ApiAuthOperationResult
    {
        public bool Success { get; private set; }
        public string ErrorMessage { get; private set; } = string.Empty;

        public static ApiAuthOperationResult SuccessResult()
        {
            return new ApiAuthOperationResult { Success = true };
        }

        public static ApiAuthOperationResult Fail(string errorMessage)
        {
            return new ApiAuthOperationResult
            {
                Success = false,
                ErrorMessage = string.IsNullOrWhiteSpace(errorMessage) ? "Не удалось выполнить запрос." : errorMessage
            };
        }
    }
}

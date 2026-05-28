using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace ParliamentGame
{
    public sealed class LobbyUIController : MonoBehaviour
    {
        [Serializable]
        private sealed class ApiSettingsData
        {
            public bool enabled = true;
            public string baseUrl = string.Empty;
        }

        [Serializable]
        private sealed class PublicLobbyInfo
        {
            public string roomCode = string.Empty;
            public string hostNickname = string.Empty;
            public string hostAddress = string.Empty;
            public int playerCount;
            public int maxPlayers;
            public bool started;
        }

        [Serializable]
        private sealed class PublicLobbyListResponse
        {
            public List<PublicLobbyInfo> lobbies = new List<PublicLobbyInfo>();
        }

        [Serializable]
        private sealed class PublicLobbyPublishRequest
        {
            public string roomCode = string.Empty;
            public string hostNickname = string.Empty;
            public string hostAddress = string.Empty;
            public int playerCount;
            public int maxPlayers;
            public bool started;
        }

        [SerializeField] private OnlineLobbyManager lobbyManager;
        [SerializeField] private LanMatchCoordinator lanMatchCoordinator;
        [SerializeField] private NetworkGameManager networkGameManager;
        [SerializeField] private PlayerProfileDatabase profileDatabase;
        [SerializeField] private RectTransform playerListRoot;
        [SerializeField] private LobbyPlayerItemView playerItemPrefab;
        [SerializeField] private TMP_InputField roomCodeInput;
        [SerializeField] private TMP_Text roomCodeText;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private TMP_Text listTitleText;
        [SerializeField] private Toggle readyToggle;
        [SerializeField] private Button hostButton;
        [SerializeField] private Button joinButton;
        [SerializeField] private Button startButton;
        [SerializeField] private Button leaveButton;
        [SerializeField] private Button openLobbyButton;
        [SerializeField] private GameObject lobbyRoot;

        private readonly List<LobbyPlayerItemView> spawnedItems = new List<LobbyPlayerItemView>();
        private readonly List<PublicLobbyInfo> publicLobbies = new List<PublicLobbyInfo>();

        private bool initialized;
        private string apiBaseUrl = string.Empty;
        private Coroutine refreshCoroutine;
        private Coroutine publishCoroutine;

        private void Start()
        {
            Initialize();
        }

        public void Initialize()
        {
            if (initialized)
                return;

            initialized = true;
            ConfigureInputHints();
            LoadApiBaseUrl();

            if (hostButton != null)
                hostButton.onClick.AddListener(HostLobby);

            if (joinButton != null)
                joinButton.onClick.AddListener(JoinLobby);

            if (startButton != null)
                startButton.onClick.AddListener(StartMatch);

            if (leaveButton != null)
                leaveButton.onClick.AddListener(LeaveLobby);

            if (readyToggle != null)
                readyToggle.onValueChanged.AddListener(value => lobbyManager?.SetReady(value));

            if (openLobbyButton != null)
                openLobbyButton.onClick.AddListener(() => SetLobbyVisible(true));

            if (lobbyManager != null)
            {
                lobbyManager.LobbyChanged += RebuildPlayers;
                lobbyManager.LobbyStarted += OnLobbyStarted;
                lobbyManager.LobbyError += SetStatus;
            }

            if (networkGameManager != null)
                networkGameManager.ActionRejected += SetStatus;
        }

        private void OnDestroy()
        {
            if (initialized && lobbyManager != null)
            {
                lobbyManager.LobbyChanged -= RebuildPlayers;
                lobbyManager.LobbyStarted -= OnLobbyStarted;
                lobbyManager.LobbyError -= SetStatus;
            }

            if (initialized && networkGameManager != null)
                networkGameManager.ActionRejected -= SetStatus;

            if (refreshCoroutine != null)
                StopCoroutine(refreshCoroutine);

            if (publishCoroutine != null)
                StopCoroutine(publishCoroutine);
        }

        public void ShowLobby()
        {
            if (!initialized)
                Initialize();

            SetLobbyVisible(true);
            RebuildPlayers(lobbyManager == null ? null : lobbyManager.CurrentRoom);

            if (lobbyManager == null || lobbyManager.CurrentRoom == null)
            {
                RefreshPublicLobbies();
                if (string.IsNullOrWhiteSpace(apiBaseUrl))
                    SetStatus("Введите IP адрес хоста или выберите комнату из списка.");
                else
                    SetStatus("Доступные лобби загружены. Можно выбрать комнату или ввести адрес вручную.");
            }
        }

        public void HideLobby()
        {
            SetLobbyVisible(false);
            SetStatus(string.Empty);
        }

        private void HostLobby()
        {
            if (lobbyManager == null || profileDatabase?.CurrentProfile == null)
                return;

            PlayerProfileData profile = profileDatabase.CurrentProfile;
            lobbyManager.HostLobby(profile.nickname, profile.level, profile.rank, profile.avatar, profile.selectedDeck, 4);
            ShowLobby();

            if (lobbyManager.CurrentRoom != null)
            {
                SetStatus($"Комната создана. Адрес хоста: {BuildPublishedJoinAddress()}");
                StartPublishingLobby();
            }
        }

        private void JoinLobby()
        {
            if (lobbyManager == null || profileDatabase?.CurrentProfile == null)
                return;

            PlayerProfileData profile = profileDatabase.CurrentProfile;
            if (lobbyManager.JoinLobby(roomCodeInput == null ? string.Empty : roomCodeInput.text, profile.nickname, profile.level, profile.rank, profile.avatar, profile.selectedDeck))
            {
                StopPublishingLobby();
                ShowLobby();
                SetStatus("Подключение к хосту запущено.");
            }
        }

        private void LeaveLobby()
        {
            RemovePublishedLobby();
            StopPublishingLobby();
            lobbyManager?.LeaveLobby();
            RefreshPublicLobbies();
        }

        private void StartMatch()
        {
            lobbyManager?.TryStartMatch();
        }

        private void RebuildPlayers(LobbyRoomState room)
        {
            if (playerListRoot == null)
                return;

            foreach (LobbyPlayerItemView item in spawnedItems)
            {
                if (item != null)
                    Destroy(item.gameObject);
            }

            spawnedItems.Clear();

            if (roomCodeText != null)
                roomCodeText.text = room == null ? "Комната: -" : $"Комната: {room.RoomCode}";

            if (readyToggle != null)
            {
                bool isReady = lobbyManager != null && lobbyManager.LocalPlayer != null && lobbyManager.LocalPlayer.IsReady;
                readyToggle.SetIsOnWithoutNotify(isReady);
            }

            RefreshControls(room);

            if (room == null)
            {
                RebuildPublicLobbyBrowser();
                return;
            }

            if (listTitleText != null)
                listTitleText.text = "Игроки в лобби";

            foreach (PlayerNetworkData player in room.Players)
            {
                LobbyPlayerItemView item = playerItemPrefab != null ? Instantiate(playerItemPrefab, playerListRoot) : CreateFallbackItem();
                item.gameObject.SetActive(true);
                item.Setup(player);
                spawnedItems.Add(item);
            }

            if (lobbyManager != null && lobbyManager.IsHost)
                StartPublishingLobby();
        }

        private void RebuildPublicLobbyBrowser()
        {
            if (listTitleText != null)
                listTitleText.text = "Доступные лобби";

            if (publicLobbies.Count == 0)
            {
                LobbyPlayerItemView emptyItem = playerItemPrefab != null ? Instantiate(playerItemPrefab, playerListRoot) : CreateFallbackItem();
                emptyItem.gameObject.SetActive(true);
                emptyItem.Setup("Лобби не найдены", "Можно создать комнату или подключиться вручную по адресу.", new Color(0.45f, 0.45f, 0.45f, 1f), null);
                spawnedItems.Add(emptyItem);
                return;
            }

            foreach (PublicLobbyInfo lobby in publicLobbies)
            {
                PublicLobbyInfo capturedLobby = lobby;
                LobbyPlayerItemView item = playerItemPrefab != null ? Instantiate(playerItemPrefab, playerListRoot) : CreateFallbackItem();
                item.gameObject.SetActive(true);
                item.Setup(
                    $"{capturedLobby.hostNickname}  •  {capturedLobby.roomCode}",
                    $"{capturedLobby.playerCount}/{capturedLobby.maxPlayers} игроков  •  {capturedLobby.hostAddress}",
                    capturedLobby.started ? new Color(0.68f, 0.35f, 0.18f, 1f) : new Color(0.79f, 0.67f, 0.25f, 1f),
                    () => JoinPublishedLobby(capturedLobby));
                spawnedItems.Add(item);
            }
        }

        private void JoinPublishedLobby(PublicLobbyInfo lobby)
        {
            if (lobby == null || string.IsNullOrWhiteSpace(lobby.roomCode) || profileDatabase?.CurrentProfile == null || lobbyManager == null)
                return;

            PlayerProfileData profile = profileDatabase.CurrentProfile;
            if (roomCodeInput != null)
                roomCodeInput.text = lobby.roomCode;

            if (lobbyManager.JoinLobby(lobby.roomCode, profile.nickname, profile.level, profile.rank, profile.avatar, profile.selectedDeck))
            {
                StopPublishingLobby();
                SetStatus($"Подключение к комнате {lobby.roomCode} запущено.");
                ShowLobby();
            }
        }

        private void OnLobbyStarted(LobbyRoomState room)
        {
            bool started = false;

            if (lanMatchCoordinator != null)
                started = lanMatchCoordinator.BeginMatch(room);
            else if (networkGameManager != null)
            {
                networkGameManager.StartMatch(room);
                started = true;
            }

            RemovePublishedLobby();
            StopPublishingLobby();

            SetStatus(started
                ? "Матч запускается. Загрузка LAN-сцены."
                : "LAN-сцена не найдена в Build Settings.");
        }

        private void SetLobbyVisible(bool visible)
        {
            if (lobbyRoot != null)
                lobbyRoot.SetActive(visible);
        }

        private void SetStatus(string message)
        {
            if (statusText != null)
                statusText.text = message;
        }

        private void ConfigureInputHints()
        {
            if (roomCodeText != null)
                roomCodeText.text = "Комната: -";

            if (roomCodeInput?.placeholder is TMP_Text placeholderText)
                placeholderText.text = "Введите IP адрес или выберите комнату";
        }

        private void RefreshControls(LobbyRoomState room)
        {
            bool inRoom = room != null;
            bool isHost = lobbyManager != null && lobbyManager.IsHost;
            bool canStart = isHost && CanStartMatch(room);

            if (roomCodeInput != null)
                roomCodeInput.gameObject.SetActive(!inRoom);

            if (hostButton != null)
                hostButton.gameObject.SetActive(!inRoom);

            if (joinButton != null)
                joinButton.gameObject.SetActive(!inRoom);

            if (readyToggle != null)
            {
                readyToggle.gameObject.SetActive(inRoom);
                readyToggle.interactable = inRoom;
            }

            if (startButton != null)
            {
                startButton.gameObject.SetActive(inRoom && isHost);
                startButton.interactable = canStart;
            }

            if (leaveButton != null)
                leaveButton.gameObject.SetActive(inRoom);

            GameObject scrollRoot = GetPlayerScrollRoot();
            if (scrollRoot != null)
                scrollRoot.SetActive(true);
        }

        private GameObject GetPlayerScrollRoot()
        {
            if (playerListRoot == null)
                return null;

            Transform viewport = playerListRoot.parent;
            Transform scrollRoot = viewport == null ? null : viewport.parent;
            return scrollRoot == null ? null : scrollRoot.gameObject;
        }

        private static bool CanStartMatch(LobbyRoomState room)
        {
            if (room == null || room.Players == null || room.Players.Count < 2)
                return false;

            foreach (PlayerNetworkData player in room.Players)
            {
                if (player == null)
                    return false;

                if (player.ConnectionState != NetworkPlayerConnectionState.Connected || !player.IsReady)
                    return false;
            }

            return true;
        }

        private LobbyPlayerItemView CreateFallbackItem()
        {
            GameObject root = new GameObject("LobbyPlayerItem", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            root.transform.SetParent(playerListRoot, false);
            return root.AddComponent<LobbyPlayerItemView>();
        }

        private void RefreshPublicLobbies()
        {
            if (string.IsNullOrWhiteSpace(apiBaseUrl))
            {
                publicLobbies.Clear();
                RebuildPublicLobbyBrowser();
                return;
            }

            if (refreshCoroutine != null)
                StopCoroutine(refreshCoroutine);

            refreshCoroutine = StartCoroutine(RefreshPublicLobbiesCoroutine());
        }

        private IEnumerator RefreshPublicLobbiesCoroutine()
        {
            using UnityWebRequest request = UnityWebRequest.Get($"{apiBaseUrl}/api/lobbies");
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                publicLobbies.Clear();
                RebuildPublicLobbyBrowser();
                SetStatus("Не удалось загрузить список лобби.");
                yield break;
            }

            PublicLobbyListResponse response = JsonUtility.FromJson<PublicLobbyListResponse>(request.downloadHandler.text);
            publicLobbies.Clear();
            if (response != null && response.lobbies != null)
            {
                publicLobbies.AddRange(response.lobbies
                    .Where(item => item != null && !item.started && !string.IsNullOrWhiteSpace(item.roomCode))
                    .OrderByDescending(item => item.playerCount)
                    .ThenBy(item => item.hostNickname));
            }

            RebuildPublicLobbyBrowser();
        }

        private void StartPublishingLobby()
        {
            if (!CanPublishLobby())
                return;

            if (publishCoroutine != null)
                StopCoroutine(publishCoroutine);

            publishCoroutine = StartCoroutine(PublishLobbyCoroutine());
        }

        private void StopPublishingLobby()
        {
            if (publishCoroutine != null)
            {
                StopCoroutine(publishCoroutine);
                publishCoroutine = null;
            }
        }

        private IEnumerator PublishLobbyCoroutine()
        {
            while (CanPublishLobby())
            {
                yield return PublishLobbySnapshot();
                yield return new WaitForSecondsRealtime(10f);
            }

            publishCoroutine = null;
        }

        private IEnumerator PublishLobbySnapshot()
        {
            PublicLobbyPublishRequest payload = BuildPublishPayload();
            if (payload == null)
                yield break;

            byte[] body = System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload));
            using UnityWebRequest request = new UnityWebRequest($"{apiBaseUrl}/api/lobbies/upsert", UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            yield return request.SendWebRequest();
        }

        private void RemovePublishedLobby()
        {
            if (!CanPublishLobby())
                return;

            StartCoroutine(RemovePublishedLobbyCoroutine(BuildPublishPayload()));
        }

        private IEnumerator RemovePublishedLobbyCoroutine(PublicLobbyPublishRequest payload)
        {
            if (payload == null)
                yield break;

            string url = $"{apiBaseUrl}/api/lobbies?roomCode={UnityWebRequest.EscapeURL(payload.roomCode)}&hostAddress={UnityWebRequest.EscapeURL(payload.hostAddress)}";
            using UnityWebRequest request = UnityWebRequest.Delete(url);
            yield return request.SendWebRequest();
        }

        private bool CanPublishLobby()
        {
            return !string.IsNullOrWhiteSpace(apiBaseUrl) &&
                lobbyManager != null &&
                lobbyManager.IsHost &&
                lobbyManager.CurrentRoom != null;
        }

        private PublicLobbyPublishRequest BuildPublishPayload()
        {
            if (!CanPublishLobby())
                return null;

            return new PublicLobbyPublishRequest
            {
                roomCode = lobbyManager.CurrentRoom.RoomCode,
                hostNickname = lobbyManager.LocalPlayer == null ? "Host" : lobbyManager.LocalPlayer.Nickname,
                hostAddress = BuildPublishedJoinAddress(),
                playerCount = lobbyManager.CurrentRoom.Players == null ? 0 : lobbyManager.CurrentRoom.Players.Count,
                maxPlayers = lobbyManager.CurrentRoom.MaxPlayers,
                started = false
            };
        }

        private string BuildPublishedJoinAddress()
        {
            return lobbyManager == null ? string.Empty : lobbyManager.HostJoinAddress;
        }

        private static bool TrySplitHostAndPort(string value, out string host, out string port)
        {
            host = string.Empty;
            port = string.Empty;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            int separatorIndex = value.LastIndexOf(':');
            if (separatorIndex <= 0 || separatorIndex >= value.Length - 1)
                return false;

            host = value.Substring(0, separatorIndex).Trim();
            port = value.Substring(separatorIndex + 1).Trim();
            return !string.IsNullOrWhiteSpace(host) && !string.IsNullOrWhiteSpace(port);
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
                Debug.LogWarning($"Failed to load lobby discovery settings: {exception.Message}");
            }
        }
    }
}

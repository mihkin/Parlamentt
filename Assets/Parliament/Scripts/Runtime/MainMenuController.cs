using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ParliamentGame
{
    public sealed class MainMenuController : MonoBehaviour
    {
        [Header("Core Layout")]
        [SerializeField] private Canvas canvas;
        [SerializeField] private Image backgroundImage;
        [SerializeField] private GameObject logoPanel;
        [SerializeField] private GameObject mainButtonsPanel;
        [SerializeField] private GameObject playModePanel;
        [SerializeField] private GameObject settingsPanel;
        [SerializeField] private GameObject collectionPanel;
        [SerializeField] private GameObject shopPanel;
        [SerializeField] private GameObject tutorialPanel;
        [SerializeField] private GameObject profilePanel;
        [SerializeField] private GameObject authPanel;
        [SerializeField] private GameObject currencyPanel;

        [Header("Buttons")]
        [SerializeField] private MainMenuButtonView playButton;
        [SerializeField] private MainMenuButtonView tutorialButton;
        [SerializeField] private MainMenuButtonView collectionButton;
        [SerializeField] private MainMenuButtonView shopButton;
        [SerializeField] private MainMenuButtonView settingsButton;
        [SerializeField] private MainMenuButtonView profileButton;
        [SerializeField] private MainMenuButtonView exitButton;
        [SerializeField] private MainMenuButtonView onlineButton;
        [SerializeField] private MainMenuButtonView offlineButton;
        [SerializeField] private MainMenuButtonView playBackButton;
        [SerializeField] private MainMenuButtonView closeCollectionButton;
        [SerializeField] private MainMenuButtonView closeShopButton;
        [SerializeField] private MainMenuButtonView closeTutorialButton;
        [SerializeField] private MainMenuButtonView closeSettingsButton;
        [SerializeField] private MainMenuButtonView closeProfileButton;
        [SerializeField] private MainMenuButtonView startTutorialButton;
        [SerializeField] private MainMenuButtonView authLoginButton;
        [SerializeField] private MainMenuButtonView authRegisterButton;
        [SerializeField] private MainMenuButtonView logoutButton;

        [Header("Feature Controllers")]
        [SerializeField] private LobbyUIController lobbyUIController;
        [SerializeField] private CollectionPanelController collectionPanelController;
        [SerializeField] private ShopPanelController shopPanelController;
        [SerializeField] private SettingsPanelController settingsPanelController;
        [SerializeField] private PlayerProfileDatabase profileDatabase;
        [SerializeField] private ApiAuthenticationService apiAuthenticationService;

        [Header("Profile")]
        [SerializeField] private TMP_Text profileText;
        [SerializeField] private TMP_Text profileNameText;
        [SerializeField] private TMP_Text profileRankText;
        [SerializeField] private TMP_Text profileAccountText;
        [SerializeField] private TMP_Text profileCollectionText;
        [SerializeField] private TMP_Text profileMatchText;
        [SerializeField] private TMP_Text coinsText;
        [SerializeField] private TMP_InputField authLoginInput;
        [SerializeField] private TMP_InputField authPasswordInput;
        [SerializeField] private TMP_InputField authNicknameInput;
        [SerializeField] private TMP_Text authStatusText;

        [Header("Navigation")]
        [SerializeField] private string offlineSceneName = "PvEGameScene";
        [SerializeField] private string tutorialSceneName = "PvEGameScene";

        private bool initialized;

        private void Start()
        {
            Initialize();
        }

        public void Initialize()
        {
            if (initialized)
                return;

            initialized = true;
            ConfigureButtons();
            ApplyInitialState();
            RefreshProfile(profileDatabase == null ? null : profileDatabase.CurrentProfile);
            RefreshAuthenticationState();

            if (profileDatabase != null)
                profileDatabase.ProfileChanged += RefreshProfile;

            if (apiAuthenticationService != null)
                apiAuthenticationService.AuthenticationChanged += OnAuthenticationChanged;
        }

        private void OnDestroy()
        {
            if (initialized && profileDatabase != null)
                profileDatabase.ProfileChanged -= RefreshProfile;

            if (initialized && apiAuthenticationService != null)
                apiAuthenticationService.AuthenticationChanged -= OnAuthenticationChanged;
        }

        public void ShowPlayModePanel()
        {
            if (!CanOpenGameplay())
                return;

            if (mainButtonsPanel != null)
                mainButtonsPanel.SetActive(false);

            if (playModePanel != null)
                playModePanel.SetActive(true);

            SetPlayModeRootButtonsVisible(true);
        }

        public void ReturnToRootMenu()
        {
            if (mainButtonsPanel != null)
                mainButtonsPanel.SetActive(IsAuthenticatedOrApiDisabled());

            if (playModePanel != null)
                playModePanel.SetActive(false);

            lobbyUIController?.HideLobby();
            SetPlayModeRootButtonsVisible(true);
        }

        public void StartOfflineGame()
        {
            if (!CanOpenGameplay())
                return;

            if (!string.IsNullOrWhiteSpace(offlineSceneName))
                SceneManager.LoadScene(offlineSceneName);
        }

        public void ShowTutorial()
        {
            SetSingleOverlay(tutorialPanel);
        }

        public void StartTutorial()
        {
            string sceneName = string.IsNullOrWhiteSpace(tutorialSceneName) ? offlineSceneName : tutorialSceneName;
            if (!string.IsNullOrWhiteSpace(sceneName))
                SceneManager.LoadScene(sceneName);
        }

        public void OpenOnlineLobby()
        {
            if (!CanOpenGameplay())
                return;

            lobbyUIController?.Initialize();
            SetPlayModeRootButtonsVisible(false);
            lobbyUIController?.ShowLobby();
        }

        public void ShowCollection()
        {
            if (!CanOpenGameplay())
                return;

            SetSingleOverlay(collectionPanel);
            collectionPanelController?.Initialize();
            collectionPanelController?.Refresh();
        }

        public void ShowShop()
        {
            if (!CanOpenGameplay())
                return;

            SetSingleOverlay(shopPanel);
            shopPanelController?.Initialize();
            shopPanelController?.Rebuild();
        }

        public void ShowSettings()
        {
            if (!CanOpenGameplay())
                return;

            settingsPanelController?.Initialize();
            SetSingleOverlay(settingsPanel);
        }

        public void ShowProfile()
        {
            if (!CanOpenGameplay())
                return;

            RefreshProfile(profileDatabase == null ? null : profileDatabase.CurrentProfile);
            SetSingleOverlay(profilePanel);
        }

        public void CloseOverlays()
        {
            if (settingsPanel != null)
                settingsPanel.SetActive(false);

            if (collectionPanel != null)
                collectionPanel.SetActive(false);

            if (shopPanel != null)
                shopPanel.SetActive(false);

            if (tutorialPanel != null)
                tutorialPanel.SetActive(false);

            if (profilePanel != null)
                profilePanel.SetActive(false);
        }

        public void ExitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void ConfigureButtons()
        {
            playButton?.Setup("Играть", ShowPlayModePanel);
            tutorialButton?.Setup("Обучение", ShowTutorial);
            collectionButton?.Setup("Коллекция", ShowCollection);
            shopButton?.Setup("Магазин", ShowShop);
            settingsButton?.Setup("Настройки", ShowSettings);
            profileButton?.Setup("Профиль", ShowProfile);
            exitButton?.Setup("Выход", ExitGame);
            onlineButton?.Setup("Онлайн", OpenOnlineLobby);
            offlineButton?.Setup("Оффлайн", StartOfflineGame);
            playBackButton?.Setup("Назад", ReturnToRootMenu);
            closeCollectionButton?.Setup("Назад", CloseOverlays);
            closeShopButton?.Setup("Назад", CloseOverlays);
            closeTutorialButton?.Setup("Назад", CloseOverlays);
            closeSettingsButton?.Setup("Назад", CloseOverlays);
            closeProfileButton?.Setup("Назад", CloseOverlays);
            startTutorialButton?.Setup("Начать", StartTutorial);
            authLoginButton?.Setup("Войти", Login);
            authRegisterButton?.Setup("Регистрация", Register);
            logoutButton?.Setup("Выйти из аккаунта", Logout);
        }

        private void ApplyInitialState()
        {
            if (playModePanel != null)
                playModePanel.SetActive(false);

            if (settingsPanel != null)
                settingsPanel.SetActive(false);

            if (collectionPanel != null)
                collectionPanel.SetActive(false);

            if (shopPanel != null)
                shopPanel.SetActive(false);

            if (tutorialPanel != null)
                tutorialPanel.SetActive(false);

            if (profilePanel != null)
                profilePanel.SetActive(false);

            if (authPanel != null)
                authPanel.SetActive(false);

            if (mainButtonsPanel != null)
                mainButtonsPanel.SetActive(true);
        }

        private void SetSingleOverlay(GameObject target)
        {
            CloseOverlays();
            if (target != null)
                target.SetActive(true);
        }

        private void SetPlayModeRootButtonsVisible(bool visible)
        {
            if (onlineButton != null)
                onlineButton.gameObject.SetActive(visible);

            if (offlineButton != null)
                offlineButton.gameObject.SetActive(visible);
        }

        private void Login()
        {
            if (apiAuthenticationService == null || !apiAuthenticationService.IsEnabled)
            {
                SetAuthStatus("API авторизации отключён.");
                return;
            }

            ApiAuthOperationResult result = apiAuthenticationService.Login(
                authLoginInput == null ? string.Empty : authLoginInput.text,
                authPasswordInput == null ? string.Empty : authPasswordInput.text);

            HandleAuthResult(result, "Вход выполнен.");
        }

        private void Register()
        {
            if (apiAuthenticationService == null || !apiAuthenticationService.IsEnabled)
            {
                SetAuthStatus("API авторизации отключён.");
                return;
            }

            ApiAuthOperationResult result = apiAuthenticationService.Register(
                authLoginInput == null ? string.Empty : authLoginInput.text,
                authPasswordInput == null ? string.Empty : authPasswordInput.text,
                authNicknameInput == null ? string.Empty : authNicknameInput.text);

            HandleAuthResult(result, "Аккаунт создан, вход выполнен.");
        }

        private void Logout()
        {
            apiAuthenticationService?.Logout();
            profileDatabase?.ResetToDefaultProfile();
            profileDatabase?.ReinitializeStorage();
            CloseOverlays();
            ReturnToRootMenu();
            SetAuthStatus("Вы вышли из аккаунта.");
            ClearAuthInputs(clearLogin: false);
        }

        private void HandleAuthResult(ApiAuthOperationResult result, string successMessage)
        {
            if (result == null || !result.Success)
            {
                SetAuthStatus(result == null ? "Не удалось выполнить запрос." : result.ErrorMessage);
                return;
            }

            profileDatabase?.ReinitializeStorage();
            RefreshAuthenticationState();
            SetAuthStatus(successMessage);
            ClearAuthInputs(clearLogin: false);
            CloseOverlays();
            ReturnToRootMenu();
        }

        private void OnAuthenticationChanged()
        {
            if (apiAuthenticationService != null && apiAuthenticationService.IsAuthenticated && profileDatabase != null)
                profileDatabase.ReinitializeStorage();

            RefreshAuthenticationState();
        }

        private void RefreshAuthenticationState()
        {
            bool requiresAuth = apiAuthenticationService != null && apiAuthenticationService.IsEnabled;
            bool authenticated = !requiresAuth || apiAuthenticationService.IsAuthenticated;

            if (authPanel != null)
                authPanel.SetActive(requiresAuth && !authenticated);

            if (currencyPanel != null)
                currencyPanel.SetActive(authenticated);

            if (logoPanel != null)
                logoPanel.SetActive(authenticated);

            if (profileButton != null)
                profileButton.gameObject.SetActive(authenticated);

            if (logoutButton != null)
                logoutButton.gameObject.SetActive(authenticated && requiresAuth);

            if (!authenticated)
            {
                CloseOverlays();

                if (playModePanel != null)
                    playModePanel.SetActive(false);

                if (mainButtonsPanel != null)
                    mainButtonsPanel.SetActive(false);

                SetAuthStatus("Войдите или зарегистрируйтесь, чтобы играть и синхронизировать профиль.");
                return;
            }

            if (mainButtonsPanel != null && (playModePanel == null || !playModePanel.activeSelf))
                mainButtonsPanel.SetActive(true);

            if (requiresAuth)
                SetAuthStatus(string.Empty);
        }

        private bool CanOpenGameplay()
        {
            if (IsAuthenticatedOrApiDisabled())
                return true;

            SetAuthStatus("Сначала войдите в аккаунт.");
            return false;
        }

        private bool IsAuthenticatedOrApiDisabled()
        {
            return apiAuthenticationService == null || !apiAuthenticationService.IsEnabled || apiAuthenticationService.IsAuthenticated;
        }

        private void SetAuthStatus(string message)
        {
            if (authStatusText != null)
                authStatusText.text = string.IsNullOrWhiteSpace(message) ? string.Empty : message;
        }

        private void ClearAuthInputs(bool clearLogin)
        {
            if (clearLogin && authLoginInput != null)
                authLoginInput.text = string.Empty;

            if (authPasswordInput != null)
                authPasswordInput.text = string.Empty;

            if (authNicknameInput != null)
                authNicknameInput.text = string.Empty;
        }

        private void RefreshProfile(PlayerProfileData profile)
        {
            if (coinsText != null)
                coinsText.text = profile == null ? "0" : profile.coins.ToString();

            if (profile == null)
            {
                ApplyProfileCardLayout(
                    "Профиль недоступен",
                    string.Empty,
                    "Войдите в аккаунт,\nчтобы загрузить профиль.",
                    "Коллекция станет доступна\nпосле входа.",
                    "Статистика матча\nпока недоступна.");
                RefreshLegacyProfileText(null);
                return;
            }

            int ownedCards = profile.ownedCards == null ? 0 : profile.ownedCards.Count;
            int deckCards = profile.selectedDeck == null ? 0 : profile.selectedDeck.Count;
            int matches = profile.statistics == null ? 0 : profile.statistics.totalMatches;
            int wins = profile.statistics == null ? 0 : profile.statistics.wins;
            int losses = profile.statistics == null ? 0 : profile.statistics.losses;
            int onlineMatches = profile.statistics == null ? 0 : profile.statistics.onlineMatches;
            int offlineMatches = profile.statistics == null ? 0 : profile.statistics.offlineMatches;
            string login = string.IsNullOrWhiteSpace(apiAuthenticationService?.CurrentLogin) ? "Не привязан" : apiAuthenticationService.CurrentLogin;
            string playerId = FormatCompactPlayerId(profile.playerId);

            ApplyProfileCardLayout(
                string.IsNullOrWhiteSpace(profile.nickname) ? "Senator" : profile.nickname,
                $"{profile.rank}  •  Ур. {profile.level}",
                $"Логин: {login}\nID: {playerId}",
                $"Монеты: {profile.coins}\nОткрыто: {ownedCards}\nВ колоде: {deckCards}",
                $"Матчи: {matches}\nПобеды: {wins}\nПоражения: {losses}\nОнл/Офл: {onlineMatches}/{offlineMatches}");

            RefreshLegacyProfileText(profile);
        }

        private void ApplyProfileCardLayout(string name, string rank, string account, string collection, string match)
        {
            if (profileNameText != null)
                profileNameText.text = string.IsNullOrWhiteSpace(name) ? "Senator" : name;

            if (profileRankText != null)
                profileRankText.text = rank ?? string.Empty;

            if (profileAccountText != null)
                profileAccountText.text = account ?? string.Empty;

            if (profileCollectionText != null)
                profileCollectionText.text = collection ?? string.Empty;

            if (profileMatchText != null)
                profileMatchText.text = match ?? string.Empty;
        }

        private void RefreshLegacyProfileText(PlayerProfileData profile)
        {
            if (profileText == null)
                return;

            if (profile == null)
            {
                profileText.text = "<size=30><b>Профиль недоступен</b></size>";
                return;
            }

            int ownedCards = profile.ownedCards == null ? 0 : profile.ownedCards.Count;
            int deckCards = profile.selectedDeck == null ? 0 : profile.selectedDeck.Count;
            int matches = profile.statistics == null ? 0 : profile.statistics.totalMatches;
            int wins = profile.statistics == null ? 0 : profile.statistics.wins;
            int losses = profile.statistics == null ? 0 : profile.statistics.losses;

            profileText.text =
                $"<size=34><b>{profile.nickname}</b></size>\n" +
                $"<size=20><b>{profile.rank}</b>  •  Уровень {profile.level}</size>\n\n" +
                "<b>Ресурсы</b>\n" +
                $"Монеты: {profile.coins}\n" +
                $"Открыто карт: {ownedCards}\n" +
                $"В колоде: {deckCards}\n\n" +
                "<b>Статистика</b>\n" +
                $"Матчи: {matches}\n" +
                $"Победы: {wins}\n" +
                $"Поражения: {losses}";
        }

        private static string FormatCompactPlayerId(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId))
                return "Локальный профиль";

            string trimmed = playerId.Trim();
            if (trimmed.Length <= 18)
                return trimmed;

            return $"{trimmed.Substring(0, 8)}...{trimmed.Substring(trimmed.Length - 6)}";
        }
    }
}

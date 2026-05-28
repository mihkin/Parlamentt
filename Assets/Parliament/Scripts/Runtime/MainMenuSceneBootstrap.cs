using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ParliamentGame
{
    public sealed class MainMenuSceneBootstrap : MonoBehaviour
    {
        [Header("Sprites")]
        [SerializeField] private Sprite backgroundSprite;
        [SerializeField] private Sprite panelSprite;
        [SerializeField] private Sprite lightPanelSprite;
        [SerializeField] private Sprite buttonSprite;
        [SerializeField] private Sprite buttonFrameSprite;
        [SerializeField] private Sprite logoSprite;
        [SerializeField] private Sprite collectionCardBackgroundSprite;
        [SerializeField] private Sprite buttonTextPlateSprite;
        [SerializeField] private Sprite profilePanelSprite;
        [SerializeField] private Sprite profileTextPlateSprite;
        [SerializeField] private Sprite collectionTextPlateSprite;
        [SerializeField] private Sprite lobbyProfilePlateSprite;
        [SerializeField] private Sprite connectionTextPlateSprite;
        [SerializeField] private Sprite authTextPlateSprite;

        [Header("Audio")]
        [SerializeField] private AudioClip hoverClip;
        [SerializeField] private AudioClip clickClip;
        [SerializeField] private AudioClip backgroundMusicClip;

        [Header("Data")]
        [SerializeField] private TMP_FontAsset fontAsset;
        [SerializeField] private CardDatabase cardDatabase;
        [SerializeField] private CardArtLibrary cardArtLibrary;
        [SerializeField] private ShopCatalog shopCatalog;
        [SerializeField] private string offlineSceneName = "PvEGameScene";

#if UNITY_EDITOR
        private static bool rebuildQueuedInEditor;
#endif

        private void Awake()
        {
            ResolveUiPlateSprites();
            EnsureCamera();
            EnsureEventSystem();
            BuildSceneIfNeeded();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            if (rebuildQueuedInEditor)
                return;

            rebuildQueuedInEditor = true;
            UnityEditor.EditorApplication.delayCall += RebuildInEditorIfNeeded;
        }

        private void RebuildInEditorIfNeeded()
        {
            rebuildQueuedInEditor = false;

            if (this == null || gameObject == null)
                return;

            if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            EnsureCamera();
            EnsureEventSystem();
            BuildScene(forceRebuild: true, initializeControllers: false);

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }
#endif

        public void RebuildSceneLayout()
        {
            ResolveUiPlateSprites();
            EnsureCamera();
            EnsureEventSystem();
            BuildScene(forceRebuild: true, initializeControllers: Application.isPlaying);
        }

        private void ResolveUiPlateSprites()
        {
            lightPanelSprite = ResolveUiPlateSprite("light_panel_plate", lightPanelSprite);
            buttonFrameSprite = ResolveUiPlateSprite("button_frame_plate", buttonFrameSprite != null ? buttonFrameSprite : buttonSprite);
            buttonTextPlateSprite = ResolveUiPlateSprite("button_frame_plate", buttonTextPlateSprite);
            profilePanelSprite = ResolveUiPlateSprite("profile_info_plate", profilePanelSprite);
            profileTextPlateSprite = ResolveUiPlateSprite("input_plate", profileTextPlateSprite);
            collectionTextPlateSprite = ResolveUiPlateSprite("title_plate", collectionTextPlateSprite);
            lobbyProfilePlateSprite = ResolveUiPlateSprite("light_panel_plate", lobbyProfilePlateSprite);
            connectionTextPlateSprite = ResolveUiPlateSprite("input_plate", connectionTextPlateSprite);
            authTextPlateSprite = ResolveUiPlateSprite("input_plate", authTextPlateSprite);
        }

        private static Sprite ResolveUiPlateSprite(string spriteName, Sprite fallbackSprite)
        {
            Sprite atlasSprite = UiPlateLibrary.Get(spriteName);
            return atlasSprite != null ? atlasSprite : fallbackSprite;
        }

        private void EnsureCamera()
        {
            if (Camera.main != null)
                return;

            GameObject cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.045f, 0.04f, 1f);
            camera.orthographic = true;
            camera.transform.position = new Vector3(0f, 0f, -10f);
        }

        private void EnsureEventSystem()
        {
            EventSystem eventSystem = FindObjectOfType<EventSystem>();
            if (eventSystem != null)
            {
                if (eventSystem.GetComponent<BaseInputModule>() == null)
                    eventSystem.gameObject.AddComponent<StandaloneInputModule>();

                return;
            }

            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        private void BuildSceneIfNeeded()
        {
            bool hasCanvas = transform.Find("Canvas") != null;
            bool hasMainMenuController = FindObjectOfType<MainMenuController>() != null;

            // Runtime-rebuilding the whole menu is expensive on weaker devices.
            // If the scene already has the main layout, reuse it and only rebind services.
            if (hasCanvas && hasMainMenuController)
            {
                if (Application.isPlaying)
                    InitializeExistingRuntime();

                return;
            }

            BuildScene(forceRebuild: hasCanvas, initializeControllers: true);
        }

        private void BuildScene(bool forceRebuild, bool initializeControllers)
        {
            if (forceRebuild)
                CleanupPartialGeneratedObjects();

            if (fontAsset == null)
                fontAsset = TMP_Settings.defaultFontAsset;

            GameObject serviceRoot = GetOrCreateServiceRoot();

            if (Application.isPlaying)
                DontDestroyOnLoad(serviceRoot);

            ApiAuthenticationService apiAuthenticationService = serviceRoot.GetComponent<ApiAuthenticationService>();
            if (apiAuthenticationService == null)
                apiAuthenticationService = serviceRoot.AddComponent<ApiAuthenticationService>();

            PlayerProfileDatabase profileDatabase = serviceRoot.GetComponent<PlayerProfileDatabase>();
            if (profileDatabase == null)
                profileDatabase = serviceRoot.AddComponent<PlayerProfileDatabase>();
            else
                profileDatabase.ReinitializeStorage();

            GameSettingsService settingsService = serviceRoot.GetComponent<GameSettingsService>();
            if (settingsService == null)
                settingsService = serviceRoot.AddComponent<GameSettingsService>();

            PersistentAudioService audioService = serviceRoot.GetComponent<PersistentAudioService>();
            if (audioService == null)
                audioService = serviceRoot.AddComponent<PersistentAudioService>();

            audioService.Initialize(settingsService, backgroundMusicClip);
            AudioSource audioSource = audioService.EffectsSource;

            OnlineLobbyManager lobbyManager = serviceRoot.GetComponent<OnlineLobbyManager>();
            if (lobbyManager == null)
                lobbyManager = serviceRoot.AddComponent<OnlineLobbyManager>();

            NetworkGameManager networkGameManager = serviceRoot.GetComponent<NetworkGameManager>();
            if (networkGameManager == null)
                networkGameManager = serviceRoot.AddComponent<NetworkGameManager>();

            LanMatchCoordinator lanMatchCoordinator = serviceRoot.GetComponent<LanMatchCoordinator>();
            if (lanMatchCoordinator == null)
                lanMatchCoordinator = serviceRoot.AddComponent<LanMatchCoordinator>();

            lanMatchCoordinator.Initialize(lobbyManager, profileDatabase);

            Canvas canvas = CreateCanvas();
            RectTransform root = CreateRect("Root", canvas.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            Color backgroundTint = backgroundSprite == null ? new Color(0.16f, 0.11f, 0.08f, 1f) : new Color(0.26f, 0.20f, 0.15f, 1f);
            Image background = CreateImage("Background", root, backgroundSprite, backgroundTint);
            background.raycastTarget = false;
            Stretch(background.rectTransform);
            Image backgroundShade = CreateImage("BackgroundShade", root, null, new Color(0.05f, 0.03f, 0.02f, 0.58f));
            backgroundShade.raycastTarget = false;
            Stretch(backgroundShade.rectTransform);

            RectTransform currencyPanel = CreatePanel("CurrencyPanel", root, new Vector2(0.03f, 0.885f), new Vector2(0.17f, 0.95f), lightPanelSprite, new Color(0.97f, 0.93f, 0.84f, 0.98f));
            Image coinIcon = CreateImage("CoinIcon", currencyPanel, logoSprite, new Color(0.92f, 0.76f, 0.25f, 1f));
            PositionRect(coinIcon.rectTransform, new Vector2(0.06f, 0.16f), new Vector2(0.28f, 0.84f), Vector2.zero, Vector2.zero);
            coinIcon.preserveAspect = true;
            TMP_Text headerCoinsText = CreateText("CoinsText", currencyPanel, "0", 28, TextAlignmentOptions.Left);
            PositionRect(headerCoinsText.rectTransform, new Vector2(0.34f, 0.12f), new Vector2(0.92f, 0.88f), Vector2.zero, Vector2.zero);

            RectTransform logoPanel = CreatePanel("LogoPanel", root, new Vector2(0.31f, 0.74f), new Vector2(0.69f, 0.95f), panelSprite, new Color(0.2f, 0.14f, 0.08f, 0.9f));
            CreateLogoContent(logoPanel);

            RectTransform mainButtonsPanel = CreatePanel("MainButtonsPanel", root, new Vector2(0.36f, 0.27f), new Vector2(0.64f, 0.66f), panelSprite, new Color(0.24f, 0.17f, 0.1f, 0.94f));
            VerticalLayoutGroup mainLayout = mainButtonsPanel.gameObject.AddComponent<VerticalLayoutGroup>();
            mainLayout.spacing = 14f;
            mainLayout.padding = new RectOffset(30, 30, 30, 30);
            mainLayout.childControlWidth = true;
            mainLayout.childControlHeight = true;
            mainLayout.childForceExpandWidth = true;
            mainLayout.childForceExpandHeight = false;

            RectTransform playModePanel = CreatePanel("PlayModePanel", root, new Vector2(0.30f, 0.10f), new Vector2(0.70f, 0.68f), panelSprite, new Color(0.24f, 0.17f, 0.1f, 0.95f));
            VerticalLayoutGroup playModeLayout = playModePanel.gameObject.AddComponent<VerticalLayoutGroup>();
            playModeLayout.spacing = 10f;
            playModeLayout.padding = new RectOffset(24, 24, 24, 24);
            playModeLayout.childControlHeight = true;
            playModeLayout.childControlWidth = true;
            playModeLayout.childForceExpandHeight = false;
            playModePanel.gameObject.SetActive(false);

            RectTransform playModeTitlePlate = CreatePanel("TitlePlate", playModePanel, Vector2.zero, Vector2.one, collectionTextPlateSprite != null ? collectionTextPlateSprite : lightPanelSprite, new Color(0.98f, 0.95f, 0.90f, 0.98f));
            SetHeight(playModeTitlePlate, 48f);
            TMP_Text playModeTitle = CreateText("Title", playModeTitlePlate, "Режим игры", 24, TextAlignmentOptions.Center);
            Stretch(playModeTitle.rectTransform, new Vector2(14f, 6f), new Vector2(-14f, -6f));

            MainMenuButtonView onlineButton = CreateButton("OnlineButton", playModePanel, audioSource);
            MainMenuButtonView offlineButton = CreateButton("OfflineButton", playModePanel, audioSource);
            MainMenuButtonView playBackButton = CreateButton("BackButton", playModePanel, audioSource);

            RectTransform lobbyRoot = CreatePanel("LobbyRoot", playModePanel, Vector2.zero, Vector2.one, lightPanelSprite, new Color(0.94f, 0.90f, 0.82f, 0.98f));
            SetHeight(lobbyRoot, 500f);
            lobbyRoot.gameObject.SetActive(false);

            VerticalLayoutGroup lobbyLayout = lobbyRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            lobbyLayout.spacing = 8f;
            lobbyLayout.padding = new RectOffset(16, 16, 16, 16);
            lobbyLayout.childControlWidth = true;
            lobbyLayout.childControlHeight = true;
            lobbyLayout.childForceExpandHeight = false;

            RectTransform lobbyCard = CreatePanel("LobbyCard", lobbyRoot, Vector2.zero, Vector2.one, lightPanelSprite, new Color(0.95f, 0.91f, 0.83f, 0.98f));
            SetHeight(lobbyCard, 412f);
            VerticalLayoutGroup lobbyCardLayout = lobbyCard.gameObject.AddComponent<VerticalLayoutGroup>();
            lobbyCardLayout.spacing = 8f;
            lobbyCardLayout.padding = new RectOffset(18, 18, 18, 18);
            lobbyCardLayout.childControlWidth = true;
            lobbyCardLayout.childControlHeight = true;
            lobbyCardLayout.childForceExpandWidth = true;
            lobbyCardLayout.childForceExpandHeight = false;

            RectTransform roomCodePlate = CreatePanel("RoomCodePlate", lobbyCard, Vector2.zero, Vector2.one, connectionTextPlateSprite != null ? connectionTextPlateSprite : lightPanelSprite, new Color(0.99f, 0.97f, 0.92f, 1f));
            SetHeight(roomCodePlate, 36f);
            TMP_Text roomCodeText = CreateText("RoomCodeText", roomCodePlate, "Комната: -", 20, TextAlignmentOptions.Center);
            Stretch(roomCodeText.rectTransform, new Vector2(12f, 3f), new Vector2(-12f, -3f));
            RectTransform lobbyStatusPlate = CreatePanel("StatusPlate", lobbyCard, Vector2.zero, Vector2.one, connectionTextPlateSprite != null ? connectionTextPlateSprite : lightPanelSprite, new Color(0.99f, 0.97f, 0.92f, 1f));
            SetHeight(lobbyStatusPlate, 56f);
            TMP_Text lobbyStatusText = CreateText("StatusText", lobbyStatusPlate, "Введите IP адрес хоста или создайте комнату.", 16, TextAlignmentOptions.Center);
            Stretch(lobbyStatusText.rectTransform, new Vector2(14f, 4f), new Vector2(-14f, -4f));
            lobbyStatusText.enableAutoSizing = true;
            lobbyStatusText.fontSizeMin = 13f;
            lobbyStatusText.fontSizeMax = 16f;
            TMP_InputField roomCodeInput = CreateInputField("RoomCodeInput", lobbyCard, "Введите IP адрес", connectionTextPlateSprite);
            MainMenuButtonView hostButton = CreateButton("HostButton", lobbyCard, audioSource);
            MainMenuButtonView joinButton = CreateButton("JoinButton", lobbyCard, audioSource);
            RectTransform readyToggleRoot = CreateUnityToggle("ReadyToggle", lobbyCard, out Toggle readyToggle);
            TMP_Text readyLabel = CreateText("ReadyLabel", readyToggleRoot, "Готов", 20, TextAlignmentOptions.Left);
            PositionRect(readyLabel.rectTransform, new Vector2(0.18f, 0.12f), new Vector2(0.96f, 0.88f), Vector2.zero, Vector2.zero);
            MainMenuButtonView startButton = CreateButton("StartButton", lobbyCard, audioSource);
            MainMenuButtonView leaveButton = CreateButton("LeaveButton", lobbyCard, audioSource);

            RectTransform playersBlock = CreatePanel("PlayersBlock", lobbyCard, Vector2.zero, Vector2.one, lightPanelSprite, new Color(0.91f, 0.87f, 0.79f, 1f));
            SetHeight(playersBlock, 180f);
            VerticalLayoutGroup playersLayout = playersBlock.gameObject.AddComponent<VerticalLayoutGroup>();
            playersLayout.spacing = 6f;
            playersLayout.padding = new RectOffset(12, 12, 12, 12);
            playersLayout.childControlWidth = true;
            playersLayout.childControlHeight = true;
            playersLayout.childForceExpandWidth = true;
            playersLayout.childForceExpandHeight = false;
            RectTransform playersTitlePlate = CreatePanel("PlayersTitlePlate", playersBlock, Vector2.zero, Vector2.one, connectionTextPlateSprite != null ? connectionTextPlateSprite : lightPanelSprite, new Color(0.99f, 0.97f, 0.92f, 1f));
            SetHeight(playersTitlePlate, 34f);
            TMP_Text playersTitle = CreateText("PlayersTitle", playersTitlePlate, "Игроки в лобби", 18, TextAlignmentOptions.Center);
            Stretch(playersTitle.rectTransform, new Vector2(12f, 2f), new Vector2(-12f, -2f));

            ScrollRect lobbyScroll = CreateScrollView("PlayerScroll", playersBlock);
            SetHeight(lobbyScroll.GetComponent<RectTransform>(), 120f);
            RectTransform lobbyListRoot = lobbyScroll.content;
            LobbyPlayerItemView lobbyItemTemplate = CreateLobbyPlayerItemTemplate(lobbyListRoot);

            RectTransform collectionPanel = CreateOverlayPanel("CollectionPanel", root, "Коллекция");
            RectTransform collectionListPanel = CreatePanel("CollectionListPanel", collectionPanel, new Vector2(0.05f, 0.15f), new Vector2(0.60f, 0.82f), lightPanelSprite, new Color(0.96f, 0.92f, 0.84f, 0.98f));
            RectTransform databaseInfoPlate = CreatePanel("DatabaseInfoPlate", collectionListPanel, new Vector2(0.03f, 0.91f), new Vector2(0.97f, 0.98f), collectionTextPlateSprite != null ? collectionTextPlateSprite : lightPanelSprite, new Color(0.98f, 0.95f, 0.90f, 0.96f));
            TMP_Text databaseInfoText = CreateText("DatabaseInfoText", databaseInfoPlate, "База: Assets/StreamingAssets/cards.json", 16, TextAlignmentOptions.Left);
            Stretch(databaseInfoText.rectTransform, new Vector2(18f, 6f), new Vector2(-18f, -6f));
            databaseInfoText.enableAutoSizing = true;
            databaseInfoText.fontSizeMin = 12f;
            databaseInfoText.fontSizeMax = 16f;

            RectTransform collectionFilterRow = CreateRect("FilterRow", collectionListPanel, new Vector2(0.03f, 0.80f), new Vector2(0.97f, 0.89f), Vector2.zero, Vector2.zero);
            HorizontalLayoutGroup filterLayout = collectionFilterRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            filterLayout.spacing = 12f;
            filterLayout.childControlWidth = true;
            filterLayout.childControlHeight = true;
            filterLayout.childForceExpandWidth = true;
            filterLayout.childForceExpandHeight = true;
            TMP_Dropdown rarityDropdown = CreateDropdown("RarityDropdown", collectionFilterRow, new[] { "Все", "Common", "Uncommon", "Rare", "Epic", "Legendary" });
            TMP_Dropdown typeDropdown = CreateDropdown("TypeDropdown", collectionFilterRow, new[] { "Все", "Influence", "Politics", "Attack", "Defense", "Neutral", "Vote", "Event", "Boost" });

            ScrollRect collectionScroll = CreateScrollView("CollectionScroll", collectionListPanel);
            PositionRect(collectionScroll.GetComponent<RectTransform>(), new Vector2(0.03f, 0.14f), new Vector2(0.97f, 0.78f), Vector2.zero, Vector2.zero);
            RectTransform collectionContent = collectionScroll.content;
            ConfigureGridContent(collectionContent, new Vector2(152f, 214f), new Vector2(12f, 12f));
            CardCollectionItemView cardItemTemplate = CreateCardCollectionItemTemplate(collectionContent);

            RectTransform deckInfoPlate = CreatePanel("DeckInfoPlate", collectionListPanel, new Vector2(0.03f, 0.04f), new Vector2(0.97f, 0.11f), collectionTextPlateSprite != null ? collectionTextPlateSprite : lightPanelSprite, new Color(0.98f, 0.95f, 0.90f, 0.96f));
            TMP_Text deckInfoText = CreateText("DeckInfoText", deckInfoPlate, "Колода: 0/20", 16, TextAlignmentOptions.Left);
            Stretch(deckInfoText.rectTransform, new Vector2(18f, 6f), new Vector2(-18f, -6f));
            deckInfoText.enableAutoSizing = true;
            deckInfoText.fontSizeMin = 12f;
            deckInfoText.fontSizeMax = 16f;

            RectTransform collectionPreviewPanel = CreatePanel("CollectionPreviewPanel", collectionPanel, new Vector2(0.63f, 0.18f), new Vector2(0.94f, 0.82f), lightPanelSprite, new Color(0.96f, 0.92f, 0.84f, 0.98f));
            Image previewBackground = CreateImage("PreviewBackground", collectionPreviewPanel, collectionCardBackgroundSprite, Color.white);
            PositionRect(previewBackground.rectTransform, new Vector2(0.06f, 0.06f), new Vector2(0.94f, 0.94f), Vector2.zero, Vector2.zero);
            RectTransform previewArtViewport = CreateRect("PreviewArtViewport", previewBackground.rectTransform, new Vector2(0.10f, 0.46f), new Vector2(0.90f, 0.84f), Vector2.zero, Vector2.zero);
            previewArtViewport.gameObject.AddComponent<RectMask2D>();
            Image previewArtBackdrop = CreateImage("PreviewArtBackdrop", previewArtViewport, null, new Color(0.25f, 0.23f, 0.2f, 0.40f));
            Stretch(previewArtBackdrop.rectTransform);
            Image previewArtwork = CreateImage("PreviewArtwork", previewArtViewport, null, Color.white);
            Stretch(previewArtwork.rectTransform);
            previewArtwork.preserveAspect = true;
            Image previewTitlePlate = CreateImage("PreviewTitlePlate", previewBackground.rectTransform, collectionTextPlateSprite != null ? collectionTextPlateSprite : lightPanelSprite, new Color(0.97f, 0.94f, 0.88f, 0.96f));
            PositionRect(previewTitlePlate.rectTransform, new Vector2(0.08f, 0.29f), new Vector2(0.92f, 0.40f), Vector2.zero, Vector2.zero);
            TMP_Text previewTitle = CreateText("PreviewTitle", previewTitlePlate.rectTransform, "Карта", 20, TextAlignmentOptions.Center);
            Stretch(previewTitle.rectTransform, new Vector2(12f, 4f), new Vector2(-12f, -4f));
            previewTitle.enableAutoSizing = true;
            previewTitle.fontSizeMin = 14f;
            previewTitle.fontSizeMax = 20f;
            Image previewDescriptionPlate = CreateImage("PreviewDescriptionPlate", previewBackground.rectTransform, null, Color.white);
            ApplyImageStyle(previewDescriptionPlate, collectionTextPlateSprite != null ? collectionTextPlateSprite : lightPanelSprite, new Color(0.97f, 0.94f, 0.88f, 0.96f));
            PositionRect(previewDescriptionPlate.rectTransform, new Vector2(0.08f, 0.07f), new Vector2(0.92f, 0.25f), Vector2.zero, Vector2.zero);
            TMP_Text previewDescription = CreateText("PreviewDescription", previewDescriptionPlate.rectTransform, "Описание карты.", 14, TextAlignmentOptions.TopLeft);
            Stretch(previewDescription.rectTransform, new Vector2(12f, 8f), new Vector2(-12f, -8f));
            previewDescription.enableAutoSizing = true;
            previewDescription.fontSizeMin = 10f;
            previewDescription.fontSizeMax = 14f;

            MainMenuButtonView deckToggleButton = CreateButton("DeckToggleButton", collectionPanel, audioSource);
            PositionRect(deckToggleButton.GetComponent<RectTransform>(), new Vector2(0.63f, 0.08f), new Vector2(0.78f, 0.145f), Vector2.zero, Vector2.zero);
            MainMenuButtonView closeCollectionButton = CreateButton("CloseCollectionButton", collectionPanel, audioSource);
            PositionRect(closeCollectionButton.GetComponent<RectTransform>(), new Vector2(0.80f, 0.08f), new Vector2(0.94f, 0.145f), Vector2.zero, Vector2.zero);

            RectTransform shopPanel = CreateOverlayPanel("ShopPanel", root, "Магазин");
            ScrollRect shopScroll = CreateScrollView("ShopScroll", shopPanel);
            PositionRect(shopScroll.GetComponent<RectTransform>(), new Vector2(0.04f, 0.16f), new Vector2(0.94f, 0.8f), Vector2.zero, Vector2.zero);
            RectTransform shopContent = shopScroll.content;
            VerticalLayoutGroup shopLayout = shopContent.GetComponent<VerticalLayoutGroup>();
            shopLayout.spacing = 18f;
            shopLayout.padding = new RectOffset(18, 18, 18, 18);
            ShopItemView shopItemTemplate = CreateShopItemTemplate(shopContent);
            TMP_Text coinsText = CreateText("CoinsText", shopPanel, "Монеты: 0", 26, TextAlignmentOptions.Left);
            SetLightText(coinsText);
            PositionRect(coinsText.rectTransform, new Vector2(0.06f, 0.845f), new Vector2(0.3f, 0.92f), Vector2.zero, Vector2.zero);
            coinsText.gameObject.SetActive(false);
            RectTransform shopFeedbackPlate = CreatePanel("FeedbackPlate", shopPanel, new Vector2(0.05f, 0.835f), new Vector2(0.94f, 0.92f), collectionTextPlateSprite != null ? collectionTextPlateSprite : lightPanelSprite, new Color(0.98f, 0.95f, 0.90f, 0.96f));
            TMP_Text shopFeedback = CreateText("FeedbackText", shopFeedbackPlate, "Выберите набор по бюджету и откройте его.", 18, TextAlignmentOptions.Left);
            Stretch(shopFeedback.rectTransform, new Vector2(18f, 6f), new Vector2(-18f, -6f));
            MainMenuButtonView closeShopButton = CreateButton("CloseShopButton", shopPanel, audioSource);
            PositionRect(closeShopButton.GetComponent<RectTransform>(), new Vector2(0.74f, 0.03f), new Vector2(0.94f, 0.11f), Vector2.zero, Vector2.zero);
            RectTransform rewardsPanel = CreatePanel("RewardsPanel", shopPanel, new Vector2(0.22f, 0.22f), new Vector2(0.78f, 0.72f), panelSprite, new Color(0.18f, 0.13f, 0.08f, 0.96f));
            rewardsPanel.gameObject.SetActive(false);
            RectTransform rewardsTitlePlate = CreatePanel("RewardsTitlePlate", rewardsPanel, new Vector2(0.10f, 0.84f), new Vector2(0.90f, 0.95f), collectionTextPlateSprite != null ? collectionTextPlateSprite : buttonTextPlateSprite != null ? buttonTextPlateSprite : lightPanelSprite, new Color(0.96f, 0.93f, 0.86f, 0.96f));
            TMP_Text rewardsTitleText = CreateText("RewardsTitleText", rewardsTitlePlate, "Полученные карты", 22, TextAlignmentOptions.Center);
            Stretch(rewardsTitleText.rectTransform, new Vector2(12f, 4f), new Vector2(-12f, -4f));
            RectTransform rewardsBodyPlate = CreatePanel("RewardsBodyPlate", rewardsPanel, new Vector2(0.08f, 0.20f), new Vector2(0.92f, 0.80f), collectionTextPlateSprite != null ? collectionTextPlateSprite : buttonTextPlateSprite != null ? buttonTextPlateSprite : lightPanelSprite, new Color(0.98f, 0.95f, 0.89f, 0.96f));
            TMP_Text rewardsText = CreateText("RewardsText", rewardsBodyPlate, string.Empty, 18, TextAlignmentOptions.Center);
            PositionRect(rewardsText.rectTransform, new Vector2(0.05f, 0.84f), new Vector2(0.95f, 0.98f), Vector2.zero, Vector2.zero);
            rewardsText.enableAutoSizing = true;
            rewardsText.fontSizeMin = 13f;
            rewardsText.fontSizeMax = 18f;
            ScrollRect rewardsScroll = CreateScrollView("RewardsScroll", rewardsBodyPlate);
            PositionRect(rewardsScroll.GetComponent<RectTransform>(), new Vector2(0.04f, 0.05f), new Vector2(0.96f, 0.80f), Vector2.zero, Vector2.zero);
            RectTransform rewardsCardsRoot = rewardsScroll.content;
            ConfigureGridContent(rewardsCardsRoot, new Vector2(152f, 214f), new Vector2(12f, 12f));
            CardCollectionItemView rewardsCardTemplate = CreateCardCollectionItemTemplate(rewardsCardsRoot);
            MainMenuButtonView rewardsCloseButton = CreateButton("RewardsCloseButton", rewardsPanel, audioSource);
            PositionRect(rewardsCloseButton.GetComponent<RectTransform>(), new Vector2(0.34f, 0.06f), new Vector2(0.66f, 0.15f), Vector2.zero, Vector2.zero);

            RectTransform tutorialPanel = CreateOverlayPanel("TutorialPanel", root, "Обучение");
            RectTransform tutorialContent = CreatePanel("TutorialContent", tutorialPanel, new Vector2(0.16f, 0.18f), new Vector2(0.84f, 0.82f), lightPanelSprite, new Color(0.96f, 0.92f, 0.84f, 0.97f));
            TMP_Text tutorialIntro = CreateText("TutorialIntro", tutorialContent, "Короткое введение перед первой партией.", 24, TextAlignmentOptions.TopLeft);
            PositionRect(tutorialIntro.rectTransform, new Vector2(0.06f, 0.80f), new Vector2(0.94f, 0.92f), Vector2.zero, Vector2.zero);
            TMP_Text tutorialBody = CreateText("TutorialBody", tutorialContent,
                "1. Сыгрывайте карты, чтобы давить на влияние и темп.\n" +
                "2. Политические очки тратятся на розыгрыш карт.\n" +
                "3. Следите за сторонниками и не отдавайте инициативу ботам.\n" +
                "4. Голосования и события часто переворачивают партию.\n" +
                "5. Колоду лучше держать компактной и понятной по ролям.",
                18,
                TextAlignmentOptions.TopLeft);
            PositionRect(tutorialBody.rectTransform, new Vector2(0.06f, 0.24f), new Vector2(0.94f, 0.76f), Vector2.zero, Vector2.zero);
            tutorialBody.enableAutoSizing = true;
            tutorialBody.fontSizeMin = 14f;
            tutorialBody.fontSizeMax = 18f;
            MainMenuButtonView startTutorialButton = CreateButton("StartTutorialButton", tutorialPanel, audioSource);
            PositionRect(startTutorialButton.GetComponent<RectTransform>(), new Vector2(0.30f, 0.04f), new Vector2(0.48f, 0.11f), Vector2.zero, Vector2.zero);
            MainMenuButtonView closeTutorialButton = CreateButton("CloseTutorialButton", tutorialPanel, audioSource);
            PositionRect(closeTutorialButton.GetComponent<RectTransform>(), new Vector2(0.72f, 0.04f), new Vector2(0.90f, 0.11f), Vector2.zero, Vector2.zero);

            RectTransform settingsPanel = CreateOverlayPanel("SettingsPanel", root, "Настройки");
            RectTransform settingsContent = CreatePanel("SettingsContent", settingsPanel, new Vector2(0.18f, 0.18f), new Vector2(0.82f, 0.82f), lightPanelSprite, new Color(0.96f, 0.92f, 0.84f, 0.97f));
            VerticalLayoutGroup settingsLayout = settingsContent.gameObject.AddComponent<VerticalLayoutGroup>();
            settingsLayout.spacing = 16f;
            settingsLayout.padding = new RectOffset(24, 24, 24, 24);
            settingsLayout.childControlWidth = true;
            settingsLayout.childControlHeight = true;
            settingsLayout.childForceExpandHeight = false;
            CreateText("MusicLabel", settingsContent, "Громкость музыки", 20, TextAlignmentOptions.Left);
            Slider musicSlider = CreateSlider("MusicSlider", settingsContent, 0f, 1f, 0.8f);
            CreateText("EffectsLabel", settingsContent, "Громкость эффектов", 20, TextAlignmentOptions.Left);
            Slider effectsSlider = CreateSlider("EffectsSlider", settingsContent, 0f, 1f, 0.85f);
            RectTransform fullscreenToggleRoot = CreateUnityToggle("FullscreenToggle", settingsContent, out Toggle fullscreenToggle);
            CreateText("FullscreenLabel", fullscreenToggleRoot, "Fullscreen", 20, TextAlignmentOptions.Left);
            CreateText("ResolutionLabel", settingsContent, "Разрешение", 20, TextAlignmentOptions.Left);
            TMP_Dropdown resolutionDropdown = CreateDropdown("ResolutionDropdown", settingsContent, new[] { "Текущее" });
            CreateText("QualityLabel", settingsContent, "Качество", 20, TextAlignmentOptions.Left);
            TMP_Dropdown qualityDropdown = CreateDropdown("QualityDropdown", settingsContent, new[] { "Low", "Medium", "High" });
            foreach (Transform child in settingsContent)
            {
                TMP_Text settingLabel = child.GetComponent<TMP_Text>();
                if (settingLabel == null)
                    continue;

                settingLabel.fontSize = 26f;
                LayoutElement labelLayout = settingLabel.GetComponent<LayoutElement>();
                if (labelLayout != null)
                {
                    labelLayout.minHeight = 34f;
                    labelLayout.preferredHeight = 40f;
                }
            }

            TMP_Text fullscreenLabel = fullscreenToggleRoot.GetComponentInChildren<TMP_Text>();
            if (fullscreenLabel != null)
            {
                fullscreenLabel.text = "Полноэкранный режим";
                fullscreenLabel.fontSize = 24f;
                PositionRect(fullscreenLabel.rectTransform, new Vector2(0.16f, 0.1f), new Vector2(0.96f, 0.9f), Vector2.zero, Vector2.zero);
            }

            if (resolutionDropdown.captionText != null)
                resolutionDropdown.captionText.fontSize = 22f;

            if (qualityDropdown.captionText != null)
                qualityDropdown.captionText.fontSize = 22f;

            SetHeight(resolutionDropdown.transform as RectTransform, 56f);
            SetHeight(qualityDropdown.transform as RectTransform, 56f);
            MainMenuButtonView applySettingsButton = CreateButton("ApplySettingsButton", settingsPanel, audioSource);
            PositionRect(applySettingsButton.GetComponent<RectTransform>(), new Vector2(0.24f, 0.03f), new Vector2(0.46f, 0.11f), Vector2.zero, Vector2.zero);
            MainMenuButtonView closeSettingsButton = CreateButton("CloseSettingsButton", settingsPanel, audioSource);
            PositionRect(closeSettingsButton.GetComponent<RectTransform>(), new Vector2(0.74f, 0.03f), new Vector2(0.94f, 0.11f), Vector2.zero, Vector2.zero);

            RectTransform profilePanel = CreateOverlayPanel("ProfilePanel", root, "Профиль");
            RectTransform profileContent = CreatePanel("ProfileContent", profilePanel, new Vector2(0.16f, 0.16f), new Vector2(0.84f, 0.82f), profilePanelSprite != null ? profilePanelSprite : lightPanelSprite, new Color(0.96f, 0.92f, 0.84f, 0.97f));
            Image profileCrest = CreateImage("ProfileCrest", profileContent, logoSprite, new Color(0.80f, 0.64f, 0.22f, 0.24f));
            PositionRect(profileCrest.rectTransform, new Vector2(0.68f, 0.46f), new Vector2(0.95f, 0.90f), Vector2.zero, Vector2.zero);
            profileCrest.preserveAspect = true;
            Image profileAccent = CreateImage("ProfileAccent", profileContent, buttonSprite, new Color(0.42f, 0.28f, 0.10f, 0.92f));
            PositionRect(profileAccent.rectTransform, new Vector2(0.04f, 0.10f), new Vector2(0.055f, 0.93f), Vector2.zero, Vector2.zero);
            RectTransform profileHeaderPlate = CreatePanel("ProfileHeaderPlate", profileContent, new Vector2(0.08f, 0.80f), new Vector2(0.42f, 0.93f), collectionTextPlateSprite != null ? collectionTextPlateSprite : lightPanelSprite, new Color(0.98f, 0.95f, 0.90f, 0.96f));
            TMP_Text profileHeaderText = CreateText("ProfileHeaderText", profileHeaderPlate, "Статистика", 22, TextAlignmentOptions.Center);
            Stretch(profileHeaderText.rectTransform, new Vector2(14f, 4f), new Vector2(-14f, -4f));
            RectTransform profileStatsPlate = CreatePanel("ProfileStatsPlate", profileContent, new Vector2(0.08f, 0.11f), new Vector2(0.96f, 0.77f), lightPanelSprite != null ? lightPanelSprite : profilePanelSprite, new Color(0.98f, 0.95f, 0.89f, 0.97f));
            RectTransform profileSummaryCard = CreatePanel("ProfileSummaryCard", profileStatsPlate, new Vector2(0.05f, 0.56f), new Vector2(0.62f, 0.92f), profileTextPlateSprite != null ? profileTextPlateSprite : lightPanelSprite, new Color(0.99f, 0.96f, 0.90f, 0.98f));
            RectTransform profileSummaryLabelPlate = CreatePanel("ProfileSummaryLabelPlate", profileSummaryCard, new Vector2(0.05f, 0.77f), new Vector2(0.42f, 0.93f), collectionTextPlateSprite != null ? collectionTextPlateSprite : lightPanelSprite, new Color(0.98f, 0.95f, 0.90f, 0.96f));
            TMP_Text profileSummaryLabel = CreateText("ProfileSummaryLabel", profileSummaryLabelPlate, "Профиль", 16, TextAlignmentOptions.Center);
            Stretch(profileSummaryLabel.rectTransform, new Vector2(10f, 4f), new Vector2(-10f, -4f));
            TMP_Text profileNameText = CreateText("ProfileNameText", profileSummaryCard, "Senator", 28, TextAlignmentOptions.TopLeft);
            PositionRect(profileNameText.rectTransform, new Vector2(0.08f, 0.40f), new Vector2(0.92f, 0.72f), Vector2.zero, Vector2.zero);
            profileNameText.enableAutoSizing = true;
            profileNameText.fontSizeMin = 20f;
            profileNameText.fontSizeMax = 28f;
            profileNameText.overflowMode = TextOverflowModes.Ellipsis;
            TMP_Text profileRankText = CreateText("ProfileRankText", profileSummaryCard, "Bronze  •  Уровень 1", 18, TextAlignmentOptions.TopLeft);
            PositionRect(profileRankText.rectTransform, new Vector2(0.08f, 0.14f), new Vector2(0.92f, 0.42f), Vector2.zero, Vector2.zero);
            profileRankText.enableAutoSizing = true;
            profileRankText.fontSizeMin = 14f;
            profileRankText.fontSizeMax = 18f;
            profileRankText.overflowMode = TextOverflowModes.Ellipsis;

            RectTransform profileMatchCard = CreatePanel("ProfileMatchCard", profileStatsPlate, new Vector2(0.66f, 0.56f), new Vector2(0.95f, 0.92f), profileTextPlateSprite != null ? profileTextPlateSprite : lightPanelSprite, new Color(0.99f, 0.96f, 0.90f, 0.98f));
            RectTransform profileMatchLabelPlate = CreatePanel("ProfileMatchLabelPlate", profileMatchCard, new Vector2(0.08f, 0.80f), new Vector2(0.92f, 0.92f), collectionTextPlateSprite != null ? collectionTextPlateSprite : lightPanelSprite, new Color(0.98f, 0.95f, 0.90f, 0.96f));
            TMP_Text profileMatchLabel = CreateText("ProfileMatchLabel", profileMatchLabelPlate, "Матчи", 16, TextAlignmentOptions.Center);
            Stretch(profileMatchLabel.rectTransform, new Vector2(10f, 4f), new Vector2(-10f, -4f));
            TMP_Text profileMatchText = CreateText("ProfileMatchText", profileMatchCard, string.Empty, 14, TextAlignmentOptions.TopLeft);
            PositionRect(profileMatchText.rectTransform, new Vector2(0.10f, 0.10f), new Vector2(0.90f, 0.72f), Vector2.zero, Vector2.zero);
            profileMatchText.enableAutoSizing = true;
            profileMatchText.fontSizeMin = 10f;
            profileMatchText.fontSizeMax = 14f;
            profileMatchText.lineSpacing = 1f;
            profileMatchText.overflowMode = TextOverflowModes.Ellipsis;

            RectTransform profileAccountCard = CreatePanel("ProfileAccountCard", profileStatsPlate, new Vector2(0.05f, 0.10f), new Vector2(0.48f, 0.47f), profileTextPlateSprite != null ? profileTextPlateSprite : lightPanelSprite, new Color(0.99f, 0.96f, 0.90f, 0.98f));
            RectTransform profileAccountLabelPlate = CreatePanel("ProfileAccountLabelPlate", profileAccountCard, new Vector2(0.06f, 0.80f), new Vector2(0.94f, 0.92f), collectionTextPlateSprite != null ? collectionTextPlateSprite : lightPanelSprite, new Color(0.98f, 0.95f, 0.90f, 0.96f));
            TMP_Text profileAccountLabel = CreateText("ProfileAccountLabel", profileAccountLabelPlate, "Аккаунт", 16, TextAlignmentOptions.Center);
            Stretch(profileAccountLabel.rectTransform, new Vector2(10f, 4f), new Vector2(-10f, -4f));
            TMP_Text profileAccountText = CreateText("ProfileAccountText", profileAccountCard, string.Empty, 14, TextAlignmentOptions.TopLeft);
            PositionRect(profileAccountText.rectTransform, new Vector2(0.08f, 0.10f), new Vector2(0.92f, 0.72f), Vector2.zero, Vector2.zero);
            profileAccountText.enableAutoSizing = true;
            profileAccountText.fontSizeMin = 10f;
            profileAccountText.fontSizeMax = 14f;
            profileAccountText.lineSpacing = 1f;
            profileAccountText.overflowMode = TextOverflowModes.Ellipsis;

            RectTransform profileCollectionCard = CreatePanel("ProfileCollectionCard", profileStatsPlate, new Vector2(0.52f, 0.10f), new Vector2(0.95f, 0.47f), profileTextPlateSprite != null ? profileTextPlateSprite : lightPanelSprite, new Color(0.99f, 0.96f, 0.90f, 0.98f));
            RectTransform profileCollectionLabelPlate = CreatePanel("ProfileCollectionLabelPlate", profileCollectionCard, new Vector2(0.06f, 0.80f), new Vector2(0.94f, 0.92f), collectionTextPlateSprite != null ? collectionTextPlateSprite : lightPanelSprite, new Color(0.98f, 0.95f, 0.90f, 0.96f));
            TMP_Text profileCollectionLabel = CreateText("ProfileCollectionLabel", profileCollectionLabelPlate, "Коллекция", 16, TextAlignmentOptions.Center);
            Stretch(profileCollectionLabel.rectTransform, new Vector2(10f, 4f), new Vector2(-10f, -4f));
            TMP_Text profileCollectionText = CreateText("ProfileCollectionText", profileCollectionCard, string.Empty, 14, TextAlignmentOptions.TopLeft);
            PositionRect(profileCollectionText.rectTransform, new Vector2(0.08f, 0.10f), new Vector2(0.92f, 0.72f), Vector2.zero, Vector2.zero);
            profileCollectionText.enableAutoSizing = true;
            profileCollectionText.fontSizeMin = 10f;
            profileCollectionText.fontSizeMax = 14f;
            profileCollectionText.lineSpacing = 1f;
            profileCollectionText.overflowMode = TextOverflowModes.Ellipsis;
            MainMenuButtonView logoutButton = CreateButton("LogoutButton", profilePanel, audioSource);
            PositionRect(logoutButton.GetComponent<RectTransform>(), new Vector2(0.50f, 0.03f), new Vector2(0.70f, 0.10f), Vector2.zero, Vector2.zero);
            MainMenuButtonView closeProfileButton = CreateButton("CloseProfileButton", profilePanel, audioSource);
            PositionRect(closeProfileButton.GetComponent<RectTransform>(), new Vector2(0.74f, 0.03f), new Vector2(0.94f, 0.10f), Vector2.zero, Vector2.zero);

            RectTransform authPanel = CreatePanel("AuthPanel", root, new Vector2(0.33f, 0.26f), new Vector2(0.67f, 0.80f), panelSprite, new Color(0.2f, 0.14f, 0.08f, 0.96f));
            VerticalLayoutGroup authLayout = authPanel.gameObject.AddComponent<VerticalLayoutGroup>();
            authLayout.spacing = 10f;
            authLayout.padding = new RectOffset(26, 26, 24, 20);
            authLayout.childControlWidth = true;
            authLayout.childControlHeight = true;
            authLayout.childForceExpandHeight = false;

            RectTransform authTitlePlate = CreatePanel("AuthTitlePlate", authPanel, Vector2.zero, Vector2.one, collectionTextPlateSprite != null ? collectionTextPlateSprite : authTextPlateSprite != null ? authTextPlateSprite : lightPanelSprite, new Color(0.98f, 0.95f, 0.90f, 0.98f));
            SetHeight(authTitlePlate, 48f);
            TMP_Text authTitle = CreateText("AuthTitle", authTitlePlate, "Вход в аккаунт", 24, TextAlignmentOptions.Center);
            Stretch(authTitle.rectTransform, new Vector2(14f, 6f), new Vector2(-14f, -6f));
            RectTransform authHintPlate = CreatePanel("AuthHintPlate", authPanel, Vector2.zero, Vector2.one, authTextPlateSprite != null ? authTextPlateSprite : lightPanelSprite, new Color(0.98f, 0.95f, 0.90f, 1f));
            SetHeight(authHintPlate, 44f);
            TMP_Text authHint = CreateText("AuthHint", authHintPlate, "Логин и пароль обязательны. Ник нужен только при регистрации.", 15, TextAlignmentOptions.Center);
            Stretch(authHint.rectTransform, new Vector2(16f, 6f), new Vector2(-16f, -6f));
            TMP_InputField authLoginInput = CreateInputField("AuthLoginInput", authPanel, "Логин", authTextPlateSprite);
            TMP_InputField authPasswordInput = CreateInputField("AuthPasswordInput", authPanel, "Пароль", authTextPlateSprite);
            authPasswordInput.contentType = TMP_InputField.ContentType.Password;
            authPasswordInput.asteriskChar = '*';
            TMP_InputField authNicknameInput = CreateInputField("AuthNicknameInput", authPanel, "Никнейм для регистрации", authTextPlateSprite);
            RectTransform authStatusPlate = CreatePanel("AuthStatusPlate", authPanel, Vector2.zero, Vector2.one, authTextPlateSprite != null ? authTextPlateSprite : lightPanelSprite, new Color(0.98f, 0.95f, 0.90f, 1f));
            SetHeight(authStatusPlate, 42f);
            TMP_Text authStatusText = CreateText("AuthStatusText", authStatusPlate, string.Empty, 15, TextAlignmentOptions.Center);
            Stretch(authStatusText.rectTransform, new Vector2(16f, 6f), new Vector2(-16f, -6f));

            RectTransform authButtonsRow = CreateRect("AuthButtonsRow", authPanel, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            SetHeight(authButtonsRow, 48f);
            HorizontalLayoutGroup authButtonsLayout = authButtonsRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            authButtonsLayout.spacing = 12f;
            authButtonsLayout.childControlWidth = true;
            authButtonsLayout.childControlHeight = true;
            authButtonsLayout.childForceExpandWidth = true;
            authButtonsLayout.childForceExpandHeight = false;
            MainMenuButtonView authLoginButton = CreateButton("AuthLoginButton", authButtonsRow, audioSource);
            MainMenuButtonView authRegisterButton = CreateButton("AuthRegisterButton", authButtonsRow, audioSource);
            SetHeight(authLoginButton.GetComponent<RectTransform>(), 40f);
            SetHeight(authRegisterButton.GetComponent<RectTransform>(), 40f);

            MainMenuButtonView playButton = CreateButton("PlayButton", mainButtonsPanel, audioSource);
            MainMenuButtonView tutorialButton = CreateButton("TutorialButton", mainButtonsPanel, audioSource);
            MainMenuButtonView collectionButton = CreateButton("CollectionButton", mainButtonsPanel, audioSource);
            MainMenuButtonView shopButton = CreateButton("ShopButton", mainButtonsPanel, audioSource);
            MainMenuButtonView settingsButton = CreateButton("SettingsButton", mainButtonsPanel, audioSource);
            MainMenuButtonView profileButton = CreateButton("ProfileButton", root, audioSource);
            PositionRect(profileButton.GetComponent<RectTransform>(), new Vector2(0.82f, 0.89f), new Vector2(0.95f, 0.945f), Vector2.zero, Vector2.zero);
            MainMenuButtonView exitButton = CreateButton("ExitButton", mainButtonsPanel, audioSource);

            CollectionPanelController collectionController = collectionPanel.gameObject.AddComponent<CollectionPanelController>();
            SetField(collectionController, "cardDatabase", cardDatabase);
            SetField(collectionController, "cardArtLibrary", cardArtLibrary);
            SetField(collectionController, "profileDatabase", profileDatabase);
            SetField(collectionController, "contentRoot", collectionContent);
            SetField(collectionController, "itemPrefab", cardItemTemplate);
            SetField(collectionController, "rarityDropdown", rarityDropdown);
            SetField(collectionController, "typeDropdown", typeDropdown);
            SetField(collectionController, "previewArtwork", previewArtwork);
            SetField(collectionController, "previewBackground", previewBackground);
            SetField(collectionController, "previewTitle", previewTitle);
            SetField(collectionController, "previewDescription", previewDescription);
            SetField(collectionController, "databaseInfoText", databaseInfoText);
            SetField(collectionController, "deckInfoText", deckInfoText);
            SetField(collectionController, "deckToggleButton", deckToggleButton);
            SetField(collectionController, "fallbackCardBackground", collectionCardBackgroundSprite);

            ShopPanelController shopController = shopPanel.gameObject.AddComponent<ShopPanelController>();
            SetField(shopController, "shopCatalog", shopCatalog);
            SetField(shopController, "cardDatabase", cardDatabase);
            SetField(shopController, "cardArtLibrary", cardArtLibrary);
            SetField(shopController, "profileDatabase", profileDatabase);
            SetField(shopController, "contentRoot", shopContent);
            SetField(shopController, "itemPrefab", shopItemTemplate);
            SetField(shopController, "coinsText", headerCoinsText);
            SetField(shopController, "feedbackText", shopFeedback);
            SetField(shopController, "rewardsPanel", rewardsPanel.gameObject);
            SetField(shopController, "rewardsText", rewardsText);
            SetField(shopController, "rewardsCloseButton", rewardsCloseButton.GetComponent<Button>());
            SetField(shopController, "rewardsCardsRoot", rewardsCardsRoot);
            SetField(shopController, "rewardsCardPrefab", rewardsCardTemplate);

            SettingsPanelController settingsController = settingsPanel.gameObject.AddComponent<SettingsPanelController>();
            SetField(settingsController, "settingsService", settingsService);
            SetField(settingsController, "musicSlider", musicSlider);
            SetField(settingsController, "effectsSlider", effectsSlider);
            SetField(settingsController, "fullscreenToggle", fullscreenToggle);
            SetField(settingsController, "resolutionDropdown", resolutionDropdown);
            SetField(settingsController, "qualityDropdown", qualityDropdown);
            SetField(settingsController, "applyButton", applySettingsButton.GetComponent<Button>());

            LobbyUIController lobbyController = lobbyRoot.gameObject.AddComponent<LobbyUIController>();
            SetField(lobbyController, "lobbyManager", lobbyManager);
            SetField(lobbyController, "lanMatchCoordinator", lanMatchCoordinator);
            SetField(lobbyController, "networkGameManager", networkGameManager);
            SetField(lobbyController, "profileDatabase", profileDatabase);
            SetField(lobbyController, "playerListRoot", lobbyListRoot);
            SetField(lobbyController, "playerItemPrefab", lobbyItemTemplate);
            SetField(lobbyController, "roomCodeInput", roomCodeInput);
            SetField(lobbyController, "roomCodeText", roomCodeText);
            SetField(lobbyController, "statusText", lobbyStatusText);
            SetField(lobbyController, "listTitleText", playersTitle);
            SetField(lobbyController, "readyToggle", readyToggle);
            SetField(lobbyController, "hostButton", hostButton.GetComponent<Button>());
            SetField(lobbyController, "joinButton", joinButton.GetComponent<Button>());
            SetField(lobbyController, "startButton", startButton.GetComponent<Button>());
            SetField(lobbyController, "leaveButton", leaveButton.GetComponent<Button>());
            SetField(lobbyController, "lobbyRoot", lobbyRoot.gameObject);

            MainMenuController menuController = root.gameObject.AddComponent<MainMenuController>();
            SetField(menuController, "canvas", canvas);
            SetField(menuController, "backgroundImage", background);
            SetField(menuController, "logoPanel", logoPanel.gameObject);
            SetField(menuController, "mainButtonsPanel", mainButtonsPanel.gameObject);
            SetField(menuController, "playModePanel", playModePanel.gameObject);
            SetField(menuController, "settingsPanel", settingsPanel.gameObject);
            SetField(menuController, "collectionPanel", collectionPanel.gameObject);
            SetField(menuController, "shopPanel", shopPanel.gameObject);
            SetField(menuController, "tutorialPanel", tutorialPanel.gameObject);
            SetField(menuController, "profilePanel", profilePanel.gameObject);
            SetField(menuController, "authPanel", authPanel.gameObject);
            SetField(menuController, "currencyPanel", currencyPanel.gameObject);
            SetField(menuController, "playButton", playButton);
            SetField(menuController, "tutorialButton", tutorialButton);
            SetField(menuController, "collectionButton", collectionButton);
            SetField(menuController, "shopButton", shopButton);
            SetField(menuController, "settingsButton", settingsButton);
            SetField(menuController, "profileButton", profileButton);
            SetField(menuController, "exitButton", exitButton);
            SetField(menuController, "onlineButton", onlineButton);
            SetField(menuController, "offlineButton", offlineButton);
            SetField(menuController, "playBackButton", playBackButton);
            SetField(menuController, "closeCollectionButton", closeCollectionButton);
            SetField(menuController, "closeShopButton", closeShopButton);
            SetField(menuController, "closeTutorialButton", closeTutorialButton);
            SetField(menuController, "closeSettingsButton", closeSettingsButton);
            SetField(menuController, "closeProfileButton", closeProfileButton);
            SetField(menuController, "startTutorialButton", startTutorialButton);
            SetField(menuController, "authLoginButton", authLoginButton);
            SetField(menuController, "authRegisterButton", authRegisterButton);
            SetField(menuController, "logoutButton", logoutButton);
            SetField(menuController, "lobbyUIController", lobbyController);
            SetField(menuController, "collectionPanelController", collectionController);
            SetField(menuController, "shopPanelController", shopController);
            SetField(menuController, "settingsPanelController", settingsController);
            SetField(menuController, "profileDatabase", profileDatabase);
            SetField(menuController, "apiAuthenticationService", apiAuthenticationService);
            SetField(menuController, "offlineSceneName", offlineSceneName);
            SetField(menuController, "tutorialSceneName", offlineSceneName);
            SetField(menuController, "profileNameText", profileNameText);
            SetField(menuController, "profileRankText", profileRankText);
            SetField(menuController, "profileAccountText", profileAccountText);
            SetField(menuController, "profileCollectionText", profileCollectionText);
            SetField(menuController, "profileMatchText", profileMatchText);
            SetField(menuController, "coinsText", headerCoinsText);
            SetField(menuController, "authLoginInput", authLoginInput);
            SetField(menuController, "authPasswordInput", authPasswordInput);
            SetField(menuController, "authNicknameInput", authNicknameInput);
            SetField(menuController, "authStatusText", authStatusText);

            if (!initializeControllers)
                return;

            collectionController.Initialize();
            shopController.Initialize();
            settingsController.Initialize();
            lobbyController.Initialize();
            menuController.Initialize();
        }

        private void InitializeExistingRuntime()
        {
            GameObject serviceRoot = GetOrCreateServiceRoot();

            if (serviceRoot == null)
            {
                BuildScene(forceRebuild: false, initializeControllers: true);
                return;
            }

            DontDestroyOnLoad(serviceRoot);

            ApiAuthenticationService apiAuthenticationService = serviceRoot.GetComponent<ApiAuthenticationService>();
            if (apiAuthenticationService == null)
                apiAuthenticationService = serviceRoot.AddComponent<ApiAuthenticationService>();

            PlayerProfileDatabase profileDatabase = serviceRoot.GetComponent<PlayerProfileDatabase>();
            if (profileDatabase == null)
                profileDatabase = serviceRoot.AddComponent<PlayerProfileDatabase>();
            else
                profileDatabase.ReinitializeStorage();

            GameSettingsService settingsService = serviceRoot.GetComponent<GameSettingsService>();
            if (settingsService == null)
                settingsService = serviceRoot.AddComponent<GameSettingsService>();

            PersistentAudioService audioService = serviceRoot.GetComponent<PersistentAudioService>();
            if (audioService == null)
                audioService = serviceRoot.AddComponent<PersistentAudioService>();

            audioService.Initialize(settingsService, backgroundMusicClip);
            AudioSource audioSource = audioService.EffectsSource;

            OnlineLobbyManager lobbyManager = serviceRoot.GetComponent<OnlineLobbyManager>();
            if (lobbyManager == null)
                lobbyManager = serviceRoot.AddComponent<OnlineLobbyManager>();

            NetworkGameManager networkGameManager = serviceRoot.GetComponent<NetworkGameManager>();
            if (networkGameManager == null)
                networkGameManager = serviceRoot.AddComponent<NetworkGameManager>();

            LanMatchCoordinator lanMatchCoordinator = serviceRoot.GetComponent<LanMatchCoordinator>();
            if (lanMatchCoordinator == null)
                lanMatchCoordinator = serviceRoot.AddComponent<LanMatchCoordinator>();

            lanMatchCoordinator.Initialize(lobbyManager, profileDatabase);

            RectTransform runtimeRoot = FindRuntimeRoot();
            RuntimeSceneBindings runtimeBindings = EnsureExistingRuntimeBindings(runtimeRoot, audioSource);

            CollectionPanelController collectionController = FindObjectOfType<CollectionPanelController>();
            if (collectionController != null)
            {
                SetField(collectionController, "cardDatabase", cardDatabase);
                SetField(collectionController, "cardArtLibrary", cardArtLibrary);
                SetField(collectionController, "profileDatabase", profileDatabase);
                collectionController.Initialize();
            }

            ShopPanelController shopController = FindObjectOfType<ShopPanelController>();
            if (shopController != null)
            {
                SetField(shopController, "shopCatalog", shopCatalog);
                SetField(shopController, "cardDatabase", cardDatabase);
                SetField(shopController, "cardArtLibrary", cardArtLibrary);
                SetField(shopController, "profileDatabase", profileDatabase);
                if (runtimeBindings.rewardsCardsRoot != null)
                    SetField(shopController, "rewardsCardsRoot", runtimeBindings.rewardsCardsRoot);
                if (runtimeBindings.rewardsCardTemplate != null)
                    SetField(shopController, "rewardsCardPrefab", runtimeBindings.rewardsCardTemplate);
                shopController.Initialize();
            }

            SettingsPanelController settingsController = FindObjectOfType<SettingsPanelController>();
            if (settingsController != null)
            {
                SetField(settingsController, "settingsService", settingsService);
                settingsController.Initialize();
            }

            LobbyUIController lobbyController = FindObjectOfType<LobbyUIController>();
            if (lobbyController != null)
            {
                SetField(lobbyController, "lobbyManager", lobbyManager);
                SetField(lobbyController, "lanMatchCoordinator", lanMatchCoordinator);
                SetField(lobbyController, "networkGameManager", networkGameManager);
                SetField(lobbyController, "profileDatabase", profileDatabase);
                if (runtimeBindings.playersTitleText != null)
                    SetField(lobbyController, "listTitleText", runtimeBindings.playersTitleText);
                lobbyController.Initialize();
            }

            MainMenuController menuController = FindObjectOfType<MainMenuController>();
            if (menuController != null)
            {
                SetField(menuController, "profileDatabase", profileDatabase);
                SetField(menuController, "apiAuthenticationService", apiAuthenticationService);
                if (runtimeBindings.tutorialPanel != null)
                    SetField(menuController, "tutorialPanel", runtimeBindings.tutorialPanel.gameObject);
                if (runtimeBindings.tutorialButton != null)
                    SetField(menuController, "tutorialButton", runtimeBindings.tutorialButton);
                if (runtimeBindings.closeTutorialButton != null)
                    SetField(menuController, "closeTutorialButton", runtimeBindings.closeTutorialButton);
                if (runtimeBindings.startTutorialButton != null)
                    SetField(menuController, "startTutorialButton", runtimeBindings.startTutorialButton);
                SetField(menuController, "tutorialSceneName", offlineSceneName);
                menuController.Initialize();
            }
        }

        private sealed class RuntimeSceneBindings
        {
            public RectTransform tutorialPanel;
            public MainMenuButtonView tutorialButton;
            public MainMenuButtonView closeTutorialButton;
            public MainMenuButtonView startTutorialButton;
            public RectTransform rewardsCardsRoot;
            public CardCollectionItemView rewardsCardTemplate;
            public TMP_Text playersTitleText;
        }

        private RuntimeSceneBindings EnsureExistingRuntimeBindings(RectTransform root, AudioSource audioSource)
        {
            RuntimeSceneBindings bindings = new RuntimeSceneBindings();
            if (root == null)
                return bindings;

            bindings.playersTitleText = FindChildComponent<TMP_Text>(root, "PlayModePanel/LobbyRoot/LobbyCard/PlayersBlock/PlayersTitlePlate/PlayersTitle");

            NormalizeExistingCollectionGrid(root);

            bindings.tutorialButton = EnsureTutorialButton(root, audioSource);
            bindings.tutorialPanel = EnsureTutorialPanel(root, audioSource, out MainMenuButtonView startTutorialButton, out MainMenuButtonView closeTutorialButton);
            bindings.startTutorialButton = startTutorialButton;
            bindings.closeTutorialButton = closeTutorialButton;

            bindings.rewardsCardTemplate = EnsureRewardsGrid(root, out RectTransform rewardsCardsRoot);
            bindings.rewardsCardsRoot = rewardsCardsRoot;
            return bindings;
        }

        private static RectTransform FindRuntimeRoot()
        {
            GameObject canvasObject = GameObject.Find("Canvas");
            if (canvasObject == null)
                return null;

            Transform root = canvasObject.transform.Find("Root");
            return root as RectTransform;
        }

        private void NormalizeExistingCollectionGrid(RectTransform root)
        {
            RectTransform collectionContent = FindChildComponent<RectTransform>(root, "CollectionPanel/CollectionListPanel/CollectionScroll/Viewport/Content");
            if (collectionContent == null)
                return;

            GridLayoutGroup existingGrid = collectionContent.GetComponent<GridLayoutGroup>();
            if (existingGrid == null)
            {
                ConfigureGridContent(collectionContent, new Vector2(152f, 214f), new Vector2(12f, 12f));
                return;
            }

            existingGrid.cellSize = new Vector2(152f, 214f);
            existingGrid.spacing = new Vector2(12f, 12f);
            existingGrid.padding = new RectOffset(10, 10, 10, 10);
            existingGrid.startCorner = GridLayoutGroup.Corner.UpperLeft;
            existingGrid.startAxis = GridLayoutGroup.Axis.Horizontal;
            existingGrid.childAlignment = TextAnchor.UpperCenter;
            existingGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            existingGrid.constraintCount = 3;
        }

        private MainMenuButtonView EnsureTutorialButton(RectTransform root, AudioSource audioSource)
        {
            MainMenuButtonView existingButton = FindChildComponent<MainMenuButtonView>(root, "MainButtonsPanel/TutorialButton");
            if (existingButton != null)
                return existingButton;

            RectTransform mainButtonsPanel = FindChildComponent<RectTransform>(root, "MainButtonsPanel");
            if (mainButtonsPanel == null)
                return null;

            return CreateButton("TutorialButton", mainButtonsPanel, audioSource);
        }

        private RectTransform EnsureTutorialPanel(
            RectTransform root,
            AudioSource audioSource,
            out MainMenuButtonView startTutorialButton,
            out MainMenuButtonView closeTutorialButton)
        {
            startTutorialButton = FindChildComponent<MainMenuButtonView>(root, "TutorialPanel/StartTutorialButton");
            closeTutorialButton = FindChildComponent<MainMenuButtonView>(root, "TutorialPanel/CloseTutorialButton");

            RectTransform existingPanel = FindChildComponent<RectTransform>(root, "TutorialPanel");
            if (existingPanel != null)
                return existingPanel;

            RectTransform tutorialPanel = CreateOverlayPanel("TutorialPanel", root, "Обучение");
            RectTransform tutorialContent = CreatePanel("TutorialContent", tutorialPanel, new Vector2(0.16f, 0.18f), new Vector2(0.84f, 0.82f), lightPanelSprite, new Color(0.96f, 0.92f, 0.84f, 0.97f));
            TMP_Text tutorialIntro = CreateText("TutorialIntro", tutorialContent, "Короткое введение перед первой партией.", 24, TextAlignmentOptions.TopLeft);
            PositionRect(tutorialIntro.rectTransform, new Vector2(0.06f, 0.80f), new Vector2(0.94f, 0.92f), Vector2.zero, Vector2.zero);
            TMP_Text tutorialBody = CreateText(
                "TutorialBody",
                tutorialContent,
                "1. Сыгрывайте карты, чтобы давить на влияние и темп.\n" +
                "2. Политические очки тратятся на розыгрыш карт.\n" +
                "3. Следите за сторонниками и не отдавайте инициативу ботам.\n" +
                "4. Голосования и события часто переворачивают партию.\n" +
                "5. Колоду лучше держать компактной и понятной по ролям.",
                18,
                TextAlignmentOptions.TopLeft);
            PositionRect(tutorialBody.rectTransform, new Vector2(0.06f, 0.24f), new Vector2(0.94f, 0.76f), Vector2.zero, Vector2.zero);
            tutorialBody.enableAutoSizing = true;
            tutorialBody.fontSizeMin = 14f;
            tutorialBody.fontSizeMax = 18f;

            startTutorialButton = CreateButton("StartTutorialButton", tutorialPanel, audioSource);
            PositionRect(startTutorialButton.GetComponent<RectTransform>(), new Vector2(0.30f, 0.04f), new Vector2(0.48f, 0.11f), Vector2.zero, Vector2.zero);
            closeTutorialButton = CreateButton("CloseTutorialButton", tutorialPanel, audioSource);
            PositionRect(closeTutorialButton.GetComponent<RectTransform>(), new Vector2(0.72f, 0.04f), new Vector2(0.90f, 0.11f), Vector2.zero, Vector2.zero);
            return tutorialPanel;
        }

        private CardCollectionItemView EnsureRewardsGrid(RectTransform root, out RectTransform rewardsCardsRoot)
        {
            rewardsCardsRoot = FindChildComponent<RectTransform>(root, "ShopPanel/RewardsPanel/RewardsBodyPlate/RewardsScroll/Viewport/Content");
            if (rewardsCardsRoot != null)
            {
                CardCollectionItemView existingTemplate = FindChildComponent<CardCollectionItemView>(root, "ShopPanel/RewardsPanel/RewardsBodyPlate/RewardsScroll/Viewport/Content/CardCollectionItemTemplate");
                if (existingTemplate == null)
                    existingTemplate = CreateCardCollectionItemTemplate(rewardsCardsRoot);

                return existingTemplate;
            }

            RectTransform rewardsBodyPlate = FindChildComponent<RectTransform>(root, "ShopPanel/RewardsPanel/RewardsBodyPlate");
            if (rewardsBodyPlate == null)
                return null;

            ScrollRect rewardsScroll = CreateScrollView("RewardsScroll", rewardsBodyPlate);
            PositionRect(rewardsScroll.GetComponent<RectTransform>(), new Vector2(0.04f, 0.05f), new Vector2(0.96f, 0.80f), Vector2.zero, Vector2.zero);
            rewardsCardsRoot = rewardsScroll.content;
            ConfigureGridContent(rewardsCardsRoot, new Vector2(152f, 214f), new Vector2(12f, 12f));
            return CreateCardCollectionItemTemplate(rewardsCardsRoot);
        }

        private static TComponent FindChildComponent<TComponent>(Transform root, string relativePath) where TComponent : Component
        {
            if (root == null)
                return null;

            Transform child = string.IsNullOrWhiteSpace(relativePath) ? root : root.Find(relativePath);
            return child == null ? null : child.GetComponent<TComponent>();
        }

        private static GameObject GetOrCreateServiceRoot()
        {
            GameObject persistentRoot = null;
            GameObject sceneRoot = null;

            foreach (GameObject candidate in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (candidate == null || candidate.name != "MenuServices" || !candidate.scene.IsValid())
                    continue;

                if (candidate.scene.name == "DontDestroyOnLoad")
                {
                    if (persistentRoot == null)
                        persistentRoot = candidate;
                    else if (Application.isPlaying && candidate != persistentRoot)
                        Destroy(candidate);

                    continue;
                }

                if (sceneRoot == null)
                    sceneRoot = candidate;
                else if (Application.isPlaying)
                    Destroy(candidate);
            }

            GameObject chosenRoot = persistentRoot != null ? persistentRoot : sceneRoot;
            if (chosenRoot != null)
            {
                if (Application.isPlaying && persistentRoot != null && sceneRoot != null && sceneRoot != persistentRoot)
                    Destroy(sceneRoot);

                return chosenRoot;
            }

            GameObject namedRoot = GameObject.Find("MenuServices");
            if (namedRoot != null)
                return namedRoot;

            return new GameObject("MenuServices");
        }

        private void CleanupPartialGeneratedObjects()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                Transform child = transform.GetChild(i);
                if (child.name != "Canvas" && child.name != "MenuServices")
                    continue;

                DestroyImmediate(child.gameObject);
            }
        }

        private void CreateLogoContent(RectTransform parent)
        {
            MainMenuLogoView logoView = parent.gameObject.AddComponent<MainMenuLogoView>();
            Shadow glow = parent.gameObject.AddComponent<Shadow>();
            glow.effectColor = new Color(0.84f, 0.72f, 0.36f, 0.55f);
            glow.effectDistance = new Vector2(0f, 0f);
            SetField(logoView, "titleShadow", glow);
            SetField(logoView, "logoShadow", glow);

            RectTransform logoSlot = CreateRect("LogoSlot", parent, new Vector2(0.26f, 0.10f), new Vector2(0.74f, 0.90f), Vector2.zero, Vector2.zero);
            AspectRatioFitter slotFitter = logoSlot.gameObject.AddComponent<AspectRatioFitter>();
            slotFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            slotFitter.aspectRatio = 1f;

            Image logo = CreateImage("Logo", logoSlot, logoSprite, new Color(0.92f, 0.81f, 0.42f, 1f));
            Stretch(logo.rectTransform);
            logo.preserveAspect = true;

            SetField<MainMenuLogoView, TMP_Text>(logoView, "titleText", null);
            SetField(logoView, "logoImage", logo);
            SetField(logoView, "fadeInSeconds", 1f);
        }

        private Canvas CreateCanvas()
        {
            GameObject canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        private MainMenuButtonView CreateButton(string name, Transform parent, AudioSource audioSource)
        {
            GameObject root = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
            root.transform.SetParent(parent, false);
            Image image = root.GetComponent<Image>();
            ApplyImageStyle(image, buttonFrameSprite != null ? buttonFrameSprite : buttonSprite, new Color(0.98f, 0.94f, 0.86f, 1f));
            Button button = root.GetComponent<Button>();
            button.targetGraphic = image;
            ColorBlock buttonColors = button.colors;
            buttonColors.normalColor = Color.white;
            buttonColors.highlightedColor = new Color(1f, 0.98f, 0.92f, 1f);
            buttonColors.pressedColor = new Color(0.90f, 0.85f, 0.76f, 1f);
            buttonColors.selectedColor = new Color(1f, 0.98f, 0.92f, 1f);
            buttonColors.disabledColor = new Color(0.70f, 0.66f, 0.60f, 0.90f);
            buttonColors.colorMultiplier = 1f;
            buttonColors.fadeDuration = 0.08f;
            button.colors = buttonColors;
            LayoutElement layout = root.GetComponent<LayoutElement>();
            layout.preferredHeight = 40f;
            layout.minHeight = 36f;

            Image sheen = CreateImage("Sheen", root.transform, null, new Color(1f, 1f, 1f, 0.05f));
            sheen.raycastTarget = false;
            PositionRect(sheen.rectTransform, new Vector2(0.05f, 0.54f), new Vector2(0.95f, 0.84f), Vector2.zero, Vector2.zero);
            sheen.transform.SetAsFirstSibling();

            Image labelPlate = CreateImage("LabelPlate", root.transform, null, new Color(1f, 1f, 1f, 0.09f));
            labelPlate.raycastTarget = false;
            PositionRect(labelPlate.rectTransform, new Vector2(0.06f, 0.16f), new Vector2(0.94f, 0.84f), Vector2.zero, Vector2.zero);
            TMP_Text label = CreateText("Label", root.transform as RectTransform, GetDefaultButtonLabel(name), 16, TextAlignmentOptions.Center);
            label.raycastTarget = false;
            label.enableAutoSizing = true;
            label.fontSizeMin = 10f;
            label.fontSizeMax = 16f;
            label.fontStyle = FontStyles.Bold;
            label.color = new Color(0.12f, 0.08f, 0.03f, 1f);
            Stretch(label.rectTransform, new Vector2(14f, 4f), new Vector2(-14f, -4f));

            MainMenuButtonView view = root.AddComponent<MainMenuButtonView>();
            SetField(view, "backgroundImage", image);
            SetField(view, "labelText", label);
            SetField(view, "audioSource", audioSource);
            SetField(view, "hoverClip", hoverClip);
            SetField(view, "clickClip", clickClip);
            return view;
        }

        private static string GetDefaultButtonLabel(string name)
        {
            switch (name)
            {
                case "PlayButton":
                    return "Играть";
                case "CollectionButton":
                    return "Коллекция";
                case "TutorialButton":
                    return "Обучение";
                case "ShopButton":
                    return "Магазин";
                case "SettingsButton":
                    return "Настройки";
                case "ExitButton":
                    return "Выход";
                case "OnlineButton":
                    return "Онлайн";
                case "OfflineButton":
                    return "Оффлайн";
                case "BackButton":
                case "CloseCollectionButton":
                case "CloseShopButton":
                case "CloseTutorialButton":
                case "CloseSettingsButton":
                    return "Назад";
                case "ApplySettingsButton":
                    return "Применить";
                case "AuthLoginButton":
                    return "Войти";
                case "AuthRegisterButton":
                    return "Регистрация";
                case "LogoutButton":
                    return "Выйти";
                case "RewardsCloseButton":
                    return "Закрыть";
                case "HostButton":
                    return "Создать комнату";
                case "JoinButton":
                    return "Войти";
                case "StartButton":
                    return "Начать игру";
                case "StartTutorialButton":
                    return "Начать";
                case "LeaveButton":
                    return "Покинуть";
                case "DeckToggleButton":
                    return "Добавить";
                default:
                    return name;
            }
        }

        private RectTransform CreateOverlayPanel(string name, RectTransform parent, string title)
        {
            RectTransform panel = CreatePanel(name, parent, new Vector2(0.1f, 0.08f), new Vector2(0.9f, 0.88f), panelSprite, new Color(0.18f, 0.13f, 0.08f, 0.95f));
            RectTransform titlePlate = CreatePanel("TitlePlate", panel, new Vector2(0.20f, 0.895f), new Vector2(0.80f, 0.97f), collectionTextPlateSprite != null ? collectionTextPlateSprite : buttonTextPlateSprite != null ? buttonTextPlateSprite : lightPanelSprite, new Color(0.98f, 0.95f, 0.90f, 0.98f));
            TMP_Text titleText = CreateText("Title", titlePlate, title, 28, TextAlignmentOptions.Center);
            Stretch(titleText.rectTransform, new Vector2(14f, 6f), new Vector2(-14f, -6f));
            panel.gameObject.SetActive(false);
            return panel;
        }

        private TMP_InputField CreateInputField(string name, Transform parent, string placeholderText, Sprite backgroundOverride = null)
        {
            Sprite inputSprite = backgroundOverride != null ? backgroundOverride : connectionTextPlateSprite != null ? connectionTextPlateSprite : lightPanelSprite;
            RectTransform root = CreatePanel(name, parent as RectTransform, Vector2.zero, Vector2.one, inputSprite, new Color(0.99f, 0.97f, 0.92f, 1f));
            SetHeight(root, 48f);
            Image textPlate = CreateImage("TextPlate", root, null, new Color(1f, 1f, 1f, 0.10f));
            PositionRect(textPlate.rectTransform, new Vector2(0.04f, 0.15f), new Vector2(0.96f, 0.85f), Vector2.zero, Vector2.zero);
            RectTransform viewport = CreateRect("TextViewport", root, Vector2.zero, Vector2.one, new Vector2(24f, 8f), new Vector2(-16f, -8f));
            TMP_Text text = CreateText("Text", viewport, string.Empty, 18, TextAlignmentOptions.Left);
            Stretch(text.rectTransform, Vector2.zero, Vector2.zero);
            text.color = new Color(0.10f, 0.07f, 0.03f, 1f);
            TMP_Text placeholder = CreateText("Placeholder", viewport, placeholderText, 18, TextAlignmentOptions.Left);
            Stretch(placeholder.rectTransform, Vector2.zero, Vector2.zero);
            placeholder.color = new Color(0.24f, 0.19f, 0.14f, 0.72f);
            TMP_InputField inputField = root.gameObject.AddComponent<TMP_InputField>();
            inputField.image = root.GetComponent<Image>();
            inputField.textViewport = viewport;
            inputField.textComponent = text;
            inputField.placeholder = placeholder;
            inputField.customCaretColor = true;
            inputField.caretColor = new Color(0.12f, 0.08f, 0.03f, 1f);
            inputField.selectionColor = new Color(0.45f, 0.33f, 0.15f, 0.18f);
            return inputField;
        }

        private TMP_Dropdown CreateDropdown(string name, Transform parent, string[] options)
        {
            RectTransform root = CreatePanel(name, parent as RectTransform, Vector2.zero, Vector2.one, connectionTextPlateSprite != null ? connectionTextPlateSprite : lightPanelSprite, new Color(0.99f, 0.97f, 0.92f, 1f));
            SetHeight(root, 48f);
            TMP_Dropdown dropdown = root.gameObject.AddComponent<TMP_Dropdown>();
            dropdown.options.Clear();
            foreach (string option in options)
                dropdown.options.Add(new TMP_Dropdown.OptionData(option));

            Image captionPlate = CreateImage("CaptionPlate", root, null, new Color(1f, 1f, 1f, 0.08f));
            PositionRect(captionPlate.rectTransform, new Vector2(0.04f, 0.15f), new Vector2(0.96f, 0.85f), Vector2.zero, Vector2.zero);
            TMP_Text label = CreateText("Label", root, options.Length > 0 ? options[0] : string.Empty, 18, TextAlignmentOptions.Left);
            Stretch(label.rectTransform, new Vector2(24f, 8f), new Vector2(-48f, -8f));
            label.color = new Color(0.10f, 0.07f, 0.03f, 1f);
            TMP_Text arrow = CreateText("Arrow", root, "▼", 16, TextAlignmentOptions.Center);
            PositionRect(arrow.rectTransform, new Vector2(0.86f, 0.18f), new Vector2(0.96f, 0.82f), Vector2.zero, Vector2.zero);
            arrow.fontStyle = FontStyles.Bold;
            arrow.color = new Color(0.12f, 0.08f, 0.03f, 1f);
            dropdown.captionText = label;
            ConfigureDropdownTemplate(dropdown, root);
            return dropdown;
        }

        private Slider CreateSlider(string name, Transform parent, float minValue, float maxValue, float value)
        {
            RectTransform root = CreateRect(name, parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            SetHeight(root, 34f);
            Slider slider = root.gameObject.AddComponent<Slider>();
            slider.minValue = minValue;
            slider.maxValue = maxValue;
            slider.value = value;
            ConfigureSliderVisuals(slider, root);
            return slider;
        }

        private RectTransform CreateUnityToggle(string name, Transform parent, out Toggle toggle)
        {
            RectTransform root = CreateRect(name, parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Image background = root.gameObject.AddComponent<Image>();
            ApplyImageStyle(background, lightPanelSprite, new Color(0.94f, 0.90f, 0.82f, 1f));
            SetHeight(root, 56f);
            toggle = root.gameObject.AddComponent<Toggle>();
            ConfigureToggleVisuals(toggle, root);
            return root;
        }

        private ScrollRect CreateScrollView(string name, Transform parent)
        {
            RectTransform root = CreatePanel(name, parent as RectTransform, Vector2.zero, Vector2.one, lightPanelSprite, new Color(0.97f, 0.94f, 0.88f, 1f));
            SetHeight(root, 160f);
            ScrollRect scroll = root.gameObject.AddComponent<ScrollRect>();
            GameObject viewport = new GameObject("Viewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(RectMask2D));
            viewport.transform.SetParent(root, false);
            RectTransform viewportRect = viewport.GetComponent<RectTransform>();
            Stretch(viewportRect);
            ApplyImageStyle(viewport.GetComponent<Image>(), lightPanelSprite, new Color(1f, 1f, 1f, 0.28f));

            GameObject content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            RectTransform contentRect = content.GetComponent<RectTransform>();
            AnchorContentTop(contentRect);
            VerticalLayoutGroup layout = content.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.padding = new RectOffset(10, 10, 10, 10);
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewportRect;
            scroll.content = contentRect;
            scroll.horizontal = false;
            return scroll;
        }

        private static void ConfigureGridContent(RectTransform contentRoot, Vector2 cellSize, Vector2 spacing)
        {
            VerticalLayoutGroup verticalLayout = contentRoot.GetComponent<VerticalLayoutGroup>();
            if (verticalLayout != null)
                DestroyImmediate(verticalLayout);

            ContentSizeFitter fitter = contentRoot.GetComponent<ContentSizeFitter>();
            if (fitter == null)
                fitter = contentRoot.gameObject.AddComponent<ContentSizeFitter>();

            GridLayoutGroup gridLayout = contentRoot.gameObject.AddComponent<GridLayoutGroup>();
            gridLayout.cellSize = cellSize;
            gridLayout.spacing = spacing;
            gridLayout.padding = new RectOffset(10, 10, 10, 10);
            gridLayout.startCorner = GridLayoutGroup.Corner.UpperLeft;
            gridLayout.startAxis = GridLayoutGroup.Axis.Horizontal;
            gridLayout.childAlignment = TextAnchor.UpperCenter;
            gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gridLayout.constraintCount = 3;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        }

        private LobbyPlayerItemView CreateLobbyPlayerItemTemplate(Transform parent)
        {
            RectTransform root = CreatePanel("LobbyPlayerItemTemplate", parent as RectTransform, Vector2.zero, Vector2.one, lobbyProfilePlateSprite != null ? lobbyProfilePlateSprite : lightPanelSprite, new Color(0.9f, 0.86f, 0.78f, 1f));
            SetHeight(root, 72f);
            LobbyPlayerItemView view = root.gameObject.AddComponent<LobbyPlayerItemView>();
            Image ready = CreateImage("ReadyIndicator", root, buttonSprite, new Color(0.79f, 0.67f, 0.25f, 1f));
            PositionRect(ready.rectTransform, new Vector2(0.03f, 0.24f), new Vector2(0.08f, 0.76f), Vector2.zero, Vector2.zero);
            Image nicknamePlate = CreateImage("NicknamePlate", root, lobbyProfilePlateSprite != null ? lobbyProfilePlateSprite : buttonTextPlateSprite != null ? buttonTextPlateSprite : lightPanelSprite, new Color(0.98f, 0.94f, 0.87f, 0.52f));
            PositionRect(nicknamePlate.rectTransform, new Vector2(0.11f, 0.50f), new Vector2(0.96f, 0.90f), Vector2.zero, Vector2.zero);
            TMP_Text nickname = CreateText("NicknameText", root, "Игрок", 18, TextAlignmentOptions.Left);
            PositionRect(nickname.rectTransform, new Vector2(0.14f, 0.54f), new Vector2(0.94f, 0.88f), Vector2.zero, Vector2.zero);
            nickname.fontStyle = FontStyles.Bold;
            nickname.enableAutoSizing = true;
            nickname.fontSizeMin = 12f;
            nickname.fontSizeMax = 18f;
            Image statusPlate = CreateImage("StatusPlate", root, lobbyProfilePlateSprite != null ? lobbyProfilePlateSprite : buttonTextPlateSprite != null ? buttonTextPlateSprite : lightPanelSprite, new Color(0.98f, 0.94f, 0.87f, 0.36f));
            PositionRect(statusPlate.rectTransform, new Vector2(0.11f, 0.10f), new Vector2(0.96f, 0.44f), Vector2.zero, Vector2.zero);
            TMP_Text status = CreateText("StatusText", root, "Уровень 1 • Не готов", 14, TextAlignmentOptions.Left);
            PositionRect(status.rectTransform, new Vector2(0.14f, 0.12f), new Vector2(0.94f, 0.42f), Vector2.zero, Vector2.zero);
            status.enableAutoSizing = true;
            status.fontSizeMin = 10f;
            status.fontSizeMax = 14f;
            SetField(view, "nicknameText", nickname);
            SetField(view, "statusText", status);
            SetField(view, "readyIndicator", ready);
            root.gameObject.SetActive(false);
            return view;
        }

        private CardCollectionItemView CreateCardCollectionItemTemplate(Transform parent)
        {
            RectTransform root = CreatePanel("CardCollectionItemTemplate", parent as RectTransform, Vector2.zero, Vector2.one, collectionCardBackgroundSprite, Color.white);
            LayoutElement layout = root.gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = 206f;
            layout.preferredWidth = 148f;
            layout.minHeight = 206f;
            layout.minWidth = 148f;
            root.gameObject.AddComponent<Button>();
            root.gameObject.AddComponent<CanvasGroup>();
            CardCollectionItemView view = root.gameObject.AddComponent<CardCollectionItemView>();
            RectTransform artViewport = CreateRect("ArtViewport", root, new Vector2(0.10f, 0.54f), new Vector2(0.90f, 0.84f), Vector2.zero, Vector2.zero);
            artViewport.gameObject.AddComponent<RectMask2D>();
            Image artBackdrop = CreateImage("ArtBackdrop", artViewport, null, new Color(0.22f, 0.2f, 0.18f, 0.45f));
            Stretch(artBackdrop.rectTransform);
            Image art = CreateImage("Artwork", artViewport, null, Color.white);
            Stretch(art.rectTransform);
            art.preserveAspect = true;
            Image metaPanel = CreateImage("MetaPanel", root, lightPanelSprite, new Color(0.96f, 0.92f, 0.84f, 0.84f));
            metaPanel.raycastTarget = false;
            PositionRect(metaPanel.rectTransform, new Vector2(0.07f, 0.18f), new Vector2(0.93f, 0.50f), Vector2.zero, Vector2.zero);
            Image footerPanel = CreateImage("FooterPanel", root, lightPanelSprite, new Color(0.98f, 0.94f, 0.87f, 0.88f));
            footerPanel.raycastTarget = false;
            PositionRect(footerPanel.rectTransform, new Vector2(0.08f, 0.07f), new Vector2(0.92f, 0.18f), Vector2.zero, Vector2.zero);
            TMP_Text title = CreateText("TitleText", root, "Карта", 17, TextAlignmentOptions.Center);
            PositionRect(title.rectTransform, new Vector2(0.09f, 0.34f), new Vector2(0.91f, 0.47f), Vector2.zero, Vector2.zero);
            title.enableAutoSizing = true;
            title.fontSizeMin = 12f;
            title.fontSizeMax = 17f;
            title.overflowMode = TextOverflowModes.Ellipsis;
            TMP_Text rarity = CreateText("RarityText", root, "Common", 13, TextAlignmentOptions.Left);
            PositionRect(rarity.rectTransform, new Vector2(0.10f, 0.24f), new Vector2(0.58f, 0.31f), Vector2.zero, Vector2.zero);
            rarity.enableAutoSizing = true;
            rarity.fontSizeMin = 10f;
            rarity.fontSizeMax = 13f;
            rarity.overflowMode = TextOverflowModes.Truncate;
            TMP_Text cost = CreateText("CostText", root, "1", 13, TextAlignmentOptions.Right);
            PositionRect(cost.rectTransform, new Vector2(0.62f, 0.24f), new Vector2(0.90f, 0.31f), Vector2.zero, Vector2.zero);
            cost.enableAutoSizing = true;
            cost.fontSizeMin = 10f;
            cost.fontSizeMax = 13f;
            TMP_Text deckState = CreateText("DeckStateText", root, "Не в колоде", 12, TextAlignmentOptions.Center);
            PositionRect(deckState.rectTransform, new Vector2(0.10f, 0.08f), new Vector2(0.90f, 0.17f), Vector2.zero, Vector2.zero);
            deckState.enableAutoSizing = true;
            deckState.fontSizeMin = 10f;
            deckState.fontSizeMax = 12f;
            deckState.overflowMode = TextOverflowModes.Ellipsis;
            TMP_Text lockText = CreateText("LockText", root, "Не получена", 12, TextAlignmentOptions.Center);
            PositionRect(lockText.rectTransform, new Vector2(0.10f, 0.58f), new Vector2(0.90f, 0.69f), Vector2.zero, Vector2.zero);
            lockText.enableAutoSizing = true;
            lockText.fontSizeMin = 9f;
            lockText.fontSizeMax = 12f;
            lockText.overflowMode = TextOverflowModes.Ellipsis;
            SetField(view, "backgroundImage", root.GetComponent<Image>());
            SetField(view, "artworkImage", art);
            SetField(view, "titleText", title);
            SetField(view, "rarityText", rarity);
            SetField(view, "costText", cost);
            SetField(view, "deckStateText", deckState);
            SetField(view, "lockText", lockText);
            SetField(view, "fallbackBackgroundSprite", collectionCardBackgroundSprite);
            root.gameObject.SetActive(false);
            return view;
        }

        private ShopItemView CreateShopItemTemplate(Transform parent)
        {
            RectTransform root = CreatePanel("ShopItemTemplate", parent as RectTransform, Vector2.zero, Vector2.one, lightPanelSprite, new Color(0.9f, 0.86f, 0.78f, 1f));
            LayoutElement layout = root.gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = 148f;
            layout.minHeight = 132f;
            root.gameObject.AddComponent<Button>();
            ShopItemView view = root.gameObject.AddComponent<ShopItemView>();
            Image icon = CreateImage("Icon", root, panelSprite, new Color(0.22f, 0.2f, 0.18f, 0.55f));
            PositionRect(icon.rectTransform, new Vector2(0.03f, 0.14f), new Vector2(0.18f, 0.86f), Vector2.zero, Vector2.zero);
            Image accent = CreateImage("Accent", root, buttonSprite, new Color(0.92f, 0.75f, 0.27f, 0.92f));
            PositionRect(accent.rectTransform, new Vector2(0.015f, 0.1f), new Vector2(0.03f, 0.9f), Vector2.zero, Vector2.zero);
            TMP_Text title = CreateText("TitleText", root, "Бустер", 20, TextAlignmentOptions.Left);
            PositionRect(title.rectTransform, new Vector2(0.22f, 0.58f), new Vector2(0.72f, 0.86f), Vector2.zero, Vector2.zero);
            TMP_Text tier = CreateText("TierText", root, "Набор", 13, TextAlignmentOptions.Center);
            PositionRect(tier.rectTransform, new Vector2(0.21f, 0.76f), new Vector2(0.72f, 0.92f), Vector2.zero, Vector2.zero);
            TMP_Text description = CreateText("DescriptionText", root, "Описание набора.", 16, TextAlignmentOptions.TopLeft);
            PositionRect(description.rectTransform, new Vector2(0.22f, 0.16f), new Vector2(0.82f, 0.58f), Vector2.zero, Vector2.zero);
            TMP_Text contents = CreateText("ContentsText", root, "0 карт", 14, TextAlignmentOptions.Left);
            PositionRect(contents.rectTransform, new Vector2(0.22f, 0.06f), new Vector2(0.72f, 0.18f), Vector2.zero, Vector2.zero);
            TMP_Text price = CreateText("PriceText", root, "150 coins", 18, TextAlignmentOptions.Right);
            PositionRect(price.rectTransform, new Vector2(0.74f, 0.58f), new Vector2(0.95f, 0.86f), Vector2.zero, Vector2.zero);
            TMP_Text action = CreateText("ActionText", root, "Открыть", 14, TextAlignmentOptions.Center);
            PositionRect(action.rectTransform, new Vector2(0.74f, 0.20f), new Vector2(0.95f, 0.36f), Vector2.zero, Vector2.zero);
            Image seal = CreateImage("Seal", root, buttonSprite, new Color(0.32f, 0.18f, 0.07f, 0.92f));
            PositionRect(seal.rectTransform, new Vector2(0.77f, 0.08f), new Vector2(0.93f, 0.18f), Vector2.zero, Vector2.zero);
            SetField(view, "iconImage", icon);
            SetField(view, "accentImage", accent);
            SetField(view, "titleText", title);
            SetField(view, "tierText", tier);
            SetField(view, "descriptionText", description);
            SetField(view, "contentsText", contents);
            SetField(view, "priceText", price);
            SetField(view, "sealImage", seal);
            SetField(view, "actionText", action);
            root.gameObject.SetActive(false);
            return view;
        }

        private RectTransform CreatePanel(string name, RectTransform parent, Vector2 anchorMin, Vector2 anchorMax, Sprite sprite, Color color)
        {
            RectTransform rect = CreateRect(name, parent, anchorMin, anchorMax, Vector2.zero, Vector2.zero);
            Image image = rect.gameObject.AddComponent<Image>();
            ApplyImageStyle(image, sprite, color);
            Shadow shadow = rect.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0.10f, 0.06f, 0.02f, 0.20f);
            shadow.effectDistance = new Vector2(0f, -1.5f);
            return rect;
        }

        private Image CreateImage(string name, Transform parent, Sprite sprite, Color color)
        {
            GameObject imageObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            imageObject.transform.SetParent(parent, false);
            Image image = imageObject.GetComponent<Image>();
            ApplyImageStyle(image, sprite, color);
            return image;
        }

        private TMP_Text CreateText(string name, RectTransform parent, string value, int size, TextAlignmentOptions alignment)
        {
            GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);
            TMP_Text text = textObject.GetComponent<TMP_Text>();
            text.font = fontAsset;
            text.text = value;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = new Color(0.07f, 0.05f, 0.02f, 1f);
            text.fontStyle = FontStyles.Bold;
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            LayoutElement layout = textObject.AddComponent<LayoutElement>();
            layout.minHeight = size + 6f;
            layout.preferredHeight = size + 12f;
            return text;
        }

        private RectTransform CreateRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            GameObject root = new GameObject(name, typeof(RectTransform));
            root.transform.SetParent(parent, false);
            RectTransform rect = root.GetComponent<RectTransform>();
            PositionRect(rect, anchorMin, anchorMax, offsetMin, offsetMax);
            return rect;
        }

        private static void Stretch(RectTransform rect, Vector2? offsetMin = null, Vector2? offsetMax = null)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = offsetMin ?? Vector2.zero;
            rect.offsetMax = offsetMax ?? Vector2.zero;
        }

        private static void AnchorContentTop(RectTransform rect)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;
            rect.localScale = Vector3.one;
        }

        private static void PositionRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            rect.localScale = Vector3.one;
        }

        private static void SetHeight(RectTransform rect, float height)
        {
            LayoutElement layout = rect.GetComponent<LayoutElement>();
            if (layout == null)
                layout = rect.gameObject.AddComponent<LayoutElement>();

            layout.preferredHeight = height;
            layout.minHeight = height;
        }

        private static void SetLightText(TMP_Text text)
        {
            if (text != null)
                text.color = new Color(0.95f, 0.90f, 0.80f, 1f);
        }

        private static void ApplyImageStyle(Image image, Sprite sprite, Color color)
        {
            if (image == null)
                return;

            image.sprite = sprite;
            image.type = sprite == null ? Image.Type.Simple : Image.Type.Sliced;
            image.color = color;
            image.preserveAspect = false;
        }

        private static void SetField<TTarget, TValue>(TTarget target, string fieldName, TValue value) where TTarget : class
        {
            System.Reflection.FieldInfo field = typeof(TTarget).GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            field?.SetValue(target, value);
        }

        private void ConfigureDropdownTemplate(TMP_Dropdown dropdown, RectTransform root)
        {
            GameObject templateObject = new GameObject("Template", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            templateObject.transform.SetParent(root, false);
            RectTransform templateRect = templateObject.GetComponent<RectTransform>();
            templateRect.anchorMin = new Vector2(0f, 0f);
            templateRect.anchorMax = new Vector2(1f, 0f);
            templateRect.pivot = new Vector2(0.5f, 1f);
            templateRect.anchoredPosition = new Vector2(0f, -4f);
            templateRect.sizeDelta = new Vector2(0f, 190f);
            ApplyImageStyle(templateObject.GetComponent<Image>(), lightPanelSprite, new Color(0.98f, 0.95f, 0.90f, 1f));
            templateObject.SetActive(false);

            GameObject viewportObject = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            viewportObject.transform.SetParent(templateObject.transform, false);
            RectTransform viewportRect = viewportObject.GetComponent<RectTransform>();
            Stretch(viewportRect);
            ApplyImageStyle(viewportObject.GetComponent<Image>(), lightPanelSprite, new Color(1f, 1f, 1f, 0.24f));

            GameObject contentObject = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentObject.transform.SetParent(viewportObject.transform, false);
            RectTransform contentRect = contentObject.GetComponent<RectTransform>();
            AnchorContentTop(contentRect);
            VerticalLayoutGroup contentLayout = contentObject.GetComponent<VerticalLayoutGroup>();
            contentLayout.spacing = 2f;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = true;
            contentLayout.childForceExpandHeight = false;
            contentObject.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            GameObject itemObject = new GameObject("Item", typeof(RectTransform), typeof(Toggle), typeof(Image));
            itemObject.transform.SetParent(contentObject.transform, false);
            RectTransform itemRect = itemObject.GetComponent<RectTransform>();
            itemRect.sizeDelta = new Vector2(0f, 32f);
            LayoutElement itemLayout = itemObject.AddComponent<LayoutElement>();
            itemLayout.minHeight = 32f;
            itemLayout.preferredHeight = 34f;
            Image itemImage = itemObject.GetComponent<Image>();
            ApplyImageStyle(itemImage, buttonFrameSprite != null ? buttonFrameSprite : buttonSprite, new Color(0.98f, 0.94f, 0.86f, 1f));
            Toggle itemToggle = itemObject.GetComponent<Toggle>();

            Image checkmark = CreateImage("Checkmark", itemObject.transform, buttonSprite, new Color(0.78f, 0.66f, 0.23f, 1f));
            PositionRect(checkmark.rectTransform, new Vector2(0.03f, 0.22f), new Vector2(0.09f, 0.78f), Vector2.zero, Vector2.zero);
            TMP_Text itemLabel = CreateText("Item Label", itemRect, "Option", 18, TextAlignmentOptions.Left);
            PositionRect(itemLabel.rectTransform, new Vector2(0.12f, 0.1f), new Vector2(0.95f, 0.9f), Vector2.zero, Vector2.zero);
            itemToggle.graphic = checkmark;
            itemToggle.targetGraphic = itemImage;
            ColorBlock toggleColors = itemToggle.colors;
            toggleColors.normalColor = Color.white;
            toggleColors.highlightedColor = new Color(1f, 0.98f, 0.92f, 1f);
            toggleColors.pressedColor = new Color(0.90f, 0.85f, 0.76f, 1f);
            toggleColors.selectedColor = new Color(1f, 0.98f, 0.92f, 1f);
            toggleColors.disabledColor = new Color(0.72f, 0.68f, 0.62f, 0.9f);
            itemToggle.colors = toggleColors;

            ScrollRect scrollRect = templateObject.GetComponent<ScrollRect>();
            scrollRect.viewport = viewportRect;
            scrollRect.content = contentRect;
            scrollRect.horizontal = false;

            dropdown.template = templateRect;
            dropdown.itemText = itemLabel;
            dropdown.itemImage = itemImage;
            dropdown.targetGraphic = root.GetComponent<Image>();
            dropdown.RefreshShownValue();
        }

        private void ConfigureSliderVisuals(Slider slider, RectTransform root)
        {
            Image track = CreateImage("Track", root, lightPanelSprite, new Color(0.94f, 0.90f, 0.82f, 0.96f));
            PositionRect(track.rectTransform, new Vector2(0f, 0.34f), new Vector2(1f, 0.66f), Vector2.zero, Vector2.zero);

            RectTransform fillArea = CreateRect("FillArea", root, new Vector2(0f, 0.34f), new Vector2(1f, 0.66f), new Vector2(2f, 0f), new Vector2(-2f, 0f));
            Image fill = CreateImage("Fill", fillArea, buttonSprite, new Color(0.92f, 0.80f, 0.36f, 1f));
            Stretch(fill.rectTransform);

            RectTransform handleArea = CreateRect("HandleArea", root, Vector2.zero, Vector2.one, new Vector2(10f, 0f), new Vector2(-10f, 0f));
            Image handle = CreateImage("Handle", handleArea, buttonSprite, new Color(0.99f, 0.95f, 0.88f, 1f));
            handle.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            handle.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            handle.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            handle.rectTransform.sizeDelta = new Vector2(20f, 20f);
            handle.rectTransform.anchoredPosition = Vector2.zero;

            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
        }

        private void ConfigureToggleVisuals(Toggle toggle, RectTransform root)
        {
            Image background = root.GetComponent<Image>();
            if (background != null)
                background.raycastTarget = true;

            Image box = CreateImage("Box", root, buttonSprite, new Color(0.28f, 0.2f, 0.11f, 1f));
            PositionRect(box.rectTransform, new Vector2(0.03f, 0.18f), new Vector2(0.14f, 0.82f), Vector2.zero, Vector2.zero);
            Image checkmark = CreateImage("Checkmark", box.rectTransform, buttonSprite, new Color(0.95f, 0.80f, 0.26f, 1f));
            PositionRect(checkmark.rectTransform, new Vector2(0.18f, 0.18f), new Vector2(0.82f, 0.82f), Vector2.zero, Vector2.zero);
            toggle.graphic = checkmark;
            toggle.targetGraphic = background != null ? background : box;
        }
    }
}




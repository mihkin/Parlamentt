using System;
using System.IO;
using System.Reflection;
using ParliamentGame;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace ParliamentGame.EditorTools
{
    public static class ParliamentSceneBuilder
    {
        private const string RootFolder = "Assets/Parliament";
        private const string PrefabsFolder = RootFolder + "/Prefabs";
        private const string FontsFolder = RootFolder + "/Fonts";
        private const string SourceFontsFolder = FontsFolder + "/SourceFonts";
        private const string ScenePath = "Assets/Scenes/PvEGameScene.unity";
        private const string FontAssetPath = FontsFolder + "/ParliamentCyrillicTMP.asset";
        private const string CardArtLibraryPath = RootFolder + "/CardArtLibrary.asset";
        private const string BackgroundSpritePath = RootFolder + "/images/NoFon.png";
        private const string PanelSpritePath = RootFolder + "/images/Fon.png";
        private const string ButtonSpritePath = RootFolder + "/images/button.png";
        private const string CardBackSpritePath = RootFolder + "/images/card.png";
        private const string CardFrontSpritePath = RootFolder + "/images/cardface 1.png";

        private static TMP_FontAsset fontAsset;
        private static Sprite backgroundSprite;
        private static Sprite panelSprite;
        private static Sprite buttonSprite;
        private static Sprite cardBackSprite;
        private static Sprite cardFrontSprite;

        [MenuItem("Parliament/Clean Generated PvE Assets")]
        public static void CleanGeneratedAssets()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("Нельзя очищать сгенерированные assets во время Play Mode. Сначала останови Play.");
                return;
            }

            DeleteAssetIfExists(ScenePath);
            DeleteAssetIfExists(PrefabsFolder + "/CardView.prefab");
            DeleteAssetIfExists(PrefabsFolder + "/BotPanel.prefab");
            DeleteAssetIfExists(PrefabsFolder + "/PlayerPanel.prefab");
            DeleteAssetIfExists(PrefabsFolder + "/EventDisplayPanel.prefab");
            DeleteAssetIfExists(FontAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Старые сгенерированные PvE assets очищены.");
        }

        [MenuItem("Parliament/Build PvE Scene")]
        public static void BuildPvEScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("Сцену нельзя пересобирать во время Play Mode. Останови Play и запусти Parliament > Build PvE Scene снова.");
                return;
            }

            EnsureFolders();
            EnsureTextMeshProResources();
            fontAsset = EnsureCyrillicFontAsset();
            LoadVisualSprites();

            CreateCardViewPrefab();
            CreateParticipantPanelPrefab("BotPanel", false);
            CreateParticipantPanelPrefab("PlayerPanel", true);
            CreateEventDisplayPrefab();

            GameObject cardPrefabObject = LoadPrefabObject("CardView");
            GameObject botPanelPrefabObject = LoadPrefabObject("BotPanel");
            GameObject playerPanelPrefabObject = LoadPrefabObject("PlayerPanel");
            GameObject eventDisplayPrefabObject = LoadPrefabObject("EventDisplayPanel");
            CardView cardPrefab = cardPrefabObject.GetComponent<CardView>();
            CardArtLibrary cardArtLibrary = EnsureCardArtLibraryAsset();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "PvEGameScene";

            GameObject camera = new GameObject("Main Camera");
            camera.tag = "MainCamera";
            Camera cam = camera.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.10f, 0.09f, 0.08f);
            camera.transform.position = new Vector3(0, 0, -10);

            GameObject eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<StandaloneInputModule>();

            Canvas canvas = CreateCanvas();
            GameObject gameRoot = CreateRect("GameRoot", canvas.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Image sceneBackground = CreateStretchImage("Background", gameRoot.transform, Color.white);
            ApplyImageStyle(sceneBackground, backgroundSprite, new Color(0.62f, 0.58f, 0.5f, 1f));
            sceneBackground.raycastTarget = false;
            sceneBackground.transform.SetAsFirstSibling();
            Image backgroundShade = CreateStretchImage("BackgroundShade", gameRoot.transform, new Color(0.08f, 0.06f, 0.04f, 0.34f));
            backgroundShade.raycastTarget = false;
            backgroundShade.transform.SetSiblingIndex(1);

            TMP_Text roundText = CreateTopLabel("RoundText", gameRoot.transform, "Раунд 1", new Vector2(0.86f, 0.94f), new Vector2(0.98f, 0.99f));
            TMP_Text timerText = CreateTopLabel("TimerPanel", gameRoot.transform, "00:60", new Vector2(0.02f, 0.94f), new Vector2(0.16f, 0.99f));
            TMP_Text neutralText = CreateTopLabel("NeutralPanel", gameRoot.transform, "Нейтралы: 60%", new Vector2(0.86f, 0.885f), new Vector2(0.98f, 0.935f));

            GameObject botRoot = CreateRect("BotPanelsRoot", gameRoot.transform, new Vector2(0.255f, 0.765f), new Vector2(0.775f, 0.97f), Vector2.zero, Vector2.zero);
            HorizontalLayoutGroup botLayout = botRoot.AddComponent<HorizontalLayoutGroup>();
            botLayout.spacing = 14;
            botLayout.childControlHeight = true;
            botLayout.childControlWidth = true;
            botLayout.childForceExpandHeight = true;
            botLayout.childForceExpandWidth = true;
            botLayout.padding = new RectOffset(4, 4, 4, 4);

            ParticipantPanelView[] botPanels = new ParticipantPanelView[3];
            for (int i = 0; i < botPanels.Length; i++)
            {
                GameObject botPanelObject = PrefabUtility.InstantiatePrefab(botPanelPrefabObject, botRoot.transform) as GameObject;
                ParticipantPanelView botPanel = botPanelObject.GetComponent<ParticipantPanelView>();
                botPanel.name = $"BotPanel_{i + 1}";
                botPanels[i] = botPanel;
            }

            GameObject playerPanelObject = PrefabUtility.InstantiatePrefab(playerPanelPrefabObject, gameRoot.transform) as GameObject;
            ParticipantPanelView playerPanel = playerPanelObject.GetComponent<ParticipantPanelView>();
            playerPanel.name = "PlayerPanel";
            SetAnchors(playerPanel.GetComponent<RectTransform>(), new Vector2(0.02f, 0.02f), new Vector2(0.24f, 0.27f), Vector2.zero, Vector2.zero);

            GameObject playerHandRoot = CreatePanel("PlayerHandRoot", gameRoot.transform, new Vector2(0.245f, 0.02f), new Vector2(0.755f, 0.30f), new Color(0.92f, 0.88f, 0.8f, 0.94f));
            HorizontalLayoutGroup handLayout = playerHandRoot.AddComponent<HorizontalLayoutGroup>();
            handLayout.spacing = 8;
            handLayout.padding = new RectOffset(8, 8, 8, 8);
            handLayout.childControlHeight = true;
            handLayout.childControlWidth = false;
            handLayout.childForceExpandWidth = false;
            handLayout.childForceExpandHeight = false;

            GameObject deckPanel = CreatePanel("DeckPanel", gameRoot.transform, new Vector2(0.76f, 0.02f), new Vector2(0.855f, 0.30f), new Color(0f, 0f, 0f, 0f));
            deckPanel.GetComponent<Image>().enabled = false;
            VerticalLayoutGroup deckLayout = deckPanel.AddComponent<VerticalLayoutGroup>();
            deckLayout.spacing = 6;
            deckLayout.padding = new RectOffset(10, 10, 10, 10);
            deckLayout.childControlHeight = true;
            deckLayout.childControlWidth = true;
            deckLayout.childForceExpandHeight = false;
            deckLayout.childForceExpandWidth = true;
            Button drawButton = CreateDeckBackButton("DrawCardButton", deckPanel.transform, "1 ПО");

            GameObject actionButtons = CreatePanel("ActionButtonsPanel", gameRoot.transform, new Vector2(0.87f, 0.02f), new Vector2(0.995f, 0.38f), new Color(0.92f, 0.88f, 0.8f, 0.98f));
            VerticalLayoutGroup actionLayout = actionButtons.AddComponent<VerticalLayoutGroup>();
            actionLayout.spacing = 8;
            actionLayout.padding = new RectOffset(8, 8, 8, 8);
            actionLayout.childControlHeight = true;
            actionLayout.childControlWidth = true;
            actionLayout.childForceExpandHeight = false;
            actionLayout.childForceExpandWidth = true;
            Button endTurnButton = CreateButton("EndTurnButton", actionButtons.transform, "Завершить ход", 22);
            Button voteButton = CreateButton("VoteButton", actionButtons.transform, "Голосование\nза исключение", 19);
            Button pauseButton = CreateButton("PauseButton", actionButtons.transform, "Меню", 22);

            GameObject voteChoicePanel = CreatePanel("VoteChoicePanel", gameRoot.transform, new Vector2(0.36f, 0.28f), new Vector2(0.64f, 0.47f), new Color(0.93f, 0.89f, 0.8f, 0.98f));
            VerticalLayoutGroup voteChoiceLayout = voteChoicePanel.AddComponent<VerticalLayoutGroup>();
            voteChoiceLayout.padding = new RectOffset(14, 14, 14, 14);
            voteChoiceLayout.spacing = 8;
            voteChoiceLayout.childControlHeight = true;
            voteChoiceLayout.childControlWidth = true;
            voteChoiceLayout.childForceExpandHeight = false;
            voteChoiceLayout.childForceExpandWidth = true;
            TMP_Text voteChoiceText = CreateText("VoteChoiceText", voteChoicePanel.transform, "Голосование", 22, TextAlignmentOptions.Center);
            GameObject voteButtonsRow = CreateRect("VoteButtonsRow", voteChoicePanel.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            voteButtonsRow.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 54);
            HorizontalLayoutGroup voteButtonsLayout = voteButtonsRow.AddComponent<HorizontalLayoutGroup>();
            voteButtonsLayout.spacing = 8;
            voteButtonsLayout.childControlHeight = true;
            voteButtonsLayout.childControlWidth = true;
            voteButtonsLayout.childForceExpandWidth = true;
            voteButtonsLayout.childForceExpandHeight = true;
            Button voteForButton = CreateButton("VoteForButton", voteButtonsRow.transform, "За", 22);
            Button voteAgainstButton = CreateButton("VoteAgainstButton", voteButtonsRow.transform, "Против", 22);
            voteChoicePanel.SetActive(false);

            GameObject targetChoicePanel = CreatePanel("TargetChoicePanel", gameRoot.transform, new Vector2(0.34f, 0.48f), new Vector2(0.76f, 0.62f), new Color(0.93f, 0.89f, 0.8f, 0.98f));
            VerticalLayoutGroup targetChoiceLayout = targetChoicePanel.AddComponent<VerticalLayoutGroup>();
            targetChoiceLayout.enabled = false;
            targetChoiceLayout.padding = new RectOffset(12, 12, 10, 10);
            targetChoiceLayout.spacing = 8;
            targetChoiceLayout.childControlHeight = true;
            targetChoiceLayout.childControlWidth = true;
            targetChoiceLayout.childForceExpandHeight = false;
            targetChoiceLayout.childForceExpandWidth = true;
            TMP_Text targetChoiceText = CreateText("TargetChoiceText", targetChoicePanel.transform, "Выберите цель", 22, TextAlignmentOptions.Center);
            targetChoiceText.enableWordWrapping = false;
            targetChoiceText.overflowMode = TextOverflowModes.Truncate;
            RectTransform targetChoiceTextRect = targetChoiceText.rectTransform;
            targetChoiceTextRect.anchorMin = new Vector2(0f, 1f);
            targetChoiceTextRect.anchorMax = new Vector2(1f, 1f);
            targetChoiceTextRect.pivot = new Vector2(0.5f, 1f);
            targetChoiceTextRect.anchoredPosition = new Vector2(0f, -10f);
            targetChoiceTextRect.sizeDelta = new Vector2(-24f, 34f);
            GameObject targetButtonsRoot = CreateRect("TargetButtonsRoot", targetChoicePanel.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            RectTransform targetButtonsRect = targetButtonsRoot.GetComponent<RectTransform>();
            targetButtonsRect.anchorMin = new Vector2(0f, 0f);
            targetButtonsRect.anchorMax = new Vector2(1f, 0f);
            targetButtonsRect.pivot = new Vector2(0.5f, 0f);
            targetButtonsRect.anchoredPosition = new Vector2(0f, 12f);
            targetButtonsRect.sizeDelta = new Vector2(-24f, 56f);
            targetButtonsRoot.AddComponent<RectMask2D>();
            HorizontalLayoutGroup targetButtonsLayout = targetButtonsRoot.AddComponent<HorizontalLayoutGroup>();
            targetButtonsLayout.spacing = 8;
            targetButtonsLayout.childControlHeight = true;
            targetButtonsLayout.childControlWidth = true;
            targetButtonsLayout.childForceExpandHeight = true;
            targetButtonsLayout.childForceExpandWidth = false;
            Button targetButtonPrefab = CreateButton("TargetButtonPrefab", targetButtonsRoot.transform, "Цель", 18);
            SetLayoutSize(targetButtonPrefab.gameObject, 120, 145, 52, 52);
            targetButtonPrefab.gameObject.SetActive(false);
            targetChoicePanel.SetActive(false);

            GameObject inGameMenuPanel = CreatePanel("InGameMenuPanel", gameRoot.transform, new Vector2(0.33f, 0.10f), new Vector2(0.67f, 0.82f), new Color(0.93f, 0.89f, 0.81f, 0.98f));
            VerticalLayoutGroup inGameMenuLayout = inGameMenuPanel.AddComponent<VerticalLayoutGroup>();
            inGameMenuLayout.padding = new RectOffset(18, 18, 18, 18);
            inGameMenuLayout.spacing = 10;
            inGameMenuLayout.childControlHeight = true;
            inGameMenuLayout.childControlWidth = true;
            inGameMenuLayout.childForceExpandHeight = false;
            inGameMenuLayout.childForceExpandWidth = true;
            CreateText("MenuTitle", inGameMenuPanel.transform, "Меню", 30, TextAlignmentOptions.Center);
            Button continueButton = CreateButton("ContinueButton", inGameMenuPanel.transform, "Продолжить", 22);
            Button settingsButton = CreateButton("SettingsButton", inGameMenuPanel.transform, "Настройки", 22);
            Button exitToMenuButton = CreateButton("ExitToMenuButton", inGameMenuPanel.transform, "Выйти", 22);

            GameObject inGameSettingsPanel = CreatePanel("InGameSettingsPanel", inGameMenuPanel.transform, Vector2.zero, Vector2.one, new Color(0.96f, 0.92f, 0.84f, 0.97f));
            LayoutElement inGameSettingsLayoutElement = GetOrAddLayoutElement(inGameSettingsPanel);
            inGameSettingsLayoutElement.minHeight = 296;
            inGameSettingsLayoutElement.preferredHeight = 296;
            VerticalLayoutGroup inGameSettingsLayout = inGameSettingsPanel.AddComponent<VerticalLayoutGroup>();
            inGameSettingsLayout.padding = new RectOffset(14, 14, 14, 14);
            inGameSettingsLayout.spacing = 8;
            inGameSettingsLayout.childControlHeight = true;
            inGameSettingsLayout.childControlWidth = true;
            inGameSettingsLayout.childForceExpandHeight = false;
            inGameSettingsLayout.childForceExpandWidth = true;
            CreateText("SettingsTitle", inGameSettingsPanel.transform, "Настройки", 24, TextAlignmentOptions.Center);
            CreateText("MusicLabel", inGameSettingsPanel.transform, "Громкость музыки", 18, TextAlignmentOptions.Left);
            Slider musicSlider = CreateSliderControl("MusicSlider", inGameSettingsPanel.transform, 0f, 1f, 0.8f);
            CreateText("EffectsLabel", inGameSettingsPanel.transform, "Громкость эффектов", 18, TextAlignmentOptions.Left);
            Slider effectsSlider = CreateSliderControl("EffectsSlider", inGameSettingsPanel.transform, 0f, 1f, 0.85f);
            Toggle fullscreenToggle = CreateToggleControl("FullscreenToggle", inGameSettingsPanel.transform, "Полноэкранный режим");
            CreateText("ResolutionLabel", inGameSettingsPanel.transform, "Разрешение", 18, TextAlignmentOptions.Left);
            TMP_Dropdown resolutionDropdown = CreateDropdownControl("ResolutionDropdown", inGameSettingsPanel.transform, new[] { "Текущее" });
            CreateText("QualityLabel", inGameSettingsPanel.transform, "Качество", 18, TextAlignmentOptions.Left);
            TMP_Dropdown qualityDropdown = CreateDropdownControl("QualityDropdown", inGameSettingsPanel.transform, new[] { "High" });
            Button applySettingsButton = CreateButton("ApplySettingsButton", inGameSettingsPanel.transform, "Применить", 20);
            SettingsPanelController inGameSettingsController = inGameSettingsPanel.AddComponent<SettingsPanelController>();
            inGameSettingsPanel.SetActive(false);
            inGameMenuPanel.SetActive(false);

            GameObject logPanel = CreatePanel("ActionLogPanel", gameRoot.transform, new Vector2(0.02f, 0.31f), new Vector2(0.25f, 0.75f), new Color(0.92f, 0.89f, 0.82f, 0.98f));
            VerticalLayoutGroup logLayout = logPanel.AddComponent<VerticalLayoutGroup>();
            logLayout.padding = new RectOffset(10, 10, 10, 10);
            logLayout.spacing = 4;
            logLayout.childControlHeight = true;
            logLayout.childControlWidth = true;
            logLayout.childForceExpandWidth = true;
            CreateText("ActionLogTitle", logPanel.transform, "История действий", 24, TextAlignmentOptions.Center);
            TMP_Text logText = CreateText("ActionLogText", logPanel.transform, "", 17, TextAlignmentOptions.TopLeft);
            logText.enableWordWrapping = true;
            logText.overflowMode = TextOverflowModes.Truncate;
            LayoutElement logElement = GetOrAddLayoutElement(logText.gameObject);
            logElement.flexibleHeight = 1;

            GameObject centerDisplayObject = PrefabUtility.InstantiatePrefab(eventDisplayPrefabObject, gameRoot.transform) as GameObject;
            CenterDisplayView centerDisplay = centerDisplayObject.GetComponent<CenterDisplayView>();
            centerDisplay.name = "CenterPlayArea";
            SetAnchors(centerDisplay.GetComponent<RectTransform>(), new Vector2(0.31f, 0.36f), new Vector2(0.78f, 0.70f), Vector2.zero, Vector2.zero);

            ResultWindowView resultWindow = CreateResultWindow(gameRoot.transform);

            GameObject managerObject = new GameObject("GameManager");
            managerObject.transform.SetParent(gameRoot.transform, false);
            GameManager gameManager = managerObject.AddComponent<GameManager>();
            UIManager uiManager = managerObject.AddComponent<UIManager>();
            GameSettingsService settingsService = managerObject.AddComponent<GameSettingsService>();

            SetPrivate(gameManager, "uiManager", uiManager);
            SetPrivate(uiManager, "gameManager", gameManager);
            SetPrivate(uiManager, "botPanels", botPanels);
            SetPrivate(uiManager, "playerPanel", playerPanel);
            SetPrivate(uiManager, "playerHandRoot", playerHandRoot.transform);
            SetPrivate(uiManager, "cardViewPrefab", cardPrefab);
            SetPrivate(uiManager, "cardArtLibrary", cardArtLibrary);
            SetPrivate(uiManager, "cardBackSprite", cardBackSprite);
            SetPrivate(uiManager, "cardFrontSprite", cardFrontSprite);
            SetPrivate(uiManager, "drawCardButton", drawButton);
            SetPrivate(uiManager, "endTurnButton", endTurnButton);
            SetPrivate(uiManager, "voteButton", voteButton);
            SetPrivate(uiManager, "pauseButton", pauseButton);
            SetPrivate(uiManager, "inGameMenuPanel", inGameMenuPanel);
            SetPrivate(uiManager, "continueButton", continueButton);
            SetPrivate(uiManager, "settingsButton", settingsButton);
            SetPrivate(uiManager, "exitToMenuButton", exitToMenuButton);
            SetPrivate(uiManager, "inGameSettingsPanel", inGameSettingsPanel);
            SetPrivate(uiManager, "inGameSettingsController", inGameSettingsController);
            SetPrivate(uiManager, "voteChoicePanel", voteChoicePanel);
            SetPrivate(uiManager, "voteChoiceText", voteChoiceText);
            SetPrivate(uiManager, "voteForButton", voteForButton);
            SetPrivate(uiManager, "voteAgainstButton", voteAgainstButton);
            SetPrivate(uiManager, "targetChoicePanel", targetChoicePanel);
            SetPrivate(uiManager, "targetChoiceText", targetChoiceText);
            SetPrivate(uiManager, "targetButtonsRoot", targetButtonsRoot.transform);
            SetPrivate(uiManager, "targetButtonPrefab", targetButtonPrefab);
            SetPrivate(uiManager, "timerText", timerText);
            SetPrivate(uiManager, "roundText", roundText);
            SetPrivate(uiManager, "neutralText", neutralText);
            SetPrivate(uiManager, "logText", logText);
            SetPrivate(uiManager, "centerDisplay", centerDisplay);
            SetPrivate(uiManager, "resultWindow", resultWindow);
            SetPrivate(inGameSettingsController, "settingsService", settingsService);
            SetPrivate(inGameSettingsController, "musicSlider", musicSlider);
            SetPrivate(inGameSettingsController, "effectsSlider", effectsSlider);
            SetPrivate(inGameSettingsController, "fullscreenToggle", fullscreenToggle);
            SetPrivate(inGameSettingsController, "resolutionDropdown", resolutionDropdown);
            SetPrivate(inGameSettingsController, "qualityDropdown", qualityDropdown);
            SetPrivate(inGameSettingsController, "applyButton", applySettingsButton);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"PvE сцена Parliament создана: {ScenePath}");
        }

        private static void EnsureFolders()
        {
            Directory.CreateDirectory(RootFolder);
            Directory.CreateDirectory(PrefabsFolder);
            Directory.CreateDirectory(FontsFolder);
            Directory.CreateDirectory(SourceFontsFolder);
            Directory.CreateDirectory("Assets/Scenes");
        }

        private static void EnsureTextMeshProResources()
        {
            Shader tmpShader = Shader.Find("TextMeshPro/Distance Field");
            bool hasSettings = AssetDatabase.FindAssets("t:TMP_Settings").Length > 0;
            if (tmpShader != null && hasSettings)
                return;

            string packagePath = FindTextMeshProPackagePath();
            if (string.IsNullOrEmpty(packagePath))
            {
                Debug.LogWarning("Не найден пакет TextMeshPro в Library/PackageCache. Открой Window > TextMeshPro > Import TMP Essential Resources вручную.");
                return;
            }

            string essentialsPath = Path.Combine(packagePath, "Package Resources/TMP Essential Resources.unitypackage");
            if (!File.Exists(essentialsPath))
            {
                Debug.LogWarning($"Не найден TMP Essential Resources: {essentialsPath}");
                return;
            }

            AssetDatabase.ImportPackage(essentialsPath, false);
            AssetDatabase.Refresh();
        }

        private static string FindTextMeshProPackagePath()
        {
            string packageCachePath = Path.GetFullPath("Library/PackageCache");
            if (!Directory.Exists(packageCachePath))
                return null;

            string[] packageDirectories = Directory.GetDirectories(packageCachePath, "com.unity.textmeshpro@*");
            return packageDirectories.Length == 0 ? null : packageDirectories[0];
        }

        private static TMP_FontAsset EnsureCyrillicFontAsset()
        {
            TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (existing != null && existing.atlasTexture != null && existing.material != null && existing.material.shader != null)
                return existing;

            if (existing != null)
                AssetDatabase.DeleteAsset(FontAssetPath);

            Font font = LoadOrCopyCyrillicSourceFont();
            if (font == null)
            {
                Debug.LogWarning("Не удалось найти системный TTF-шрифт для кириллицы. Использую TMP Settings defaultFontAsset.");
                return TMP_Settings.defaultFontAsset;
            }

            TMP_FontAsset asset = null;
            try
            {
                asset = TMP_FontAsset.CreateFontAsset(
                    font,
                    90,
                    9,
                    GlyphRenderMode.SDFAA,
                    2048,
                    2048,
                    AtlasPopulationMode.Dynamic);
            }
            catch (Exception exception)
            {
                Debug.LogError($"Не удалось создать TMP Font Asset для кириллицы: {exception.Message}");
            }

            if (asset == null)
                return TMP_Settings.defaultFontAsset;

            asset.name = "ParliamentCyrillicTMP";
            asset.atlasPopulationMode = AtlasPopulationMode.Dynamic;

            Texture2D atlasTexture = asset.atlasTexture != null ? UnityEngine.Object.Instantiate(asset.atlasTexture) : null;
            if (atlasTexture == null)
            {
                Debug.LogWarning("TMP Font Asset создан без atlas texture. Использую TMP defaultFontAsset.");
                return TMP_Settings.defaultFontAsset;
            }

            atlasTexture.name = "ParliamentCyrillicTMP Atlas";
            asset.atlasTextures = new[] { atlasTexture };

            Shader shader = Shader.Find("TextMeshPro/Distance Field");
            if (shader == null)
                shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/TextMesh Pro/Shaders/TMP_SDF.shader");

            Material material = shader == null ? new Material(Shader.Find("UI/Default")) : new Material(shader);
            material.name = "ParliamentCyrillicTMP Material";
            material.mainTexture = atlasTexture;
            material.SetTexture("_MainTex", atlasTexture);
            material.SetFloat("_TextureWidth", atlasTexture.width);
            material.SetFloat("_TextureHeight", atlasTexture.height);
            material.SetFloat("_GradientScale", 9);
            asset.material = material;

            AssetDatabase.CreateAsset(asset, FontAssetPath);
            AssetDatabase.AddObjectToAsset(atlasTexture, asset);
            AssetDatabase.AddObjectToAsset(material, asset);
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(FontAssetPath, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
        }

        private static CardArtLibrary EnsureCardArtLibraryAsset()
        {
            CardArtLibrary existing = AssetDatabase.LoadAssetAtPath<CardArtLibrary>(CardArtLibraryPath);
            if (existing != null)
                return existing;

            CardArtLibrary library = ScriptableObject.CreateInstance<CardArtLibrary>();
            AssetDatabase.CreateAsset(library, CardArtLibraryPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(CardArtLibraryPath, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<CardArtLibrary>(CardArtLibraryPath);
        }

        private static void LoadVisualSprites()
        {
            backgroundSprite = AssetDatabase.LoadAssetAtPath<Sprite>(BackgroundSpritePath);
            panelSprite = AssetDatabase.LoadAssetAtPath<Sprite>(PanelSpritePath);
            buttonSprite = AssetDatabase.LoadAssetAtPath<Sprite>(ButtonSpritePath);
            cardBackSprite = AssetDatabase.LoadAssetAtPath<Sprite>(CardBackSpritePath);
            cardFrontSprite = AssetDatabase.LoadAssetAtPath<Sprite>(CardFrontSpritePath);
        }

        private static Font LoadOrCopyCyrillicSourceFont()
        {
            string assetFontPath = SourceFontsFolder + "/arial.ttf";
            Font existingFont = AssetDatabase.LoadAssetAtPath<Font>(assetFontPath);
            if (existingFont != null)
                return existingFont;

            string[] candidates =
            {
                @"C:\Windows\Fonts\arial.ttf",
                @"C:\Windows\Fonts\segoeui.ttf",
                @"C:\Windows\Fonts\tahoma.ttf",
                @"C:\Windows\Fonts\calibri.ttf"
            };

            foreach (string sourcePath in candidates)
            {
                if (!File.Exists(sourcePath))
                    continue;

                FileUtil.CopyFileOrDirectory(sourcePath, assetFontPath);
                AssetDatabase.ImportAsset(assetFontPath, ImportAssetOptions.ForceUpdate);
                Font copiedFont = AssetDatabase.LoadAssetAtPath<Font>(assetFontPath);
                if (copiedFont != null)
                    return copiedFont;
            }

            return null;
        }

        private static CardView CreateCardViewPrefab()
        {
            GameObject root = CreatePrefabRoot("CardView", new Vector2(144, 226), new Color(0.90f, 0.88f, 0.80f, 1f));
            Image rootImage = root.GetComponent<Image>();
            if (rootImage != null)
                ApplyImageStyle(rootImage, cardFrontSprite, Color.white);

            Image cardBackImage = CreateStretchImage("CardBackImage", root.transform, Color.white);
            ApplyImageStyle(cardBackImage, cardBackSprite, Color.white);
            cardBackImage.enabled = false;
            cardBackImage.raycastTarget = false;
            cardBackImage.preserveAspect = false;
            cardBackImage.type = Image.Type.Simple;
            GetOrAddLayoutElement(cardBackImage.gameObject).ignoreLayout = true;
            Button button = root.AddComponent<Button>();
            VerticalLayoutGroup layout = root.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 8, 7);
            layout.spacing = 3;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            TMP_Text title = CreateText("TitleText", root.transform, "Название", 17, TextAlignmentOptions.Center);
            title.fontStyle = FontStyles.Bold;
            title.color = new Color(0.06f, 0.045f, 0.035f, 1f);
            GameObject artFrame = CreatePanel("ArtFrame", root.transform, Vector2.zero, Vector2.one, new Color(0.10f, 0.075f, 0.045f, 0.72f));
            LayoutElement artLayout = GetOrAddLayoutElement(artFrame);
            artLayout.minWidth = 128;
            artLayout.preferredWidth = 128;
            artLayout.minHeight = 78;
            artLayout.preferredHeight = 78;
            artLayout.flexibleWidth = 0;
            Image artImage = CreateStretchImage("ArtImage", artFrame.transform, Color.white);
            GameObject artPlaceholder = CreateRect("ArtPlaceholder", artFrame.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            TMP_Text placeholderText = CreateText("Text", artPlaceholder.transform, "Иллюстрация", 13, TextAlignmentOptions.Center);
            placeholderText.color = new Color(0.76f, 0.72f, 0.62f, 1f);
            SetStretch(placeholderText.GetComponent<RectTransform>());
            TMP_Text cost = CreateText("CostText", root.transform, "Стоимость: 1 ОД", 13, TextAlignmentOptions.Center);
            TMP_Text type = CreateText("TypeText", root.transform, "Тип", 13, TextAlignmentOptions.Center);
            TMP_Text target = CreateText("TargetText", root.transform, "", 1, TextAlignmentOptions.Center);
            TMP_Text description = CreateText("DescriptionText", root.transform, "Эффект", 13, TextAlignmentOptions.Top);
            cost.fontStyle = FontStyles.Bold;
            type.fontStyle = FontStyles.Bold;
            description.fontStyle = FontStyles.Bold;
            cost.color = new Color(0.10f, 0.085f, 0.075f, 1f);
            type.color = new Color(0.10f, 0.085f, 0.075f, 1f);
            description.color = new Color(0.08f, 0.065f, 0.055f, 1f);
            artFrame.transform.SetSiblingIndex(1);
            title.transform.SetSiblingIndex(2);
            cost.transform.SetSiblingIndex(3);
            type.transform.SetSiblingIndex(4);
            target.transform.SetSiblingIndex(5);
            description.transform.SetSiblingIndex(6);

            ConfigureCardText(title, 128, 32, true);
            ConfigureCardText(cost, 128, 18, false);
            ConfigureCardText(type, 128, 0, false);
            ConfigureCardText(target, 128, 0, false);
            ConfigureCardText(description, 128, 72, true);
            description.enableWordWrapping = true;
            description.overflowMode = TextOverflowModes.Truncate;
            LayoutElement descriptionLayout = GetOrAddLayoutElement(description.gameObject);
            descriptionLayout.flexibleHeight = 1;
            descriptionLayout.preferredHeight = 72;

            CardView view = root.AddComponent<CardView>();
            SetPrivate(view, "titleText", title);
            SetPrivate(view, "costText", cost);
            SetPrivate(view, "typeText", type);
            SetPrivate(view, "targetText", target);
            SetPrivate(view, "descriptionText", description);
            SetPrivate(view, "cardBackImage", cardBackImage);
            SetPrivate(view, "artImage", artImage);
            SetPrivate(view, "artPlaceholder", artPlaceholder);
            SetPrivate(view, "button", button);
            return SavePrefab<CardView>(root, PrefabsFolder + "/CardView.prefab");
        }

        private static ParticipantPanelView CreateParticipantPanelPrefab(string prefabName, bool includeHelp)
        {
            GameObject root = CreatePrefabRoot(prefabName, includeHelp ? new Vector2(340, 245) : new Vector2(250, 180), new Color(0.78f, 0.82f, 0.84f, 0.98f));
            VerticalLayoutGroup layout = root.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.spacing = 3;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            TMP_Text name = CreateText("NameText", root.transform, "Участник", 23, TextAlignmentOptions.Center);
            TMP_Text pp = CreateText("PoliticalPointsText", root.transform, "ПО: 5", 18, TextAlignmentOptions.Left);
            TMP_Text influence = CreateText("InfluenceText", root.transform, "Сторонники: 10%", 18, TextAlignmentOptions.Left);
            TMP_Text status = CreateText("StatusText", root.transform, "Статус: активен", 16, TextAlignmentOptions.Left);
            TMP_Text cards = CreateText("CardsText", root.transform, "Карт: 3", 16, TextAlignmentOptions.Left);
            Slider turnTimer = CreateTurnTimerSlider("TurnTimerSlider", root.transform);
            TMP_Text help = null;
            if (includeHelp)
            {
                help = CreateText("HelpText", root.transform, "ПО — ресурс для добора, голосований, нейтралов и событий\nОД — очки действия на розыгрыш карт\nСторонники — процент поддержки партии", 14, TextAlignmentOptions.Left);
                help.enableWordWrapping = true;
                help.overflowMode = TextOverflowModes.Truncate;
                GetOrAddLayoutElement(help.gameObject).preferredHeight = 50;
            }

            ParticipantPanelView view = root.AddComponent<ParticipantPanelView>();
            SetPrivate(view, "nameText", name);
            SetPrivate(view, "politicalPointsText", pp);
            SetPrivate(view, "influenceText", influence);
            SetPrivate(view, "statusText", status);
            SetPrivate(view, "cardsText", cards);
            SetPrivate(view, "helpText", help);
            SetPrivate(view, "turnTimerSlider", turnTimer);
            SetPrivate(view, "turnTimerFill", turnTimer.fillRect.GetComponent<Image>());
            return SavePrefab<ParticipantPanelView>(root, PrefabsFolder + "/" + prefabName + ".prefab");
        }

        private static CenterDisplayView CreateEventDisplayPrefab()
        {
            GameObject root = CreatePrefabRoot("EventDisplayPanel", new Vector2(620, 230), new Color(0f, 0f, 0f, 0f));
            CanvasGroup group = root.AddComponent<CanvasGroup>();
            GameObject cardSlot = CreateRect("CardSlot", root.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            RectTransform cardSlotRect = cardSlot.GetComponent<RectTransform>();
            cardSlotRect.anchoredPosition = new Vector2(-210f, -6f);
            cardSlotRect.sizeDelta = new Vector2(144f, 226f);

            GameObject infoPanel = CreatePanel("InfoPanel", root.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Color(0.86f, 0.82f, 0.72f, 0.98f));
            infoPanel.AddComponent<RectMask2D>();
            CanvasGroup infoGroup = infoPanel.AddComponent<CanvasGroup>();
            RectTransform infoRect = infoPanel.GetComponent<RectTransform>();
            infoRect.pivot = new Vector2(0f, 0.5f);
            infoRect.anchoredPosition = new Vector2(-122f, -6f);
            infoRect.sizeDelta = new Vector2(520f, 180f);
            VerticalLayoutGroup infoVertical = infoPanel.AddComponent<VerticalLayoutGroup>();
            infoVertical.padding = new RectOffset(18, 18, 18, 18);
            infoVertical.spacing = 10;
            infoVertical.childControlHeight = true;
            infoVertical.childControlWidth = true;
            infoVertical.childForceExpandWidth = true;
            TMP_Text title = CreateText("TitleText", infoPanel.transform, "Действие", 23, TextAlignmentOptions.Center);
            title.enableWordWrapping = true;
            title.overflowMode = TextOverflowModes.Truncate;
            title.enableAutoSizing = true;
            title.fontSizeMin = 14;
            title.fontSizeMax = 23;
            SetLayoutSize(title.gameObject, 440, 480, 42, 48);
            TMP_Text body = CreateText("BodyText", infoPanel.transform, "Описание", 18, TextAlignmentOptions.Center);
            body.enableWordWrapping = true;
            body.overflowMode = TextOverflowModes.Truncate;
            body.enableAutoSizing = true;
            body.fontSizeMin = 11;
            body.fontSizeMax = 18;
            GetOrAddLayoutElement(body.gameObject).flexibleHeight = 1;

            CenterDisplayView view = root.AddComponent<CenterDisplayView>();
            SetPrivate(view, "canvasGroup", group);
            SetPrivate(view, "titleText", title);
            SetPrivate(view, "bodyText", body);
            SetPrivate(view, "cardSlot", cardSlotRect);
            SetPrivate(view, "infoPanel", infoRect);
            SetPrivate(view, "infoCanvasGroup", infoGroup);
            return SavePrefab<CenterDisplayView>(root, PrefabsFolder + "/EventDisplayPanel.prefab");
        }

        private static ResultWindowView CreateResultWindow(Transform parent)
        {
            GameObject root = CreatePanel("ResultWindow", parent, Vector2.zero, Vector2.one, new Color(0, 0, 0, 0.72f));
            CanvasGroup group = root.AddComponent<CanvasGroup>();

            GameObject window = CreatePanel("Window", root.transform, new Vector2(0.34f, 0.32f), new Vector2(0.66f, 0.68f), new Color(0.87f, 0.84f, 0.77f, 1f));
            VerticalLayoutGroup layout = window.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(18, 18, 18, 18);
            layout.spacing = 12;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            TMP_Text title = CreateText("TitleText", window.transform, "Победа", 34, TextAlignmentOptions.Center);
            TMP_Text reason = CreateText("ReasonText", window.transform, "Причина", 22, TextAlignmentOptions.Center);
            Button restart = CreateButton("RestartButton", window.transform, "Перезапустить", 24);

            ResultWindowView view = root.AddComponent<ResultWindowView>();
            SetPrivate(view, "canvasGroup", group);
            SetPrivate(view, "titleText", title);
            SetPrivate(view, "reasonText", reason);
            SetPrivate(view, "restartButton", restart);
            return view;
        }

        private static Canvas CreateCanvas()
        {
            GameObject canvasObject = new GameObject("Canvas");
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        private static TMP_Text CreateTopLabel(string name, Transform parent, string text, Vector2 min, Vector2 max)
        {
            GameObject panel = CreatePanel(name, parent, min, max, new Color(0.86f, 0.84f, 0.78f, 0.95f));
            TMP_Text label = CreateText("Text", panel.transform, text, 28, TextAlignmentOptions.Center);
            SetStretch(label.GetComponent<RectTransform>());
            return label;
        }

        private static Button CreateDeckBackButton(string name, Transform parent, string costText)
        {
            GameObject root = CreatePanel(name, parent, Vector2.zero, Vector2.one, new Color(1f, 1f, 1f, 1f));
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(132, 210);

            LayoutElement rootLayout = GetOrAddLayoutElement(root);
            rootLayout.minWidth = 132;
            rootLayout.preferredWidth = 132;
            rootLayout.minHeight = 210;
            rootLayout.preferredHeight = 210;
            rootLayout.flexibleWidth = 0;
            rootLayout.flexibleHeight = 0;

            Button button = root.AddComponent<Button>();
            Image image = root.GetComponent<Image>();
            ApplyImageStyle(image, cardBackSprite, Color.white);
            image.preserveAspect = true;

            GameObject costBar = CreatePanel("CostBar", root.transform, new Vector2(0f, -0.10f), new Vector2(1f, 0.08f), new Color(0f, 0f, 0f, 0f));
            costBar.GetComponent<Image>().enabled = false;
            costBar.GetComponent<Image>().raycastTarget = false;
            TMP_Text label = CreateText("Text", costBar.transform, costText, 22, TextAlignmentOptions.Center);
            label.color = new Color(0.86f, 0.82f, 0.72f, 1f);
            label.enableAutoSizing = true;
            label.fontSizeMin = 13;
            label.fontSizeMax = 22;
            label.raycastTarget = false;
            SetStretch(label.GetComponent<RectTransform>());

            return button;
        }

        private static Button CreateButton(string name, Transform parent, string text, int size)
        {
            GameObject root = CreatePanel(name, parent, Vector2.zero, Vector2.one, new Color(0.74f, 0.70f, 0.62f, 1f));
            root.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 64);
            LayoutElement rootLayout = GetOrAddLayoutElement(root);
            rootLayout.minHeight = 56;
            rootLayout.preferredHeight = 64;
            rootLayout.minWidth = 120;
            Button button = root.AddComponent<Button>();
            ApplyImageStyle(root.GetComponent<Image>(), buttonSprite, new Color(0.74f, 0.70f, 0.62f, 1f));
            TMP_Text label = CreateText("Text", root.transform, text, size, TextAlignmentOptions.Center);
            label.enableWordWrapping = true;
            label.overflowMode = TextOverflowModes.Truncate;
            label.enableAutoSizing = true;
            label.fontSizeMin = Mathf.Max(13, size - 7);
            label.fontSizeMax = size;
            SetStretch(label.GetComponent<RectTransform>());
            return button;
        }

        private static Slider CreateSliderControl(string name, Transform parent, float minValue, float maxValue, float value)
        {
            GameObject root = CreatePanel(name, parent, Vector2.zero, Vector2.one, new Color(0.34f, 0.30f, 0.24f, 0.95f));
            root.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 44);
            LayoutElement layout = GetOrAddLayoutElement(root);
            layout.minHeight = 40;
            layout.preferredHeight = 44;

            Slider slider = root.AddComponent<Slider>();
            slider.minValue = minValue;
            slider.maxValue = maxValue;
            slider.value = value;

            GameObject fill = CreateRect("Fill", root.transform, new Vector2(0.02f, 0.25f), new Vector2(0.75f, 0.75f), Vector2.zero, Vector2.zero);
            Image fillImage = fill.AddComponent<Image>();
            ApplyImageStyle(fillImage, buttonSprite, new Color(0.8f, 0.68f, 0.28f, 1f));

            GameObject handle = CreateRect("Handle", root.transform, new Vector2(0.72f, 0.12f), new Vector2(0.82f, 0.88f), Vector2.zero, Vector2.zero);
            Image handleImage = handle.AddComponent<Image>();
            ApplyImageStyle(handleImage, buttonSprite, new Color(0.98f, 0.94f, 0.84f, 1f));

            slider.fillRect = fill.GetComponent<RectTransform>();
            slider.handleRect = handle.GetComponent<RectTransform>();
            slider.targetGraphic = handleImage;
            slider.direction = Slider.Direction.LeftToRight;
            return slider;
        }

        private static Toggle CreateToggleControl(string name, Transform parent, string labelText)
        {
            GameObject root = CreatePanel(name, parent, Vector2.zero, Vector2.one, new Color(0.34f, 0.30f, 0.24f, 0.95f));
            root.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 46);
            LayoutElement layout = GetOrAddLayoutElement(root);
            layout.minHeight = 42;
            layout.preferredHeight = 46;

            Toggle toggle = root.AddComponent<Toggle>();
            ApplyImageStyle(root.GetComponent<Image>(), panelSprite, new Color(0.34f, 0.30f, 0.24f, 0.95f));
            Image checkmark = CreateStretchImage("Checkmark", root.transform, new Color(0.8f, 0.68f, 0.28f, 1f));
            ApplyImageStyle(checkmark, buttonSprite, new Color(0.8f, 0.68f, 0.28f, 1f));
            RectTransform checkRect = checkmark.rectTransform;
            checkRect.anchorMin = new Vector2(0.02f, 0.2f);
            checkRect.anchorMax = new Vector2(0.09f, 0.8f);
            checkRect.offsetMin = Vector2.zero;
            checkRect.offsetMax = Vector2.zero;
            toggle.graphic = checkmark;
            toggle.targetGraphic = root.GetComponent<Image>();

            TMP_Text label = CreateText("Label", root.transform, labelText, 18, TextAlignmentOptions.Left);
            RectTransform labelRect = label.rectTransform;
            labelRect.anchorMin = new Vector2(0.14f, 0f);
            labelRect.anchorMax = new Vector2(0.96f, 1f);
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            return toggle;
        }

        private static TMP_Dropdown CreateDropdownControl(string name, Transform parent, string[] options)
        {
            GameObject root = CreatePanel(name, parent, Vector2.zero, Vector2.one, new Color(0.94f, 0.90f, 0.82f, 1f));
            root.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 46);
            LayoutElement layout = GetOrAddLayoutElement(root);
            layout.minHeight = 42;
            layout.preferredHeight = 46;

            TMP_Dropdown dropdown = root.AddComponent<TMP_Dropdown>();
            ApplyImageStyle(root.GetComponent<Image>(), panelSprite, new Color(0.94f, 0.90f, 0.82f, 1f));
            dropdown.options.Clear();
            foreach (string option in options)
                dropdown.options.Add(new TMP_Dropdown.OptionData(option));

            TMP_Text label = CreateText("Label", root.transform, options.Length > 0 ? options[0] : string.Empty, 18, TextAlignmentOptions.Left);
            RectTransform labelRect = label.rectTransform;
            labelRect.anchorMin = new Vector2(0.04f, 0.1f);
            labelRect.anchorMax = new Vector2(0.92f, 0.9f);
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            dropdown.captionText = label;
            ConfigureDropdownTemplate(dropdown, root.GetComponent<RectTransform>());
            return dropdown;
        }

        private static Slider CreateTurnTimerSlider(string name, Transform parent)
        {
            GameObject root = CreateRect(name, parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            SetLayoutSize(root, 120, 220, 12, 12);
            Slider slider = root.AddComponent<Slider>();
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = 1f;
            slider.interactable = false;
            slider.transition = Selectable.Transition.None;

            Image background = root.AddComponent<Image>();
            ApplyImageStyle(background, panelSprite, new Color(0.18f, 0.18f, 0.16f, 1f));

            GameObject fillArea = CreateRect("Fill Area", root.transform, Vector2.zero, Vector2.one, new Vector2(2f, 2f), new Vector2(-2f, -2f));
            GameObject fill = CreateRect("Fill", fillArea.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Image fillImage = fill.AddComponent<Image>();
            ApplyImageStyle(fillImage, buttonSprite, new Color(0.12f, 0.72f, 0.26f, 1f));
            slider.fillRect = fill.GetComponent<RectTransform>();
            slider.targetGraphic = fillImage;
            root.SetActive(false);
            return slider;
        }

        private static void SetLayoutSize(GameObject target, float minWidth, float preferredWidth, float minHeight, float preferredHeight)
        {
            LayoutElement layout = GetOrAddLayoutElement(target);

            layout.minWidth = minWidth;
            layout.preferredWidth = preferredWidth;
            layout.minHeight = minHeight;
            layout.preferredHeight = preferredHeight;
        }

        private static void ConfigureCardText(TMP_Text text, float width, float height, bool wrap)
        {
            text.enableWordWrapping = wrap;
            text.overflowMode = TextOverflowModes.Truncate;
            text.enableAutoSizing = true;
            text.fontSizeMin = 7;
            text.fontSizeMax = text.fontSize;

            RectTransform rect = text.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(width, height);

            LayoutElement layout = GetOrAddLayoutElement(text.gameObject);
            layout.minWidth = width;
            layout.preferredWidth = width;
            layout.minHeight = height;
            layout.preferredHeight = height;
            layout.flexibleWidth = 0;
        }

        private static TMP_Text CreateText(string name, Transform parent, string text, int size, TextAlignmentOptions alignment)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rect = go.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(320, size + 14);
            TMP_Text tmp = go.AddComponent<TextMeshProUGUI>();
            if (fontAsset != null)
                tmp.font = fontAsset;
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = new Color(0.08f, 0.07f, 0.06f, 1f);
            tmp.alignment = alignment;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.raycastTarget = false;
            Shadow shadow = go.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.22f);
            shadow.effectDistance = new Vector2(1.1f, -1.1f);

            LayoutElement layoutElement = go.AddComponent<LayoutElement>();
            layoutElement.minWidth = 40;
            layoutElement.preferredWidth = 220;
            layoutElement.minHeight = size + 8;
            layoutElement.preferredHeight = size + 14;
            layoutElement.flexibleWidth = 1;
            return tmp;
        }

        private static void ConfigureDropdownTemplate(TMP_Dropdown dropdown, RectTransform root)
        {
            GameObject templateObject = new GameObject("Template");
            templateObject.transform.SetParent(root, false);
            RectTransform templateRect = templateObject.AddComponent<RectTransform>();
            Image templateImage = templateObject.AddComponent<Image>();
            ScrollRect scrollRect = templateObject.AddComponent<ScrollRect>();
            templateRect.anchorMin = new Vector2(0f, 0f);
            templateRect.anchorMax = new Vector2(1f, 0f);
            templateRect.pivot = new Vector2(0.5f, 1f);
            templateRect.anchoredPosition = new Vector2(0f, -4f);
            templateRect.sizeDelta = new Vector2(0f, 190f);
            ApplyImageStyle(templateImage, panelSprite, new Color(0.95f, 0.92f, 0.86f, 1f));
            templateObject.SetActive(false);

            GameObject viewportObject = new GameObject("Viewport");
            viewportObject.transform.SetParent(templateObject.transform, false);
            RectTransform viewportRect = viewportObject.AddComponent<RectTransform>();
            Image viewportImage = viewportObject.AddComponent<Image>();
            viewportObject.AddComponent<RectMask2D>();
            SetStretch(viewportRect);
            ApplyImageStyle(viewportImage, backgroundSprite, new Color(1f, 1f, 1f, 0.08f));

            GameObject contentObject = new GameObject("Content");
            contentObject.transform.SetParent(viewportObject.transform, false);
            RectTransform contentRect = contentObject.AddComponent<RectTransform>();
            VerticalLayoutGroup contentLayout = contentObject.AddComponent<VerticalLayoutGroup>();
            ContentSizeFitter contentFitter = contentObject.AddComponent<ContentSizeFitter>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = Vector2.zero;
            contentLayout.spacing = 2f;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = true;
            contentLayout.childForceExpandHeight = false;
            contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            GameObject itemObject = new GameObject("Item");
            itemObject.transform.SetParent(contentObject.transform, false);
            RectTransform itemRect = itemObject.AddComponent<RectTransform>();
            Toggle itemToggle = itemObject.AddComponent<Toggle>();
            Image itemImage = itemObject.AddComponent<Image>();
            LayoutElement itemLayout = itemObject.AddComponent<LayoutElement>();
            itemRect.sizeDelta = new Vector2(0f, 32f);
            itemLayout.minHeight = 32f;
            itemLayout.preferredHeight = 34f;
            ApplyImageStyle(itemImage, buttonSprite, new Color(0.9f, 0.86f, 0.78f, 1f));

            Image checkmark = CreateStretchImage("Checkmark", itemObject.transform, new Color(0.78f, 0.66f, 0.23f, 1f));
            ApplyImageStyle(checkmark, buttonSprite, new Color(0.78f, 0.66f, 0.23f, 1f));
            RectTransform checkRect = checkmark.rectTransform;
            checkRect.anchorMin = new Vector2(0.03f, 0.22f);
            checkRect.anchorMax = new Vector2(0.09f, 0.78f);
            checkRect.offsetMin = Vector2.zero;
            checkRect.offsetMax = Vector2.zero;

            TMP_Text itemLabel = CreateText("Item Label", itemObject.transform, "Option", 18, TextAlignmentOptions.Left);
            RectTransform itemLabelRect = itemLabel.rectTransform;
            itemLabelRect.anchorMin = new Vector2(0.12f, 0.1f);
            itemLabelRect.anchorMax = new Vector2(0.95f, 0.9f);
            itemLabelRect.offsetMin = Vector2.zero;
            itemLabelRect.offsetMax = Vector2.zero;

            itemToggle.graphic = checkmark;
            itemToggle.targetGraphic = itemImage;
            scrollRect.viewport = viewportRect;
            scrollRect.content = contentRect;
            scrollRect.horizontal = false;

            dropdown.template = templateRect;
            dropdown.itemText = itemLabel;
            dropdown.itemImage = itemImage;
            dropdown.targetGraphic = root.GetComponent<Image>();
            dropdown.RefreshShownValue();
        }

        private static GameObject CreatePanel(string name, Transform parent, Vector2 min, Vector2 max, Color color)
        {
            GameObject go = CreateRect(name, parent, min, max, Vector2.zero, Vector2.zero);
            Image image = go.AddComponent<Image>();
            ApplyImageStyle(image, panelSprite, color);
            return go;
        }

        private static Image CreateStretchImage(string name, Transform parent, Color color)
        {
            GameObject go = CreateRect(name, parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            Image image = go.AddComponent<Image>();
            image.color = color;
            image.preserveAspect = true;
            return image;
        }

        private static GameObject CreateRect(string name, Transform parent, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rect = go.AddComponent<RectTransform>();
            SetAnchors(rect, min, max, offsetMin, offsetMax);
            return go;
        }

        private static LayoutElement GetOrAddLayoutElement(GameObject target)
        {
            LayoutElement layout = target.GetComponent<LayoutElement>();
            return layout != null ? layout : target.AddComponent<LayoutElement>();
        }

        private static GameObject CreatePrefabRoot(string name, Vector2 size, Color color)
        {
            GameObject root = new GameObject(name);
            RectTransform rect = root.AddComponent<RectTransform>();
            rect.sizeDelta = size;
            Image image = root.AddComponent<Image>();
            ApplyImageStyle(image, panelSprite, color);
            LayoutElement layout = root.AddComponent<LayoutElement>();
            layout.minWidth = size.x;
            layout.minHeight = size.y;
            layout.preferredWidth = size.x;
            layout.preferredHeight = size.y;
            layout.flexibleWidth = 0;
            layout.flexibleHeight = 0;
            return root;
        }

        private static void SetAnchors(RectTransform rect, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        private static void SetStretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void ApplyImageStyle(Image image, Sprite sprite, Color color)
        {
            if (image == null)
                return;

            image.sprite = sprite;
            image.type = sprite == null ? Image.Type.Simple : Image.Type.Sliced;
            image.color = color;
        }

        private static T SavePrefab<T>(GameObject root, string path) where T : Component
        {
            PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            return prefab.GetComponent<T>();
        }

        private static GameObject LoadPrefabObject(string prefabName)
        {
            string path = $"{PrefabsFolder}/{prefabName}.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                throw new FileNotFoundException($"Не найден prefab: {path}");

            return prefab;
        }

        private static void DeleteAssetIfExists(string path)
        {
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) != null)
                AssetDatabase.DeleteAsset(path);
        }

        private static void SetPrivate(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
                throw new MissingFieldException(target.GetType().Name, fieldName);

            field.SetValue(target, value);
            if (target is UnityEngine.Object unityObject)
                EditorUtility.SetDirty(unityObject);
        }
    }
}

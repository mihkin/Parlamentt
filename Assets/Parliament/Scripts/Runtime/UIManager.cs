using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ParliamentGame
{
    public class UIManager : MonoBehaviour
    {
        [Header("Managers")]
        [SerializeField] private GameManager gameManager;

        [Header("Participants")]
        [SerializeField] private ParticipantPanelView[] botPanels;
        [SerializeField] private ParticipantPanelView playerPanel;

        [Header("Player hand")]
        [SerializeField] private Transform playerHandRoot;
        [SerializeField] private CardView cardViewPrefab;

        [Header("Card Art")]
        [SerializeField] private CardArtLibrary cardArtLibrary;
        [SerializeField] private Sprite cardBackSprite;
        [SerializeField] private Sprite cardFrontSprite;

        [Header("Controls")]
        [SerializeField] private Button drawCardButton;
        [SerializeField] private Button endTurnButton;
        [SerializeField] private Button voteButton;
        [SerializeField] private Button pauseButton;
        [SerializeField] private GameObject voteChoicePanel;
        [SerializeField] private TMP_Text voteChoiceText;
        [SerializeField] private Button voteForButton;
        [SerializeField] private Button voteAgainstButton;
        [SerializeField] private GameObject targetChoicePanel;
        [SerializeField] private TMP_Text targetChoiceText;
        [SerializeField] private Transform targetButtonsRoot;
        [SerializeField] private Button targetButtonPrefab;
        [SerializeField] private TMP_Text timerText;
        [SerializeField] private TMP_Text roundText;
        [SerializeField] private TMP_Text neutralText;

        [Header("Information")]
        [SerializeField] private TMP_Text logText;
        [SerializeField] private CenterDisplayView centerDisplay;
        [SerializeField] private ResultWindowView resultWindow;

        [Header("Menu")]
        [SerializeField] private GameObject inGameMenuPanel;
        [SerializeField] private Button continueButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button exitToMenuButton;
        [SerializeField] private GameObject inGameSettingsPanel;
        [SerializeField] private SettingsPanelController inGameSettingsController;
        [SerializeField] private string mainMenuSceneName = "MainMenuScene";

        private readonly List<CardView> spawnedCards = new List<CardView>();
        private string lastHandSignature = string.Empty;
        private Vector2? pendingPlayedCardStartScreenPosition;
        private CardView pendingPlayedCardView;
        private CardView previewInsertionCard;
        private int previewInsertionIndex = -1;
        private Button inGameSettingsBackButton;
        private const float CardWidth = 144f;
        private const float CardHeight = 226f;
        private const float HandSpacing = 8f;
        private const float HandMoveSeconds = 0.22f;
        private const float MinHandScale = 0.72f;
        private const float MinHandSpacing = 2f;
        private const float HandPadding = 12f;

        private void Awake()
        {
            if (gameManager == null)
                gameManager = FindObjectOfType<GameManager>();

            drawCardButton.onClick.AddListener(gameManager.BuyPlayerCard);
            endTurnButton.onClick.AddListener(gameManager.EndPlayerTurn);
            voteButton.onClick.AddListener(gameManager.StartPlayerEliminationVote);
            if (pauseButton != null)
                pauseButton.onClick.AddListener(OpenInGameMenu);

            if (continueButton != null)
                continueButton.onClick.AddListener(CloseInGameMenu);

            if (settingsButton != null)
                settingsButton.onClick.AddListener(ToggleSettingsPanel);

            if (exitToMenuButton != null)
                exitToMenuButton.onClick.AddListener(ExitToMainMenu);

            voteForButton.onClick.AddListener(() => SubmitVote(true));
            voteAgainstButton.onClick.AddListener(() => SubmitVote(false));
            voteChoicePanel.SetActive(false);
            targetChoicePanel.SetActive(false);
            if (inGameMenuPanel != null)
                inGameMenuPanel.SetActive(false);

            if (inGameSettingsPanel != null)
                inGameSettingsPanel.SetActive(false);

            resultWindow.Setup(gameManager);
            ConfigureGeneratedLayout();
            ConfigureDeckBack();
        }

        private void OnEnable()
        {
            gameManager.StateChanged += Refresh;
            gameManager.Log.Changed += RefreshLog;
            gameManager.CardPlayed += OnCardPlayed;
            gameManager.CenterMessageRequested += OnCenterMessage;
            gameManager.GameFinished += resultWindow.Show;
            gameManager.VoteChoiceRequested += OnVoteChoiceRequested;
            gameManager.CardTargetChoiceRequested += OnCardTargetChoiceRequested;
        }

        private void OnDisable()
        {
            if (gameManager == null)
                return;

            gameManager.StateChanged -= Refresh;
            gameManager.Log.Changed -= RefreshLog;
            gameManager.CardPlayed -= OnCardPlayed;
            gameManager.CenterMessageRequested -= OnCenterMessage;
            gameManager.GameFinished -= resultWindow.Show;
            gameManager.VoteChoiceRequested -= OnVoteChoiceRequested;
            gameManager.CardTargetChoiceRequested -= OnCardTargetChoiceRequested;
        }

        public bool OnPlayerCardClicked(CardView cardView, Vector2 screenPosition)
        {
            CardDefinition card = cardView == null ? null : cardView.Card;
            pendingPlayedCardStartScreenPosition = screenPosition;
            pendingPlayedCardView = cardView;
            bool accepted = gameManager.TryPlayPlayerCard(card);
            if (!accepted)
            {
                pendingPlayedCardStartScreenPosition = null;
                pendingPlayedCardView = null;
            }

            return accepted;
        }

        public bool OnPlayerCardDropped(CardView cardView, Vector2 screenPosition)
        {
            CardDefinition card = cardView == null ? null : cardView.Card;
            pendingPlayedCardStartScreenPosition = screenPosition;
            pendingPlayedCardView = cardView;
            bool accepted = gameManager.TryPlayPlayerCard(card);
            if (!accepted)
            {
                pendingPlayedCardStartScreenPosition = null;
                pendingPlayedCardView = null;
            }

            return accepted;
        }

        public bool IsPointerOverPlayArea(Vector2 screenPosition, Canvas canvas)
        {
            RectTransform playArea = centerDisplay.GetComponent<RectTransform>();
            Camera eventCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            return RectTransformUtility.RectangleContainsScreenPoint(playArea, screenPosition, eventCamera);
        }

        public void AlignCardToPlayArea(RectTransform cardTransform, Canvas canvas)
        {
            RectTransform playArea = centerDisplay.GetComponent<RectTransform>();
            Vector3 worldCenter = playArea.TransformPoint(playArea.rect.center);
            cardTransform.position = worldCenter;
            cardTransform.localScale = Vector3.one;
        }

        public void ReturnCardToHand(CardView cardView, Vector2 screenPosition, Canvas canvas)
        {
            if (cardView == null)
                return;

            if (!spawnedCards.Contains(cardView))
                spawnedCards.Add(cardView);

            int insertionIndex = GetInsertionIndex(screenPosition, canvas, true);
            gameManager.MovePlayerCardToIndex(cardView.Card, insertionIndex);
            cardView.RestoreToHandParent(playerHandRoot, GetHandIndex(cardView.Card));
            SortSpawnedCardsByHand(gameManager.State.Player.hand);
            lastHandSignature = MakeHandSignature(gameManager.State.Player);
            previewInsertionCard = null;
            previewInsertionIndex = -1;
            LayoutHand(true);
        }

        public void ReflowHand(bool animate)
        {
            LayoutHand(animate);
        }

        public void PreviewCardInsertion(CardView cardView, Vector2 screenPosition, Canvas canvas)
        {
            if (cardView == null || !spawnedCards.Contains(cardView))
                return;

            int insertionIndex = GetInsertionIndex(screenPosition, canvas, false);
            if (insertionIndex < 0)
            {
                previewInsertionCard = null;
                previewInsertionIndex = -1;
            }
            else
            {
                previewInsertionCard = cardView;
                previewInsertionIndex = insertionIndex;
            }

            LayoutHand(true);
        }

        // CardView запрашивает иллюстрацию здесь, чтобы сами карты не зависели от asset-библиотеки.
        public Sprite GetCardArt(CardDefinition card)
        {
            return cardArtLibrary == null ? null : cardArtLibrary.GetSprite(card);
        }

        public Sprite GetCardBackSprite()
        {
            return cardBackSprite;
        }

        public Sprite GetCardFrontSprite()
        {
            return cardFrontSprite;
        }

        public void OpenInGameMenu()
        {
            if (inGameMenuPanel != null)
            {
                inGameMenuPanel.SetActive(true);
                inGameMenuPanel.transform.SetAsLastSibling();
            }

            if (inGameSettingsPanel != null)
                inGameSettingsPanel.SetActive(false);
        }

        public void CloseInGameMenu()
        {
            if (inGameSettingsPanel != null)
                inGameSettingsPanel.SetActive(false);

            if (inGameMenuPanel != null)
                inGameMenuPanel.SetActive(false);
        }

        public void ToggleSettingsPanel()
        {
            if (inGameMenuPanel != null && !inGameMenuPanel.activeSelf)
                OpenInGameMenu();

            if (inGameSettingsController != null)
                inGameSettingsController.Initialize();

            if (inGameSettingsPanel != null)
            {
                bool shouldOpen = !inGameSettingsPanel.activeSelf;
                if (shouldOpen)
                {
                    EnsureInGameSettingsBackButton();
                    inGameSettingsPanel.SetActive(true);
                    inGameSettingsPanel.transform.SetAsLastSibling();
                }
                else
                    inGameSettingsPanel.SetActive(false);
            }
        }

        public void ExitToMainMenu()
        {
            FindObjectOfType<LanMatchCoordinator>()?.EndMatch();
            LeaveOnlineLobbyIfActive();

            if (!string.IsNullOrWhiteSpace(mainMenuSceneName))
                SceneManager.LoadScene(mainMenuSceneName);
        }

        private static void LeaveOnlineLobbyIfActive()
        {
            OnlineLobbyManager lobbyManager = FindObjectOfType<OnlineLobbyManager>();
            if (lobbyManager != null && lobbyManager.CurrentRoom != null)
                lobbyManager.LeaveLobby();
        }

        private void Refresh()
        {
            GameState state = gameManager.State;
            roundText.text = $"Раунд {state.currentRound}";
            bool hasActiveTurnTimer = state.phase == GamePhase.PlayerTurn || state.phase == GamePhase.BotTurn;
            timerText.text = hasActiveTurnTimer ? $"00:{Mathf.CeilToInt(gameManager.CurrentTurnTimeLeft):00}" : "--:--";
            if (neutralText != null)
            {
                ConfigureNeutralPanel();
                neutralText.text = $"Нейтралы: {state.neutralInfluence}%";
            }

            ParticipantState player = state.Player;
            if (player != null)
            {
                playerPanel.Setup(player, true);
                playerPanel.SetTurnTimer(state.CurrentParticipant == player, gameManager.CurrentTurnTimeLeft, gameManager.CurrentTurnDuration);
                playerPanel.SetActionPoints(state.CurrentParticipant == player, gameManager.CurrentActionPointsRemaining, gameManager.CurrentActionPointsMax, player.CardCount);
            }

            List<ParticipantState> bots = state.participants.Where(p => !p.isPlayer).ToList();
            for (int i = 0; i < botPanels.Length; i++)
            {
                botPanels[i].gameObject.SetActive(i < bots.Count);
                if (i < bots.Count)
                {
                    botPanels[i].Setup(bots[i], false);
                    botPanels[i].SetTurnTimer(state.CurrentParticipant == bots[i], gameManager.CurrentTurnTimeLeft, gameManager.CurrentTurnDuration);
                    botPanels[i].SetActionPoints(state.CurrentParticipant == bots[i], gameManager.CurrentActionPointsRemaining, gameManager.CurrentActionPointsMax, bots[i].CardCount);
                }
                else
                {
                    botPanels[i].SetTurnTimer(false, 0f, 1f);
                }
            }

            RefreshHand(player);
            RefreshLog();

            bool playerTurn = state.phase == GamePhase.PlayerTurn && state.result == GameResult.None && !gameManager.IsPaused;
            bool waitingForTarget = gameManager.IsWaitingForPlayerCardTarget;
            drawCardButton.interactable = playerTurn && !waitingForTarget;
            ConfigureDeckBack();
            endTurnButton.interactable = playerTurn && !waitingForTarget;
            voteButton.interactable = playerTurn && !waitingForTarget;
            voteButton.GetComponentInChildren<TMP_Text>().text = $"Голосование\n{gameManager.VoteCost} ПО";
            pauseButton.GetComponentInChildren<TMP_Text>().text = "Меню";
        }

        private void EnsureInGameSettingsBackButton()
        {
            if (inGameSettingsPanel == null)
                return;

            if (inGameSettingsBackButton != null)
                return;

            Transform existingButton = inGameSettingsPanel.transform.Find("BackButton");
            if (existingButton != null)
            {
                inGameSettingsBackButton = existingButton.GetComponent<Button>();
                if (inGameSettingsBackButton != null)
                {
                    inGameSettingsBackButton.onClick.RemoveAllListeners();
                    inGameSettingsBackButton.onClick.AddListener(CloseSettingsPanel);
                }

                return;
            }

            GameObject buttonObject = new GameObject("BackButton", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(inGameSettingsPanel.transform, false);
            RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0.06f, 0.08f);
            buttonRect.anchorMax = new Vector2(0.26f, 0.16f);
            buttonRect.offsetMin = Vector2.zero;
            buttonRect.offsetMax = Vector2.zero;

            Image buttonImage = buttonObject.GetComponent<Image>();
            buttonImage.color = new Color(0.26f, 0.19f, 0.11f, 0.96f);
            buttonImage.raycastTarget = true;

            inGameSettingsBackButton = buttonObject.GetComponent<Button>();
            inGameSettingsBackButton.onClick.AddListener(CloseSettingsPanel);

            GameObject textObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(buttonObject.transform, false);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            TMP_Text label = textObject.GetComponent<TMP_Text>();
            label.text = "Назад";
            label.fontSize = 26f;
            label.alignment = TextAlignmentOptions.Center;
            label.color = new Color(0.95f, 0.90f, 0.80f, 1f);
            label.raycastTarget = false;
        }

        private void CloseSettingsPanel()
        {
            if (inGameSettingsPanel != null)
                inGameSettingsPanel.SetActive(false);

            if (inGameMenuPanel != null)
                inGameMenuPanel.SetActive(true);
        }

        private void RefreshHand(ParticipantState player)
        {
            string handSignature = MakeHandSignature(player);
            if (handSignature == lastHandSignature)
                return;

            lastHandSignature = handSignature;
            if (player == null)
            {
                foreach (CardView cardView in spawnedCards)
                    Destroy(cardView.gameObject);

                spawnedCards.Clear();
                return;
            }

            for (int i = spawnedCards.Count - 1; i >= 0; i--)
            {
                CardView view = spawnedCards[i];
                if (view == null || !ContainsCard(player.hand, view.Card))
                {
                    spawnedCards.RemoveAt(i);
                    if (view != null && view != pendingPlayedCardView)
                        Destroy(view.gameObject);
                }
            }

            bool addedCard = false;
            HandLayoutMetrics metrics = GetHandLayoutMetrics(player.hand.Count);
            for (int i = 0; i < player.hand.Count; i++)
            {
                CardDefinition card = player.hand[i];
                if (FindSpawnedCard(card) != null)
                    continue;

                CardView view = Instantiate(cardViewPrefab, playerHandRoot);
                view.Setup(card, this);
                ConfigureHandCard(view, i, metrics);
                Vector2 deckStart = GetDeckStartPositionInHand();
                view.SnapToLocalPosition(deckStart);
                spawnedCards.Add(view);
                addedCard = true;
            }

            SortSpawnedCardsByHand(player.hand);
            LayoutHand(addedCard);
        }

        private void RefreshLog()
        {
            if (gameManager?.Log == null)
                return;

            int visibleEntries = gameManager.VisibleLogEntries;
            int skip = Mathf.Max(0, gameManager.Log.Entries.Count - visibleEntries);
            logText.text = string.Join("\n", gameManager.Log.Entries.Skip(skip));
        }

        private string MakeHandSignature(ParticipantState player)
        {
            return player == null
                ? string.Empty
                : string.Join(",", player.hand.Select(card => card == null
                    ? "null"
                    : !string.IsNullOrWhiteSpace(card.runtimeInstanceId)
                        ? card.runtimeInstanceId
                        : card.id.ToString()));
        }

        private void OnCardPlayed(CardDefinition card, ParticipantState actor, ParticipantState target)
        {
            Canvas canvas = centerDisplay.GetComponentInParent<Canvas>();
            bool playedByPlayer = actor != null && actor.isPlayer;
            Vector2? startPosition = playedByPlayer
                ? pendingPlayedCardStartScreenPosition ?? GetPlayerHandScreenPosition(canvas)
                : GetBotPanelScreenPosition(actor, canvas);
            CardView playedView = playedByPlayer && pendingPlayedCardView != null
                ? pendingPlayedCardView
                : CreateBotPlayedCardView(card, canvas);
            if (playedView != null)
                spawnedCards.Remove(playedView);

            centerDisplay.ShowCard(card, actor, target, playedView, startPosition, canvas);
            pendingPlayedCardStartScreenPosition = null;
            pendingPlayedCardView = null;
        }

        private CardView CreateBotPlayedCardView(CardDefinition card, Canvas canvas)
        {
            if (card == null || canvas == null || cardViewPrefab == null)
                return null;

            CardView view = Instantiate(cardViewPrefab, canvas.transform);
            view.Setup(card, this);
            view.PrepareForPlayAnimation();
            return view;
        }

        private Vector2? GetBotPanelScreenPosition(ParticipantState actor, Canvas canvas)
        {
            if (actor == null || actor.isPlayer || canvas == null)
                return null;

            int botIndex = Mathf.Max(0, actor.id - 1);
            if (botIndex >= botPanels.Length || botPanels[botIndex] == null)
                return null;

            RectTransform rect = botPanels[botIndex].GetComponent<RectTransform>();
            Camera eventCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            return RectTransformUtility.WorldToScreenPoint(eventCamera, rect.position);
        }

        private void OnCenterMessage(string title, string body)
        {
            float seconds = title.StartsWith("Конец раунда") ? 4f : 3.6f;
            centerDisplay.Show(title, body, seconds);
        }

        private void OnVoteChoiceRequested(ParticipantState target)
        {
            voteChoiceText.text = $"Голосование против {target.displayName}";
            voteChoicePanel.SetActive(true);
        }

        private void SubmitVote(bool voteFor)
        {
            voteChoicePanel.SetActive(false);
            gameManager.SubmitPlayerVote(voteFor);
        }

        private void OnCardTargetChoiceRequested(CardDefinition card, IReadOnlyList<ParticipantState> targets)
        {
            HidePendingPlayedCardDuringTargetChoice();
            ConfigureTargetChoiceLayout();
            targetChoiceText.text = $"Цель для карты: {card.name}";
            ClearTargetButtons();

            foreach (ParticipantState target in targets)
            {
                Button button = Instantiate(targetButtonPrefab, targetButtonsRoot);
                button.gameObject.SetActive(true);
                button.GetComponentInChildren<TMP_Text>().text = $"{target.displayName} ({target.influence}% сторонников)";
                LayoutElement layoutElement = button.GetComponent<LayoutElement>();
                if (layoutElement != null)
                {
                    layoutElement.minWidth = 120;
                    layoutElement.preferredWidth = 145;
                    layoutElement.minHeight = 52;
                    layoutElement.preferredHeight = 52;
                }

                int targetId = target.id;
                button.onClick.AddListener(() => SubmitTarget(targetId));
            }

            targetChoicePanel.SetActive(true);
            LayoutRebuilder.ForceRebuildLayoutImmediate(targetButtonsRoot as RectTransform);
            LayoutRebuilder.ForceRebuildLayoutImmediate(targetChoicePanel.transform as RectTransform);
        }

        private void HidePendingPlayedCardDuringTargetChoice()
        {
            if (pendingPlayedCardView == null)
                return;

            spawnedCards.Remove(pendingPlayedCardView);
            Destroy(pendingPlayedCardView.gameObject);
            pendingPlayedCardView = null;
            pendingPlayedCardStartScreenPosition = null;
            previewInsertionCard = null;
            previewInsertionIndex = -1;
            LayoutHand(true);
        }

        private void ConfigureTargetChoiceLayout()
        {
            RectTransform panelRect = targetChoicePanel.transform as RectTransform;
            if (panelRect != null)
            {
                panelRect.anchorMin = new Vector2(0.34f, 0.48f);
                panelRect.anchorMax = new Vector2(0.76f, 0.62f);
                panelRect.offsetMin = Vector2.zero;
                panelRect.offsetMax = Vector2.zero;
            }

            VerticalLayoutGroup panelVertical = targetChoicePanel.GetComponent<VerticalLayoutGroup>();
            if (panelVertical != null)
                panelVertical.enabled = false;

            RectTransform titleRect = targetChoiceText.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -10f);
            titleRect.sizeDelta = new Vector2(-24f, 34f);
            targetChoiceText.enableWordWrapping = false;
            targetChoiceText.overflowMode = TextOverflowModes.Truncate;

            RectTransform buttonsRect = targetButtonsRoot as RectTransform;
            if (buttonsRect != null)
            {
                buttonsRect.anchorMin = new Vector2(0f, 0f);
                buttonsRect.anchorMax = new Vector2(1f, 0f);
                buttonsRect.pivot = new Vector2(0.5f, 0f);
                buttonsRect.anchoredPosition = new Vector2(0f, 12f);
                buttonsRect.sizeDelta = new Vector2(-24f, 56f);
            }

            VerticalLayoutGroup vertical = targetButtonsRoot.GetComponent<VerticalLayoutGroup>();
            if (vertical != null)
                vertical.enabled = false;

            HorizontalLayoutGroup horizontal = targetButtonsRoot.GetComponent<HorizontalLayoutGroup>();
            if (horizontal == null)
                horizontal = targetButtonsRoot.gameObject.AddComponent<HorizontalLayoutGroup>();

            horizontal.spacing = 10;
            horizontal.padding = new RectOffset(0, 0, 0, 0);
            horizontal.childControlWidth = true;
            horizontal.childControlHeight = true;
            horizontal.childForceExpandWidth = false;
            horizontal.childForceExpandHeight = true;

            LayoutElement rootLayout = targetButtonsRoot.GetComponent<LayoutElement>();
            if (rootLayout != null)
            {
                rootLayout.minHeight = 58;
                rootLayout.preferredHeight = 58;
            }
        }

        private void ConfigureNeutralPanel()
        {
            RectTransform panelRect = neutralText.transform.parent as RectTransform;
            if (panelRect == null)
                return;

            panelRect.anchorMin = new Vector2(0.86f, 0.885f);
            panelRect.anchorMax = new Vector2(0.98f, 0.935f);
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
        }

        private void ConfigureGeneratedLayout()
        {
            RectTransform botRoot = botPanels != null && botPanels.Length > 0 && botPanels[0] != null
                ? botPanels[0].transform.parent as RectTransform
                : null;
            if (botRoot != null)
            {
                botRoot.anchorMin = new Vector2(0.255f, 0.765f);
                botRoot.anchorMax = new Vector2(0.775f, 0.97f);
                botRoot.offsetMin = Vector2.zero;
                botRoot.offsetMax = Vector2.zero;
            }

            HorizontalLayoutGroup handLayout = playerHandRoot == null ? null : playerHandRoot.GetComponent<HorizontalLayoutGroup>();
            if (handLayout != null)
                handLayout.enabled = false;

            RectTransform handRect = playerHandRoot as RectTransform;
            if (handRect != null)
            {
                handRect.anchorMin = new Vector2(0.25f, 0.02f);
                handRect.anchorMax = new Vector2(0.72f, 0.30f);
                handRect.offsetMin = Vector2.zero;
                handRect.offsetMax = Vector2.zero;
            }

            ContentSizeFitter handFitter = playerHandRoot == null ? null : playerHandRoot.GetComponent<ContentSizeFitter>();
            if (handFitter != null)
                handFitter.enabled = false;
        }

        private void ConfigureDeckBack()
        {
            if (drawCardButton == null)
                return;

            Transform deckParent = drawCardButton.transform.parent;
            if (deckParent != null)
            {
                Transform title = deckParent.Find("DeckTitle");
                if (title != null)
                    title.gameObject.SetActive(false);

                Image parentImage = deckParent.GetComponent<Image>();
                if (parentImage != null)
                    parentImage.enabled = false;
            }

            RectTransform deckRect = drawCardButton.transform as RectTransform;
            if (deckRect != null)
                deckRect.sizeDelta = new Vector2(132f, 210f);

            LayoutElement deckLayout = drawCardButton.GetComponent<LayoutElement>();
            if (deckLayout != null)
            {
                deckLayout.minWidth = 132f;
                deckLayout.preferredWidth = 132f;
                deckLayout.flexibleWidth = 0f;
                deckLayout.minHeight = 210f;
                deckLayout.preferredHeight = 210f;
                deckLayout.flexibleHeight = 0f;
            }

            Image deckImage = drawCardButton.GetComponent<Image>();
            if (deckImage != null)
            {
                deckImage.sprite = cardBackSprite;
                deckImage.preserveAspect = true;
                deckImage.type = Image.Type.Simple;
                deckImage.color = cardBackSprite == null
                    ? new Color(0.74f, 0.70f, 0.62f, 1f)
                    : Color.white;
            }

            TMP_Text costText = drawCardButton.GetComponentInChildren<TMP_Text>(true);
            if (costText != null)
            {
                costText.text = $"{gameManager.DrawCardCost} ПО";
                costText.color = new Color(0.86f, 0.82f, 0.72f, 1f);
                costText.raycastTarget = false;

                Image costBackground = costText.transform.parent == null ? null : costText.transform.parent.GetComponent<Image>();
                if (costBackground != null && costBackground.gameObject.name == "CostBar")
                    costBackground.enabled = false;
            }
        }

        private void ConfigureHandCard(CardView view, int siblingIndex, HandLayoutMetrics metrics)
        {
            RectTransform rect = view.RectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, metrics.CardWidth);
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, metrics.CardHeight);
            view.transform.SetSiblingIndex(Mathf.Max(0, siblingIndex));

            LayoutElement layout = view.GetComponent<LayoutElement>();
            if (layout != null)
            {
                layout.ignoreLayout = true;
                layout.minWidth = metrics.CardWidth;
                layout.preferredWidth = metrics.CardWidth;
                layout.flexibleWidth = 0f;
                layout.minHeight = metrics.CardHeight;
                layout.preferredHeight = metrics.CardHeight;
                layout.flexibleHeight = 0f;
            }
        }

        private void LayoutHand(bool animate)
        {
            ParticipantState player = gameManager == null ? null : gameManager.State.Player;
            if (player == null || player.hand.Count == 0)
                return;

            SortSpawnedCardsByHand(player.hand);
            List<CardView> layoutCards = spawnedCards
                .Where(view => view != null && !view.IsDragging)
                .ToList();
            int totalSlots = layoutCards.Count;
            bool hasPreviewSlot = previewInsertionCard != null && previewInsertionIndex >= 0;
            if (hasPreviewSlot)
                totalSlots++;

            HandLayoutMetrics metrics = GetHandLayoutMetrics(totalSlots);
            for (int i = 0; i < layoutCards.Count; i++)
            {
                CardView view = layoutCards[i];

                ConfigureHandCard(view, i, metrics);
                int slotIndex = hasPreviewSlot && i >= previewInsertionIndex ? i + 1 : i;
                Vector2 target = GetHandSlotPosition(slotIndex, totalSlots, metrics);
                if (animate)
                    view.AnimateToLocalPosition(target, HandMoveSeconds);
                else
                    view.SnapToLocalPosition(target);
            }
        }

        private Vector2 GetHandSlotPosition(int index, int count, HandLayoutMetrics metrics)
        {
            count = Mathf.Max(1, count);
            float totalWidth = count * metrics.CardWidth + (count - 1) * metrics.Spacing;
            float x = -totalWidth * 0.5f + metrics.CardWidth * 0.5f + index * (metrics.CardWidth + metrics.Spacing);
            return new Vector2(x, 0f);
        }

        private Vector2 GetDeckStartPositionInHand()
        {
            RectTransform handRect = playerHandRoot as RectTransform;
            RectTransform deckRect = drawCardButton == null ? null : drawCardButton.transform as RectTransform;
            if (handRect == null || deckRect == null)
                return GetHandSlotPosition(spawnedCards.Count, spawnedCards.Count + 1, GetHandLayoutMetrics(spawnedCards.Count + 1));

            Canvas canvas = handRect.GetComponentInParent<Canvas>();
            Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(eventCamera, deckRect.position);
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(handRect, screenPoint, eventCamera, out Vector2 localPoint))
                return localPoint;

            return GetHandSlotPosition(spawnedCards.Count, spawnedCards.Count + 1, GetHandLayoutMetrics(spawnedCards.Count + 1));
        }

        private CardView FindSpawnedCard(CardDefinition card)
        {
            return spawnedCards.FirstOrDefault(view => view != null && HaveSameCardIdentity(view.Card, card));
        }

        private int GetHandIndex(CardDefinition card)
        {
            ParticipantState player = gameManager == null ? null : gameManager.State.Player;
            if (player == null || card == null)
                return spawnedCards.Count;

            int index = IndexOfCard(player.hand, card);
            return index < 0 ? spawnedCards.Count : index;
        }

        private int GetInsertionIndex(Vector2 screenPosition, Canvas canvas, bool fallbackToEnd)
        {
            RectTransform handRect = playerHandRoot as RectTransform;
            if (handRect == null)
                return fallbackToEnd ? spawnedCards.Count : -1;

            Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            if (!RectTransformUtility.RectangleContainsScreenPoint(handRect, screenPosition, eventCamera))
                return fallbackToEnd ? spawnedCards.Count : -1;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(handRect, screenPosition, eventCamera, out Vector2 localPoint))
                return fallbackToEnd ? spawnedCards.Count : -1;

            int cardCountWithoutDragged = spawnedCards.Count(view => view != null && !view.IsDragging);
            int slotCount = Mathf.Clamp(cardCountWithoutDragged + 1, 1, gameManager.MaxHandSize);
            HandLayoutMetrics metrics = GetHandLayoutMetrics(slotCount);
            float totalWidth = slotCount * metrics.CardWidth + (slotCount - 1) * metrics.Spacing;
            float leftEdge = -totalWidth * 0.5f;
            float step = metrics.CardWidth + metrics.Spacing;
            int index = Mathf.RoundToInt((localPoint.x - leftEdge - metrics.CardWidth * 0.5f) / step);
            return Mathf.Clamp(index, 0, slotCount - 1);
        }

        private HandLayoutMetrics GetHandLayoutMetrics(int count)
        {
            count = Mathf.Max(1, count);
            float cardWidth = CardWidth;
            float cardHeight = CardHeight;
            float spacing = HandSpacing;
            RectTransform handRect = playerHandRoot as RectTransform;
            if (handRect == null)
                return new HandLayoutMetrics(cardWidth, cardHeight, spacing);

            float availableWidth = Mathf.Max(cardWidth, handRect.rect.width - HandPadding * 2f);
            float availableHeight = Mathf.Max(cardHeight * MinHandScale, handRect.rect.height - HandPadding * 2f);
            float widthScale = availableWidth / (count * CardWidth + (count - 1) * HandSpacing);
            float heightScale = availableHeight / CardHeight;
            float scale = Mathf.Clamp(Mathf.Min(1f, widthScale, heightScale), MinHandScale, 1f);

            cardWidth = CardWidth * scale;
            cardHeight = CardHeight * scale;
            spacing = Mathf.Max(MinHandSpacing, HandSpacing * scale);

            float scaledWidth = count * cardWidth + (count - 1) * spacing;
            if (scaledWidth > availableWidth && count > 1)
            {
                spacing = Mathf.Max(MinHandSpacing, (availableWidth - count * cardWidth) / (count - 1));
                scaledWidth = count * cardWidth + (count - 1) * spacing;
            }

            if (scaledWidth > availableWidth && count > 0)
            {
                float squeezeScale = Mathf.Clamp(availableWidth / Mathf.Max(1f, scaledWidth), MinHandScale / scale, 1f);
                cardWidth *= squeezeScale;
                cardHeight *= squeezeScale;
            }

            return new HandLayoutMetrics(cardWidth, cardHeight, spacing);
        }

        private void SortSpawnedCardsByHand(IReadOnlyList<CardDefinition> hand)
        {
            spawnedCards.Sort((left, right) => GetHandOrder(left, hand).CompareTo(GetHandOrder(right, hand)));
            for (int i = 0; i < spawnedCards.Count; i++)
                spawnedCards[i].transform.SetSiblingIndex(i);
        }

        private int GetHandOrder(CardView view, IReadOnlyList<CardDefinition> hand)
        {
            if (view == null || hand == null)
                return int.MaxValue;

            int index = IndexOfCard(hand, view.Card);
            return index < 0 ? int.MaxValue : index;
        }

        private static int IndexOfCard(IReadOnlyList<CardDefinition> hand, CardDefinition card)
        {
            if (hand == null || card == null)
                return -1;

            for (int i = 0; i < hand.Count; i++)
            {
                if (HaveSameCardIdentity(hand[i], card))
                    return i;
            }

            return -1;
        }

        private static bool ContainsCard(IReadOnlyList<CardDefinition> hand, CardDefinition card)
        {
            return IndexOfCard(hand, card) >= 0;
        }

        private static bool HaveSameCardIdentity(CardDefinition left, CardDefinition right)
        {
            if (left == null || right == null)
                return false;

            if (!string.IsNullOrWhiteSpace(left.runtimeInstanceId) && !string.IsNullOrWhiteSpace(right.runtimeInstanceId))
                return string.Equals(left.runtimeInstanceId, right.runtimeInstanceId, System.StringComparison.Ordinal);

            return left.id == right.id;
        }

        private Vector2? GetPlayerHandScreenPosition(Canvas canvas)
        {
            RectTransform handRect = playerHandRoot as RectTransform;
            if (handRect == null)
                return null;

            Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            return RectTransformUtility.WorldToScreenPoint(eventCamera, handRect.position);
        }

        private void SubmitTarget(int targetId)
        {
            targetChoicePanel.SetActive(false);
            ClearTargetButtons();
            gameManager.SubmitPlayerCardTarget(targetId);
        }

        private void ClearTargetButtons()
        {
            for (int i = targetButtonsRoot.childCount - 1; i >= 0; i--)
            {
                Transform child = targetButtonsRoot.GetChild(i);
                if (child.gameObject == targetButtonPrefab.gameObject)
                    continue;

                Destroy(child.gameObject);
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(targetButtonsRoot as RectTransform);
        }

        private readonly struct HandLayoutMetrics
        {
            public HandLayoutMetrics(float cardWidth, float cardHeight, float spacing)
            {
                CardWidth = cardWidth;
                CardHeight = cardHeight;
                Spacing = spacing;
            }

            public float CardWidth { get; }
            public float CardHeight { get; }
            public float Spacing { get; }
        }
    }
}

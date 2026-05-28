using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ParliamentGame
{
    // Главный контроллер матча: принимает команды UI, двигает ходы и сообщает UI об изменениях.
    public class GameManager : MonoBehaviour
    {
        public event Action StateChanged;
        public event Action<CardDefinition, ParticipantState, ParticipantState> CardPlayed;
        public event Action<string, string> CenterMessageRequested;
        public event Action<GameResult, string> GameFinished;
        public event Action<ParticipantState> VoteChoiceRequested;
        public event Action<CardDefinition, IReadOnlyList<ParticipantState>> CardTargetChoiceRequested;

        [SerializeField] private UIManager uiManager;
        [Header("Turn Timing")]
        [Min(5f)]
        [SerializeField] private float playerTurnSeconds = 60f;
        [Min(5f)]
        [SerializeField] private float botTurnSeconds = 60f;
        [Min(0f)]
        [SerializeField] private float botDelayMin = 2.2f;
        [Min(0f)]
        [SerializeField] private float botDelayMax = 2.8f;
        [Min(0.5f)]
        [SerializeField] private float botDelayBetweenCards = 4.6f;

        [Header("Starting Values")]
        [Min(0)]
        [SerializeField] private int startingPoliticalPoints = 5;
        [Range(0, 100)]
        [SerializeField] private int startingInfluence = 10;
        [Min(0)]
        [SerializeField] private int startingHandSize = 3;

        [Header("Balance")]
        [Min(1)]
        [SerializeField] private int maxHandSize = 5;
        [Min(0)]
        [SerializeField] private int roundPoliticalPointGain = 3;
        [Min(0)]
        [SerializeField] private int drawCardCost = 1;
        [Min(0)]
        [SerializeField] private int voteCost = 10;
        [Range(0f, 1f)]
        [SerializeField] private float roundEventChance = 0.2f;

        [Header("Bot Control")]
        [Min(0)]
        [SerializeField] private int botTargetHandSize = 3;
        [SerializeField] private bool botsSpendPoliticalPointsToDraw = true;

        [Header("Decks")]
        [SerializeField] private bool useSelectedDeckForPlayer = true;
        [Min(1)]
        [SerializeField] private int minimumPlayableDeckSize = 3;
        [SerializeField] private bool autoStartOnSceneLoad = true;

        public GameState State { get; private set; } = new GameState();
        public ActionLog Log { get; private set; } = new ActionLog(20);
        public float PlayerTurnTimeLeft { get; private set; }
        public float CurrentTurnTimeLeft { get; private set; }
        public float CurrentTurnDuration { get; private set; }
        public bool IsPaused { get; private set; }
        public bool IsWaitingForPlayerCardTarget => pendingPlayerCard != null;
        public int CurrentActionPointsRemaining => currentActionPointsRemaining;
        public int CurrentActionPointsMax => GetActionPointsForRound();
        public int MaxHandSize => Mathf.Max(1, maxHandSize);
        public int DrawCardCost => Mathf.Max(0, drawCardCost);
        public int VoteCost => Mathf.Max(0, voteCost);
        public int VisibleLogEntries => Mathf.Max(1, visibleLogEntries);

        private readonly CardResolver cardResolver = new CardResolver();
        private readonly BotAI botAI = new BotAI();
        private readonly TurnManager turnManager = new TurnManager();
        private readonly List<EventDefinition> events = new List<EventDefinition>();
        private Coroutine turnRoutine;
        private ParticipantState pendingVoteTarget;
        private CardDefinition pendingPlayerCard;
        private int currentActionPointsRemaining;
        private bool lanMode;
        private bool authoritativeLanHost;
        private string localNetworkPlayerId = string.Empty;
        private Action<LanGameCommand> lanCommandSender;
        private Action lanStateChangedCallback;
        private int lanWinnerParticipantId = -1;
        private int lanPlayedCardSequence;
        private int lastAppliedLanPlayedCardSequence;
        private LanPlayedCardEvent lastPlayedCardEvent;
        [Header("UI")]
        [Min(1)]
        [SerializeField] private int visibleLogEntries = 9;

        private void Awake()
        {
            if (uiManager == null)
                uiManager = FindObjectOfType<UIManager>();
        }

        private void OnValidate()
        {
            if (botDelayMax < botDelayMin)
                botDelayMax = botDelayMin;

            startingHandSize = Mathf.Min(startingHandSize, MaxHandSize);
            botTargetHandSize = Mathf.Min(botTargetHandSize, MaxHandSize);
        }

        private void Start()
        {
            if (autoStartOnSceneLoad)
                StartNewGame();
        }

        private void Update()
        {
            if (State.result != GameResult.None || IsPaused)
                return;

            if (State.phase != GamePhase.PlayerTurn && State.phase != GamePhase.BotTurn)
                return;

            CurrentTurnTimeLeft = Mathf.Max(0f, CurrentTurnTimeLeft - Time.deltaTime);
            if (State.phase == GamePhase.PlayerTurn)
                PlayerTurnTimeLeft = CurrentTurnTimeLeft;

            if (State.phase == GamePhase.PlayerTurn && PlayerTurnTimeLeft <= 0f)
            {
                if (pendingPlayerCard != null)
                {
                    PlayerTurnTimeLeft = 0f;
                    StateChanged?.Invoke();
                    return;
                }

                ParticipantState expiredParticipant = State.CurrentParticipant;
                Log.Add(lanMode && expiredParticipant != null
                    ? $"Время {expiredParticipant.displayName} вышло. Ход завершен автоматически."
                    : "Время игрока вышло. Ход завершен автоматически.");

                if (lanMode && authoritativeLanHost)
                {
                    if (expiredParticipant != null)
                        TryEndTurnForParticipant(expiredParticipant, out _);
                }
                else
                {
                    EndPlayerTurn();
                }
            }

            StateChanged?.Invoke();
        }

        // Создает новую партию, загружает карты/события из JSON и раздает стартовые руки.
        public void StartNewGame()
        {
            StopActiveTurnRoutine();
            IsPaused = false;
            lanMode = false;
            authoritativeLanHost = false;
            localNetworkPlayerId = string.Empty;
            lanCommandSender = null;
            lanWinnerParticipantId = -1;
            lanPlayedCardSequence = 0;
            lastAppliedLanPlayedCardSequence = 0;
            lastPlayedCardEvent = null;
            Log.Clear();
            State = new GameState();
            cardResolver.HandSizeLimit = MaxHandSize;
            List<CardDefinition> allCards = JsonDatabase.LoadCards();
            State.deck = allCards;
            events.Clear();
            events.AddRange(JsonDatabase.LoadEvents());

            State.participants.Add(CreateParticipant(0, "Игрок", true));
            State.participants.Add(CreateParticipant(1, "Бот 1", false));
            State.participants.Add(CreateParticipant(2, "Бот 2", false));
            State.participants.Add(CreateParticipant(3, "Бот 3", false));
            AssignDecks(allCards);

            foreach (ParticipantState participant in State.participants)
                DrawStartingHand(participant);

            State.currentRound = 1;
            State.currentParticipantIndex = 0;
            State.phase = GamePhase.PlayerTurn;
            StartTurnTimer(Mathf.Max(5f, playerTurnSeconds));
            ResetActionPointsForTurn();
            Log.Add($"Раунд 1. Ход игрока. Колода игрока: {State.Player?.deck.Count ?? State.deck.Count} карт.");
            NotifyStateChanged();
        }

        public void SetLanCommandSender(Action<LanGameCommand> sender)
        {
            lanCommandSender = sender;
        }

        public void SetLanStateChangedCallback(Action callback)
        {
            lanStateChangedCallback = callback;
        }

        public void ConfigureLanMatch(LobbyRoomState room, string localPlayerId, bool isAuthoritativeHost)
        {
            StopActiveTurnRoutine();
            IsPaused = false;
            lanMode = true;
            authoritativeLanHost = isAuthoritativeHost;
            localNetworkPlayerId = localPlayerId ?? string.Empty;
            lanWinnerParticipantId = -1;
            pendingVoteTarget = null;
            pendingPlayerCard = null;
            currentActionPointsRemaining = 0;
            lanPlayedCardSequence = 0;
            lastAppliedLanPlayedCardSequence = 0;
            lastPlayedCardEvent = null;
            Log.Clear();
            State = new GameState();
            cardResolver.HandSizeLimit = MaxHandSize;
            events.Clear();
            events.AddRange(JsonDatabase.LoadEvents());

            if (room == null)
            {
                NotifyStateChanged();
                return;
            }

            List<CardDefinition> allCards = JsonDatabase.LoadCards();
            State.deck = allCards;

            foreach (PlayerNetworkData networkPlayer in room.Players.OrderBy(player => player.SeatIndex))
            {
                ParticipantState participant = CreateParticipant(networkPlayer.SeatIndex, networkPlayer.Nickname, string.Equals(networkPlayer.PlayerId, localNetworkPlayerId, StringComparison.Ordinal));
                participant.networkPlayerId = networkPlayer.PlayerId;
                participant.seatIndex = networkPlayer.SeatIndex;
                participant.displayName = networkPlayer.Nickname;
                participant.deck.Clear();
                participant.deck.AddRange(ResolveNetworkDeck(networkPlayer, allCards));
                State.participants.Add(participant);
            }

            if (authoritativeLanHost)
            {
                foreach (ParticipantState participant in State.participants)
                    DrawStartingHand(participant);

                State.currentRound = 1;
                State.currentParticipantIndex = 0;
                State.phase = GamePhase.PlayerTurn;
                State.result = GameResult.None;
                State.resultReason = string.Empty;
                StartTurnTimer(Mathf.Max(5f, playerTurnSeconds));
                ResetActionPointsForTurn();
                Log.Add($"Раунд 1. Ходит {State.CurrentParticipant?.displayName}. Доступно {currentActionPointsRemaining} ОД.");
            }

            NotifyStateChanged();
        }

        public LanGameSnapshot CaptureLanSnapshot()
        {
            return new LanGameSnapshot
            {
                state = CloneGameState(State),
                currentTurnTimeLeft = CurrentTurnTimeLeft,
                currentTurnDuration = CurrentTurnDuration,
                currentActionPointsRemaining = currentActionPointsRemaining,
                isPaused = IsPaused,
                matchFinished = State != null && State.phase == GamePhase.GameOver,
                winnerParticipantId = lanWinnerParticipantId,
                resultReason = State == null ? string.Empty : State.resultReason,
                lastPlayedCardEvent = CloneLanPlayedCardEvent(lastPlayedCardEvent)
            };
        }

        public void ApplyLanSnapshot(LanGameSnapshot snapshot, string localPlayerId)
        {
            StopActiveTurnRoutine();
            lanMode = true;
            authoritativeLanHost = false;
            localNetworkPlayerId = localPlayerId ?? string.Empty;

            if (snapshot == null)
            {
                State = new GameState();
                pendingVoteTarget = null;
                pendingPlayerCard = null;
                NotifyStateChanged();
                return;
            }

            State = CloneGameState(snapshot.state) ?? new GameState();
            foreach (ParticipantState participant in State.participants)
                participant.isPlayer = string.Equals(participant.networkPlayerId, localNetworkPlayerId, StringComparison.Ordinal);

            CurrentTurnTimeLeft = snapshot.currentTurnTimeLeft;
            CurrentTurnDuration = Mathf.Max(0.1f, snapshot.currentTurnDuration);
            PlayerTurnTimeLeft = State.phase == GamePhase.PlayerTurn ? CurrentTurnTimeLeft : 0f;
            currentActionPointsRemaining = snapshot.currentActionPointsRemaining;
            IsPaused = snapshot.isPaused;
            lanWinnerParticipantId = snapshot.winnerParticipantId;
            lastPlayedCardEvent = CloneLanPlayedCardEvent(snapshot.lastPlayedCardEvent);

            if (State.phase != GamePhase.PlayerTurn || State.CurrentParticipant != State.Player)
            {
                pendingVoteTarget = null;
                pendingPlayerCard = null;
            }
            else
            {
                ParticipantState localPlayer = State.Player;
                if (pendingPlayerCard != null && ResolveCardFromHand(localPlayer, pendingPlayerCard) == null)
                    pendingPlayerCard = null;

                if (pendingVoteTarget != null)
                {
                    ParticipantState snapshotVoteTarget = State.FindById(pendingVoteTarget.id);
                    pendingVoteTarget = snapshotVoteTarget != null && snapshotVoteTarget.IsActive ? snapshotVoteTarget : null;
                }
            }

            if (snapshot.lastPlayedCardEvent != null && snapshot.lastPlayedCardEvent.sequence > lastAppliedLanPlayedCardSequence)
            {
                RaiseLanPlayedCardEvent(snapshot.lastPlayedCardEvent);
                lastAppliedLanPlayedCardSequence = snapshot.lastPlayedCardEvent.sequence;
            }

            if (snapshot.matchFinished)
            {
                pendingVoteTarget = null;
                pendingPlayerCard = null;
                ParticipantState localParticipant = State.Player;
                bool localWon = localParticipant != null && localParticipant.id == lanWinnerParticipantId;
                State.phase = GamePhase.GameOver;
                State.result = localWon ? GameResult.Victory : GameResult.Defeat;
                State.resultReason = snapshot.resultReason ?? string.Empty;
                GameFinished?.Invoke(State.result, State.resultReason);
            }
            else
            {
                State.result = GameResult.None;
                State.resultReason = string.Empty;
            }

            NotifyStateChanged();
        }

        public bool ApplyLanCommand(LanGameCommand command, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (!lanMode || !authoritativeLanHost)
            {
                errorMessage = "LAN-хост не инициализирован.";
                return false;
            }

            if (command == null || string.IsNullOrWhiteSpace(command.playerId))
            {
                errorMessage = "Некорректная команда LAN-матча.";
                return false;
            }

            ParticipantState actor = State.participants.FirstOrDefault(participant => string.Equals(participant.networkPlayerId, command.playerId, StringComparison.Ordinal));
            if (actor == null)
            {
                errorMessage = "Игрок не найден в активном LAN-матче.";
                return false;
            }

            switch (command.commandType)
            {
                case LanGameCommandType.PlayCard:
                    return TryPlayCardForParticipant(actor, command.cardInstanceId, command.cardId, command.targetParticipantId, out errorMessage);
                case LanGameCommandType.BuyCard:
                    return TryBuyCardForParticipant(actor, out errorMessage);
                case LanGameCommandType.EndTurn:
                    return TryEndTurnForParticipant(actor, out errorMessage);
                case LanGameCommandType.Vote:
                    return TrySubmitVoteForParticipant(actor, command.targetParticipantId, command.voteFor, out errorMessage);
                default:
                    errorMessage = "Неизвестная команда LAN-матча.";
                    return false;
            }
        }

        // Пытается разыграть карту игрока. Если нужна цель, просит UI показать выбор.
        public bool TryPlayPlayerCard(CardDefinition card)
        {
            if (IsPaused)
                return false;

            ParticipantState player = State.Player;
            if (State.phase != GamePhase.PlayerTurn || player == null || card == null || State.CurrentParticipant != player)
                return false;

            card = ResolveCardFromHand(player, card);
            if (card == null)
                return false;

            if (ContainsCardIdentity(player.cardsLockedUntilNextRound, card))
            {
                Log.Add($"Карта \"{card.name}\" куплена в этом раунде. Ее можно разыграть со следующего раунда.");
                NotifyStateChanged();
                return false;
            }

            if (pendingPlayerCard != null)
            {
                Log.Add("Сначала выберите цель для уже разыгранной карты.");
                NotifyStateChanged();
                return false;
            }

            if (card.cost > currentActionPointsRemaining)
            {
                Log.Add($"Не хватает ОД для карты \"{card.name}\": нужно {card.cost}, осталось {currentActionPointsRemaining}.");
                NotifyStateChanged();
                return false;
            }

            if (NeedsParticipantTargetChoice(card))
            {
                IReadOnlyList<ParticipantState> targets = GetValidTargetsForParticipantCard(player, card);
                if (targets.Count == 0)
                {
                    Log.Add($"Нет доступной цели для карты \"{card.name}\".");
                    NotifyStateChanged();
                    return false;
                }

                pendingPlayerCard = card;
                CardTargetChoiceRequested?.Invoke(card, targets);
                Log.Add($"Выберите цель для карты \"{card.name}\".");
                NotifyStateChanged();
                return true;
            }

            if (lanMode && !authoritativeLanHost)
            {
                SendLanCommand(LanGameCommandType.PlayCard, card.id, card.runtimeInstanceId);
                return true;
            }

            return TryPlayCardForParticipant(player, card, -1, out _);
        }

        // Получает выбранную игроком цель для карты, которая ждала выбора.
        public void SubmitPlayerCardTarget(int targetParticipantId)
        {
            if (IsPaused)
                return;

            ParticipantState player = State.Player;
            if (State.phase != GamePhase.PlayerTurn || player == null || pendingPlayerCard == null || State.CurrentParticipant != player)
                return;

            CardDefinition card = ResolveCardFromHand(player, pendingPlayerCard);
            pendingPlayerCard = null;

            if (card == null)
            {
                NotifyStateChanged();
                return;
            }

            ParticipantState target = State.FindById(targetParticipantId);
            IReadOnlyList<ParticipantState> validTargets = GetValidTargetsForParticipantCard(player, card);
            if (target == null || !validTargets.Contains(target))
            {
                Log.Add("Эту цель нельзя выбрать для карты.");
                NotifyStateChanged();
                return;
            }

            if (lanMode && !authoritativeLanHost)
            {
                SendLanCommand(LanGameCommandType.PlayCard, card.id, card.runtimeInstanceId, targetParticipantId);
                NotifyStateChanged();
                return;
            }

            TryPlayCardForParticipant(player, card, targetParticipantId, out _);
        }

        // Покупает одну карту за ПО, если в руке есть место.
        public void BuyPlayerCard()
        {
            if (IsPaused)
                return;

            ParticipantState player = State.Player;
            if (State.phase != GamePhase.PlayerTurn || player == null || State.CurrentParticipant != player)
                return;

            if (player.politicalPoints < DrawCardCost)
            {
                Log.Add("Недостаточно ПО, чтобы взять карту.");
                NotifyStateChanged();
                return;
            }

            if (player.hand.Count >= MaxHandSize)
            {
                Log.Add($"Нельзя взять карту: рука заполнена ({MaxHandSize}/{MaxHandSize}).");
                NotifyStateChanged();
                return;
            }

            if (lanMode && !authoritativeLanHost)
            {
                SendLanCommand(LanGameCommandType.BuyCard);
                return;
            }

            TryBuyCardForParticipant(player, out _);
        }

        // Завершает ход игрока и передает очередь следующему участнику.
        public void EndPlayerTurn()
        {
            if (IsPaused)
                return;

            if (State.phase != GamePhase.PlayerTurn || State.CurrentParticipant != State.Player)
                return;

            if (pendingPlayerCard != null)
            {
                Log.Add("Сначала выберите цель для разыгранной карты.");
                NotifyStateChanged();
                return;
            }

            if (lanMode && !authoritativeLanHost)
            {
                SendLanCommand(LanGameCommandType.EndTurn);
                return;
            }

            if (lanMode && authoritativeLanHost)
            {
                TryEndTurnForParticipant(State.Player, out _);
                return;
            }

            AdvanceTurn();
        }

        // Запускает ручное голосование игрока против лидирующего противника.
        public void StartPlayerEliminationVote()
        {
            if (IsPaused)
                return;

            ParticipantState player = State.Player;
            if (State.phase != GamePhase.PlayerTurn || player == null || State.CurrentParticipant != player)
                return;

            if (player.politicalPoints < VoteCost)
            {
                Log.Add($"Для голосования нужно {VoteCost} ПО.");
                NotifyStateChanged();
                return;
            }

            ParticipantState target = GetVoteTargetForParticipant(player);
            if (target == null)
            {
                Log.Add("Нет цели для голосования.");
                return;
            }

            pendingVoteTarget = target;
            VoteChoiceRequested?.Invoke(target);
            Log.Add($"Выберите голос игрока по цели: {target.displayName}.");
            NotifyStateChanged();
        }

        // Получает решение игрока по голосованию и применяет результат.
        public void SubmitPlayerVote(bool voteFor)
        {
            if (IsPaused)
                return;

            ParticipantState player = State.Player;
            if (State.phase != GamePhase.PlayerTurn || player == null || pendingVoteTarget == null || State.CurrentParticipant != player)
                return;

            if (player.politicalPoints < VoteCost)
            {
                Log.Add($"Для голосования нужно {VoteCost} ПО.");
                pendingVoteTarget = null;
                NotifyStateChanged();
                return;
            }

            ParticipantState target = pendingVoteTarget;
            pendingVoteTarget = null;
            if (lanMode && !authoritativeLanHost)
            {
                SendLanCommand(LanGameCommandType.Vote, -1, null, target.id, voteFor);
                NotifyStateChanged();
                return;
            }

            TrySubmitVoteForParticipant(player, target.id, voteFor, out _);
        }

        // Переключает паузу игрового таймера.
        public void TogglePause()
        {
            SetPaused(!IsPaused);
        }

        public void SetPaused(bool paused)
        {
            if (IsPaused == paused)
                return;

            IsPaused = paused;
            Log.Add(IsPaused ? "Пауза." : "Игра продолжается.");
            NotifyStateChanged();
        }

        public void ForceFinishLanMatch(GameResult result, string reason)
        {
            StopActiveTurnRoutine();
            pendingVoteTarget = null;
            pendingPlayerCard = null;
            currentActionPointsRemaining = 0;
            lanWinnerParticipantId = -1;
            State.phase = GamePhase.GameOver;
            State.result = result;
            State.resultReason = reason ?? string.Empty;
            Log.Add(string.IsNullOrWhiteSpace(reason)
                ? "Матч завершен."
                : result == GameResult.Victory
                    ? $"Победа: {reason}"
                    : $"Поражение: {reason}");
            GameFinished?.Invoke(result, State.resultReason);
            NotifyStateChanged();
        }

        public void MovePlayerCardToIndex(CardDefinition card, int index)
        {
            ParticipantState player = State.Player;
            if (player == null || card == null)
                return;

            card = ResolveCardFromHand(player, card);
            if (card == null)
                return;

            player.hand.Remove(card);
            index = Mathf.Clamp(index, 0, player.hand.Count);
            player.hand.Insert(index, card);
        }

        private static CardDefinition ResolveCardFromHand(ParticipantState participant, CardDefinition card)
        {
            if (participant == null || card == null)
                return null;

            return participant.hand.FirstOrDefault(handCard => HaveSameCardIdentity(handCard, card));
        }

        private static CardDefinition ResolveCardFromHand(ParticipantState participant, string cardInstanceId, int fallbackCardId)
        {
            if (participant == null)
                return null;

            if (!string.IsNullOrWhiteSpace(cardInstanceId))
            {
                CardDefinition cardByInstance = participant.hand.FirstOrDefault(handCard =>
                    handCard != null &&
                    !string.IsNullOrWhiteSpace(handCard.runtimeInstanceId) &&
                    string.Equals(handCard.runtimeInstanceId, cardInstanceId, StringComparison.Ordinal));
                if (cardByInstance != null)
                    return cardByInstance;
            }

            return fallbackCardId < 0
                ? null
                : participant.hand.FirstOrDefault(handCard => handCard != null && handCard.id == fallbackCardId);
        }

        private static bool HaveSameCardIdentity(CardDefinition left, CardDefinition right)
        {
            if (left == null || right == null)
                return false;

            if (!string.IsNullOrWhiteSpace(left.runtimeInstanceId) && !string.IsNullOrWhiteSpace(right.runtimeInstanceId))
                return string.Equals(left.runtimeInstanceId, right.runtimeInstanceId, StringComparison.Ordinal);

            return left.id == right.id;
        }

        private static bool ContainsCardIdentity(IReadOnlyList<CardDefinition> cards, CardDefinition card)
        {
            if (cards == null || card == null)
                return false;

            for (int i = 0; i < cards.Count; i++)
            {
                if (HaveSameCardIdentity(cards[i], card))
                    return true;
            }

            return false;
        }

        // Двигает очередь участников, завершает раунд и запускает ход игрока или бота.
        private void AdvanceTurn()
        {
            if (State.result != GameResult.None)
                return;

            int previousRound = State.currentRound;
            int safety = 0;
            do
            {
                turnManager.MoveToNextParticipant(State);
                if (State.currentRound != previousRound && turnManager.IsRoundStart(State))
                    FinishRound();

                if (State.result != GameResult.None)
                    return;

                safety++;
            }
            while (!State.CurrentParticipant.IsActive && safety < State.participants.Count + 1);

            ParticipantState current = State.CurrentParticipant;
            if (current == null || !current.IsActive)
                return;

            if (current.skipNextTurn)
            {
                current.skipNextTurn = false;
                current.status = current.protectedFromVote ? ParticipantStatus.Protected : ParticipantStatus.Active;
                Log.Add($"{current.displayName} пропускает ход.");
                AdvanceTurn();
                return;
            }

            if (IsHumanControlledParticipant(current))
            {
                State.phase = GamePhase.PlayerTurn;
                StartTurnTimer(Mathf.Max(5f, playerTurnSeconds));
                ResetActionPointsForTurn();
                Log.Add(lanMode
                    ? $"Раунд {State.currentRound}. Ходит {current.displayName}. Доступно {currentActionPointsRemaining} ОД."
                    : $"Раунд {State.currentRound}. Ход игрока. Доступно {currentActionPointsRemaining} ОД.");
            }
            else
            {
                State.phase = GamePhase.BotTurn;
                StartTurnTimer(Mathf.Max(5f, botTurnSeconds));
                ResetActionPointsForTurn();
                StopActiveTurnRoutine();
                turnRoutine = StartCoroutine(BotTurnRoutine(current));
            }

            NotifyStateChanged();
        }

        // Корутина хода бота: добор, пауза, выбор карты, розыгрыш и передача хода.
        private IEnumerator BotTurnRoutine(ParticipantState bot)
        {
            Log.Add($"Ходит {bot.displayName}. Доступно {currentActionPointsRemaining} ОД.");
            BotDrawUpToTargetHandSize(bot);
            NotifyStateChanged();
            yield return WaitForGameSeconds(UnityEngine.Random.Range(botDelayMin, botDelayMax));

            int cardsPlayed = 0;
            while (currentActionPointsRemaining > 0)
            {
                CardDefinition card = botAI.PickCard(bot, currentActionPointsRemaining);
                if (card == null)
                    break;

                ParticipantState target = botAI.PickTarget(State, bot, card);
                PlayCard(bot, card, target);
                cardsPlayed++;
                CheckWinLose();
                NotifyStateChanged();

                if (State.result != GameResult.None)
                    yield break;

                yield return WaitForGameSeconds(botDelayBetweenCards);
            }

            if (cardsPlayed == 0)
                Log.Add($"{bot.displayName} сбрасывает ход.");

            yield return WaitForGameSeconds(2.4f);
            AdvanceTurn();
        }

        private IEnumerator WaitForGameSeconds(float seconds)
        {
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                if (!IsPaused)
                    elapsed += Time.deltaTime;

                yield return null;
            }

            while (IsPaused)
                yield return null;
        }

        // Бот тратит ПО на добор до целевого размера руки.
        private void BotDrawUpToTargetHandSize(ParticipantState bot)
        {
            if (bot == null || (bot.deck.Count == 0 && State.deck.Count == 0))
                return;

            int targetHandSize = Mathf.Min(botTargetHandSize, MaxHandSize);
            int cardsToDraw = Mathf.Max(0, targetHandSize - bot.hand.Count);
            int affordableCards = botsSpendPoliticalPointsToDraw ? Mathf.Min(cardsToDraw, bot.politicalPoints) : cardsToDraw;
            if (affordableCards <= 0)
                return;

            if (botsSpendPoliticalPointsToDraw)
                bot.politicalPoints -= affordableCards;

            List<CardDefinition> drawnCards = cardResolver.DrawCards(State, bot, affordableCards, Log);
            bot.cardsLockedUntilNextRound.AddRange(drawnCards);
            Log.Add($"{bot.displayName} добирает карты до руки: {bot.hand.Count}/{targetHandSize}.");
        }

        // Удаляет карту из руки и передает ее в CardResolver.
        private void PlayCard(ParticipantState actor, CardDefinition card, ParticipantState target)
        {
            actor.hand.Remove(card);
            currentActionPointsRemaining = Mathf.Max(0, currentActionPointsRemaining - Mathf.Max(0, card.cost));
            RegisterPlayedCardEvent(actor, card, target);

            Log.Add($"{actor.displayName} играет \"{card.name}\" за {card.cost} ОД. Осталось ОД: {currentActionPointsRemaining}.");
            CardPlayed?.Invoke(card, actor, target);
            cardResolver.ResolveCard(State, actor, card, target, Log);
        }

        private void RegisterPlayedCardEvent(ParticipantState actor, CardDefinition card, ParticipantState target)
        {
            if (actor == null || card == null)
                return;

            lanPlayedCardSequence++;
            lastPlayedCardEvent = new LanPlayedCardEvent
            {
                sequence = lanPlayedCardSequence,
                actorParticipantId = actor.id,
                cardId = card.id,
                cardInstanceId = card.runtimeInstanceId,
                targetParticipantId = target == null ? -1 : target.id
            };
        }

        private void DrawStartingHand(ParticipantState participant)
        {
            int handSize = Mathf.Max(0, startingHandSize);
            if (handSize <= 0)
                return;

            cardResolver.DrawCardsByActionCost(State, participant, 1, 1, Log);
            int remainingCards = Mathf.Max(0, handSize - 1);
            if (remainingCards > 0)
                cardResolver.DrawCards(State, participant, remainingCards, Log);
        }

        private void AssignDecks(List<CardDefinition> allCards)
        {
            List<CardDefinition> playerDeck = ResolvePlayerDeck(allCards);
            foreach (ParticipantState participant in State.participants)
            {
                participant.deck.Clear();
                participant.deck.AddRange(participant.isPlayer ? playerDeck : allCards);
            }
        }

        private List<CardDefinition> ResolveNetworkDeck(PlayerNetworkData networkPlayer, List<CardDefinition> allCards)
        {
            if (networkPlayer?.SelectedDeckCardIds == null || networkPlayer.SelectedDeckCardIds.Count == 0)
                return allCards;

            HashSet<int> selectedDeckIds = new HashSet<int>(networkPlayer.SelectedDeckCardIds);
            List<CardDefinition> selectedCards = allCards.Where(card => selectedDeckIds.Contains(card.id)).ToList();
            return selectedCards.Count >= Mathf.Max(1, minimumPlayableDeckSize) ? selectedCards : allCards;
        }

        private List<CardDefinition> ResolvePlayerDeck(List<CardDefinition> allCards)
        {
            if (!useSelectedDeckForPlayer)
                return allCards;

            if (!PlayerProfileDatabase.TryLoadSavedProfile(out PlayerProfileData profile))
                return allCards;

            HashSet<int> selectedDeckIds = new HashSet<int>(profile.selectedDeck);
            List<CardDefinition> selectedCards = allCards.Where(card => selectedDeckIds.Contains(card.id)).ToList();
            if (selectedCards.Count >= Mathf.Max(1, minimumPlayableDeckSize))
                return selectedCards;

            HashSet<int> ownedCardIds = new HashSet<int>(profile.ownedCards);
            List<CardDefinition> ownedCards = allCards.Where(card => ownedCardIds.Contains(card.id)).ToList();
            if (ownedCards.Count >= Mathf.Max(1, minimumPlayableDeckSize))
                return ownedCards;

            return allCards;
        }

        private void ResetActionPointsForTurn()
        {
            currentActionPointsRemaining = GetActionPointsForRound();
        }

        private int GetActionPointsForRound()
        {
            return Mathf.Max(1, State.currentRound);
        }

        // Завершает раунд: выдает ПО, применяет длительные эффекты и случайное событие.
        private void FinishRound()
        {
            State.currentRound++;
            Log.Add($"Раунд завершен. Начинается раунд {State.currentRound}.");
            GiveRoundPoliticalPoint();
            UnlockCardsForNewRound();
            cardResolver.TickRoundEffects(State, Log);

            if (UnityEngine.Random.value <= roundEventChance && events.Count > 0)
            {
                EventDefinition gameEvent = PickWeightedEvent();
                cardResolver.ResolveEvent(State, gameEvent, Log);
                CenterMessageRequested?.Invoke("Конец раунда: событие", $"{gameEvent.name}\n{gameEvent.description}");
            }
            else
            {
                CenterMessageRequested?.Invoke("Конец раунда", "Событие не произошло.");
            }

            CheckWinLose();
        }

        // Выбирает событие по весу chanceWeight.
        private EventDefinition PickWeightedEvent()
        {
            float total = events.Sum(e => Mathf.Max(0.1f, e.chanceWeight));
            float roll = UnityEngine.Random.Range(0f, total);
            foreach (EventDefinition gameEvent in events)
            {
                roll -= Mathf.Max(0.1f, gameEvent.chanceWeight);
                if (roll <= 0f)
                    return gameEvent;
            }

            return events[0];
        }

        // Автоматически выбирает цель для карт, которым не нужен ручной выбор.
        private ParticipantState PickParticipantTarget(ParticipantState actor, CardDefinition card)
        {
            switch (card.TargetType)
            {
                case TargetType.Self:
                    return actor;
                case TargetType.SingleEnemy:
                    return GetOpponents(actor).OrderByDescending(p => p.influence).FirstOrDefault();
                case TargetType.AnyParticipant:
                    return State.ActiveParticipants.OrderByDescending(p => p.influence).FirstOrDefault();
                default:
                    return null;
            }
        }

        // Определяет, надо ли открыть UI выбора цели.
        private bool NeedsParticipantTargetChoice(CardDefinition card)
        {
            TargetType targetType = card.TargetType;
            return targetType == TargetType.SingleEnemy || targetType == TargetType.AnyParticipant;
        }

        // Возвращает список целей, которые игрок может выбрать вручную.
        private IReadOnlyList<ParticipantState> GetValidTargetsForParticipantCard(ParticipantState actor, CardDefinition card)
        {
            switch (card.TargetType)
            {
                case TargetType.SingleEnemy:
                case TargetType.RandomEnemy:
                    return GetOpponents(actor).ToList();
                case TargetType.AnyParticipant:
                    return State.ActiveParticipants.ToList();
                default:
                    return new List<ParticipantState>();
            }
        }

        // Выдает ПО в начале нового раунда.
        private void GiveRoundPoliticalPoint()
        {
            foreach (ParticipantState participant in State.ActiveParticipants)
                participant.politicalPoints += roundPoliticalPointGain;

            Log.Add($"Все активные участники получают +{roundPoliticalPointGain} ПО за новый раунд.");
        }

        private void UnlockCardsForNewRound()
        {
            foreach (ParticipantState participant in State.participants)
                participant.cardsLockedUntilNextRound.Clear();
        }

        // Создает участника с параметрами, которые можно менять в инспекторе GameManager.
        private ParticipantState CreateParticipant(int id, string displayName, bool isPlayer)
        {
            ParticipantState participant = new ParticipantState(id, displayName, isPlayer);
            participant.politicalPoints = Mathf.Max(0, startingPoliticalPoints);
            participant.influence = Mathf.Clamp(startingInfluence, 0, 100);
            return participant;
        }

        // Проверяет условия победы и поражения после каждого значимого действия.
        private void CheckWinLose()
        {
            if (State.result != GameResult.None)
                return;

            if (lanMode)
            {
                CheckLanWinLose();
                return;
            }

            ParticipantState player = State.Player;
            if (player == null || !player.IsActive)
            {
                FinishGame(GameResult.Defeat, "Игрок был исключен.");
                return;
            }

            if (player.influence >= 51)
            {
                FinishGame(GameResult.Victory, "Игрок набрал 51% сторонников.");
                return;
            }

            ParticipantState winningBot = State.ActiveEnemies.FirstOrDefault(p => p.influence >= 51);
            if (winningBot != null)
            {
                FinishGame(GameResult.Defeat, $"{winningBot.displayName} набрал 51% сторонников.");
                return;
            }

            if (!State.ActiveEnemies.Any())
                FinishGame(GameResult.Victory, "Все противники исключены.");
        }

        // Ставит финальный результат и сообщает UI показать окно конца игры.
        private void FinishGame(GameResult result, string reason)
        {
            StopActiveTurnRoutine();
            State.phase = GamePhase.GameOver;
            State.result = result;
            State.resultReason = reason;
            Log.Add(result == GameResult.Victory ? $"Победа: {reason}" : $"Поражение: {reason}");
            GameFinished?.Invoke(result, reason);
            NotifyStateChanged();
        }

        private void StartTurnTimer(float seconds)
        {
            CurrentTurnDuration = Mathf.Max(0.1f, seconds);
            CurrentTurnTimeLeft = CurrentTurnDuration;
            PlayerTurnTimeLeft = State.phase == GamePhase.PlayerTurn ? CurrentTurnTimeLeft : 0f;
        }

        // Централизованное уведомление UI после изменения состояния.
        private void NotifyStateChanged()
        {
            State.RecalculateNeutrals();
            StateChanged?.Invoke();
        }

        private void NotifyLanStateChanged()
        {
            if (!lanMode || !authoritativeLanHost)
                return;

            lanStateChangedCallback?.Invoke();
        }

        private IEnumerable<ParticipantState> GetOpponents(ParticipantState actor)
        {
            if (actor == null)
                return Enumerable.Empty<ParticipantState>();

            return State.ActiveParticipants.Where(participant => participant.id != actor.id);
        }

        private ParticipantState GetVoteTargetForParticipant(ParticipantState actor)
        {
            return GetOpponents(actor).OrderByDescending(participant => participant.influence).FirstOrDefault();
        }

        private bool IsHumanControlledParticipant(ParticipantState participant)
        {
            if (participant == null)
                return false;

            return lanMode ? !string.IsNullOrWhiteSpace(participant.networkPlayerId) : participant.isPlayer;
        }

        private void SendLanCommand(LanGameCommandType commandType, int cardId = -1, string cardInstanceId = null, int targetParticipantId = -1, bool voteFor = false)
        {
            if (string.IsNullOrWhiteSpace(localNetworkPlayerId))
                return;

            lanCommandSender?.Invoke(new LanGameCommand
            {
                playerId = localNetworkPlayerId,
                commandType = commandType,
                cardId = cardId,
                cardInstanceId = cardInstanceId,
                targetParticipantId = targetParticipantId,
                voteFor = voteFor
            });
        }

        private bool EnsureParticipantTurn(ParticipantState actor, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (IsPaused)
            {
                errorMessage = "Игра на паузе.";
                return false;
            }

            if (State.phase != GamePhase.PlayerTurn || actor == null || State.CurrentParticipant != actor)
            {
                errorMessage = "Сейчас не ход этого игрока.";
                return false;
            }

            return true;
        }

        private bool TryPlayCardForParticipant(ParticipantState actor, string cardInstanceId, int cardId, int targetParticipantId, out string errorMessage)
        {
            CardDefinition card = ResolveCardFromHand(actor, cardInstanceId, cardId);
            return TryPlayCardForParticipant(actor, card, targetParticipantId, out errorMessage);
        }

        private bool TryPlayCardForParticipant(ParticipantState actor, CardDefinition card, int targetParticipantId, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (!EnsureParticipantTurn(actor, out errorMessage))
                return false;

            if (card == null)
            {
                errorMessage = "Карты нет в руке игрока.";
                return false;
            }

            if (ContainsCardIdentity(actor.cardsLockedUntilNextRound, card))
            {
                errorMessage = $"Карта \"{card.name}\" куплена в этом раунде. Ее можно разыграть со следующего раунда.";
                return false;
            }

            if (card.cost > currentActionPointsRemaining)
            {
                errorMessage = $"Не хватает ОД для карты \"{card.name}\".";
                return false;
            }

            ParticipantState target = ResolveTargetForParticipant(actor, card, targetParticipantId, out errorMessage);
            if (!string.IsNullOrEmpty(errorMessage))
                return false;

            CenterMessageRequested?.Invoke(card.name, "Карта разыгрывается.");
            PlayCard(actor, card, target);
            CheckWinLose();
            NotifyStateChanged();
            NotifyLanStateChanged();
            return true;
        }

        private ParticipantState ResolveTargetForParticipant(ParticipantState actor, CardDefinition card, int targetParticipantId, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (card == null)
                return null;

            IReadOnlyList<ParticipantState> validTargets = GetValidTargetsForParticipantCard(actor, card);
            if (validTargets.Count == 0)
                return PickParticipantTarget(actor, card);

            ParticipantState target = State.FindById(targetParticipantId);
            if (target == null || !validTargets.Contains(target))
            {
                errorMessage = $"Нет доступной цели для карты \"{card.name}\".";
                return null;
            }

            return target;
        }

        private bool TryBuyCardForParticipant(ParticipantState actor, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (!EnsureParticipantTurn(actor, out errorMessage))
                return false;

            if (actor.politicalPoints < DrawCardCost)
            {
                errorMessage = "Недостаточно ПО, чтобы взять карту.";
                return false;
            }

            if (actor.hand.Count >= MaxHandSize)
            {
                errorMessage = $"Нельзя взять карту: рука заполнена ({MaxHandSize}/{MaxHandSize}).";
                return false;
            }

            actor.politicalPoints -= DrawCardCost;
            List<CardDefinition> drawnCards = cardResolver.DrawCards(State, actor, 1, Log);
            actor.cardsLockedUntilNextRound.AddRange(drawnCards);
            NotifyStateChanged();
            NotifyLanStateChanged();
            return true;
        }

        private bool TryEndTurnForParticipant(ParticipantState actor, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (!EnsureParticipantTurn(actor, out errorMessage))
                return false;

            AdvanceTurn();
            NotifyLanStateChanged();
            return true;
        }

        private bool TrySubmitVoteForParticipant(ParticipantState actor, int targetParticipantId, bool voteFor, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (!EnsureParticipantTurn(actor, out errorMessage))
                return false;

            if (actor.politicalPoints < VoteCost)
            {
                errorMessage = $"Для голосования нужно {VoteCost} ПО.";
                return false;
            }

            ParticipantState target = State.FindById(targetParticipantId) ?? GetVoteTargetForParticipant(actor);
            if (target == null || target == actor || !target.IsActive)
            {
                errorMessage = "Нет цели для голосования.";
                return false;
            }

            actor.politicalPoints -= VoteCost;
            bool excluded = cardResolver.RunEliminationVote(State, actor, target, voteFor, Log);
            CenterMessageRequested?.Invoke("Голосование", excluded ? $"{target.displayName} исключен" : $"{target.displayName} остается");
            CheckWinLose();
            NotifyStateChanged();
            NotifyLanStateChanged();
            return true;
        }

        private void CheckLanWinLose()
        {
            List<ParticipantState> activeParticipants = State.ActiveParticipants.ToList();
            if (activeParticipants.Count == 0)
                return;

            ParticipantState winner = activeParticipants.OrderByDescending(participant => participant.influence).FirstOrDefault(participant => participant.influence >= 51);
            if (winner == null && activeParticipants.Count == 1)
                winner = activeParticipants[0];

            if (winner == null)
                return;

            FinishLanGame(winner, winner.influence >= 51
                ? $"{winner.displayName} набрал 51% сторонников."
                : $"{winner.displayName} остался последним активным участником.");
        }

        private void FinishLanGame(ParticipantState winner, string reason)
        {
            StopActiveTurnRoutine();
            lanWinnerParticipantId = winner == null ? -1 : winner.id;
            State.phase = GamePhase.GameOver;
            State.resultReason = reason;
            ParticipantState localParticipant = State.Player;
            bool localWon = localParticipant != null && winner != null && localParticipant.id == winner.id;
            State.result = localWon ? GameResult.Victory : GameResult.Defeat;
            Log.Add(localWon ? $"Победа: {reason}" : $"Поражение: {reason}");
            GameFinished?.Invoke(State.result, reason);
            NotifyStateChanged();
            NotifyLanStateChanged();
        }

        private static GameState CloneGameState(GameState state)
        {
            if (state == null)
                return null;

            string json = JsonUtility.ToJson(state);
            return JsonUtility.FromJson<GameState>(json);
        }

        private static LanPlayedCardEvent CloneLanPlayedCardEvent(LanPlayedCardEvent playedCardEvent)
        {
            if (playedCardEvent == null)
                return null;

            return new LanPlayedCardEvent
            {
                sequence = playedCardEvent.sequence,
                actorParticipantId = playedCardEvent.actorParticipantId,
                cardId = playedCardEvent.cardId,
                cardInstanceId = playedCardEvent.cardInstanceId,
                targetParticipantId = playedCardEvent.targetParticipantId
            };
        }

        private void RaiseLanPlayedCardEvent(LanPlayedCardEvent playedCardEvent)
        {
            if (playedCardEvent == null)
                return;

            ParticipantState actor = State.FindById(playedCardEvent.actorParticipantId);
            CardDefinition card = ResolveCardDefinitionForEvent(playedCardEvent.cardInstanceId, playedCardEvent.cardId);
            if (actor == null || card == null)
                return;

            ParticipantState target = playedCardEvent.targetParticipantId < 0
                ? null
                : State.FindById(playedCardEvent.targetParticipantId);
            CardPlayed?.Invoke(card, actor, target);
        }

        private CardDefinition ResolveCardDefinitionForEvent(string cardInstanceId, int cardId)
        {
            if (!string.IsNullOrWhiteSpace(cardInstanceId))
            {
                foreach (ParticipantState participant in State.participants)
                {
                    CardDefinition cardByInstance = participant.hand.FirstOrDefault(item =>
                        item != null &&
                        !string.IsNullOrWhiteSpace(item.runtimeInstanceId) &&
                        string.Equals(item.runtimeInstanceId, cardInstanceId, StringComparison.Ordinal));
                    if (cardByInstance != null)
                        return cardByInstance;
                }
            }

            if (cardId < 0)
                return null;

            CardDefinition card = State.deck.FirstOrDefault(item => item != null && item.id == cardId);
            if (card != null)
                return card;

            foreach (ParticipantState participant in State.participants)
            {
                card = participant.hand.FirstOrDefault(item => item != null && item.id == cardId);
                if (card != null)
                    return card;

                card = participant.deck.FirstOrDefault(item => item != null && item.id == cardId);
                if (card != null)
                    return card;
            }

            return null;
        }

        // Останавливает активную корутину хода бота перед сменой состояния.
        private void StopActiveTurnRoutine()
        {
            if (turnRoutine != null)
            {
                StopCoroutine(turnRoutine);
                turnRoutine = null;
            }
        }
    }
}

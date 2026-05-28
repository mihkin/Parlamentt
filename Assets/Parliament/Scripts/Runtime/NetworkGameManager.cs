using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ParliamentGame
{
    [Serializable]
    public sealed class NetworkPlayerMatchState
    {
        public string playerId;
        public string nickname;
        public bool disconnected;
        public int influence = 10;
        public int politicalPoints = 5;
        public List<int> handCardIds = new List<int>();
    }

    [Serializable]
    public sealed class NetworkMatchState
    {
        public string roomCode;
        public int currentTurnIndex;
        public int turnSequence;
        public float turnTimeRemaining;
        public float turnDuration;
        public bool started;
        public List<int> deckCardIds = new List<int>();
        public List<NetworkPlayerMatchState> players = new List<NetworkPlayerMatchState>();
    }

    [Serializable]
    public sealed class NetworkActionRequest
    {
        public string playerId;
        public int actionSequence;
        public int cardId;
        public int targetSeatIndex = -1;
    }

    public sealed class NetworkGameManager : MonoBehaviour
    {
        [SerializeField] private CardDatabase cardDatabase;
        [SerializeField] private float turnDurationSeconds = 60f;
        [SerializeField] private int startingHandSize = 3;

        private readonly HashSet<int> processedActionSequences = new HashSet<int>();
        private readonly System.Random random = new System.Random();

        public event Action<NetworkMatchState> MatchStateChanged;
        public event Action<NetworkActionRequest> ActionAccepted;
        public event Action<string> ActionRejected;
        public event Action<NetworkPlayerMatchState> PlayerDisconnected;

        public NetworkMatchState CurrentMatch { get; private set; }

        private void Update()
        {
            if (CurrentMatch == null || !CurrentMatch.started)
                return;

            CurrentMatch.turnTimeRemaining = Mathf.Max(0f, CurrentMatch.turnTimeRemaining - Time.unscaledDeltaTime);
            if (CurrentMatch.turnTimeRemaining <= 0f)
                ForceAdvanceTurn();

            MatchStateChanged?.Invoke(CurrentMatch);
        }

        /// <summary>
        /// Создает сетевой матч из lobby room и раздает стартовые руки игрокам.
        /// </summary>
        public void StartMatch(LobbyRoomState room)
        {
            if (room == null)
                return;

            List<CardDefinition> cards = cardDatabase != null ? cardDatabase.LoadRuntimeCards() : JsonDatabase.LoadCards();
            List<int> deck = cards.Select(card => card.id).OrderBy(_ => random.Next()).ToList();

            CurrentMatch = new NetworkMatchState
            {
                roomCode = room.RoomCode,
                currentTurnIndex = 0,
                turnSequence = 1,
                turnDuration = turnDurationSeconds,
                turnTimeRemaining = turnDurationSeconds,
                started = true,
                deckCardIds = deck,
                players = room.Players.Select(player => new NetworkPlayerMatchState
                {
                    playerId = player.PlayerId,
                    nickname = player.Nickname
                }).ToList()
            };

            processedActionSequences.Clear();
            foreach (NetworkPlayerMatchState player in CurrentMatch.players)
                DrawCards(player, startingHandSize);

            MatchStateChanged?.Invoke(CurrentMatch);
        }

        /// <summary>
        /// Пытается разыграть карту через авторитетный сетевой валидатор.
        /// </summary>
        public bool TryPlayCard(NetworkActionRequest request)
        {
            if (!TryValidateAction(request, out NetworkPlayerMatchState actor))
                return false;

            if (!actor.handCardIds.Remove(request.cardId))
            {
                ActionRejected?.Invoke("Карты нет в руке игрока.");
                return false;
            }

            actor.politicalPoints = Mathf.Max(0, actor.politicalPoints - 1);
            processedActionSequences.Add(request.actionSequence);
            ActionAccepted?.Invoke(request);
            AdvanceTurn();
            return true;
        }

        /// <summary>
        /// Принудительно завершает текущий ход и передает его следующему активному игроку.
        /// </summary>
        public void ForceAdvanceTurn()
        {
            if (CurrentMatch == null)
                return;

            AdvanceTurn();
        }

        /// <summary>
        /// Обрабатывает отключение игрока без разрушения матча для остальных участников.
        /// </summary>
        public void HandleDisconnect(string playerId)
        {
            if (CurrentMatch == null)
                return;

            NetworkPlayerMatchState player = CurrentMatch.players.FirstOrDefault(item => item.playerId == playerId);
            if (player == null)
                return;

            player.disconnected = true;
            PlayerDisconnected?.Invoke(player);

            if (CurrentMatch.players[CurrentMatch.currentTurnIndex].playerId == playerId)
                AdvanceTurn();
            else
                MatchStateChanged?.Invoke(CurrentMatch);
        }

        private bool TryValidateAction(NetworkActionRequest request, out NetworkPlayerMatchState actor)
        {
            actor = null;
            if (CurrentMatch == null || !CurrentMatch.started || request == null)
            {
                ActionRejected?.Invoke("Матч еще не инициализирован.");
                return false;
            }

            if (processedActionSequences.Contains(request.actionSequence))
            {
                ActionRejected?.Invoke("Дублирующееся действие было отклонено.");
                return false;
            }

            actor = CurrentMatch.players.FirstOrDefault(player => player.playerId == request.playerId);
            if (actor == null)
            {
                ActionRejected?.Invoke("Игрок не найден в текущем матче.");
                return false;
            }

            if (actor.disconnected)
            {
                ActionRejected?.Invoke("Игрок отключен от матча.");
                return false;
            }

            NetworkPlayerMatchState currentTurnPlayer = CurrentMatch.players[CurrentMatch.currentTurnIndex];
            if (currentTurnPlayer.playerId != request.playerId)
            {
                ActionRejected?.Invoke("Защита от двойного хода: сейчас не ваш ход.");
                return false;
            }

            return true;
        }

        private void AdvanceTurn()
        {
            if (CurrentMatch == null || CurrentMatch.players.Count == 0)
                return;

            int nextIndex = CurrentMatch.currentTurnIndex;
            for (int step = 0; step < CurrentMatch.players.Count; step++)
            {
                nextIndex = (nextIndex + 1) % CurrentMatch.players.Count;
                if (!CurrentMatch.players[nextIndex].disconnected)
                    break;
            }

            CurrentMatch.currentTurnIndex = nextIndex;
            CurrentMatch.turnSequence++;
            CurrentMatch.turnTimeRemaining = CurrentMatch.turnDuration;
            DrawCards(CurrentMatch.players[nextIndex], 1);
            MatchStateChanged?.Invoke(CurrentMatch);
        }

        private void DrawCards(NetworkPlayerMatchState player, int amount)
        {
            if (CurrentMatch == null || player == null)
                return;

            for (int i = 0; i < amount && CurrentMatch.deckCardIds.Count > 0; i++)
            {
                int cardId = CurrentMatch.deckCardIds[0];
                CurrentMatch.deckCardIds.RemoveAt(0);
                player.handCardIds.Add(cardId);
            }
        }
    }
}

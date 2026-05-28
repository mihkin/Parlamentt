using System;
using System.Collections.Generic;

namespace ParliamentGame
{
    // Хранит все изменяемые данные одного участника партии во время матча.
    [Serializable]
    public class ParticipantState
    {
        public int id;
        public string displayName;
        public bool isPlayer;
        public string networkPlayerId;
        public int seatIndex;
        public int politicalPoints;
        public int influence;
        public ParticipantStatus status;
        public List<CardDefinition> deck = new List<CardDefinition>();
        public List<CardDefinition> hand = new List<CardDefinition>();
        public List<CardDefinition> cardsLockedUntilNextRound = new List<CardDefinition>();

        // Простые временные флаги прототипа. Позже их можно заменить системой модификаторов.
        public bool protectedFromVote;
        public bool skipNextTurn;
        public float nextInfluenceGainMultiplier = 1f;
        public int poisonInfluenceRounds;
        public int poisonInfluenceValue;
        public int poisonPoliticalPointsRounds;
        public int poisonPoliticalPointsValue;

        public bool IsActive => status != ParticipantStatus.Excluded;
        public int CardCount => hand.Count;

        // Создает участника со стартовыми сторонников и ПО. Эти значения можно менять для баланса.
        public ParticipantState(int id, string displayName, bool isPlayer)
        {
            this.id = id;
            this.displayName = displayName;
            this.isPlayer = isPlayer;
            politicalPoints = 5;
            influence = 10;
            status = ParticipantStatus.Active;
        }
    }
}

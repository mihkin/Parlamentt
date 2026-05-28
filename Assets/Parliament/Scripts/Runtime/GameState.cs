using System;
using System.Collections.Generic;
using System.Linq;

namespace ParliamentGame
{
    // Общее состояние одной партии: участники, колода, фаза, раунд и результат.
    [Serializable]
    public class GameState
    {
        public List<ParticipantState> participants = new List<ParticipantState>();
        public List<CardDefinition> deck = new List<CardDefinition>();
        public int currentRound = 1;
        public int currentParticipantIndex;
        public int neutralInfluence = 60;
        public GamePhase phase = GamePhase.Setup;
        public GameResult result = GameResult.None;
        public string resultReason = string.Empty;

        public ParticipantState Player => participants.FirstOrDefault(p => p.isPlayer);
        public ParticipantState CurrentParticipant => participants.Count == 0 ? null : participants[currentParticipantIndex];
        public IEnumerable<ParticipantState> ActiveParticipants => participants.Where(p => p.IsActive);
        public IEnumerable<ParticipantState> ActiveEnemies => participants.Where(p => p.IsActive && !p.isPlayer);

        // Пересчитывает свободные нейтральные сторонников после любых изменений влияния.
        public void RecalculateNeutrals()
        {
            neutralInfluence = Math.Max(0, 100 - participants.Where(p => p.IsActive).Sum(p => p.influence));
        }

        // Ищет участника по id, который используется UI при выборе цели.
        public ParticipantState FindById(int id)
        {
            return participants.FirstOrDefault(p => p.id == id);
        }
    }
}

using System.Linq;
using UnityEngine;

namespace ParliamentGame
{
    // Простая логика бота: выбирает самую дорогую карту и подходящую цель.
    public class BotAI
    {
        // Возвращает карту, которую бот сыграет в этот ход.
        public CardDefinition PickCard(ParticipantState bot, int actionPointsRemaining)
        {
            if (bot == null || bot.hand.Count == 0)
                return null;

            return bot.hand
                .Where(card => !bot.cardsLockedUntilNextRound.Contains(card))
                .Where(card => card.cost <= actionPointsRemaining)
                .OrderByDescending(card => card.cost)
                .FirstOrDefault();
        }

        // Подбирает цель под targetType карты. Массовые эффекты цель не требуют.
        public ParticipantState PickTarget(GameState state, ParticipantState bot, CardDefinition card)
        {
            if (card == null)
                return null;

            switch (card.TargetType)
            {
                case TargetType.Self:
                    return bot;
                case TargetType.SingleEnemy:
                case TargetType.RandomEnemy:
                    return state.participants
                        .Where(p => p.IsActive && p.id != bot.id)
                        .OrderByDescending(p => p.influence)
                        .FirstOrDefault();
                case TargetType.AnyParticipant:
                    return state.ActiveParticipants
                        .OrderByDescending(p => p.influence)
                        .FirstOrDefault();
                default:
                    return null;
            }
        }
    }
}

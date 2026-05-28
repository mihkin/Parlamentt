using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ParliamentGame
{
    // Применяет игровые эффекты карт, событий, голосований и эффектов конца раунда.
    // ЗДЕСЬ ОБРАБАТЫВАЮТСЯ ЭФФЕКТЫ из EffectType.
    public class CardResolver
    {
        public const int DefaultMaxHandSize = 5;
        public int HandSizeLimit { get; set; } = DefaultMaxHandSize;

        private readonly System.Random random = new System.Random();

        // Главная точка входа для розыгрыша карты из cards.json.
        // Если добавляешь новый EffectType, добавь case в ApplyCardEffect.
        public void ResolveCard(GameState state, ParticipantState actor, CardDefinition card, ParticipantState target, ActionLog log)
        {
            if (actor == null || card == null || !actor.IsActive)
                return;

            if (card.chance < 1f && UnityEngine.Random.value > Mathf.Clamp01(card.chance))
            {
                log.Add($"Карта \"{card.name}\" не сработала.");
                state.RecalculateNeutrals();
                return;
            }

            foreach (ParticipantState currentTarget in SelectCardTargets(state, actor, card.TargetType, target))
                ApplyCardEffect(state, actor, card, currentTarget, log);

            state.RecalculateNeutrals();
        }

        // Обрабатывает один эффект по одной цели. Массовые цели раскладываются выше.
        private void ApplyCardEffect(GameState state, ParticipantState actor, CardDefinition card, ParticipantState target, ActionLog log)
        {
            switch (card.EffectType)
            {
                case EffectType.AddInfluence:
                    AddInfluence(state, actor, target ?? actor, card.value, log);
                    break;
                case EffectType.RemoveInfluence:
                    ChangeInfluence(state, target, -card.value, log, false);
                    break;
                case EffectType.StealInfluence:
                    StealInfluence(state, actor, target, card.value, log);
                    break;
                case EffectType.AddPoliticalPoints:
                    (target ?? actor).politicalPoints += card.value;
                    log.Add($"{(target ?? actor).displayName} получает {card.value} ПО.");
                    break;
                case EffectType.RemovePoliticalPoints:
                    ChangePoliticalPoints(target, -card.value, log);
                    break;
                case EffectType.DrawCard:
                    DrawCards(state, target ?? actor, Math.Max(1, card.value), log);
                    break;
                case EffectType.StartEliminationVote:
                    RunEliminationVote(state, actor, target, true, log);
                    break;
                case EffectType.ProtectFromVote:
                    ParticipantState protectedTarget = target ?? actor;
                    protectedTarget.protectedFromVote = true;
                    protectedTarget.status = ParticipantStatus.Protected;
                    log.Add($"{protectedTarget.displayName} защищается от следующего голосования.");
                    break;
                case EffectType.SkipTurn:
                    if (target != null)
                    {
                        target.skipNextTurn = true;
                        target.status = ParticipantStatus.SkipsTurn;
                        log.Add($"{target.displayName} пропустит следующий ход.");
                    }
                    break;
                case EffectType.MultiplyNextInfluenceGain:
                    ParticipantState boostedTarget = target ?? actor;
                    boostedTarget.nextInfluenceGainMultiplier = Mathf.Max(boostedTarget.nextInfluenceGainMultiplier, card.value <= 0 ? 2f : card.value);
                    log.Add($"{boostedTarget.displayName} усиливает следующий прирост сторонников.");
                    break;
                case EffectType.RandomInfluenceChange:
                    int randomDelta = random.Next(-card.value, card.value + 1);
                    ChangeInfluence(state, target ?? actor, randomDelta, log, randomDelta > 0);
                    break;
                case EffectType.PoisonInfluence:
                    AddInfluencePoison(target ?? actor, Mathf.Max(1, card.value), Mathf.Max(1, card.duration), log);
                    break;
                case EffectType.PoisonPoliticalPoints:
                    AddPoliticalPoison(target ?? actor, Mathf.Max(1, card.value), Mathf.Max(1, card.duration), log);
                    break;
            }
        }

        // Вызывается в конце раунда и применяет эффекты с duration.
        public void TickRoundEffects(GameState state, ActionLog log)
        {
            foreach (ParticipantState participant in state.ActiveParticipants.ToList())
            {
                if (participant.poisonInfluenceRounds > 0)
                {
                    int damage = Mathf.Max(1, participant.poisonInfluenceValue);
                    ChangeInfluence(state, participant, -damage, log, false);
                    participant.poisonInfluenceRounds--;
                    if (participant.poisonInfluenceRounds <= 0)
                    {
                        participant.poisonInfluenceValue = 0;
                        log.Add($"{participant.displayName}: яд сторонников иссяк.");
                    }
                }

                if (participant.poisonPoliticalPointsRounds > 0)
                {
                    int damage = Mathf.Max(1, participant.poisonPoliticalPointsValue);
                    ChangePoliticalPoints(participant, -damage, log);
                    participant.poisonPoliticalPointsRounds--;
                    if (participant.poisonPoliticalPointsRounds <= 0)
                    {
                        participant.poisonPoliticalPointsValue = 0;
                        log.Add($"{participant.displayName}: яд ПО иссяк.");
                    }
                }
            }

            state.RecalculateNeutrals();
        }

        // Применяет случайное событие из events.json.
        public void ResolveEvent(GameState state, EventDefinition gameEvent, ActionLog log)
        {
            if (gameEvent == null)
                return;

            log.Add($"Событие: {gameEvent.name}. {gameEvent.description}");

            if (gameEvent.id == "military_coup")
            {
                ParticipantState loser = PickRandomActive(state.participants);
                ParticipantState winner = PickRandomActive(state.participants.Where(p => p != loser).ToList());
                ChangeInfluence(state, loser, -10, log, false);
                ChangeInfluence(state, winner, 5, log, true);
            }
            else if (gameEvent.id == "trust_growth")
            {
                ChangeInfluence(state, state.ActiveParticipants.OrderBy(p => p.influence).FirstOrDefault(), gameEvent.value, log, true);
            }
            else if (gameEvent.id == "mass_protests")
            {
                ChangeInfluence(state, state.ActiveParticipants.OrderByDescending(p => p.influence).FirstOrDefault(), -gameEvent.value, log, false);
            }
            else
            {
                foreach (ParticipantState participant in SelectTargets(state, gameEvent.TargetType))
                {
                    if (gameEvent.EffectType == EffectType.RemovePoliticalPoints)
                        ChangePoliticalPoints(participant, -gameEvent.value, log);
                    else
                    {
                        int delta = gameEvent.EffectType == EffectType.RemoveInfluence ? -gameEvent.value : gameEvent.value;
                        ChangeInfluence(state, participant, delta, log, delta > 0);
                    }
                }
            }

            state.RecalculateNeutrals();
        }

        // Запускает голосование за исключение. Возвращает true, если цель исключена.
        public bool RunEliminationVote(GameState state, ParticipantState initiator, ParticipantState target, bool playerVotesFor, ActionLog log)
        {
            if (target == null || !target.IsActive)
            {
                log.Add("Голосование не началось: цель не выбрана.");
                return false;
            }

            if (target.protectedFromVote)
            {
                target.protectedFromVote = false;
                target.status = ParticipantStatus.Active;
                log.Add($"{target.displayName} защищен от голосования.");
                return false;
            }

            float votesFor = 0f;
            float votesAgainst = 0f;

            foreach (ParticipantState voter in state.ActiveParticipants)
            {
                bool voteFor;
                if (ReferenceEquals(voter, initiator))
                {
                    voteFor = playerVotesFor;
                }
                else
                {
                    // Бот чаще голосует против лидера и чаще защищает слабых.
                    bool targetIsLeader = target.influence >= state.ActiveParticipants.Max(p => p.influence);
                    bool targetIsWeak = target.influence <= state.ActiveParticipants.Min(p => p.influence);
                    float chanceFor = targetIsLeader ? 0.8f : targetIsWeak ? 0.25f : 0.5f;
                    voteFor = UnityEngine.Random.value < chanceFor;
                }

                float loyalShare = ReferenceEquals(voter, initiator) ? 1f : UnityEngine.Random.Range(0.7f, 0.9f);
                float loyalVotes = voter.influence * loyalShare;
                float rebelVotes = voter.influence - loyalVotes;

                if (voteFor)
                {
                    votesFor += loyalVotes;
                    votesAgainst += rebelVotes;
                }
                else
                {
                    votesAgainst += loyalVotes;
                    votesFor += rebelVotes;
                }
            }

            float neutralFor = UnityEngine.Random.Range(0f, state.neutralInfluence);
            votesFor += neutralFor;
            votesAgainst += state.neutralInfluence - neutralFor;

            log.Add($"Голосование против {target.displayName}: за {votesFor:0.0}%, против {votesAgainst:0.0}%.");

            if (votesFor <= votesAgainst)
            {
                log.Add($"{target.displayName} остается в игре.");
                return false;
            }

            int releasedInfluence = target.influence;
            target.influence = 0;
            target.status = ParticipantStatus.Excluded;
            target.poisonInfluenceRounds = 0;
            target.poisonInfluenceValue = 0;
            target.poisonPoliticalPointsRounds = 0;
            target.poisonPoliticalPointsValue = 0;
            target.hand.Clear();
            state.RecalculateNeutrals();
            log.Add($"{target.displayName} исключен. Его {releasedInfluence}% сторонников стали нейтралами.");
            return true;
        }

        // Добирает карты до лимита руки.
        public List<CardDefinition> DrawCards(GameState state, ParticipantState participant, int count, ActionLog log)
        {
            List<CardDefinition> drawnCards = new List<CardDefinition>();
            List<CardDefinition> drawDeck = GetDrawDeck(state, participant);
            if (drawDeck.Count == 0 || participant == null)
                return drawnCards;

            int freeSlots = HandSizeLimit - participant.hand.Count;
            if (freeSlots <= 0)
            {
                log.Add($"{participant.displayName}: рука заполнена ({HandSizeLimit}/{HandSizeLimit}).");
                return drawnCards;
            }

            count = Mathf.Min(count, freeSlots);
            for (int i = 0; i < count; i++)
            {
                CardDefinition drawnCard = CloneCard(drawDeck[UnityEngine.Random.Range(0, drawDeck.Count)]);
                participant.hand.Add(drawnCard);
                drawnCards.Add(drawnCard);
            }

            log.Add($"{participant.displayName} берет карт: {count}.");
            return drawnCards;
        }

        // Добирает карты с конкретной стоимостью действия. Используется для стартовой руки:
        // первая карта всегда должна быть доступна уже в первом раунде.
        public List<CardDefinition> DrawCardsByActionCost(GameState state, ParticipantState participant, int count, int actionCost, ActionLog log)
        {
            List<CardDefinition> drawnCards = new List<CardDefinition>();
            List<CardDefinition> drawDeck = GetDrawDeck(state, participant);
            if (drawDeck.Count == 0 || participant == null)
                return drawnCards;

            List<CardDefinition> matchingCards = drawDeck.Where(card => card.cost == actionCost).ToList();
            if (matchingCards.Count == 0)
                return DrawCards(state, participant, count, log);

            int freeSlots = HandSizeLimit - participant.hand.Count;
            count = Mathf.Min(count, freeSlots);
            for (int i = 0; i < count; i++)
            {
                CardDefinition drawnCard = CloneCard(matchingCards[UnityEngine.Random.Range(0, matchingCards.Count)]);
                participant.hand.Add(drawnCard);
                drawnCards.Add(drawnCard);
            }

            log.Add($"{participant.displayName} берет стартовую карту за {actionCost} ОД.");
            return drawnCards;
        }

        private static List<CardDefinition> GetDrawDeck(GameState state, ParticipantState participant)
        {
            if (participant != null && participant.deck != null && participant.deck.Count > 0)
                return participant.deck;

            return state == null || state.deck == null ? new List<CardDefinition>() : state.deck;
        }

        private CardDefinition CloneCard(CardDefinition source)
        {
            return new CardDefinition
            {
                id = source.id,
                runtimeInstanceId = Guid.NewGuid().ToString("N"),
                name = source.name,
                description = source.description,
                cost = source.cost,
                type = source.type,
                targetType = source.targetType,
                effectType = source.effectType,
                value = source.value,
                duration = source.duration,
                chance = source.chance,
                rarity = source.rarity
            };
        }

        // Добавляет сторонников и применяет накопленный множитель следующего прироста.
        private void AddInfluence(GameState state, ParticipantState actor, ParticipantState target, int amount, ActionLog log)
        {
            bool affectsActor = target == actor;
            int finalAmount = affectsActor ? Mathf.RoundToInt(amount * actor.nextInfluenceGainMultiplier) : amount;
            if (affectsActor)
                actor.nextInfluenceGainMultiplier = 1f;

            ChangeInfluence(state, target, finalAmount, log, true);
        }

        // Забирает сторонников у цели и передает их автору карты.
        private void StealInfluence(GameState state, ParticipantState actor, ParticipantState target, int amount, ActionLog log)
        {
            if (target == null)
                return;

            int stolen = Mathf.Min(amount, target.influence);
            ChangeInfluence(state, target, -stolen, log, false);
            ChangeInfluence(state, actor, stolen, log, false);
        }

        // Меняет сторонников участника и не дает значениям выйти за допустимые границы.
        private void ChangeInfluence(GameState state, ParticipantState target, int delta, ActionLog log, bool takeFromNeutrals)
        {
            if (target == null || !target.IsActive || delta == 0)
                return;

            state.RecalculateNeutrals();
            if (takeFromNeutrals && delta > state.neutralInfluence)
                delta = state.neutralInfluence;

            if (delta == 0)
            {
                log.Add("У нейтралов не осталось свободных сторонников.");
                return;
            }

            int before = target.influence;
            target.influence = Mathf.Clamp(target.influence + delta, 0, 100);
            log.Add($"{target.displayName}: сторонников {before}% -> {target.influence}%.");
        }

        // Меняет ПО участника. ПО не могут упасть ниже нуля.
        private void ChangePoliticalPoints(ParticipantState target, int delta, ActionLog log)
        {
            if (target == null || !target.IsActive || delta == 0)
                return;

            int before = target.politicalPoints;
            target.politicalPoints = Mathf.Max(0, target.politicalPoints + delta);
            log.Add($"{target.displayName}: ПО {before} -> {target.politicalPoints}.");
        }

        // Вешает или усиливает временный вред по сторонников.
        private void AddInfluencePoison(ParticipantState target, int value, int rounds, ActionLog log)
        {
            if (target == null || !target.IsActive)
                return;

            target.poisonInfluenceRounds = Mathf.Max(target.poisonInfluenceRounds, rounds);
            target.poisonInfluenceValue = Mathf.Max(target.poisonInfluenceValue, value);
            log.Add($"{target.displayName}: яд сторонников -{target.poisonInfluenceValue}% еще {target.poisonInfluenceRounds} раунд.");
        }

        // Вешает или усиливает временный вред по ПО.
        private void AddPoliticalPoison(ParticipantState target, int value, int rounds, ActionLog log)
        {
            if (target == null || !target.IsActive)
                return;

            target.poisonPoliticalPointsRounds = Mathf.Max(target.poisonPoliticalPointsRounds, rounds);
            target.poisonPoliticalPointsValue = Mathf.Max(target.poisonPoliticalPointsValue, value);
            log.Add($"{target.displayName}: яд ПО -{target.poisonPoliticalPointsValue} еще {target.poisonPoliticalPointsRounds} раунд.");
        }

        // Выбирает цели для карты с учетом actor и targetType.
        private List<ParticipantState> SelectCardTargets(GameState state, ParticipantState actor, TargetType targetType, ParticipantState chosenTarget)
        {
            switch (targetType)
            {
                case TargetType.Self:
                    return new List<ParticipantState> { actor };
                case TargetType.SingleEnemy:
                case TargetType.AnyParticipant:
                    return new List<ParticipantState> { chosenTarget };
                case TargetType.AllEnemies:
                    return state.ActiveParticipants.Where(p => p.id != actor.id).ToList();
                case TargetType.Everyone:
                    return state.ActiveParticipants.ToList();
                case TargetType.RandomEnemy:
                    return new List<ParticipantState> { PickRandomActive(state.ActiveParticipants.Where(p => p.id != actor.id).ToList()) };
                default:
                    return new List<ParticipantState> { null };
            }
        }

        // Старый выбор целей для событий раунда.
        private List<ParticipantState> SelectTargets(GameState state, TargetType targetType)
        {
            switch (targetType)
            {
                case TargetType.Everyone:
                    return state.ActiveParticipants.ToList();
                case TargetType.RandomEnemy:
                case TargetType.SingleEnemy:
                    return new List<ParticipantState> { PickRandomActive(state.ActiveEnemies.ToList()) };
                default:
                    return new List<ParticipantState>();
            }
        }

        // Возвращает случайного активного участника из списка.
        private ParticipantState PickRandomActive(IList<ParticipantState> participants)
        {
            List<ParticipantState> active = participants.Where(p => p != null && p.IsActive).ToList();
            return active.Count == 0 ? null : active[UnityEngine.Random.Range(0, active.Count)];
        }
    }
}

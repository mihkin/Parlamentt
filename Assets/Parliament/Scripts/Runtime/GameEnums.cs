using System;

namespace ParliamentGame
{
    // Тип карты отвечает за визуальную и смысловую категорию.
    // ЭТО МОЖНО РЕДАКТИРОВАТЬ, если добавляешь новую категорию карт.
    public enum CardType
    {
        Influence,
        Politics,
        Attack,
        Defense,
        Neutral,
        Vote,
        Event,
        Boost
    }

    // TargetType описывает, кто будет целью карты или события из JSON.
    public enum TargetType
    {
        Self,
        SingleEnemy,
        AnyParticipant,
        AllEnemies,
        Everyone,
        RandomEnemy,
        None
    }

    // EffectType описывает конкретную механику карты.
    // НЕ ЗАБУДЬ ОБНОВИТЬ ENUM, README.md и CardResolver.ResolveCard при добавлении нового эффекта.
    public enum EffectType
    {
        AddInfluence,
        RemoveInfluence,
        StealInfluence,
        AddPoliticalPoints,
        RemovePoliticalPoints,
        DrawCard,
        StartEliminationVote,
        ProtectFromVote,
        SkipTurn,
        MultiplyNextInfluenceGain,
        RandomInfluenceChange,
        PoisonInfluence,
        PoisonPoliticalPoints
    }

    // Статус участника нужен UI и логике ходов.
    public enum ParticipantStatus
    {
        Active,
        Protected,
        SkipsTurn,
        Excluded
    }

    // Фаза игры помогает GameManager понимать, чей сейчас ход и можно ли нажимать кнопки.
    public enum GamePhase
    {
        Setup,
        PlayerTurn,
        BotTurn,
        RoundEnd,
        GameOver
    }

    // Итог матча: игра еще идет, победа или поражение.
    public enum GameResult
    {
        None,
        Victory,
        Defeat
    }

    // Безопасный парсер enum из JSON. Если в JSON ошибка, игра берет fallback и не падает.
    public static class EnumParser
    {
        public static T ParseOrDefault<T>(string value, T fallback) where T : struct
        {
            if (string.IsNullOrWhiteSpace(value))
                return fallback;

            return Enum.TryParse(value, true, out T result) ? result : fallback;
        }
    }
}

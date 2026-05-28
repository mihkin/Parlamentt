using System;

namespace ParliamentGame
{
    // Данные одной карты из Assets/StreamingAssets/cards.json.
    // ЭТО МОЖНО РЕДАКТИРОВАТЬ через JSON без пересборки кода, если enum-имена совпадают.
    [Serializable]
    public class CardDefinition
    {
        public int id;
        public string runtimeInstanceId;
        public string name;
        public string description;
        public int cost;
        public string type;
        public string targetType;
        public string effectType;
        public int value;
        public int duration;
        public float chance;
        public string rarity;

        // Эти свойства переводят строки из JSON в enum-значения для игровой логики.
        public CardType CardType => EnumParser.ParseOrDefault(type, CardType.Politics);
        public TargetType TargetType => EnumParser.ParseOrDefault(targetType, ParliamentGame.TargetType.None);
        public EffectType EffectType => EnumParser.ParseOrDefault(effectType, ParliamentGame.EffectType.AddPoliticalPoints);
    }

    // Обертка нужна JsonUtility: Unity не умеет читать массив верхнего уровня без класса.
    [Serializable]
    public class CardDefinitionList
    {
        public CardDefinition[] cards;
    }
}

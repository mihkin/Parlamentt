using System;

namespace ParliamentGame
{
    [Serializable]
    public class EventDefinition
    {
        public string id;
        public string name;
        public string description;
        public string effectType;
        public string targetType;
        public int value;
        public float chanceWeight;

        public TargetType TargetType => EnumParser.ParseOrDefault(targetType, ParliamentGame.TargetType.None);
        public EffectType EffectType => EnumParser.ParseOrDefault(effectType, ParliamentGame.EffectType.RandomInfluenceChange);
    }

    [Serializable]
    public class EventDefinitionList
    {
        public EventDefinition[] events;
    }
}

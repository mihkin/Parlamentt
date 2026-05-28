using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ParliamentGame
{
    public enum CardRarity
    {
        Common,
        Uncommon,
        Rare,
        Epic,
        Legendary
    }

    [Serializable]
    public sealed class CardPresentationData
    {
        public int Id;
        public string Name;
        public string Description;
        public int Cost;
        public string Rarity;
        public string Type;
        public string EffectType;
        public int Value;
        public int Duration;
        public float Chance;
        public Sprite Artwork;
        public Sprite Background;
    }

    [Serializable]
    public sealed class CardDatabaseEntry
    {
        [SerializeField] private int id;
        [SerializeField] private string cardName = "Новая карта";
        [SerializeField] [TextArea(2, 5)] private string description = "Описание карты.";
        [SerializeField] private int cost = 1;
        [SerializeField] private CardRarity rarity = CardRarity.Common;
        [SerializeField] private CardType type = CardType.Politics;
        [SerializeField] private TargetType targetType = TargetType.None;
        [SerializeField] private EffectType effectType = EffectType.AddPoliticalPoints;
        [SerializeField] private int value = 1;
        [SerializeField] private int duration;
        [SerializeField] [Range(0f, 1f)] private float chance = 1f;
        [SerializeField] private Sprite artwork;
        [SerializeField] private Sprite background;

        public int Id => id;
        public string Name => cardName;
        public CardRarity Rarity => rarity;
        public CardType Type => type;
        public TargetType TargetType => targetType;
        public EffectType EffectType => effectType;
        public Sprite Artwork => artwork;
        public Sprite Background => background;

        public CardDefinition ToDefinition()
        {
            return new CardDefinition
            {
                id = id,
                name = cardName,
                description = description,
                cost = cost,
                rarity = rarity.ToString(),
                type = type.ToString(),
                targetType = targetType.ToString(),
                effectType = effectType.ToString(),
                value = value,
                duration = duration,
                chance = chance
            };
        }

        public CardPresentationData ToPresentationData()
        {
            return new CardPresentationData
            {
                Id = id,
                Name = cardName,
                Description = description,
                Cost = cost,
                Rarity = rarity.ToString(),
                Type = type.ToString(),
                EffectType = effectType.ToString(),
                Value = value,
                Duration = duration,
                Chance = chance,
                Artwork = artwork,
                Background = background
            };
        }
    }

    [CreateAssetMenu(fileName = "CardDatabase", menuName = "Parliament/Data/Card Database")]
    public sealed class CardDatabase : ScriptableObject
    {
        [SerializeField] private List<CardDatabaseEntry> cards = new List<CardDatabaseEntry>();

        public IReadOnlyList<CardDatabaseEntry> Cards => cards;

        /// <summary>
        /// Возвращает все карты в формате, совместимом с существующей игровой логикой.
        /// </summary>
        public List<CardDefinition> LoadRuntimeCards()
        {
            return cards.Select(entry => entry.ToDefinition()).ToList();
        }

        /// <summary>
        /// Возвращает данные для UI-экранов коллекции и магазина.
        /// </summary>
        public List<CardPresentationData> LoadPresentationCards()
        {
            return cards.Select(entry => entry.ToPresentationData()).ToList();
        }

        /// <summary>
        /// Пытается найти карту по id.
        /// </summary>
        public bool TryGetCard(int id, out CardDatabaseEntry entry)
        {
            entry = cards.FirstOrDefault(item => item != null && item.Id == id);
            return entry != null;
        }

        /// <summary>
        /// Создает UI-представление из JSON-базы и опциональной библиотеки спрайтов, если asset-база еще не заполнена.
        /// </summary>
        public static List<CardPresentationData> CreateFallbackPresentation(CardArtLibrary artLibrary)
        {
            List<CardDefinition> definitions = JsonDatabase.LoadCards();
            List<CardPresentationData> result = new List<CardPresentationData>(definitions.Count);
            foreach (CardDefinition definition in definitions)
            {
                Sprite art = artLibrary == null ? null : artLibrary.GetSprite(definition);
                result.Add(new CardPresentationData
                {
                    Id = definition.id,
                    Name = definition.name,
                    Description = definition.description,
                    Cost = definition.cost,
                    Rarity = definition.rarity,
                    Type = definition.type,
                    EffectType = definition.effectType,
                    Value = definition.value,
                    Duration = definition.duration,
                    Chance = definition.chance,
                    Artwork = art,
                    Background = null
                });
            }

            return result;
        }
    }
}

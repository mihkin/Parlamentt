using System;
using System.Collections.Generic;
using UnityEngine;

namespace ParliamentGame
{
    // Одна привязка картинки к карте. В инспекторе укажи id карты и Sprite.
    [Serializable]
    public class CardArtEntry
    {
        public int cardId;
        public Sprite sprite;
    }

    // Библиотека иллюстраций карт. Создается как asset через Create > Parliament > Card Art Library.
    [CreateAssetMenu(fileName = "CardArtLibrary", menuName = "Parliament/Card Art Library")]
    public class CardArtLibrary : ScriptableObject
    {
        [SerializeField] private Sprite defaultSprite;
        [SerializeField] private List<CardArtEntry> cardArt = new List<CardArtEntry>();

        // Возвращает картинку карты по id. Если привязки нет, вернет defaultSprite.
        public Sprite GetSprite(CardDefinition card)
        {
            if (card == null)
                return defaultSprite;

            return GetSprite(card.id);
        }

        public Sprite GetSprite(int cardId)
        {
            foreach (CardArtEntry entry in cardArt)
            {
                if (entry != null && entry.cardId == cardId && entry.sprite != null)
                    return entry.sprite;
            }

            return defaultSprite;
        }
    }
}

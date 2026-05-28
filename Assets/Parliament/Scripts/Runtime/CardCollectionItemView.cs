using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ParliamentGame
{
    [RequireComponent(typeof(Button))]
    public sealed class CardCollectionItemView : MonoBehaviour
    {
        [SerializeField] private Image backgroundImage;
        [SerializeField] private Image artworkImage;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text rarityText;
        [SerializeField] private TMP_Text costText;
        [SerializeField] private TMP_Text deckStateText;
        [SerializeField] private TMP_Text lockText;
        [SerializeField] private Sprite fallbackBackgroundSprite;

        private Button button;
        private CardPresentationData currentData;

        private void Awake()
        {
            button = GetComponent<Button>();
        }

        public void Setup(CardPresentationData data, Action<CardPresentationData> onSelected)
        {
            Setup(data, true, false, onSelected);
        }

        public void Setup(CardPresentationData data, bool isOwned, bool isInDeck, Action<CardPresentationData> onSelected)
        {
            currentData = data;

            string cardName = isOwned ? (data?.Name ?? "Карта") : "Неизвестная карта";
            string rarityLabel = isOwned ? (data?.Rarity ?? "Common") : "Скрыта";
            string costLabel = isOwned ? (data == null ? "0" : data.Cost.ToString()) : "?";

            if (titleText != null)
            {
                titleText.text = cardName;
                titleText.color = isOwned
                    ? CardRarityVisuals.TintText(new Color(0.12f, 0.08f, 0.03f, 1f), data?.Rarity)
                    : new Color(0.18f, 0.12f, 0.06f, 0.90f);
            }

            if (rarityText != null)
            {
                rarityText.text = rarityLabel;
                rarityText.color = isOwned
                    ? CardRarityVisuals.TintText(new Color(0.18f, 0.12f, 0.06f, 1f), data?.Rarity)
                    : new Color(0.24f, 0.16f, 0.08f, 0.88f);
            }

            if (costText != null)
            {
                costText.text = costLabel;
                costText.color = isOwned
                    ? CardRarityVisuals.TintText(new Color(0.16f, 0.11f, 0.05f, 1f), data?.Rarity)
                    : new Color(0.22f, 0.15f, 0.07f, 0.86f);
            }

            if (artworkImage != null)
            {
                artworkImage.sprite = data?.Artwork;
                artworkImage.enabled = data?.Artwork != null;
                artworkImage.preserveAspect = true;

                if (data?.Artwork == null)
                {
                    artworkImage.color = new Color(0.22f, 0.20f, 0.18f, 0.45f);
                }
                else if (isOwned)
                {
                    artworkImage.color = CardRarityVisuals.TintArtwork(Color.white, data?.Rarity);
                }
                else
                {
                    artworkImage.color = new Color(0.06f, 0.05f, 0.04f, 0.92f);
                }
            }

            if (backgroundImage != null)
            {
                backgroundImage.sprite = data?.Background != null ? data.Background : fallbackBackgroundSprite;
                backgroundImage.type = backgroundImage.sprite == null ? Image.Type.Simple : Image.Type.Sliced;

                Color baseBackgroundColor = isOwned
                    ? (isInDeck ? new Color(0.93f, 0.90f, 0.82f, 1f) : Color.white)
                    : new Color(0.76f, 0.72f, 0.66f, 0.96f);
                backgroundImage.color = isOwned
                    ? CardRarityVisuals.TintBackground(baseBackgroundColor, data?.Rarity)
                    : baseBackgroundColor;
            }

            if (deckStateText != null)
            {
                if (!isOwned)
                    deckStateText.text = "Силуэт";
                else
                    deckStateText.text = isInDeck ? "В колоде" : "Открыта";
            }

            if (lockText != null)
            {
                lockText.text = isOwned ? string.Empty : "Не получена";
                lockText.gameObject.SetActive(!isOwned);
            }

            if (button == null)
                button = GetComponent<Button>();

            button.onClick.RemoveAllListeners();
            if (onSelected != null)
                button.onClick.AddListener(() => onSelected.Invoke(currentData));
        }
    }

    internal static class CardRarityVisuals
    {
        public static Color TintBackground(Color baseColor, string rarity)
        {
            return ApplyTint(baseColor, GetRarityColor(rarity), GetBackgroundStrength(rarity));
        }

        public static Color TintArtwork(Color baseColor, string rarity)
        {
            return ApplyTint(baseColor, GetRarityColor(rarity), GetArtworkStrength(rarity));
        }

        public static Color TintText(Color baseColor, string rarity)
        {
            return ApplyTint(baseColor, GetRarityColor(rarity), GetTextStrength(rarity));
        }

        private static Color ApplyTint(Color baseColor, Color tintColor, float strength)
        {
            Color tinted = Color.Lerp(baseColor, tintColor, Mathf.Clamp01(strength));
            tinted.a = baseColor.a;
            return tinted;
        }

        private static Color GetRarityColor(string rarity)
        {
            switch (NormalizeRarity(rarity))
            {
                case CardRarity.Uncommon:
                    return new Color(0.78f, 0.88f, 0.76f, 1f);
                case CardRarity.Rare:
                    return new Color(0.76f, 0.84f, 0.92f, 1f);
                case CardRarity.Epic:
                    return new Color(0.89f, 0.78f, 0.86f, 1f);
                case CardRarity.Legendary:
                    return new Color(0.94f, 0.86f, 0.67f, 1f);
                default:
                    return new Color(0.94f, 0.91f, 0.85f, 1f);
            }
        }

        private static float GetBackgroundStrength(string rarity)
        {
            switch (NormalizeRarity(rarity))
            {
                case CardRarity.Uncommon:
                    return 0.05f;
                case CardRarity.Rare:
                    return 0.08f;
                case CardRarity.Epic:
                    return 0.11f;
                case CardRarity.Legendary:
                    return 0.15f;
                default:
                    return 0.02f;
            }
        }

        private static float GetArtworkStrength(string rarity)
        {
            switch (NormalizeRarity(rarity))
            {
                case CardRarity.Uncommon:
                    return 0.03f;
                case CardRarity.Rare:
                    return 0.05f;
                case CardRarity.Epic:
                    return 0.07f;
                case CardRarity.Legendary:
                    return 0.10f;
                default:
                    return 0.01f;
            }
        }

        private static float GetTextStrength(string rarity)
        {
            switch (NormalizeRarity(rarity))
            {
                case CardRarity.Uncommon:
                    return 0.06f;
                case CardRarity.Rare:
                    return 0.10f;
                case CardRarity.Epic:
                    return 0.14f;
                case CardRarity.Legendary:
                    return 0.18f;
                default:
                    return 0.03f;
            }
        }

        private static CardRarity NormalizeRarity(string rarity)
        {
            if (Enum.TryParse(rarity, true, out CardRarity parsed))
                return parsed;

            return CardRarity.Common;
        }
    }
}

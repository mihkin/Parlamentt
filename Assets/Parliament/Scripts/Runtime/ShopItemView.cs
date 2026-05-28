using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ParliamentGame
{
    [RequireComponent(typeof(Button))]
    public sealed class ShopItemView : MonoBehaviour
    {
        [SerializeField] private Image iconImage;
        [SerializeField] private Image packBodyImage;
        [SerializeField] private Image accentImage;
        [SerializeField] private Image sealImage;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text tierText;
        [SerializeField] private TMP_Text descriptionText;
        [SerializeField] private TMP_Text contentsText;
        [SerializeField] private TMP_Text priceText;
        [SerializeField] private TMP_Text actionText;

        private Button button;
        private Image backgroundImage;

        private void Awake()
        {
            button = GetComponent<Button>();
            backgroundImage = GetComponent<Image>();
        }

        public void Setup(ShopOfferDefinition offer, Action<ShopOfferDefinition> onBuy)
        {
            if (iconImage != null)
            {
                bool hasIcon = offer != null && offer.Icon != null;
                iconImage.sprite = hasIcon ? offer.Icon : null;
                iconImage.preserveAspect = true;
                iconImage.enabled = hasIcon;
                iconImage.color = Color.white;
            }

            if (titleText != null)
                titleText.text = offer?.Title ?? "Набор карт";

            if (tierText != null)
                tierText.text = GetTierBadge(offer);

            if (descriptionText != null)
                descriptionText.text = offer?.Description ?? "Описание набора.";

            if (contentsText != null)
            {
                contentsText.text = offer == null
                    ? "0 карт"
                    : $"{offer.RewardCardCount} карты • стоимость {offer.MinCardCost}-{offer.MaxCardCost}";
            }

            if (priceText != null)
                priceText.text = offer == null ? "0 монет" : $"{offer.Price} монет";

            if (actionText != null)
                actionText.text = offer == null ? string.Empty : "Открыть набор";

            ApplyPackStyle(offer);

            if (button == null)
                button = GetComponent<Button>();

            button.onClick.RemoveAllListeners();
            if (offer != null && onBuy != null)
                button.onClick.AddListener(() => onBuy.Invoke(offer));
        }

        private void ApplyPackStyle(ShopOfferDefinition offer)
        {
            if (backgroundImage == null)
                backgroundImage = GetComponent<Image>();

            ShopPackStyle style = GetStyle(offer);

            if (backgroundImage != null)
                backgroundImage.color = style.Background;

            if (packBodyImage != null)
                packBodyImage.color = style.Pack;

            if (accentImage != null)
                accentImage.color = style.Accent;

            if (sealImage != null)
                sealImage.color = style.Seal;

            if (tierText != null)
                tierText.color = style.HighlightText;

            if (priceText != null)
                priceText.color = style.HighlightText;

            if (actionText != null)
                actionText.color = style.HighlightText;
        }

        private static string GetTierBadge(ShopOfferDefinition offer)
        {
            if (offer == null)
                return "НАБОР";

            if (offer.MaxCardCost <= 2)
                return "ДЕШЕВЫЕ КАРТЫ";

            if (offer.MaxCardCost <= 4)
                return "СРЕДНИЙ СЕГМЕНТ";

            return "ДОРОГИЕ КАРТЫ";
        }

        private static ShopPackStyle GetStyle(ShopOfferDefinition offer)
        {
            if (offer != null && offer.MaxCardCost <= 2)
            {
                return new ShopPackStyle(
                    new Color(0.89f, 0.84f, 0.72f, 1f),
                    new Color(0.66f, 0.49f, 0.22f, 1f),
                    new Color(0.92f, 0.75f, 0.27f, 1f),
                    new Color(0.35f, 0.21f, 0.08f, 0.92f),
                    new Color(0.31f, 0.17f, 0.03f, 1f));
            }

            if (offer != null && offer.MaxCardCost <= 4)
            {
                return new ShopPackStyle(
                    new Color(0.84f, 0.86f, 0.79f, 1f),
                    new Color(0.34f, 0.44f, 0.29f, 1f),
                    new Color(0.72f, 0.84f, 0.48f, 1f),
                    new Color(0.1f, 0.17f, 0.08f, 0.9f),
                    new Color(0.15f, 0.25f, 0.08f, 1f));
            }

            return new ShopPackStyle(
                new Color(0.8f, 0.8f, 0.85f, 1f),
                new Color(0.33f, 0.28f, 0.48f, 1f),
                new Color(0.87f, 0.8f, 0.34f, 1f),
                new Color(0.08f, 0.08f, 0.16f, 0.92f),
                new Color(0.95f, 0.86f, 0.44f, 1f));
        }

        private readonly struct ShopPackStyle
        {
            public ShopPackStyle(Color background, Color pack, Color accent, Color seal, Color highlightText)
            {
                Background = background;
                Pack = pack;
                Accent = accent;
                Seal = seal;
                HighlightText = highlightText;
            }

            public Color Background { get; }
            public Color Pack { get; }
            public Color Accent { get; }
            public Color Seal { get; }
            public Color HighlightText { get; }
        }
    }
}

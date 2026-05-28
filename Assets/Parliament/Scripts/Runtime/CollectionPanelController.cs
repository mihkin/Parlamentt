using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ParliamentGame
{
    public sealed class CollectionPanelController : MonoBehaviour
    {
        [SerializeField] private CardDatabase cardDatabase;
        [SerializeField] private CardArtLibrary cardArtLibrary;
        [SerializeField] private PlayerProfileDatabase profileDatabase;
        [SerializeField] private RectTransform contentRoot;
        [SerializeField] private CardCollectionItemView itemPrefab;
        [SerializeField] private TMP_Dropdown rarityDropdown;
        [SerializeField] private TMP_Dropdown typeDropdown;
        [SerializeField] private Image previewArtwork;
        [SerializeField] private Image previewBackground;
        [SerializeField] private TMP_Text previewTitle;
        [SerializeField] private TMP_Text previewDescription;
        [SerializeField] private TMP_Text databaseInfoText;
        [SerializeField] private TMP_Text deckInfoText;
        [SerializeField] private MainMenuButtonView deckToggleButton;
        [SerializeField] private Sprite fallbackCardBackground;

        private readonly List<CardCollectionItemView> spawnedItems = new List<CardCollectionItemView>();
        private List<CardPresentationData> allCards = new List<CardPresentationData>();
        private CardPresentationData selectedCard;
        private bool initialized;

        private void Start()
        {
            Initialize();
        }

        public void Initialize()
        {
            if (initialized)
                return;

            initialized = true;

            if (rarityDropdown != null)
                rarityDropdown.onValueChanged.AddListener(_ => Refresh());

            if (typeDropdown != null)
                typeDropdown.onValueChanged.AddListener(_ => Refresh());

            if (profileDatabase != null)
                profileDatabase.ProfileChanged += OnProfileChanged;

            if (deckToggleButton != null)
                deckToggleButton.Setup("Добавить в колоду", ToggleSelectedCardInDeck);

            LoadCards();
        }

        private void OnDestroy()
        {
            if (initialized && profileDatabase != null)
                profileDatabase.ProfileChanged -= OnProfileChanged;
        }

        public void LoadCards()
        {
            allCards = cardDatabase != null ? cardDatabase.LoadPresentationCards() : CardDatabase.CreateFallbackPresentation(cardArtLibrary);
            foreach (CardPresentationData card in allCards)
            {
                if (card == null)
                    continue;

                if (card.Artwork == null && cardArtLibrary != null)
                    card.Artwork = cardArtLibrary.GetSprite(card.Id);

                if (card.Background == null)
                    card.Background = fallbackCardBackground;
            }

            Refresh();
        }

        public void Refresh()
        {
            if (!initialized)
                Initialize();

            if (contentRoot == null)
                return;

            foreach (CardCollectionItemView item in spawnedItems)
            {
                if (item != null)
                    Destroy(item.gameObject);
            }

            spawnedItems.Clear();

            PlayerProfileData profile = profileDatabase?.CurrentProfile;
            HashSet<int> ownedCards = profile == null
                ? new HashSet<int>()
                : new HashSet<int>(profile.ownedCards ?? new List<int>());
            HashSet<int> deckCards = profile == null
                ? new HashSet<int>()
                : new HashSet<int>(profile.selectedDeck ?? new List<int>());

            IEnumerable<CardPresentationData> filtered = allCards.Where(card => card != null);
            string rarity = GetSelectedOption(rarityDropdown);
            string type = GetSelectedOption(typeDropdown);

            if (!string.IsNullOrEmpty(rarity) && rarity != "Все")
                filtered = filtered.Where(card => string.Equals(card.Rarity, rarity, System.StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(type) && type != "Все")
                filtered = filtered.Where(card => string.Equals(card.Type, type, System.StringComparison.OrdinalIgnoreCase));

            List<CardPresentationData> visibleCards = filtered
                .OrderByDescending(card => ownedCards.Contains(card.Id))
                .ThenBy(card => card.Cost)
                .ThenBy(card => card.Name)
                .ToList();

            foreach (CardPresentationData card in visibleCards)
            {
                CardCollectionItemView item = InstantiateItem();
                if (item == null)
                    return;

                bool isOwned = ownedCards.Contains(card.Id);
                bool isInDeck = deckCards.Contains(card.Id);
                item.Setup(card, isOwned, isInDeck, ShowPreview);

                CanvasGroup canvasGroup = item.GetComponent<CanvasGroup>();
                if (canvasGroup != null)
                {
                    canvasGroup.alpha = isOwned ? 1f : 0.88f;
                    canvasGroup.interactable = true;
                    canvasGroup.blocksRaycasts = true;
                }

                spawnedItems.Add(item);
            }

            UpdateInfoTexts();

            CardPresentationData previewCard = selectedCard == null
                ? visibleCards.FirstOrDefault()
                : visibleCards.FirstOrDefault(card => card.Id == selectedCard.Id) ?? visibleCards.FirstOrDefault();

            if (previewCard != null)
                ShowPreview(previewCard);
            else
                UpdateDeckButton();
        }

        public void ShowPreview(CardPresentationData card)
        {
            if (card == null)
                return;

            selectedCard = card;

            bool isOwned = profileDatabase?.CurrentProfile != null &&
                profileDatabase.CurrentProfile.ownedCards != null &&
                profileDatabase.CurrentProfile.ownedCards.Contains(card.Id);
            bool isInDeck = profileDatabase?.CurrentProfile != null &&
                profileDatabase.CurrentProfile.selectedDeck != null &&
                profileDatabase.CurrentProfile.selectedDeck.Contains(card.Id);

            if (previewArtwork != null)
            {
                previewArtwork.sprite = card.Artwork;
                previewArtwork.enabled = card.Artwork != null;
                previewArtwork.preserveAspect = true;
                previewArtwork.color = card.Artwork == null
                    ? new Color(0.25f, 0.23f, 0.20f, 0.45f)
                    : isOwned ? Color.white : new Color(0.07f, 0.06f, 0.05f, 0.94f);
            }

            if (previewBackground != null)
            {
                previewBackground.sprite = card.Background != null ? card.Background : fallbackCardBackground;
                previewBackground.color = previewBackground.sprite == null ? new Color(0.92f, 0.89f, 0.82f, 1f) : Color.white;
                previewBackground.preserveAspect = false;
            }

            if (previewTitle != null)
                previewTitle.text = isOwned ? $"{card.Name} [{card.Rarity}]" : "Неизвестная карта [Скрыта]";

            if (previewDescription != null)
            {
                if (!isOwned)
                {
                    previewDescription.text =
                        "Карта ещё не открыта.\n\n" +
                        "Статус: Силуэт\n" +
                        $"Редкость: {card.Rarity}\n" +
                        $"Тип: {card.Type}";
                }
                else
                {
                    string status = isInDeck ? "В колоде" : "Открыта";
                    previewDescription.text =
                        $"{card.Description}\n\n" +
                        $"Статус: {status}\n" +
                        $"Стоимость: {card.Cost}\n" +
                        $"Тип: {card.Type}";
                }
            }

            UpdateDeckButton();
        }

        private void OnProfileChanged(PlayerProfileData _)
        {
            Refresh();
        }

        private void ToggleSelectedCardInDeck()
        {
            if (selectedCard == null || profileDatabase == null)
                return;

            profileDatabase.TryToggleCardInSelectedDeck(selectedCard.Id, out string message);
            UpdateInfoTexts(message);
            Refresh();
        }

        private void UpdateInfoTexts(string feedback = null)
        {
            PlayerProfileData profile = profileDatabase?.CurrentProfile;
            int ownedCount = profile == null ? 0 : profile.ownedCards.Count;
            int deckCount = profile == null ? 0 : profile.selectedDeck.Count;

            if (databaseInfoText != null)
                databaseInfoText.text = $"База: cards.json  •  Всего: {allCards.Count}  •  Открыто: {ownedCount}";

            if (deckInfoText != null)
            {
                string baseText = $"Колода: {deckCount}/{PlayerProfileDatabase.MaxSelectedDeckCards}  •  Мин: {PlayerProfileDatabase.MinimumSelectedDeckCards}";
                deckInfoText.text = string.IsNullOrWhiteSpace(feedback) ? baseText : $"{baseText}  |  {feedback}";
            }
        }

        private void UpdateDeckButton()
        {
            if (deckToggleButton == null)
                return;

            Button unityButton = deckToggleButton.GetComponent<Button>();
            if (selectedCard == null || profileDatabase?.CurrentProfile == null)
            {
                deckToggleButton.Setup("Выберите карту", null);
                if (unityButton != null)
                    unityButton.interactable = false;
                return;
            }

            bool isOwned = profileDatabase.CurrentProfile.ownedCards != null &&
                profileDatabase.CurrentProfile.ownedCards.Contains(selectedCard.Id);
            bool isInDeck = profileDatabase.CurrentProfile.selectedDeck != null &&
                profileDatabase.CurrentProfile.selectedDeck.Contains(selectedCard.Id);
            deckToggleButton.Setup(
                isOwned ? (isInDeck ? "Убрать из колоды" : "Добавить в колоду") : "Карта не открыта",
                ToggleSelectedCardInDeck);

            if (unityButton != null)
                unityButton.interactable = isOwned;
        }

        private CardCollectionItemView InstantiateItem()
        {
            if (itemPrefab != null)
            {
                CardCollectionItemView item = Instantiate(itemPrefab, contentRoot);
                item.gameObject.SetActive(true);
                return item;
            }

            GameObject fallback = new GameObject("CardCollectionItem", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(CanvasGroup));
            fallback.transform.SetParent(contentRoot, false);
            fallback.AddComponent<CardCollectionItemView>();
            return fallback.GetComponent<CardCollectionItemView>();
        }

        private static string GetSelectedOption(TMP_Dropdown dropdown)
        {
            if (dropdown == null || dropdown.options.Count == 0)
                return string.Empty;

            return dropdown.options[Mathf.Clamp(dropdown.value, 0, dropdown.options.Count - 1)].text;
        }
    }
}

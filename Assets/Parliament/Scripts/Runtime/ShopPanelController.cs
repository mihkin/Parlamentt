using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ParliamentGame
{
    public sealed class ShopPanelController : MonoBehaviour
    {
        [SerializeField] private ShopCatalog shopCatalog;
        [SerializeField] private CardDatabase cardDatabase;
        [SerializeField] private CardArtLibrary cardArtLibrary;
        [SerializeField] private PlayerProfileDatabase profileDatabase;
        [SerializeField] private RectTransform contentRoot;
        [SerializeField] private ShopItemView itemPrefab;
        [SerializeField] private TMP_Text coinsText;
        [SerializeField] private TMP_Text feedbackText;
        [SerializeField] private GameObject rewardsPanel;
        [SerializeField] private TMP_Text rewardsText;
        [SerializeField] private Button rewardsCloseButton;
        [SerializeField] private RectTransform rewardsCardsRoot;
        [SerializeField] private CardCollectionItemView rewardsCardPrefab;

        private readonly List<ShopItemView> spawnedItems = new List<ShopItemView>();
        private readonly List<CardCollectionItemView> spawnedRewardCards = new List<CardCollectionItemView>();
        private readonly List<ShopOfferDefinition> runtimeOffers = new List<ShopOfferDefinition>();

        private List<CardPresentationData> allCards = new List<CardPresentationData>();
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
            allCards = LoadPresentationCards();
            BuildOffers();
            UpdateCoinsText();
            UpdateFeedback(string.Empty);
            if (rewardsPanel != null)
                rewardsPanel.SetActive(false);

            if (rewardsCloseButton != null)
            {
                rewardsCloseButton.onClick.RemoveAllListeners();
                rewardsCloseButton.onClick.AddListener(HideRewardsPopup);
            }

            if (profileDatabase != null)
                profileDatabase.ProfileChanged += OnProfileChanged;
        }

        private void OnDestroy()
        {
            if (initialized && profileDatabase != null)
                profileDatabase.ProfileChanged -= OnProfileChanged;
        }

        public void Rebuild()
        {
            if (!initialized)
                Initialize();

            if (contentRoot == null)
                return;

            for (int i = 0; i < spawnedItems.Count; i++)
            {
                if (spawnedItems[i] != null)
                    Destroy(spawnedItems[i].gameObject);
            }

            spawnedItems.Clear();

            foreach (ShopOfferDefinition offer in GetOffers())
            {
                ShopItemView item = InstantiateItem();
                if (item == null)
                    continue;

                item.Setup(offer, TryBuyOffer);
                spawnedItems.Add(item);
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRoot);
            UpdateCoinsText();
        }

        private void OnProfileChanged(PlayerProfileData _)
        {
            UpdateCoinsText();
        }

        private void BuildOffers()
        {
            runtimeOffers.Clear();

            if (shopCatalog != null && shopCatalog.Offers != null && shopCatalog.Offers.Count > 0)
            {
                foreach (ShopOfferDefinition offer in shopCatalog.Offers.Where(item => item != null))
                    runtimeOffers.Add(CloneOfferWithResolvedIcon(offer));

                return;
            }

            runtimeOffers.Add(CreateDefaultOffer(
                "cheap_pack",
                "Стартовый набор",
                "Только дешёвые карты для быстрого усиления первой колоды.",
                120,
                3,
                1,
                2));

            runtimeOffers.Add(CreateDefaultOffer(
                "mid_pack",
                "Тактический набор",
                "Карты средней стоимости для гибкой игры и усиления связок.",
                260,
                4,
                3,
                4));

            runtimeOffers.Add(CreateDefaultOffer(
                "high_pack",
                "Элитный набор",
                "Дорогие карты с сильными эффектами и поздней мощью.",
                520,
                5,
                5,
                6));
        }

        private ShopOfferDefinition CreateDefaultOffer(
            string id,
            string title,
            string description,
            int price,
            int cardCount,
            int minCost,
            int maxCost)
        {
            Sprite icon = FindOfferIcon(minCost, maxCost, null);

            return ShopOfferDefinition.CreateRuntime(id, title, description, price, cardCount, minCost, maxCost, icon);
        }

        private List<CardPresentationData> LoadPresentationCards()
        {
            List<CardPresentationData> cards = cardDatabase != null
                ? cardDatabase.LoadPresentationCards()
                : CardDatabase.CreateFallbackPresentation(cardArtLibrary);

            foreach (CardPresentationData card in cards)
            {
                if (card == null || card.Artwork != null || cardArtLibrary == null)
                    continue;

                card.Artwork = cardArtLibrary.GetSprite(card.Id);
            }

            return cards;
        }

        private ShopOfferDefinition CloneOfferWithResolvedIcon(ShopOfferDefinition offer)
        {
            if (offer == null)
                return null;

            Sprite resolvedIcon = offer.Icon != null
                ? offer.Icon
                : FindOfferIcon(offer.MinCardCost, offer.MaxCardCost, offer.AllowedRarities);

            return ShopOfferDefinition.CreateRuntime(
                offer.Id,
                offer.Title,
                offer.Description,
                offer.Price,
                offer.RewardCardCount,
                offer.MinCardCost,
                offer.MaxCardCost,
                resolvedIcon,
                offer.GuaranteedCardIds,
                offer.AllowedRarities);
        }

        private Sprite FindOfferIcon(int minCost, int maxCost, IReadOnlyList<string> allowedRarities)
        {
            return allCards
                .Where(card => card != null && card.Cost >= minCost && card.Cost <= maxCost)
                .Where(card => allowedRarities == null || allowedRarities.Count == 0 ||
                    allowedRarities.Any(rarity => string.Equals(rarity, card.Rarity, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(card => card.Cost)
                .Select(card => card.Artwork)
                .FirstOrDefault(sprite => sprite != null);
        }

        private IReadOnlyList<ShopOfferDefinition> GetOffers()
        {
            return runtimeOffers;
        }

        private void TryBuyOffer(ShopOfferDefinition offer)
        {
            if (offer == null)
                return;

            if (profileDatabase == null)
            {
                UpdateFeedback("Профиль игрока недоступен.");
                return;
            }

            if (!profileDatabase.TrySpendCoins(offer.Price))
            {
                UpdateFeedback($"Недостаточно монет для покупки: {offer.Title}.");
                return;
            }

            List<CardPresentationData> rewards = BuildRewardList(offer);
            if (rewards.Count == 0)
            {
                profileDatabase.AddCoins(offer.Price);
                UpdateFeedback($"Для набора {offer.Title} не найдено подходящих карт.");
                return;
            }

            profileDatabase.UnlockCards(rewards.Select(reward => reward.Id));

            string rewardNames = string.Join(", ", rewards.Select(card => card.Name));
            UpdateFeedback($"{offer.Title}: {rewardNames}");
            UpdateCoinsText();
            ShowRewardsPopup(offer.Title, rewards);
        }

        private List<CardPresentationData> BuildRewardList(ShopOfferDefinition offer)
        {
            List<CardPresentationData> rewards = new List<CardPresentationData>();
            if (offer == null || allCards.Count == 0)
                return rewards;

            HashSet<int> addedIds = new HashSet<int>();
            HashSet<int> ownedIds = profileDatabase?.CurrentProfile == null
                ? new HashSet<int>()
                : new HashSet<int>(profileDatabase.CurrentProfile.ownedCards);

            foreach (int guaranteedId in offer.GuaranteedCardIds)
            {
                CardPresentationData guaranteedCard = allCards.FirstOrDefault(card => card != null && card.Id == guaranteedId);
                if (guaranteedCard == null || !OfferAllowsCard(offer, guaranteedCard) || !addedIds.Add(guaranteedCard.Id))
                    continue;

                rewards.Add(guaranteedCard);
                if (rewards.Count >= offer.RewardCardCount)
                    return rewards;
            }

            List<CardPresentationData> pool = allCards
                .Where(card => card != null && OfferAllowsCard(offer, card))
                .ToList();

            List<CardPresentationData> unownedPool = pool
                .Where(card => !ownedIds.Contains(card.Id))
                .ToList();

            while (rewards.Count < offer.RewardCardCount)
            {
                CardPresentationData next = DrawRandomCard(unownedPool, addedIds) ?? DrawRandomCard(pool, addedIds);
                if (next == null)
                    break;

                rewards.Add(next);
            }

            return rewards;
        }

        private static CardPresentationData DrawRandomCard(List<CardPresentationData> pool, HashSet<int> addedIds)
        {
            if (pool == null || pool.Count == 0)
                return null;

            List<CardPresentationData> available = pool.Where(card => card != null && !addedIds.Contains(card.Id)).ToList();
            if (available.Count == 0)
                return null;

            CardPresentationData pick = available[UnityEngine.Random.Range(0, available.Count)];
            addedIds.Add(pick.Id);
            return pick;
        }

        private static bool OfferAllowsCard(ShopOfferDefinition offer, CardPresentationData card)
        {
            if (offer == null || card == null)
                return false;

            if (card.Cost < offer.MinCardCost || card.Cost > offer.MaxCardCost)
                return false;

            if (offer.AllowedRarities == null || offer.AllowedRarities.Count == 0)
                return true;

            return offer.AllowedRarities.Any(rarity => string.Equals(rarity, card.Rarity, StringComparison.OrdinalIgnoreCase));
        }

        private void UpdateCoinsText()
        {
            if (coinsText == null)
                return;

            int coins = profileDatabase?.CurrentProfile == null ? 0 : profileDatabase.CurrentProfile.coins;
            coinsText.text = $"Монеты: {coins}";
        }

        private void UpdateFeedback(string message)
        {
            if (feedbackText != null)
                feedbackText.text = message ?? string.Empty;
        }

        private void ShowRewardsPopup(string offerTitle, IReadOnlyList<CardPresentationData> rewards)
        {
            if (rewardsPanel == null)
                return;

            ClearRewardCards();

            List<CardPresentationData> rewardList = rewards == null
                ? new List<CardPresentationData>()
                : rewards.Where(card => card != null).ToList();

            if (rewardsText != null)
            {
                rewardsText.text = string.IsNullOrWhiteSpace(offerTitle)
                    ? $"Получено карт: {rewardList.Count}"
                    : $"{offerTitle}  •  {rewardList.Count} карт";
            }

            if (rewardsCardsRoot != null && rewardsCardPrefab != null)
            {
                foreach (CardPresentationData reward in rewardList)
                {
                    CardCollectionItemView rewardView = Instantiate(rewardsCardPrefab, rewardsCardsRoot);
                    rewardView.gameObject.SetActive(true);
                    rewardView.Setup(reward, true, false, null);

                    Button button = rewardView.GetComponent<Button>();
                    if (button != null)
                        button.interactable = false;

                    spawnedRewardCards.Add(rewardView);
                }

                LayoutRebuilder.ForceRebuildLayoutImmediate(rewardsCardsRoot);
            }

            rewardsPanel.SetActive(true);
            rewardsPanel.transform.SetAsLastSibling();
        }

        private void HideRewardsPopup()
        {
            ClearRewardCards();
            if (rewardsPanel != null)
                rewardsPanel.SetActive(false);
        }

        private void ClearRewardCards()
        {
            foreach (CardCollectionItemView item in spawnedRewardCards)
            {
                if (item != null)
                    Destroy(item.gameObject);
            }

            spawnedRewardCards.Clear();
        }

        private ShopItemView InstantiateItem()
        {
            if (itemPrefab != null)
            {
                ShopItemView item = Instantiate(itemPrefab, contentRoot);
                item.gameObject.SetActive(true);
                return item;
            }

            GameObject fallback = new GameObject("ShopItem", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            fallback.transform.SetParent(contentRoot, false);
            fallback.AddComponent<ShopItemView>();
            return fallback.GetComponent<ShopItemView>();
        }
    }
}

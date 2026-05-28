using System;
using System.Collections.Generic;
using UnityEngine;

namespace ParliamentGame
{
    [Serializable]
    public sealed class ShopOfferDefinition
    {
        [SerializeField] private string id = "starter_pack";
        [SerializeField] private string title = "Стартовый набор";
        [SerializeField] [TextArea(2, 4)] private string description = "Набор карт для первого шага в коллекции.";
        [SerializeField] private int price = 150;
        [SerializeField] private int rewardCardCount = 3;
        [SerializeField] private List<int> guaranteedCardIds = new List<int>();
        [SerializeField] private int minCardCost = 1;
        [SerializeField] private int maxCardCost = 6;
        [SerializeField] private List<string> allowedRarities = new List<string>();
        [SerializeField] private Sprite icon;

        public string Id => id;
        public string Title => title;
        public string Description => description;
        public int Price => price;
        public int RewardCardCount => rewardCardCount;
        public IReadOnlyList<int> GuaranteedCardIds => guaranteedCardIds;
        public int MinCardCost => minCardCost;
        public int MaxCardCost => Mathf.Max(minCardCost, maxCardCost);
        public IReadOnlyList<string> AllowedRarities => allowedRarities;
        public Sprite Icon => icon;

        public static ShopOfferDefinition CreateRuntime(
            string offerId,
            string offerTitle,
            string offerDescription,
            int offerPrice,
            int cardCount,
            int minimumCardCost,
            int maximumCardCost,
            Sprite offerIcon,
            IEnumerable<int> guaranteedIds = null,
            IEnumerable<string> rarities = null)
        {
            ShopOfferDefinition offer = new ShopOfferDefinition
            {
                id = string.IsNullOrWhiteSpace(offerId) ? "runtime_pack" : offerId,
                title = string.IsNullOrWhiteSpace(offerTitle) ? "Набор карт" : offerTitle,
                description = string.IsNullOrWhiteSpace(offerDescription) ? "Набор карт для коллекции." : offerDescription,
                price = Mathf.Max(0, offerPrice),
                rewardCardCount = Mathf.Max(1, cardCount),
                minCardCost = Mathf.Max(0, minimumCardCost),
                maxCardCost = Mathf.Max(minimumCardCost, maximumCardCost),
                icon = offerIcon,
                guaranteedCardIds = guaranteedIds == null ? new List<int>() : new List<int>(guaranteedIds),
                allowedRarities = rarities == null ? new List<string>() : new List<string>(rarities)
            };

            return offer;
        }
    }

    [CreateAssetMenu(fileName = "ShopCatalog", menuName = "Parliament/Data/Shop Catalog")]
    public sealed class ShopCatalog : ScriptableObject
    {
        [SerializeField] private List<ShopOfferDefinition> offers = new List<ShopOfferDefinition>();

        public IReadOnlyList<ShopOfferDefinition> Offers => offers;
    }
}

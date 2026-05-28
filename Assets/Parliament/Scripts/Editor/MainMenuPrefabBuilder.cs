using System.IO;
using System.Reflection;
using ParliamentGame;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace ParliamentGame.EditorTools
{
    public static class MainMenuPrefabBuilder
    {
        private const string PrefabFolder = "Assets/Parliament/Prefabs/MainMenu";

        [MenuItem("Parliament/Build Main Menu Prefabs")]
        public static void BuildMainMenuPrefabs()
        {
            EnsureFolder("Assets/Parliament/Prefabs");
            EnsureFolder(PrefabFolder);

            TMP_FontAsset font = TMP_Settings.defaultFontAsset;
            CreateMainMenuButtonPrefab(font);
            CreateLobbyPlayerItemPrefab(font);
            CreateCardCollectionItemPrefab(font);
            CreateShopItemPrefab(font);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Main Menu prefabs created in Assets/Parliament/Prefabs/MainMenu.");
        }

        private static void CreateMainMenuButtonPrefab(TMP_FontAsset font)
        {
            GameObject root = CreateRoot("MainMenuButton");
            root.AddComponent<CanvasRenderer>();
            Image image = root.AddComponent<Image>();
            image.color = new Color(0.86f, 0.79f, 0.55f, 0.98f);
            root.AddComponent<Button>();
            LayoutElement layout = root.AddComponent<LayoutElement>();
            layout.preferredHeight = 62f;
            layout.minHeight = 54f;
            MainMenuButtonView view = root.AddComponent<MainMenuButtonView>();

            TMP_Text label = CreateText("Label", root.transform, font, "Кнопка", 24, TextAlignmentOptions.Center);
            Stretch(label.rectTransform);

            SetPrivate(view, "backgroundImage", image);
            SetPrivate(view, "labelText", label);
            SavePrefab(root, Path.Combine(PrefabFolder, "MainMenuButton.prefab"));
        }

        private static void CreateLobbyPlayerItemPrefab(TMP_FontAsset font)
        {
            GameObject root = CreateRoot("LobbyPlayerItem");
            root.AddComponent<CanvasRenderer>();
            Image background = root.AddComponent<Image>();
            background.color = new Color(0.16f, 0.15f, 0.18f, 0.92f);
            LayoutElement layout = root.AddComponent<LayoutElement>();
            layout.preferredHeight = 56f;
            layout.minHeight = 48f;
            LobbyPlayerItemView view = root.AddComponent<LobbyPlayerItemView>();

            Image ready = CreateImage("ReadyIndicator", root.transform, new Color(0.79f, 0.67f, 0.25f, 1f));
            Position(ready.rectTransform, new Vector2(0.03f, 0.2f), new Vector2(0.08f, 0.8f));

            TMP_Text nickname = CreateText("NicknameText", root.transform, font, "Player", 20, TextAlignmentOptions.Left);
            Position(nickname.rectTransform, new Vector2(0.11f, 0.18f), new Vector2(0.62f, 0.82f));

            TMP_Text status = CreateText("StatusText", root.transform, font, "Not Ready", 18, TextAlignmentOptions.Right);
            Position(status.rectTransform, new Vector2(0.66f, 0.18f), new Vector2(0.95f, 0.82f));

            SetPrivate(view, "nicknameText", nickname);
            SetPrivate(view, "statusText", status);
            SetPrivate(view, "readyIndicator", ready);
            SavePrefab(root, Path.Combine(PrefabFolder, "LobbyPlayerItem.prefab"));
        }

        private static void CreateCardCollectionItemPrefab(TMP_FontAsset font)
        {
            GameObject root = CreateRoot("CardCollectionItem");
            root.AddComponent<CanvasRenderer>();
            Image background = root.AddComponent<Image>();
            background.color = Color.white;
            root.AddComponent<Button>();
            root.AddComponent<CanvasGroup>();
            LayoutElement layout = root.AddComponent<LayoutElement>();
            layout.preferredHeight = 226f;
            layout.minHeight = 226f;
            layout.preferredWidth = 144f;
            layout.minWidth = 144f;
            CardCollectionItemView view = root.AddComponent<CardCollectionItemView>();

            Image art = CreateImage("Artwork", root.transform, new Color(0.25f, 0.22f, 0.2f, 0.45f));
            Position(art.rectTransform, new Vector2(0.11f, 0.50f), new Vector2(0.89f, 0.84f));

            TMP_Text title = CreateText("TitleText", root.transform, font, "Карта", 18, TextAlignmentOptions.Center);
            Position(title.rectTransform, new Vector2(0.09f, 0.36f), new Vector2(0.91f, 0.48f));

            TMP_Text rarity = CreateText("RarityText", root.transform, font, "Common", 15, TextAlignmentOptions.Left);
            Position(rarity.rectTransform, new Vector2(0.10f, 0.24f), new Vector2(0.58f, 0.31f));

            TMP_Text cost = CreateText("CostText", root.transform, font, "1", 15, TextAlignmentOptions.Right);
            Position(cost.rectTransform, new Vector2(0.62f, 0.24f), new Vector2(0.90f, 0.31f));

            TMP_Text deckState = CreateText("DeckStateText", root.transform, font, "РќРµ РІ РєРѕР»РѕРґРµ", 13, TextAlignmentOptions.Center);
            Position(deckState.rectTransform, new Vector2(0.10f, 0.10f), new Vector2(0.90f, 0.17f));

            TMP_Text lockText = CreateText("LockText", root.transform, font, "РќРµ РїРѕР»СѓС‡РµРЅР°", 14, TextAlignmentOptions.Center);
            Position(lockText.rectTransform, new Vector2(0.10f, 0.58f), new Vector2(0.90f, 0.70f));

            SetPrivate(view, "backgroundImage", background);
            SetPrivate(view, "artworkImage", art);
            SetPrivate(view, "titleText", title);
            SetPrivate(view, "rarityText", rarity);
            SetPrivate(view, "costText", cost);
            SetPrivate(view, "deckStateText", deckState);
            SetPrivate(view, "lockText", lockText);
            SavePrefab(root, Path.Combine(PrefabFolder, "CardCollectionItem.prefab"));
        }

        private static void CreateShopItemPrefab(TMP_FontAsset font)
        {
            GameObject root = CreateRoot("ShopItem");
            root.AddComponent<CanvasRenderer>();
            Image background = root.AddComponent<Image>();
            background.color = new Color(0.88f, 0.83f, 0.72f, 0.95f);
            root.AddComponent<Button>();
            LayoutElement layout = root.AddComponent<LayoutElement>();
            layout.preferredHeight = 140f;
            layout.minHeight = 120f;
            ShopItemView view = root.AddComponent<ShopItemView>();

            Image icon = CreateImage("Icon", root.transform, new Color(0.25f, 0.22f, 0.2f, 0.45f));
            Position(icon.rectTransform, new Vector2(0.03f, 0.14f), new Vector2(0.21f, 0.86f));

            TMP_Text title = CreateText("TitleText", root.transform, font, "Стартовый бустер", 20, TextAlignmentOptions.Left);
            Position(title.rectTransform, new Vector2(0.25f, 0.58f), new Vector2(0.72f, 0.86f));

            TMP_Text description = CreateText("DescriptionText", root.transform, font, "Описание набора.", 16, TextAlignmentOptions.TopLeft);
            Position(description.rectTransform, new Vector2(0.25f, 0.2f), new Vector2(0.8f, 0.56f));

            TMP_Text price = CreateText("PriceText", root.transform, font, "150 coins", 18, TextAlignmentOptions.Right);
            Position(price.rectTransform, new Vector2(0.74f, 0.58f), new Vector2(0.96f, 0.86f));

            SetPrivate(view, "iconImage", icon);
            SetPrivate(view, "titleText", title);
            SetPrivate(view, "descriptionText", description);
            SetPrivate(view, "priceText", price);
            SavePrefab(root, Path.Combine(PrefabFolder, "ShopItem.prefab"));
        }

        private static GameObject CreateRoot(string name)
        {
            GameObject root = new GameObject(name, typeof(RectTransform));
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(200f, 60f);
            return root;
        }

        private static TMP_Text CreateText(string name, Transform parent, TMP_FontAsset font, string value, int size, TextAlignmentOptions alignment)
        {
            GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);
            TMP_Text text = textObject.GetComponent<TMP_Text>();
            text.font = font;
            text.text = value;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = new Color(0.1f, 0.08f, 0.06f, 1f);
            text.enableWordWrapping = true;
            return text;
        }

        private static Image CreateImage(string name, Transform parent, Color color)
        {
            GameObject imageObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            imageObject.transform.SetParent(parent, false);
            Image image = imageObject.GetComponent<Image>();
            image.color = color;
            return image;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void Position(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void SavePrefab(GameObject root, string assetPath)
        {
            PrefabUtility.SaveAsPrefabAsset(root, assetPath);
            Object.DestroyImmediate(root);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string folder = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent))
                EnsureFolder(parent);

            AssetDatabase.CreateFolder(parent ?? "Assets", folder);
        }

        private static void SetPrivate(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            field?.SetValue(target, value);
        }
    }
}

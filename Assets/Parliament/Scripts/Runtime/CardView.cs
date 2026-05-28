using TMPro;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ParliamentGame
{
    public class CardView : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text costText;
        [SerializeField] private TMP_Text typeText;
        [SerializeField] private TMP_Text targetText;
        [SerializeField] private TMP_Text descriptionText;
        [SerializeField] private Image cardBackImage;
        [SerializeField] private Image artImage;
        [SerializeField] private GameObject artPlaceholder;
        [SerializeField] private Button button;

        private CardDefinition card;
        private UIManager uiManager;
        private RectTransform rectTransform;
        private Canvas rootCanvas;
        private CanvasGroup canvasGroup;
        private Image rootImage;
        private Transform originalParent;
        private int originalSiblingIndex;
        private bool suppressNextClick;
        private Vector2 dragPointerOffset;
        private Vector2 originalSize;
        private Vector2 originalAnchorMin;
        private Vector2 originalAnchorMax;
        private Vector2 originalPivot;
        private bool originalIgnoreLayout;
        private Canvas dragCanvas;
        private GraphicRaycaster dragRaycaster;
        private bool isDragging;
        private Vector2 lastPointerPosition;
        private float currentTilt;
        private float targetTilt;
        private float tiltVelocity;
        private Coroutine moveRoutine;
        private const float DragScale = 1.22f;
        private static readonly Vector2 DefaultCardSize = new Vector2(144f, 226f);
        public CardDefinition Card => card;
        public RectTransform RectTransform => rectTransform;
        public bool IsDragging => isDragging;

        private void Awake()
        {
            rectTransform = GetComponent<RectTransform>();
            rootImage = GetComponent<Image>();
            canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null)
                canvasGroup = gameObject.AddComponent<CanvasGroup>();
            EnsureArtViewportMask();
        }

        private void Update()
        {
            if (isDragging)
            {
                currentTilt = Mathf.SmoothDamp(currentTilt, targetTilt, ref tiltVelocity, 0.075f);
                rectTransform.localScale = Vector3.one * DragScale;
            }
            else
            {
                currentTilt = Mathf.SmoothDamp(currentTilt, 0f, ref tiltVelocity, 0.08f);
            }

            rectTransform.localRotation = Quaternion.Euler(0f, 0f, currentTilt);
        }

        public void Setup(CardDefinition cardDefinition, UIManager owner)
        {
            card = cardDefinition;
            uiManager = owner;
            rootCanvas = GetComponentInParent<Canvas>();

            titleText.text = card.name;
            costText.text = $"Стоимость: {card.cost} ОД";
            typeText.text = GetReadableTypeLabel(card.CardType);
            targetText.text = string.Empty;
            descriptionText.text = GetShortDescription(card.description);
            SetupCardFront(uiManager.GetCardFrontSprite());
            SetupArt(uiManager.GetCardArt(card));
            ConfigureCardLayout();
            ConfigureTextReadability();

            button.onClick.RemoveAllListeners();
            ConfigureFixedSize();
        }

        public void ConfigureFixedSize()
        {
            LayoutElement layoutElement = GetComponent<LayoutElement>();
            if (layoutElement != null)
            {
                layoutElement.minWidth = DefaultCardSize.x;
                layoutElement.preferredWidth = DefaultCardSize.x;
                layoutElement.flexibleWidth = 0f;
                layoutElement.minHeight = DefaultCardSize.y;
                layoutElement.preferredHeight = DefaultCardSize.y;
                layoutElement.flexibleHeight = 0f;
            }

            rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, DefaultCardSize.x);
            rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, DefaultCardSize.y);
        }

        private void SetupCardFront(Sprite sprite)
        {
            EnsureCardBackImage();
            if (cardBackImage == null)
                return;

            cardBackImage.sprite = sprite;
            cardBackImage.enabled = sprite != null;
            cardBackImage.preserveAspect = false;
            cardBackImage.type = Image.Type.Simple;

            RectTransform backgroundRect = cardBackImage.rectTransform;
            backgroundRect.anchorMin = Vector2.zero;
            backgroundRect.anchorMax = Vector2.one;
            backgroundRect.offsetMin = Vector2.zero;
            backgroundRect.offsetMax = Vector2.zero;
            cardBackImage.color = CardRarityVisuals.TintBackground(Color.white, card == null ? null : card.rarity);

            if (rootImage != null)
            {
                Color baseColor = sprite == null ? new Color(0.90f, 0.88f, 0.80f, 1f) : Color.clear;
                rootImage.color = sprite == null
                    ? CardRarityVisuals.TintBackground(baseColor, card == null ? null : card.rarity)
                    : baseColor;
            }

            DisableOldArtFrame();
        }

        private void DisableOldArtFrame()
        {
            if (artImage == null || artImage.transform.parent == null)
                return;

            Image frameImage = artImage.transform.parent.GetComponent<Image>();
            if (frameImage != null)
            {
                frameImage.enabled = true;
                frameImage.color = CardRarityVisuals.TintBackground(new Color(0.10f, 0.075f, 0.045f, 0.72f), card == null ? null : card.rarity);
            }
        }

        private void ConfigureTextReadability()
        {
            RemoveLegacyTextBackings();

            ConfigureReadableText(titleText, 0.06f);
            ConfigureReadableText(costText, 0.10f);
            ConfigureReadableText(typeText, 0.10f);
            ConfigureReadableText(descriptionText, 0.08f);

            if (artImage != null)
                artImage.color = CardRarityVisuals.TintArtwork(new Color(0.92f, 0.86f, 0.74f, 1f), card == null ? null : card.rarity);
        }

        private void ConfigureCardLayout()
        {
            VerticalLayoutGroup layout = GetComponent<VerticalLayoutGroup>();
            if (layout != null)
            {
                layout.padding = new RectOffset(8, 8, 8, 7);
                layout.spacing = 3;
                layout.childControlHeight = true;
                layout.childControlWidth = true;
                layout.childForceExpandHeight = false;
                layout.childForceExpandWidth = true;
            }

            Transform artFrame = artImage == null ? null : artImage.transform.parent;
            if (artFrame != null)
                artFrame.SetSiblingIndex(1);

            if (titleText != null)
                titleText.transform.SetSiblingIndex(2);

            if (costText != null)
                costText.transform.SetSiblingIndex(3);

            if (typeText != null)
                typeText.transform.SetSiblingIndex(4);

            if (targetText != null)
                targetText.transform.SetSiblingIndex(5);

            if (descriptionText != null)
                descriptionText.transform.SetSiblingIndex(6);

            ConfigureCardBlock(artFrame as RectTransform, 128f, 78f);
            ConfigureCardBlock(titleText == null ? null : titleText.rectTransform, 128f, 32f);
            ConfigureCardBlock(costText == null ? null : costText.rectTransform, 128f, 18f);
            ConfigureCardBlock(typeText == null ? null : typeText.rectTransform, 128f, 0f);
            ConfigureCardBlock(targetText == null ? null : targetText.rectTransform, 128f, 0f);
            ConfigureCardBlock(descriptionText == null ? null : descriptionText.rectTransform, 128f, 72f);

            ConfigureTextBounds(titleText, true, TextAlignmentOptions.Center, 8f, 15f);
            ConfigureTextBounds(costText, false, TextAlignmentOptions.Center, 7f, 12f);
            ConfigureTextBounds(typeText, false, TextAlignmentOptions.Center, 1f, 1f);
            ConfigureTextBounds(targetText, false, TextAlignmentOptions.Center, 1f, 1f);
            ConfigureTextBounds(descriptionText, true, TextAlignmentOptions.Top, 7f, 11f);
        }

        private void ConfigureCardBlock(RectTransform rect, float width, float height)
        {
            if (rect == null)
                return;

            rect.sizeDelta = new Vector2(width, height);
            LayoutElement layout = rect.GetComponent<LayoutElement>();
            if (layout == null)
                layout = rect.gameObject.AddComponent<LayoutElement>();

            layout.minWidth = width;
            layout.preferredWidth = width;
            layout.flexibleWidth = 0f;
            layout.minHeight = height;
            layout.preferredHeight = height;
            layout.flexibleHeight = 0f;
        }

        private void ConfigureTextBounds(TMP_Text text, bool wrap, TextAlignmentOptions alignment, float minSize, float maxSize)
        {
            if (text == null)
                return;

            text.enableWordWrapping = wrap;
            text.overflowMode = TextOverflowModes.Truncate;
            text.alignment = alignment;
            text.enableAutoSizing = true;
            text.fontSizeMin = minSize;
            text.fontSizeMax = maxSize;
        }

        private void ConfigureReadableText(TMP_Text text, float darkness)
        {
            if (text == null)
                return;

            Color baseColor = new Color(darkness, Mathf.Max(0f, darkness - 0.015f), Mathf.Max(0f, darkness - 0.025f), 1f);
            text.color = CardRarityVisuals.TintText(baseColor, card == null ? null : card.rarity);
            text.fontStyle |= FontStyles.Bold;
            text.enableAutoSizing = true;
        }

        private void RemoveLegacyTextBackings()
        {
            RemoveLegacyTextBacking(titleText);
            RemoveLegacyTextBacking(costText);
            RemoveLegacyTextBacking(typeText);
            RemoveLegacyTextBacking(descriptionText);
        }

        private void RemoveLegacyTextBacking(TMP_Text text)
        {
            if (text == null || text.transform.parent == null)
                return;

            Transform backing = text.transform.parent.Find(text.name + "Backing");
            if (backing == null)
                return;

            Destroy(backing.gameObject);
        }

        private void EnsureCardBackImage()
        {
            if (cardBackImage != null)
                return;

            GameObject background = new GameObject("CardBackImage");
            background.transform.SetParent(transform, false);
            background.transform.SetAsFirstSibling();

            RectTransform backgroundRect = background.AddComponent<RectTransform>();
            backgroundRect.anchorMin = Vector2.zero;
            backgroundRect.anchorMax = Vector2.one;
            backgroundRect.offsetMin = Vector2.zero;
            backgroundRect.offsetMax = Vector2.zero;

            LayoutElement backgroundLayout = background.AddComponent<LayoutElement>();
            backgroundLayout.ignoreLayout = true;

            cardBackImage = background.AddComponent<Image>();
            cardBackImage.raycastTarget = false;
            cardBackImage.enabled = false;
        }

        // Показывает иллюстрацию карты или оставляет нейтральное место под будущую картинку.
        private void SetupArt(Sprite sprite)
        {
            if (artImage == null)
                return;

            EnsureArtViewportMask();
            artImage.sprite = sprite;
            artImage.enabled = sprite != null;
            artImage.preserveAspect = true;

            if (artPlaceholder != null)
                artPlaceholder.SetActive(sprite == null);
        }

        private void EnsureArtViewportMask()
        {
            if (artImage == null || artImage.transform.parent == null)
                return;

            RectMask2D mask = artImage.transform.parent.GetComponent<RectMask2D>();
            if (mask == null)
                mask = artImage.transform.parent.gameObject.AddComponent<RectMask2D>();

            mask.enabled = true;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (suppressNextClick)
            {
                suppressNextClick = false;
                return;
            }

            uiManager.OnPlayerCardClicked(this, RectTransformUtility.WorldToScreenPoint(GetEventCamera(), rectTransform.position));
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            suppressNextClick = true;
            isDragging = true;
            StopMoveRoutine();
            currentTilt = 0f;
            targetTilt = 0f;
            tiltVelocity = 0f;
            lastPointerPosition = eventData.position;
            originalParent = transform.parent;
            originalSiblingIndex = transform.GetSiblingIndex();
            Canvas.ForceUpdateCanvases();
            originalSize = GetStableCardSize();
            originalAnchorMin = rectTransform.anchorMin;
            originalAnchorMax = rectTransform.anchorMax;
            originalPivot = rectTransform.pivot;

            if (rootCanvas == null)
                rootCanvas = GetComponentInParent<Canvas>();

            LayoutElement layoutElement = GetComponent<LayoutElement>();
            if (layoutElement != null)
            {
                originalIgnoreLayout = layoutElement.ignoreLayout;
                layoutElement.ignoreLayout = true;
            }

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rectTransform,
                eventData.position,
                GetEventCamera(),
                out dragPointerOffset);

            transform.SetParent(rootCanvas.transform, true);
            transform.SetAsLastSibling();
            EnableDragSorting();
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, originalSize.x);
            rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, originalSize.y);
            LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);

            canvasGroup.blocksRaycasts = false;
            canvasGroup.alpha = 0.92f;
            rectTransform.localScale = Vector3.one * DragScale;
            uiManager.ReflowHand(true);
            MoveToPointer(eventData);
            uiManager.PreviewCardInsertion(this, eventData.position, rootCanvas);
        }

        public void OnDrag(PointerEventData eventData)
        {
            float horizontalDelta = eventData.position.x - lastPointerPosition.x;
            targetTilt = Mathf.Clamp(-horizontalDelta * 1.55f, -45f, 45f);
            lastPointerPosition = eventData.position;
            MoveToPointer(eventData);
            uiManager.PreviewCardInsertion(this, eventData.position, rootCanvas);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            canvasGroup.blocksRaycasts = true;
            canvasGroup.alpha = 1f;

            if (uiManager.IsPointerOverPlayArea(eventData.position, rootCanvas))
            {
                Vector2 cardScreenPosition = RectTransformUtility.WorldToScreenPoint(GetEventCamera(), rectTransform.position);
                bool played = uiManager.OnPlayerCardDropped(this, cardScreenPosition);
                if (played)
                {
                    isDragging = false;
                    return;
                }
            }

            ReturnToHand(eventData.position);
        }

        private void MoveToPointer(PointerEventData eventData)
        {
            RectTransform canvasRect = rootCanvas.transform as RectTransform;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, eventData.position, GetEventCamera(), out Vector2 localPoint))
                rectTransform.anchoredPosition = localPoint - dragPointerOffset;
        }

        private void ReturnToHand(Vector2 screenPosition)
        {
            uiManager.ReturnCardToHand(this, screenPosition, rootCanvas);
        }

        public void RestoreToHandParent(Transform handParent, int siblingIndex)
        {
            transform.SetParent(handParent, false);
            transform.SetSiblingIndex(siblingIndex);

            LayoutElement layoutElement = GetComponent<LayoutElement>();
            if (layoutElement != null)
                layoutElement.ignoreLayout = originalIgnoreLayout;

            rectTransform.anchorMin = originalAnchorMin;
            rectTransform.anchorMax = originalAnchorMax;
            rectTransform.pivot = originalPivot;
            rectTransform.localScale = Vector3.one;
            rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, originalSize.x);
            rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, originalSize.y);
            isDragging = false;
            targetTilt = 0f;
            tiltVelocity = 0f;
            ConfigureFixedSize();
            DisableDragSorting();
        }

        public void AnimateToLocalPosition(Vector2 targetPosition, float seconds)
        {
            StopMoveRoutine();
            moveRoutine = StartCoroutine(MoveToLocalPosition(targetPosition, seconds));
        }

        public void SnapToLocalPosition(Vector2 targetPosition)
        {
            StopMoveRoutine();
            rectTransform.anchoredPosition = targetPosition;
            rectTransform.localScale = Vector3.one;
            rectTransform.localRotation = Quaternion.identity;
        }

        private IEnumerator MoveToLocalPosition(Vector2 targetPosition, float seconds)
        {
            Vector2 startPosition = rectTransform.anchoredPosition;
            Quaternion startRotation = rectTransform.localRotation;
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / seconds));
                rectTransform.anchoredPosition = Vector2.Lerp(startPosition, targetPosition, t);
                rectTransform.localRotation = Quaternion.Slerp(startRotation, Quaternion.identity, t);
                rectTransform.localScale = Vector3.one;
                yield return null;
            }

            rectTransform.anchoredPosition = targetPosition;
            rectTransform.localRotation = Quaternion.identity;
            rectTransform.localScale = Vector3.one;
            moveRoutine = null;
        }

        private void StopMoveRoutine()
        {
            if (moveRoutine != null)
            {
                StopCoroutine(moveRoutine);
                moveRoutine = null;
            }
        }

        private Vector2 GetStableCardSize()
        {
            LayoutElement layoutElement = GetComponent<LayoutElement>();
            if (layoutElement != null && layoutElement.preferredWidth > 1f && layoutElement.preferredHeight > 1f)
                return new Vector2(layoutElement.preferredWidth, layoutElement.preferredHeight);

            Vector2 rectSize = rectTransform.rect.size;
            if (rectSize.x > 1f && rectSize.y > 1f)
                return rectSize;

            Vector2 sizeDelta = rectTransform.sizeDelta;
            if (sizeDelta.x > 1f && sizeDelta.y > 1f)
                return sizeDelta;

            return DefaultCardSize;
        }

        // Готовит текущий объект карты к перелету в центр, отключая взаимодействие и layout.
        public void PrepareForPlayAnimation()
        {
            isDragging = false;
            targetTilt = 0f;
            currentTilt = 0f;
            tiltVelocity = 0f;
            rectTransform.localRotation = Quaternion.identity;
            rectTransform.localScale = Vector3.one;
            StopMoveRoutine();

            LayoutElement layoutElement = GetComponent<LayoutElement>();
            if (layoutElement != null)
                layoutElement.ignoreLayout = true;

            canvasGroup.blocksRaycasts = false;
            canvasGroup.alpha = 1f;
            canvasGroup.ignoreParentGroups = true;
            EnableDragSorting();
        }

        private void EnableDragSorting()
        {
            if (dragCanvas == null)
                dragCanvas = gameObject.AddComponent<Canvas>();

            dragCanvas.overrideSorting = true;
            dragCanvas.sortingOrder = 1000;

            if (dragRaycaster == null)
                dragRaycaster = gameObject.AddComponent<GraphicRaycaster>();
        }

        private void DisableDragSorting()
        {
            if (dragRaycaster != null)
            {
                Destroy(dragRaycaster);
                dragRaycaster = null;
            }

            if (dragCanvas != null)
            {
                dragCanvas.overrideSorting = false;
                dragCanvas.sortingOrder = 0;
                Destroy(dragCanvas);
                dragCanvas = null;
            }
        }

        private Camera GetEventCamera()
        {
            if (rootCanvas == null || rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
                return null;

            return rootCanvas.worldCamera;
        }

        private string GetReadableTypeLabel(CardType cardType)
        {
            switch (cardType)
            {
                case CardType.Attack:
                    return "Атака";
                case CardType.Defense:
                    return "Защита";
                case CardType.Influence:
                    return "Влияние";
                case CardType.Neutral:
                    return "Нейтралы";
                case CardType.Politics:
                    return "Политика";
                case CardType.Vote:
                    return "Голосование";
                case CardType.Boost:
                    return "Буст";
                default:
                    return cardType.ToString();
            }
        }

        private string GetTypeLabel(CardType cardType)
        {
            switch (cardType)
            {
                case CardType.Attack:
                    return "Атака";
                case CardType.Defense:
                    return "Защита";
                case CardType.Influence:
                    return "Влияние";
                case CardType.Neutral:
                    return "Нейтралы";
                case CardType.Politics:
                    return "Политика";
                case CardType.Vote:
                    return "Голосование";
                case CardType.Boost:
                    return "Буст";
                default:
                    return cardType.ToString();
            }
        }

        private string GetShortDescription(string description)
        {
            if (string.IsNullOrWhiteSpace(description))
                return string.Empty;

            return description.Length <= 58 ? description : description.Substring(0, 55) + "...";
        }
    }
}

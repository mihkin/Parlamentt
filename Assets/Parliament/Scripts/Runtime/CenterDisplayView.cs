using System.Collections;
using TMPro;
using UnityEngine;

namespace ParliamentGame
{
    // Центральная зона разыгровки: слева показывает карту, справа текст эффекта.
    public class CenterDisplayView : MonoBehaviour
    {
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text bodyText;
        [SerializeField] private RectTransform cardSlot;
        [SerializeField] private RectTransform infoPanel;
        [SerializeField] private CanvasGroup infoCanvasGroup;
        [SerializeField] private float showSeconds = 3.6f;
        [SerializeField] private float flySeconds = 0.42f;
        [SerializeField] private float slideSeconds = 0.28f;
        [SerializeField] private float fadeSeconds = 0.38f;

        private Coroutine routine;
        private float infoPanelWidth;
        private CardView activeCardView;

        private void Awake()
        {
            RememberInfoPanelSize();
            ConfigureTextForPanel();
            HideImmediate();
        }

        public void ShowCard(CardDefinition card, ParticipantState actor, ParticipantState target, CardView playedCardView, Vector2? startScreenPosition, Canvas canvas)
        {
            string targetName = target == null ? "нет" : target.displayName;
            ShowCard(
                card,
                playedCardView,
                $"{actor.displayName} сыграл: {card.name}",
                $"Цель: {targetName}\nЭффект: {card.description}",
                startScreenPosition,
                canvas);
        }

        public void Show(string title, string body)
        {
            Show(title, body, showSeconds);
        }

        public void Show(string title, string body, float seconds)
        {
            if (routine != null)
            {
                StopCoroutine(routine);
                CleanupActivePresentation();
            }

            routine = StartCoroutine(ShowRoutine(null, null, title, body, seconds, null, null));
        }

        private void ShowCard(CardDefinition card, CardView playedCardView, string title, string body, Vector2? startScreenPosition, Canvas canvas)
        {
            if (routine != null)
            {
                StopCoroutine(routine);
                CleanupActivePresentation();
            }

            routine = StartCoroutine(ShowRoutine(card, playedCardView, title, body, showSeconds, startScreenPosition, canvas));
        }

        private IEnumerator ShowRoutine(CardDefinition card, CardView playedCardView, string title, string body, float seconds, Vector2? startScreenPosition, Canvas canvas)
        {
            RememberInfoPanelSize();
            titleText.text = title;
            bodyText.text = body;
            PrepareInfoPanel(false);

            canvasGroup.alpha = 1f;
            canvasGroup.blocksRaycasts = true;
            canvasGroup.interactable = false;

            if (card != null && playedCardView != null)
                yield return FlyActualCard(playedCardView, startScreenPosition, canvas);

            yield return SlideInfoPanel();

            yield return new WaitForSeconds(seconds);
            yield return FadeOut();
            DestroyActiveCard();
            canvasGroup.blocksRaycasts = false;
            routine = null;
        }

        private IEnumerator FlyActualCard(CardView cardView, Vector2? startScreenPosition, Canvas canvas)
        {
            if (cardSlot == null || canvas == null)
                yield break;

            activeCardView = cardView;
            cardView.PrepareForPlayAnimation();

            RectTransform cardTransform = cardView.RectTransform;
            RectTransform panelRect = transform as RectTransform;
            Camera eventCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            Vector2 startLocal = cardTransform.anchoredPosition;

            if (startScreenPosition.HasValue)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(panelRect, startScreenPosition.Value, eventCamera, out startLocal);
            }

            Vector2 originalSize = cardTransform.rect.size;
            cardTransform.SetParent(transform, false);
            cardTransform.anchorMin = new Vector2(0.5f, 0.5f);
            cardTransform.anchorMax = new Vector2(0.5f, 0.5f);
            cardTransform.pivot = new Vector2(0.5f, 0.5f);
            cardTransform.sizeDelta = originalSize;
            cardTransform.anchoredPosition = startLocal;
            cardTransform.localScale = Vector3.one;
            cardTransform.localRotation = Quaternion.identity;
            cardTransform.SetAsLastSibling();

            Vector2 endLocal = cardSlot.anchoredPosition;

            float elapsed = 0f;
            while (elapsed < flySeconds)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / flySeconds);
                float eased = Mathf.SmoothStep(0f, 1f, t);
                cardTransform.anchoredPosition = Vector2.Lerp(startLocal, endLocal, eased);
                yield return null;
            }

            cardTransform.anchoredPosition = endLocal;
            cardTransform.localScale = Vector3.one;
        }

        private IEnumerator SlideInfoPanel()
        {
            if (infoPanel == null)
                yield break;

            PrepareInfoPanel(true);
            float elapsed = 0f;
            while (elapsed < slideSeconds)
            {
                elapsed += Time.deltaTime;
                float eased = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / slideSeconds));
                infoPanel.sizeDelta = new Vector2(infoPanelWidth * eased, infoPanel.sizeDelta.y);
                if (infoCanvasGroup != null)
                    infoCanvasGroup.alpha = eased;
                yield return null;
            }

            infoPanel.sizeDelta = new Vector2(infoPanelWidth, infoPanel.sizeDelta.y);
            if (infoCanvasGroup != null)
                infoCanvasGroup.alpha = 1f;
        }

        private IEnumerator FadeOut()
        {
            float elapsed = 0f;
            while (elapsed < fadeSeconds)
            {
                elapsed += Time.deltaTime;
                canvasGroup.alpha = Mathf.Lerp(1f, 0f, Mathf.Clamp01(elapsed / fadeSeconds));
                yield return null;
            }

            canvasGroup.alpha = 0f;
        }

        private void HideImmediate()
        {
            if (canvasGroup == null)
                canvasGroup = GetComponent<CanvasGroup>();

            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
            PrepareInfoPanel(false);
        }

        private void RememberInfoPanelSize()
        {
            if (infoPanel == null || infoPanelWidth > 0f)
                return;

            infoPanelWidth = infoPanel.sizeDelta.x <= 0f ? 360f : infoPanel.sizeDelta.x;
        }

        private void ConfigureTextForPanel()
        {
            if (titleText != null)
            {
                titleText.enableWordWrapping = true;
                titleText.overflowMode = TextOverflowModes.Truncate;
                titleText.enableAutoSizing = true;
                titleText.fontSizeMin = 14f;
                titleText.fontSizeMax = Mathf.Min(titleText.fontSize, 23f);
            }

            if (bodyText != null)
            {
                bodyText.enableWordWrapping = true;
                bodyText.overflowMode = TextOverflowModes.Truncate;
                bodyText.enableAutoSizing = true;
                bodyText.fontSizeMin = 11f;
                bodyText.fontSizeMax = Mathf.Min(bodyText.fontSize, 18f);
            }
        }

        private void PrepareInfoPanel(bool visible)
        {
            if (infoPanel == null)
                return;

            infoPanel.gameObject.SetActive(visible);
            infoPanel.sizeDelta = new Vector2(visible ? 0f : infoPanelWidth, infoPanel.sizeDelta.y);
            if (infoCanvasGroup != null)
                infoCanvasGroup.alpha = visible ? 0f : 1f;
        }

        private void DestroyActiveCard()
        {
            if (activeCardView == null)
                return;

            Destroy(activeCardView.gameObject);
            activeCardView = null;
        }

        private void CleanupActivePresentation()
        {
            DestroyActiveCard();
            PrepareInfoPanel(false);
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
        }
    }
}

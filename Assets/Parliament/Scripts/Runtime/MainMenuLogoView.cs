using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ParliamentGame
{
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class MainMenuLogoView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private Image logoImage;
        [SerializeField] private float fadeInSeconds = 0.8f;
        [SerializeField] private float glowStrength = 0.12f;
        [SerializeField] private float glowSpeed = 2f;
        [SerializeField] private float hoverScale = 1.05f;
        [SerializeField] private Shadow titleShadow;
        [SerializeField] private Shadow logoShadow;

        private CanvasGroup canvasGroup;
        private Vector3 targetScale = Vector3.one;

        private void Awake()
        {
            canvasGroup = GetComponent<CanvasGroup>();
            canvasGroup.alpha = 0f;
        }

        private void Update()
        {
            canvasGroup.alpha = Mathf.MoveTowards(canvasGroup.alpha, 1f, Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeInSeconds));
            transform.localScale = Vector3.Lerp(transform.localScale, targetScale, Time.unscaledDeltaTime * 10f);

            float glow = 1f + Mathf.Sin(Time.unscaledTime * glowSpeed) * glowStrength;
            ApplyGlow(titleShadow, glow);
            ApplyGlow(logoShadow, glow);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            targetScale = Vector3.one * hoverScale;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            targetScale = Vector3.one;
        }

        private static void ApplyGlow(Shadow shadow, float glow)
        {
            if (shadow == null)
                return;

            Color color = shadow.effectColor;
            color.a = Mathf.Clamp01(glow * 0.75f);
            shadow.effectColor = color;
        }
    }
}

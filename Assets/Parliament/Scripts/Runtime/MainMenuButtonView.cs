using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ParliamentGame
{
    [RequireComponent(typeof(Button))]
    public sealed class MainMenuButtonView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        [SerializeField] private Image backgroundImage;
        [SerializeField] private TMP_Text labelText;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip hoverClip;
        [SerializeField] private AudioClip clickClip;
        [SerializeField] private Vector3 defaultScale = Vector3.one;
        [SerializeField] private Vector3 hoverScale = new Vector3(1.04f, 1.04f, 1f);
        [SerializeField] private Vector3 pressedScale = new Vector3(0.98f, 0.98f, 1f);
        [SerializeField] private float scaleLerpSpeed = 12f;

        private Button button;
        private Vector3 targetScale;

        private void Awake()
        {
            button = GetComponent<Button>();
            if (backgroundImage == null)
                backgroundImage = GetComponent<Image>();

            targetScale = defaultScale;
        }

        private void Update()
        {
            transform.localScale = Vector3.Lerp(transform.localScale, targetScale, Time.unscaledDeltaTime * scaleLerpSpeed);
        }

        /// <summary>
        /// Настраивает заголовок и callback кнопки.
        /// </summary>
        public void Setup(string label, Action onClick)
        {
            if (labelText != null)
                labelText.text = label;

            if (button == null)
                button = GetComponent<Button>();

            button.onClick.RemoveAllListeners();
            if (onClick != null)
                button.onClick.AddListener(() => onClick.Invoke());
        }

        /// <summary>
        /// Назначает спрайт фона для кнопки через инспектор или рантайм-инициализацию.
        /// </summary>
        public void SetBackgroundSprite(Sprite sprite)
        {
            if (backgroundImage != null)
            {
                backgroundImage.sprite = sprite;
                backgroundImage.type = sprite == null ? Image.Type.Simple : Image.Type.Sliced;
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (button != null && !button.interactable)
                return;

            targetScale = hoverScale;
            PlayClip(hoverClip);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            targetScale = defaultScale;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (button != null && !button.interactable)
                return;

            targetScale = pressedScale;
            PlayClip(clickClip);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            targetScale = hoverScale;
        }

        private void PlayClip(AudioClip clip)
        {
            if (audioSource != null && clip != null)
                audioSource.PlayOneShot(clip);
        }
    }
}

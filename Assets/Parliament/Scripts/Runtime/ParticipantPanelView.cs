using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ParliamentGame
{
    public class ParticipantPanelView : MonoBehaviour
    {
        [SerializeField] private Sprite participantPanelSprite;
        [SerializeField] private Sprite participantTextPlateSprite;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text politicalPointsText;
        [SerializeField] private TMP_Text influenceText;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private TMP_Text cardsText;
        [SerializeField] private TMP_Text helpText;
        [SerializeField] private Slider turnTimerSlider;
        [SerializeField] private Image turnTimerFill;

        private void Awake()
        {
            participantPanelSprite = ResolveUiPlateSprite(participantPanelSprite, "participant_panel_plate");
            participantTextPlateSprite = ResolveUiPlateSprite(participantTextPlateSprite, "participant_text_plate");
            EnsureVisualPlates();
        }

        public void Setup(ParticipantState participant, bool showHelp)
        {
            nameText.text = participant.displayName;
            politicalPointsText.text = $"ПО: {participant.politicalPoints}";
            influenceText.text = $"Сторонники: {participant.influence}%";
            statusText.text = $"Статус: {GetStatusText(participant)}";
            cardsText.text = $"Карт: {participant.CardCount}";

            if (helpText != null)
            {
                helpText.gameObject.SetActive(showHelp);
                helpText.text =
                    "ПО — ресурс для добора, голосований, нейтралов и событий\n" +
                    "ОД — очки действия на розыгрыш карт\n" +
                    "Сторонники — процент поддержки партии";
            }
        }

        public void SetActionPoints(bool active, int remaining, int maximum, int cardCount)
        {
            if (cardsText == null)
                return;

            cardsText.text = active ? $"Карт: {cardCount} / ОД: {remaining}/{maximum}" : $"Карт: {cardCount}";
        }

        public void SetTurnTimer(bool active, float timeLeft, float duration)
        {
            if (turnTimerSlider == null)
                return;

            turnTimerSlider.gameObject.SetActive(active);
            if (!active)
                return;

            float normalized = duration <= 0f ? 0f : Mathf.Clamp01(timeLeft / duration);
            turnTimerSlider.value = normalized;

            if (turnTimerFill != null)
                turnTimerFill.color = GetTimerColor(timeLeft, duration);
        }

        private Color GetTimerColor(float timeLeft, float duration)
        {
            Color green = new Color(0.12f, 0.72f, 0.26f, 1f);
            Color yellow = new Color(0.95f, 0.78f, 0.14f, 1f);
            Color red = new Color(0.82f, 0.12f, 0.10f, 1f);

            if (timeLeft > 30f)
            {
                float longPhase = Mathf.Max(0.01f, duration - 30f);
                float greenToYellow = Mathf.Clamp01((duration - timeLeft) / longPhase);
                return Color.Lerp(green, yellow, greenToYellow);
            }

            float redToYellow = Mathf.Clamp01(timeLeft / 30f);
            return Color.Lerp(red, yellow, redToYellow);
        }

        private string GetStatusText(ParticipantState participant)
        {
            string status;
            switch (participant.status)
            {
                case ParticipantStatus.Protected:
                    status = "защищен";
                    break;
                case ParticipantStatus.SkipsTurn:
                    status = "пропуск хода";
                    break;
                case ParticipantStatus.Excluded:
                    status = "исключен";
                    break;
                default:
                    status = "активен";
                    break;
            }

            if (participant.poisonInfluenceRounds > 0)
                status += $" / яд сторонников {participant.poisonInfluenceRounds}";

            if (participant.poisonPoliticalPointsRounds > 0)
                status += $" / яд ПО {participant.poisonPoliticalPointsRounds}";

            return status;
        }

        private void EnsureVisualPlates()
        {
            if (TryGetComponent(out Image panelImage) && participantPanelSprite != null)
            {
                panelImage.sprite = participantPanelSprite;
                panelImage.type = Image.Type.Sliced;
            }

            EnsureTextPlate("NamePlate", nameText, new Vector2(-10f, -4f), new Vector2(10f, 4f), 0.74f);
            EnsureTextPlate("PointsPlate", politicalPointsText, new Vector2(-8f, -3f), new Vector2(8f, 3f), 0.60f);
            EnsureTextPlate("InfluencePlate", influenceText, new Vector2(-8f, -3f), new Vector2(8f, 3f), 0.60f);
            EnsureTextPlate("StatusPlate", statusText, new Vector2(-8f, -3f), new Vector2(8f, 3f), 0.54f);
            EnsureTextPlate("CardsPlate", cardsText, new Vector2(-8f, -3f), new Vector2(8f, 3f), 0.60f);
        }

        private void EnsureTextPlate(string objectName, TMP_Text text, Vector2 offsetMinDelta, Vector2 offsetMaxDelta, float alpha)
        {
            if (text == null)
                return;

            RectTransform textRect = text.rectTransform;
            Transform parent = textRect.parent;
            if (parent == null)
                return;

            RectTransform plateRect = parent.Find(objectName) as RectTransform;
            if (plateRect == null)
            {
                GameObject plateObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                plateObject.transform.SetParent(parent, false);
                plateRect = plateObject.GetComponent<RectTransform>();
            }

            plateRect.anchorMin = textRect.anchorMin;
            plateRect.anchorMax = textRect.anchorMax;
            plateRect.pivot = textRect.pivot;
            plateRect.anchoredPosition = textRect.anchoredPosition;
            plateRect.sizeDelta = textRect.sizeDelta;
            plateRect.offsetMin = textRect.offsetMin + offsetMinDelta;
            plateRect.offsetMax = textRect.offsetMax + offsetMaxDelta;
            plateRect.localScale = Vector3.one;
            plateRect.SetSiblingIndex(textRect.GetSiblingIndex());

            Image plateImage = plateRect.GetComponent<Image>();
            if (plateImage != null)
            {
                plateImage.sprite = participantTextPlateSprite != null ? participantTextPlateSprite : participantPanelSprite;
                plateImage.type = plateImage.sprite == null ? Image.Type.Simple : Image.Type.Sliced;
                plateImage.color = new Color(0.99f, 0.96f, 0.90f, alpha);
                plateImage.raycastTarget = false;
            }

            textRect.SetSiblingIndex(plateRect.GetSiblingIndex() + 1);
            text.fontStyle = FontStyles.Bold;
            text.color = new Color(0.08f, 0.05f, 0.02f, 1f);
        }

        private static Sprite ResolveUiPlateSprite(Sprite currentSprite, string spriteName)
        {
            return currentSprite != null ? currentSprite : UiPlateLibrary.Get(spriteName);
        }
    }
}

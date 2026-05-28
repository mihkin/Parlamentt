using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ParliamentGame
{
    public class ResultWindowView : MonoBehaviour
    {
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text reasonText;
        [SerializeField] private Button restartButton;
        [SerializeField] private string mainMenuSceneName = "MainMenuScene";

        private GameManager gameManager;

        private void Awake()
        {
            Hide();
        }

        public void Setup(GameManager manager)
        {
            gameManager = manager;
            restartButton.onClick.RemoveAllListeners();
            restartButton.onClick.AddListener(ExitToMenu);

            TMP_Text buttonText = restartButton.GetComponentInChildren<TMP_Text>(true);
            if (buttonText != null)
                buttonText.text = "Выйти в меню";
        }

        public void Show(GameResult result, string reason)
        {
            titleText.text = result == GameResult.Victory ? "Победа" : "Поражение";
            reasonText.text = reason;
            canvasGroup.alpha = 1f;
            canvasGroup.blocksRaycasts = true;
            canvasGroup.interactable = true;
        }

        public void Hide()
        {
            if (canvasGroup == null)
                canvasGroup = GetComponent<CanvasGroup>();

            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }

        private void ExitToMenu()
        {
            Hide();
            gameManager?.SetPaused(false);
            FindObjectOfType<LanMatchCoordinator>()?.EndMatch();
            LeaveOnlineLobbyIfActive();

            if (!string.IsNullOrWhiteSpace(mainMenuSceneName))
                SceneManager.LoadScene(mainMenuSceneName);
        }

        private static void LeaveOnlineLobbyIfActive()
        {
            OnlineLobbyManager lobbyManager = FindObjectOfType<OnlineLobbyManager>();
            if (lobbyManager != null && lobbyManager.CurrentRoom != null)
                lobbyManager.LeaveLobby();
        }
    }
}

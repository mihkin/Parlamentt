using UnityEngine;
using UnityEngine.SceneManagement;

namespace ParliamentGame
{
    public sealed class LanGameSceneBootstrap : MonoBehaviour
    {
        [SerializeField] private GameManager gameManager;
        [SerializeField] private string mainMenuSceneName = "MainMenuScene";
        [SerializeField] private float attachTimeoutSeconds = 1f;

        private string fatalError = string.Empty;

        private System.Collections.IEnumerator Start()
        {
            float deadline = Time.unscaledTime + Mathf.Max(0.1f, attachTimeoutSeconds);
            while (Time.unscaledTime < deadline)
            {
                if (gameManager == null)
                    gameManager = FindObjectOfType<GameManager>();

                LanMatchCoordinator coordinator = LanMatchCoordinator.Instance;
                if (coordinator != null && coordinator.TryRestoreMatchContext() && gameManager != null)
                {
                    fatalError = string.Empty;
                    coordinator.RegisterGameManager(gameManager);
                    yield break;
                }

                yield return null;
            }

            LanMatchCoordinator failedCoordinator = LanMatchCoordinator.Instance;
            fatalError = $"LAN scene bootstrap failed. Coordinator: {(failedCoordinator == null ? "missing" : "ready=" + failedCoordinator.HasActiveMatch)}, GameManager: {(gameManager == null ? "missing" : "ok")}.";
            LanMatchCoordinator.SetFatalMessage(fatalError);
            Debug.LogError($"{fatalError} Trace: {LanMatchCoordinator.TraceFilePath}");
        }

        private void OnGUI()
        {
            string message = string.IsNullOrWhiteSpace(fatalError) ? LanMatchCoordinator.LastFatalMessage : fatalError;
            if (string.IsNullOrWhiteSpace(message))
                return;

            GUI.color = new Color(0f, 0f, 0f, 0.85f);
            GUI.Box(new Rect(20f, 20f, Mathf.Min(Screen.width - 40f, 860f), 150f), string.Empty);
            GUI.color = Color.white;
            GUI.Label(new Rect(36f, 36f, Mathf.Min(Screen.width - 72f, 824f), 120f),
                $"{message}\n\nLog: {LanMatchCoordinator.TraceFilePath}\nНажмите Esc, чтобы вернуться в меню.");

            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape && !string.IsNullOrWhiteSpace(mainMenuSceneName))
                SceneManager.LoadScene(mainMenuSceneName);
        }
    }
}

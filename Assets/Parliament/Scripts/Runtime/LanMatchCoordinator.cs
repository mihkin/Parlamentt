using System;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ParliamentGame
{
    public sealed class LanMatchCoordinator : MonoBehaviour
    {
        [SerializeField] private string lanSceneName = "LanGameScene";
        [SerializeField] private float snapshotIntervalSeconds = 0.2f;

        private static LanMatchCoordinator instance;
        private static bool traceHookRegistered;
        private static string traceFilePath = string.Empty;
        private static string lastFatalMessage = string.Empty;

        private OnlineLobbyManager lobbyManager;
        private PlayerProfileDatabase profileDatabase;
        private LobbyRoomState currentRoom;
        private GameManager sceneGameManager;
        private string localPlayerId = string.Empty;
        private string pendingSnapshotPayload = string.Empty;
        private float nextSnapshotBroadcastTime;
        private bool matchOutcomeHandled;

        public static LanMatchCoordinator Instance => instance;
        public static string TraceFilePath => traceFilePath;
        public static string LastFatalMessage => lastFatalMessage;
        public bool HasActiveMatch
        {
            get
            {
                RestoreMatchContext();
                return currentRoom != null && !string.IsNullOrWhiteSpace(localPlayerId);
            }
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            EnsureTraceHook();
            DontDestroyOnLoad(gameObject);
            Trace("LanMatchCoordinator awake.");
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            UnsubscribeLobbyEvents();
        }

        private void Update()
        {
            if (lobbyManager == null || !lobbyManager.IsHost || sceneGameManager == null || currentRoom == null)
                return;

            if (Time.unscaledTime < nextSnapshotBroadcastTime)
                return;

            BroadcastSnapshot();
        }

        public void Initialize(OnlineLobbyManager lobbyManager, PlayerProfileDatabase profileDatabase)
        {
            if (this.lobbyManager == lobbyManager && this.profileDatabase == profileDatabase)
                return;

            UnsubscribeLobbyEvents();
            this.lobbyManager = lobbyManager;
            this.profileDatabase = profileDatabase;
            SubscribeLobbyEvents();
        }

        public bool BeginMatch(LobbyRoomState room)
        {
            ResetTrace();
            lastFatalMessage = string.Empty;
            currentRoom = room;
            localPlayerId = lobbyManager == null ? string.Empty : lobbyManager.LocalPlayerId;
            pendingSnapshotPayload = string.Empty;
            nextSnapshotBroadcastTime = 0f;
            matchOutcomeHandled = false;
            RestoreMatchContext();
            Trace($"BeginMatch room={(room == null ? "null" : room.RoomCode)} localPlayerId={(string.IsNullOrWhiteSpace(localPlayerId) ? "<empty>" : localPlayerId)} managerLocalPlayerId={(lobbyManager == null ? "<no_manager>" : string.IsNullOrWhiteSpace(lobbyManager.LocalPlayerId) ? "<empty>" : lobbyManager.LocalPlayerId)} players={room?.Players?.Count ?? 0}");

            if (!Application.CanStreamedLevelBeLoaded(lanSceneName))
            {
                Debug.LogError($"LAN scene '{lanSceneName}' is not available in Build Settings.");
                lastFatalMessage = $"LAN scene '{lanSceneName}' is not available in Build Settings.";
                return false;
            }

            if (SceneManager.GetActiveScene().name != lanSceneName)
            {
                Trace($"Loading scene '{lanSceneName}' from '{SceneManager.GetActiveScene().name}'.");
                SceneManager.LoadScene(lanSceneName);
            }
            else
                TryAttachToSceneManager();

            return true;
        }

        public void RegisterGameManager(GameManager gameManager)
        {
            RestoreMatchContext();
            if (sceneGameManager != null)
                sceneGameManager.GameFinished -= HandleGameFinished;

            sceneGameManager = gameManager;
            if (sceneGameManager != null)
            {
                sceneGameManager.GameFinished -= HandleGameFinished;
                sceneGameManager.GameFinished += HandleGameFinished;
            }

            Trace($"RegisterGameManager gameManager={(gameManager == null ? "null" : gameManager.name)} activeMatch={HasActiveMatch}");
            TryAttachToSceneManager();
        }

        public void EndMatch()
        {
            Trace("EndMatch called.");
            if (sceneGameManager != null)
                sceneGameManager.GameFinished -= HandleGameFinished;

            currentRoom = null;
            localPlayerId = string.Empty;
            pendingSnapshotPayload = string.Empty;
            sceneGameManager = null;
            matchOutcomeHandled = false;
        }

        public bool TryRestoreMatchContext()
        {
            RestoreMatchContext();
            return currentRoom != null && !string.IsNullOrWhiteSpace(localPlayerId);
        }

        private void TryAttachToSceneManager()
        {
            RestoreMatchContext();
            Trace($"TryAttachToSceneManager sceneGameManager={(sceneGameManager == null ? "null" : sceneGameManager.name)} room={(currentRoom == null ? "null" : currentRoom.RoomCode)} localPlayerId={(string.IsNullOrWhiteSpace(localPlayerId) ? "<empty>" : localPlayerId)} host={(lobbyManager != null && lobbyManager.IsHost)}");
            if (sceneGameManager == null || currentRoom == null || lobbyManager == null)
                return;

            if (lobbyManager.IsHost)
            {
                sceneGameManager.SetLanCommandSender(null);
                sceneGameManager.SetLanStateChangedCallback(() => BroadcastSnapshot(force: true));
                sceneGameManager.ConfigureLanMatch(currentRoom, localPlayerId, true);
                BroadcastSnapshot(force: true);
            }
            else
            {
                sceneGameManager.SetLanCommandSender(SendCommandToHost);
                sceneGameManager.SetLanStateChangedCallback(null);
                sceneGameManager.ConfigureLanMatch(currentRoom, localPlayerId, false);
                ApplyPendingSnapshot();
            }
        }

        private void SubscribeLobbyEvents()
        {
            if (lobbyManager == null)
                return;

            lobbyManager.GameplayCommandReceived += HandleGameplayCommandReceived;
            lobbyManager.GameplaySnapshotReceived += HandleGameplaySnapshotReceived;
            lobbyManager.LobbyError += HandleLobbyError;
        }

        private void UnsubscribeLobbyEvents()
        {
            if (lobbyManager == null)
                return;

            lobbyManager.GameplayCommandReceived -= HandleGameplayCommandReceived;
            lobbyManager.GameplaySnapshotReceived -= HandleGameplaySnapshotReceived;
            lobbyManager.LobbyError -= HandleLobbyError;
        }

        private void HandleGameplayCommandReceived(string payload)
        {
            if (lobbyManager == null || !lobbyManager.IsHost || sceneGameManager == null || string.IsNullOrWhiteSpace(payload))
                return;

            LanGameCommand command = JsonUtility.FromJson<LanGameCommand>(payload);
            if (command == null)
                return;

            Trace($"CommandReceived type={command.commandType} playerId={command.playerId} cardId={command.cardId} targetId={command.targetParticipantId} voteFor={command.voteFor}");
            bool applied = sceneGameManager.ApplyLanCommand(command, out string errorMessage);
            Trace(applied
                ? $"CommandApplied type={command.commandType} currentParticipant={sceneGameManager.State.CurrentParticipant?.displayName} round={sceneGameManager.State.currentRound}"
                : $"CommandRejected type={command.commandType} reason={errorMessage}");
            BroadcastSnapshot(force: true);
        }

        private void HandleGameplaySnapshotReceived(string payload)
        {
            if (lobbyManager == null || lobbyManager.IsHost || string.IsNullOrWhiteSpace(payload))
                return;

            pendingSnapshotPayload = payload;
            Trace("SnapshotReceived from host.");
            ApplyPendingSnapshot();
        }

        private void ApplyPendingSnapshot()
        {
            if (sceneGameManager == null || string.IsNullOrWhiteSpace(pendingSnapshotPayload))
                return;

            LanGameSnapshot snapshot = JsonUtility.FromJson<LanGameSnapshot>(pendingSnapshotPayload);
            if (snapshot == null)
                return;

            sceneGameManager.ApplyLanSnapshot(snapshot, localPlayerId);
            Trace($"SnapshotApplied localPlayerId={localPlayerId} currentParticipant={sceneGameManager.State.CurrentParticipant?.displayName} localPlayer={sceneGameManager.State.Player?.displayName} round={sceneGameManager.State.currentRound} phase={sceneGameManager.State.phase} ap={sceneGameManager.CurrentActionPointsRemaining}");
        }

        private void SendCommandToHost(LanGameCommand command)
        {
            if (lobbyManager == null || lobbyManager.IsHost || command == null)
                return;

            Trace($"CommandSent type={command.commandType} playerId={command.playerId} cardId={command.cardId} targetId={command.targetParticipantId} voteFor={command.voteFor}");
            lobbyManager.SendGameplayCommandToHost(JsonUtility.ToJson(command));
        }

        private void BroadcastSnapshot(bool force = false)
        {
            if (lobbyManager == null || !lobbyManager.IsHost || sceneGameManager == null)
                return;

            if (!force && Time.unscaledTime < nextSnapshotBroadcastTime)
                return;

            LanGameSnapshot snapshot = sceneGameManager.CaptureLanSnapshot();
            Trace($"SnapshotSent currentParticipant={snapshot.state?.CurrentParticipant?.displayName} round={snapshot.state?.currentRound ?? 0} phase={snapshot.state?.phase} ap={snapshot.currentActionPointsRemaining} time={snapshot.currentTurnTimeLeft:0.00}");
            lobbyManager.BroadcastGameplaySnapshot(JsonUtility.ToJson(snapshot));
            nextSnapshotBroadcastTime = Time.unscaledTime + Mathf.Max(0.05f, snapshotIntervalSeconds);
        }

        private void HandleLobbyError(string message)
        {
            Trace($"LobbyError scene={SceneManager.GetActiveScene().name} activeMatch={HasActiveMatch} message={message}");
            if (SceneManager.GetActiveScene().name == lanSceneName && HasActiveMatch && !string.IsNullOrWhiteSpace(message))
            {
                if (message.IndexOf("Хост закрыл комнату", StringComparison.OrdinalIgnoreCase) >= 0 && lobbyManager != null && !lobbyManager.IsHost)
                {
                    if (!matchOutcomeHandled)
                    {
                        profileDatabase?.AddCoins(25);
                        matchOutcomeHandled = true;
                    }

                    sceneGameManager?.ForceFinishLanMatch(GameResult.Defeat, "Хост закрыл комнату. Матч завершен. Остальным участникам начислено 25 монет.");
                    EndMatch();
                    return;
                }
            }

            if (SceneManager.GetActiveScene().name == lanSceneName && !HasActiveMatch)
            {
                lastFatalMessage = string.IsNullOrWhiteSpace(message)
                    ? "LAN session reported an error and the match context was lost."
                    : message;
                Trace("Lobby error detected in LAN scene. Match context lost.");
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != lanSceneName)
            {
                sceneGameManager = null;
                return;
            }

            Trace($"OnSceneLoaded '{scene.name}'.");
            RestoreMatchContext();
            TryAttachToSceneManager();
        }

        private void RestoreMatchContext()
        {
            if (lobbyManager == null)
                return;

            if (currentRoom == null)
                currentRoom = lobbyManager.CurrentRoom;

            if (string.IsNullOrWhiteSpace(localPlayerId))
                localPlayerId = !string.IsNullOrWhiteSpace(lobbyManager.LocalPlayerId)
                    ? lobbyManager.LocalPlayerId
                    : lobbyManager.LocalPlayer?.PlayerId ?? localPlayerId;
        }

        private void HandleGameFinished(GameResult result, string reason)
        {
            if (matchOutcomeHandled)
                return;

            matchOutcomeHandled = true;
            profileDatabase?.RecordMatchResult(result == GameResult.Victory, online: true, coinsReward: result == GameResult.Victory ? 50 : 0);
            Trace($"MatchFinished result={result} reason={reason}");
        }

        public static void SetFatalMessage(string message)
        {
            lastFatalMessage = string.IsNullOrWhiteSpace(message) ? "Unknown LAN scene error." : message;
            Trace($"Fatal: {lastFatalMessage}");
        }

        private static void EnsureTraceHook()
        {
            if (traceHookRegistered)
                return;

            traceHookRegistered = true;
            traceFilePath = Path.Combine(Application.persistentDataPath, "lan_trace.log");
            Application.logMessageReceived += HandleUnityLog;
        }

        private static void ResetTrace()
        {
            EnsureTraceHook();
            try
            {
                File.WriteAllText(traceFilePath, $"LAN trace started {DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}");
            }
            catch
            {
            }
        }

        private static void Trace(string message)
        {
            EnsureTraceHook();
            string line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
            Debug.Log(line);

            try
            {
                File.AppendAllText(traceFilePath, line + Environment.NewLine);
            }
            catch
            {
            }
        }

        private static void HandleUnityLog(string condition, string stackTrace, LogType type)
        {
            EnsureTraceHook();
            try
            {
                string line = $"[{DateTime.Now:HH:mm:ss.fff}] {type}: {condition}";
                File.AppendAllText(traceFilePath, line + Environment.NewLine);
                if ((type == LogType.Exception || type == LogType.Error || type == LogType.Assert) && !string.IsNullOrWhiteSpace(stackTrace))
                    File.AppendAllText(traceFilePath, stackTrace + Environment.NewLine);
            }
            catch
            {
            }
        }
    }
}

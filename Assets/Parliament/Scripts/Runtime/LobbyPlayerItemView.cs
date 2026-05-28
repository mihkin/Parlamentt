using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ParliamentGame
{
    public sealed class LobbyPlayerItemView : MonoBehaviour
    {
        [SerializeField] private TMP_Text nicknameText;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private Image readyIndicator;

        private Button button;

        public void Setup(PlayerNetworkData player)
        {
            if (player == null)
                return;

            Setup(
                player.IsHost ? $"{player.Nickname} [Хост]" : player.Nickname,
                $"Уровень {player.Level} • {GetReadyState(player)}",
                player.IsReady ? new Color(0.79f, 0.67f, 0.25f, 1f) : new Color(0.4f, 0.4f, 0.4f, 1f),
                null);
        }

        public void Setup(string title, string subtitle, Color indicatorColor, Action onClick)
        {
            if (nicknameText != null)
                nicknameText.text = title ?? string.Empty;

            if (statusText != null)
                statusText.text = subtitle ?? string.Empty;

            if (readyIndicator != null)
                readyIndicator.color = indicatorColor;

            if (button == null)
                button = GetComponent<Button>();

            if (button == null)
                button = gameObject.AddComponent<Button>();

            button.onClick.RemoveAllListeners();
            button.interactable = onClick != null;
            if (onClick != null)
                button.onClick.AddListener(() => onClick.Invoke());
        }

        private static string GetReadyState(PlayerNetworkData player)
        {
            if (player == null)
                return "Неизвестно";

            if (player.ConnectionState == NetworkPlayerConnectionState.Disconnected)
                return "Отключен";

            return player.IsReady ? "Готов" : "Не готов";
        }
    }
}

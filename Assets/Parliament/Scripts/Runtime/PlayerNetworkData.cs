using System;
using System.Collections.Generic;
using UnityEngine;

namespace ParliamentGame
{
    public enum NetworkPlayerConnectionState
    {
        Connecting,
        Connected,
        Disconnected
    }

    [Serializable]
    public sealed class PlayerNetworkData
    {
        [SerializeField] private string playerId;
        [SerializeField] private string nickname;
        [SerializeField] private int seatIndex;
        [SerializeField] private bool isHost;
        [SerializeField] private bool isReady;
        [SerializeField] private int level = 1;
        [SerializeField] private string rank = "Bronze";
        [SerializeField] private string avatar = "default";
        [SerializeField] private List<int> selectedDeckCardIds = new List<int>();
        [SerializeField] private NetworkPlayerConnectionState connectionState = NetworkPlayerConnectionState.Connecting;

        public string PlayerId => playerId;
        public string Nickname => nickname;
        public int SeatIndex => seatIndex;
        public bool IsHost => isHost;
        public bool IsReady => isReady;
        public int Level => level;
        public string Rank => rank;
        public string Avatar => avatar;
        public IReadOnlyList<int> SelectedDeckCardIds => selectedDeckCardIds;
        public NetworkPlayerConnectionState ConnectionState => connectionState;

        public PlayerNetworkData(string playerId, string nickname, int seatIndex, bool isHost, int level, string rank, string avatar, IEnumerable<int> selectedDeckCardIds)
        {
            this.playerId = playerId;
            this.nickname = nickname;
            this.seatIndex = seatIndex;
            this.isHost = isHost;
            this.level = Mathf.Max(1, level);
            this.rank = rank;
            this.avatar = avatar;
            if (selectedDeckCardIds != null)
                this.selectedDeckCardIds = new List<int>(selectedDeckCardIds);
            connectionState = NetworkPlayerConnectionState.Connected;
        }

        public void SetReady(bool value)
        {
            isReady = value;
        }

        public void SetConnectionState(NetworkPlayerConnectionState state)
        {
            connectionState = state;
        }
    }
}

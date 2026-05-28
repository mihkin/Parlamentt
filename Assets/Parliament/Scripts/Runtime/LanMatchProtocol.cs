using System;

namespace ParliamentGame
{
    public enum LanGameCommandType
    {
        PlayCard,
        BuyCard,
        EndTurn,
        Vote
    }

    [Serializable]
    public sealed class LanGameCommand
    {
        public string playerId;
        public LanGameCommandType commandType;
        public int cardId = -1;
        public string cardInstanceId;
        public int targetParticipantId = -1;
        public bool voteFor;
    }

    [Serializable]
    public sealed class LanPlayedCardEvent
    {
        public int sequence;
        public int actorParticipantId = -1;
        public int cardId = -1;
        public string cardInstanceId;
        public int targetParticipantId = -1;
    }

    [Serializable]
    public sealed class LanGameSnapshot
    {
        public GameState state;
        public float currentTurnTimeLeft;
        public float currentTurnDuration;
        public int currentActionPointsRemaining;
        public bool isPaused;
        public bool matchFinished;
        public int winnerParticipantId = -1;
        public string resultReason;
        public LanPlayedCardEvent lastPlayedCardEvent;
    }
}

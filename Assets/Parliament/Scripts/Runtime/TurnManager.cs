namespace ParliamentGame
{
    public class TurnManager
    {
        public void MoveToNextParticipant(GameState state)
        {
            if (state.participants.Count == 0)
                return;

            state.currentParticipantIndex++;
            if (state.currentParticipantIndex >= state.participants.Count)
            {
                state.currentParticipantIndex = 0;
                state.currentRound++;
            }
        }

        public bool IsRoundStart(GameState state)
        {
            return state.currentParticipantIndex == 0;
        }
    }
}

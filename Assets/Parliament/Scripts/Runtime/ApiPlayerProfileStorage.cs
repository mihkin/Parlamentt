using System;
using System.Collections.Generic;

namespace ParliamentGame
{
    internal sealed class ApiPlayerProfileStorage : IPlayerProfileStorage
    {
        private readonly ApiAuthenticationService authenticationService;

        public ApiPlayerProfileStorage(ApiAuthenticationService authenticationService)
        {
            this.authenticationService = authenticationService;
        }

        public static bool IsApiEnabled(ApiAuthenticationService authenticationService)
        {
            return authenticationService != null && authenticationService.IsEnabled && authenticationService.IsAuthenticated;
        }

        public bool TryLoad(string path, out PlayerProfileData profile)
        {
            if (authenticationService == null)
            {
                profile = null;
                return false;
            }

            return authenticationService.TryLoadProfile(out profile);
        }

        public void Save(string path, PlayerProfileData profile)
        {
            authenticationService?.SaveProfile(profile);
        }
    }

    internal static class ApiProfileMapper
    {
        [Serializable]
        public sealed class UpdateProfileRequestDto
        {
            public string playerId = string.Empty;
            public string nickname = "Senator";
            public int level = 1;
            public int experience;
            public int coins = 500;
            public List<int> ownedCards = new List<int>();
            public List<int> selectedDeck = new List<int>();
            public ApiAuthenticationService.ApiStatisticsDto statistics = new ApiAuthenticationService.ApiStatisticsDto();
            public string rank = "Bronze";
            public string avatar = "default";
        }

        public static PlayerProfileData ToProfileData(ApiAuthenticationService.ApiProfileDto profileDto)
        {
            if (profileDto == null)
                return null;

            return new PlayerProfileData
            {
                playerId = profileDto.playerId ?? string.Empty,
                nickname = string.IsNullOrWhiteSpace(profileDto.nickname) ? "Senator" : profileDto.nickname,
                level = Math.Max(1, profileDto.level),
                experience = Math.Max(0, profileDto.experience),
                coins = Math.Max(0, profileDto.coins),
                ownedCards = profileDto.ownedCards == null ? new List<int>() : new List<int>(profileDto.ownedCards),
                selectedDeck = profileDto.selectedDeck == null ? new List<int>() : new List<int>(profileDto.selectedDeck),
                statistics = ToStatisticsData(profileDto.statistics),
                rank = string.IsNullOrWhiteSpace(profileDto.rank) ? "Bronze" : profileDto.rank,
                avatar = string.IsNullOrWhiteSpace(profileDto.avatar) ? "default" : profileDto.avatar
            };
        }

        public static UpdateProfileRequestDto ToUpdateRequest(PlayerProfileData profile)
        {
            return new UpdateProfileRequestDto
            {
                playerId = profile.playerId ?? string.Empty,
                nickname = profile.nickname ?? "Senator",
                level = profile.level,
                experience = profile.experience,
                coins = profile.coins,
                ownedCards = profile.ownedCards == null ? new List<int>() : new List<int>(profile.ownedCards),
                selectedDeck = profile.selectedDeck == null ? new List<int>() : new List<int>(profile.selectedDeck),
                statistics = ToStatisticsDto(profile.statistics),
                rank = profile.rank ?? "Bronze",
                avatar = profile.avatar ?? "default"
            };
        }

        private static PlayerStatisticsData ToStatisticsData(ApiAuthenticationService.ApiStatisticsDto statisticsDto)
        {
            if (statisticsDto == null)
                return new PlayerStatisticsData();

            return new PlayerStatisticsData
            {
                totalMatches = statisticsDto.totalMatches,
                wins = statisticsDto.wins,
                losses = statisticsDto.losses,
                onlineMatches = statisticsDto.onlineMatches,
                offlineMatches = statisticsDto.offlineMatches,
                cardsPlayed = statisticsDto.cardsPlayed,
                turnsPlayed = statisticsDto.turnsPlayed
            };
        }

        private static ApiAuthenticationService.ApiStatisticsDto ToStatisticsDto(PlayerStatisticsData statistics)
        {
            if (statistics == null)
                return new ApiAuthenticationService.ApiStatisticsDto();

            return new ApiAuthenticationService.ApiStatisticsDto
            {
                totalMatches = statistics.totalMatches,
                wins = statistics.wins,
                losses = statistics.losses,
                onlineMatches = statistics.onlineMatches,
                offlineMatches = statistics.offlineMatches,
                cardsPlayed = statistics.cardsPlayed,
                turnsPlayed = statistics.turnsPlayed
            };
        }
    }
}

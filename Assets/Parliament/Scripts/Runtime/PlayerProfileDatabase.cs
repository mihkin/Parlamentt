using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace ParliamentGame
{
    [Serializable]
    public sealed class PlayerStatisticsData
    {
        public int totalMatches;
        public int wins;
        public int losses;
        public int onlineMatches;
        public int offlineMatches;
        public int cardsPlayed;
        public int turnsPlayed;
    }

    [Serializable]
    public sealed class PlayerProfileData
    {
        public string playerId = string.Empty;
        public string nickname = "Senator";
        public int level = 1;
        public int experience;
        public int coins = 500;
        public List<int> ownedCards = new List<int>();
        public List<int> selectedDeck = new List<int>();
        public PlayerStatisticsData statistics = new PlayerStatisticsData();
        public string rank = "Bronze";
        public string avatar = "default";
    }

    internal interface IPlayerProfileStorage
    {
        bool TryLoad(string path, out PlayerProfileData profile);
        void Save(string path, PlayerProfileData profile);
    }

    internal sealed class JsonPlayerProfileStorage : IPlayerProfileStorage
    {
        [Serializable]
        private sealed class Wrapper
        {
            public PlayerProfileData profile;
        }

        public bool TryLoad(string path, out PlayerProfileData profile)
        {
            profile = null;
            if (!File.Exists(path))
                return false;

            Wrapper wrapper = JsonUtility.FromJson<Wrapper>(File.ReadAllText(path));
            profile = wrapper?.profile;
            return profile != null;
        }

        public void Save(string path, PlayerProfileData profile)
        {
            Wrapper wrapper = new Wrapper { profile = profile };
            string json = JsonUtility.ToJson(wrapper, true);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Application.persistentDataPath);
            File.WriteAllText(path, json);
        }
    }

    public sealed class PlayerProfileDatabase : MonoBehaviour
    {
        public const string DefaultFileName = "player_profile.json";
        public const int MinimumSelectedDeckCards = 3;
        public const int MaxSelectedDeckCards = 20;
        public const string DefaultApiSettingsFileName = ApiAuthenticationService.DefaultSettingsFileName;
        public const string DefaultSqlServerSettingsFileName = "sqlserver-profile-settings.json";

        [SerializeField] private string fileName = DefaultFileName;
        [SerializeField] private string sqlServerSettingsFileName = DefaultSqlServerSettingsFileName;
        [SerializeField] private PlayerProfileData defaultProfile = new PlayerProfileData();

        private IPlayerProfileStorage storage;

        public event Action<PlayerProfileData> ProfileChanged;

        public PlayerProfileData CurrentProfile { get; private set; }

        private string SavePath => GetSavePath(fileName);

        private void Awake()
        {
            InitializeStorage();
            LoadProfile();
        }

        private void InitializeStorage()
        {
            ApiAuthenticationService apiAuthenticationService = GetComponent<ApiAuthenticationService>();
            if (apiAuthenticationService != null && apiAuthenticationService.IsEnabled)
            {
                storage = apiAuthenticationService.IsAuthenticated
                    ? new ApiPlayerProfileStorage(apiAuthenticationService)
                    : new JsonPlayerProfileStorage();
                return;
            }

            string settingsPath = GetSqlServerSettingsPath(sqlServerSettingsFileName);
            storage = SqlServerPlayerProfileStorage.IsSqlServerEnabled(settingsPath)
                ? new SqlServerPlayerProfileStorage(settingsPath)
                : new JsonPlayerProfileStorage();
        }

        /// <summary>
        /// Загружает профиль игрока из локального JSON, либо создает его из дефолтных значений.
        /// </summary>
        public void LoadProfile()
        {
            if (!storage.TryLoad(SavePath, out PlayerProfileData profile))
            {
                profile = CloneProfile(defaultProfile);
            }

            EnsureCollectionDefaults(profile);
            SanitizeSelectedDeck(profile);
            storage.Save(SavePath, profile);
            CurrentProfile = profile;
            ProfileChanged?.Invoke(CurrentProfile);
        }

        /// <summary>
        /// Сохраняет текущий профиль локально.
        /// </summary>
        public void SaveProfile()
        {
            if (CurrentProfile == null)
                return;

            storage.Save(SavePath, CurrentProfile);
        }

        public void ReinitializeStorage()
        {
            InitializeStorage();
            LoadProfile();
        }

        public void ReplaceProfile(PlayerProfileData profile, bool saveImmediately = true)
        {
            PlayerProfileData replacement = profile == null ? CloneProfile(defaultProfile) : CloneProfile(profile);
            EnsureCollectionDefaults(replacement);
            SanitizeSelectedDeck(replacement);
            CurrentProfile = replacement;

            if (saveImmediately)
                storage.Save(SavePath, CurrentProfile);

            ProfileChanged?.Invoke(CurrentProfile);
        }

        public void ResetToDefaultProfile(bool saveImmediately = true)
        {
            ReplaceProfile(defaultProfile, saveImmediately);
        }

        /// <summary>
        /// Обновляет никнейм игрока и сразу сохраняет профиль.
        /// </summary>
        public void SetNickname(string nickname)
        {
            if (CurrentProfile == null)
                LoadProfile();

            CurrentProfile.nickname = string.IsNullOrWhiteSpace(nickname) ? defaultProfile.nickname : nickname.Trim();
            CommitProfile();
        }

        /// <summary>
        /// Добавляет валюту в профиль игрока.
        /// </summary>
        public void AddCoins(int amount)
        {
            if (CurrentProfile == null)
                LoadProfile();

            CurrentProfile.coins = Mathf.Max(0, CurrentProfile.coins + amount);
            CommitProfile();
        }

        /// <summary>
        /// Пытается списать валюту. Возвращает false, если монет недостаточно.
        /// </summary>
        public bool TrySpendCoins(int amount)
        {
            if (CurrentProfile == null)
                LoadProfile();

            if (amount < 0 || CurrentProfile.coins < amount)
                return false;

            CurrentProfile.coins -= amount;
            CommitProfile();
            return true;
        }

        /// <summary>
        /// Открывает карту в коллекции, если она еще не была получена ранее.
        /// </summary>
        public void UnlockCard(int cardId)
        {
            if (CurrentProfile == null)
                LoadProfile();

            if (!CurrentProfile.ownedCards.Contains(cardId))
                CurrentProfile.ownedCards.Add(cardId);

            if (CurrentProfile.selectedDeck.Count < MinimumSelectedDeckCards && !CurrentProfile.selectedDeck.Contains(cardId))
                CurrentProfile.selectedDeck.Add(cardId);

            SanitizeSelectedDeck(CurrentProfile);
            CommitProfile();
        }

        public void UnlockCards(IEnumerable<int> cardIds)
        {
            if (CurrentProfile == null)
                LoadProfile();

            if (cardIds == null)
                return;

            bool changed = false;
            foreach (int cardId in cardIds)
            {
                if (!CurrentProfile.ownedCards.Contains(cardId))
                {
                    CurrentProfile.ownedCards.Add(cardId);
                    changed = true;
                }

                if (CurrentProfile.selectedDeck.Count < MinimumSelectedDeckCards && !CurrentProfile.selectedDeck.Contains(cardId))
                {
                    CurrentProfile.selectedDeck.Add(cardId);
                    changed = true;
                }
            }

            if (!changed)
                return;

            SanitizeSelectedDeck(CurrentProfile);
            CommitProfile();
        }

        /// <summary>
        /// Заменяет выбранную колоду новым набором карт.
        /// </summary>
        public void SetSelectedDeck(List<int> deckCardIds)
        {
            if (CurrentProfile == null)
                LoadProfile();

            CurrentProfile.selectedDeck = deckCardIds == null ? new List<int>() : new List<int>(deckCardIds);
            SanitizeSelectedDeck(CurrentProfile);
            CommitProfile();
        }

        /// <summary>
        /// Добавляет или убирает карту из выбранной колоды с проверкой владения и лимитов.
        /// </summary>
        public bool TryToggleCardInSelectedDeck(int cardId, out string message)
        {
            if (CurrentProfile == null)
                LoadProfile();

            message = string.Empty;
            if (!CurrentProfile.ownedCards.Contains(cardId))
            {
                message = "Эта карта еще не получена.";
                return false;
            }

            if (CurrentProfile.selectedDeck.Contains(cardId))
            {
                if (CurrentProfile.selectedDeck.Count <= MinimumSelectedDeckCards)
                {
                    message = $"В колоде должно быть минимум {MinimumSelectedDeckCards} карты.";
                    return false;
                }

                CurrentProfile.selectedDeck.Remove(cardId);
                message = "Карта убрана из колоды.";
                CommitProfile();
                return true;
            }

            if (CurrentProfile.selectedDeck.Count >= MaxSelectedDeckCards)
            {
                message = $"В колоде максимум {MaxSelectedDeckCards} карт.";
                return false;
            }

            CurrentProfile.selectedDeck.Add(cardId);
            message = "Карта добавлена в колоду.";
            CommitProfile();
            return true;
        }

        /// <summary>
        /// Возвращает true, если карта сейчас входит в выбранную колоду.
        /// </summary>
        public bool IsCardInSelectedDeck(int cardId)
        {
            if (CurrentProfile == null)
                LoadProfile();

            return CurrentProfile.selectedDeck.Contains(cardId);
        }

        public void RecordMatchResult(bool won, bool online, int coinsReward = 0)
        {
            if (CurrentProfile == null)
                LoadProfile();

            if (CurrentProfile.statistics == null)
                CurrentProfile.statistics = new PlayerStatisticsData();

            CurrentProfile.statistics.totalMatches++;
            if (online)
                CurrentProfile.statistics.onlineMatches++;
            else
                CurrentProfile.statistics.offlineMatches++;

            if (won)
                CurrentProfile.statistics.wins++;
            else
                CurrentProfile.statistics.losses++;

            if (coinsReward != 0)
                CurrentProfile.coins = Mathf.Max(0, CurrentProfile.coins + coinsReward);

            CommitProfile();
        }

        /// <summary>
        /// Возвращает путь к локальному JSON профиля игрока.
        /// </summary>
        public static string GetSavePath(string profileFileName = DefaultFileName)
        {
            return Path.Combine(Application.persistentDataPath, string.IsNullOrWhiteSpace(profileFileName) ? DefaultFileName : profileFileName);
        }

        public static string GetSqlServerSettingsPath(string settingsFileName = DefaultSqlServerSettingsFileName)
        {
            return Path.Combine(Application.streamingAssetsPath, string.IsNullOrWhiteSpace(settingsFileName) ? DefaultSqlServerSettingsFileName : settingsFileName);
        }

        /// <summary>
        /// Загружает профиль без MonoBehaviour, чтобы игровая сцена могла взять выбранную колоду.
        /// </summary>
        public static bool TryLoadSavedProfile(out PlayerProfileData profile, string profileFileName = DefaultFileName)
        {
            JsonPlayerProfileStorage jsonStorage = new JsonPlayerProfileStorage();
            if (!jsonStorage.TryLoad(GetSavePath(profileFileName), out profile))
                return false;

            EnsureCollectionDefaults(profile);
            SanitizeSelectedDeck(profile);
            return true;
        }

        private void CommitProfile()
        {
            SaveProfile();
            ProfileChanged?.Invoke(CurrentProfile);
        }

        internal static void EnsureCollectionDefaults(PlayerProfileData profile)
        {
            if (profile.ownedCards.Count > 0)
                return;

            for (int id = 1; id <= 8; id++)
                profile.ownedCards.Add(id);

            if (profile.selectedDeck.Count == 0)
                profile.selectedDeck.AddRange(profile.ownedCards);
        }

        internal static void SanitizeSelectedDeck(PlayerProfileData profile)
        {
            if (profile == null)
                return;

            profile.ownedCards = profile.ownedCards.Distinct().OrderBy(id => id).ToList();
            profile.selectedDeck = profile.selectedDeck
                .Where(id => profile.ownedCards.Contains(id))
                .Distinct()
                .Take(MaxSelectedDeckCards)
                .ToList();

            foreach (int ownedCardId in profile.ownedCards)
            {
                if (profile.selectedDeck.Count >= MinimumSelectedDeckCards)
                    break;

                if (!profile.selectedDeck.Contains(ownedCardId))
                    profile.selectedDeck.Add(ownedCardId);
            }
        }

        internal static PlayerProfileData CloneProfile(PlayerProfileData source)
        {
            return new PlayerProfileData
            {
                playerId = source.playerId,
                nickname = source.nickname,
                level = source.level,
                experience = source.experience,
                coins = source.coins,
                ownedCards = new List<int>(source.ownedCards),
                selectedDeck = new List<int>(source.selectedDeck),
                statistics = new PlayerStatisticsData
                {
                    totalMatches = source.statistics.totalMatches,
                    wins = source.statistics.wins,
                    losses = source.statistics.losses,
                    onlineMatches = source.statistics.onlineMatches,
                    offlineMatches = source.statistics.offlineMatches,
                    cardsPlayed = source.statistics.cardsPlayed,
                    turnsPlayed = source.statistics.turnsPlayed
                },
                rank = source.rank,
                avatar = source.avatar
            };
        }
    }
}

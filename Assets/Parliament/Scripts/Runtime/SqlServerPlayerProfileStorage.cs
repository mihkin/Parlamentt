using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace ParliamentGame
{
    [Serializable]
    internal sealed class SqlServerProfileSettings
    {
        public bool enabled;
        public string connectionString = string.Empty;
        public string preferredNickname = "Senator";
    }

    internal sealed class SqlServerPlayerProfileStorage : IPlayerProfileStorage
    {
        private readonly string settingsPath;
        private readonly JsonPlayerProfileStorage localCacheStorage = new JsonPlayerProfileStorage();

        public SqlServerPlayerProfileStorage(string settingsPath)
        {
            this.settingsPath = settingsPath;
        }

        public static bool IsSqlServerEnabled(string settingsPath)
        {
            if (!TryLoadSettings(settingsPath, out SqlServerProfileSettings settings))
                return false;

            return settings.enabled && !string.IsNullOrWhiteSpace(settings.connectionString);
        }

        public bool TryLoad(string path, out PlayerProfileData profile)
        {
            profile = null;
            localCacheStorage.TryLoad(path, out PlayerProfileData cachedProfile);

            if (!IsSqlServerEnabled(settingsPath))
            {
                profile = cachedProfile;
                return profile != null;
            }

            try
            {
                string outputJson = RunBridge("Load", path, null);
                if (!string.IsNullOrWhiteSpace(outputJson))
                {
                    profile = JsonUtility.FromJson<PlayerProfileData>(outputJson);
                    if (profile != null)
                    {
                        PlayerProfileDatabase.EnsureCollectionDefaults(profile);
                        PlayerProfileDatabase.SanitizeSelectedDeck(profile);
                        localCacheStorage.Save(path, profile);
                        return true;
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"SQL Server profile load failed. Falling back to local cache. {exception.Message}");
            }

            profile = cachedProfile;
            return profile != null;
        }

        public void Save(string path, PlayerProfileData profile)
        {
            if (profile == null)
                return;

            localCacheStorage.Save(path, profile);

            if (!IsSqlServerEnabled(settingsPath))
                return;

            string tempInputPath = null;
            try
            {
                tempInputPath = Path.Combine(Path.GetTempPath(), $"parlamb-profile-{Guid.NewGuid():N}.json");
                File.WriteAllText(tempInputPath, JsonUtility.ToJson(profile, true));

                string outputJson = RunBridge("Save", path, tempInputPath);
                if (!string.IsNullOrWhiteSpace(outputJson))
                {
                    PlayerProfileData savedProfile = JsonUtility.FromJson<PlayerProfileData>(outputJson);
                    if (savedProfile != null)
                    {
                        CopyProfile(profile, savedProfile);
                        localCacheStorage.Save(path, profile);
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"SQL Server profile save failed. Local cache stays active. {exception.Message}");
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(tempInputPath) && File.Exists(tempInputPath))
                    File.Delete(tempInputPath);
            }
        }

        private static bool TryLoadSettings(string settingsPath, out SqlServerProfileSettings settings)
        {
            settings = null;
            if (string.IsNullOrWhiteSpace(settingsPath) || !File.Exists(settingsPath))
                return false;

            settings = JsonUtility.FromJson<SqlServerProfileSettings>(File.ReadAllText(settingsPath));
            return settings != null;
        }

        private string RunBridge(string mode, string cachePath, string inputPath)
        {
            string bridgePath = Path.Combine(Application.streamingAssetsPath, "sqlserver-profile-bridge.ps1");
            if (!File.Exists(bridgePath))
                throw new FileNotFoundException("SQL Server bridge script not found.", bridgePath);

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments =
                    $"-NoProfile -ExecutionPolicy Bypass -File \"{bridgePath}\" " +
                    $"-Mode {mode} " +
                    $"-SettingsPath \"{settingsPath}\" " +
                    $"-CachePath \"{cachePath}\"" +
                    (string.IsNullOrWhiteSpace(inputPath) ? string.Empty : $" -InputPath \"{inputPath}\""),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (Process process = Process.Start(startInfo))
            {
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? $"Bridge exited with code {process.ExitCode}." : error.Trim());

                return output?.Trim();
            }
        }

        private static void CopyProfile(PlayerProfileData target, PlayerProfileData source)
        {
            target.playerId = source.playerId;
            target.nickname = source.nickname;
            target.level = source.level;
            target.experience = source.experience;
            target.coins = source.coins;
            target.rank = source.rank;
            target.avatar = source.avatar;
            target.ownedCards = source.ownedCards == null ? new List<int>() : new List<int>(source.ownedCards);
            target.selectedDeck = source.selectedDeck == null ? new List<int>() : new List<int>(source.selectedDeck);
            target.statistics = source.statistics ?? new PlayerStatisticsData();
        }
    }
}

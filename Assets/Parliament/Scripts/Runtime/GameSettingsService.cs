using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;

namespace ParliamentGame
{
    [Serializable]
    public sealed class GameSettingsData
    {
        public float musicVolume = 0.8f;
        public float effectsVolume = 0.85f;
        public bool fullscreen = true;
        public int resolutionIndex = -1;
        public int qualityIndex = 2;
    }

    public sealed class GameSettingsService : MonoBehaviour
    {
        [SerializeField] private string fileName = "settings.json";
        [SerializeField] private GameSettingsData defaultSettings = new GameSettingsData();

        [Serializable]
        private sealed class Wrapper
        {
            public GameSettingsData settings;
        }

        public event Action<GameSettingsData> SettingsChanged;

        public GameSettingsData CurrentSettings { get; private set; }

        private string SavePath => Path.Combine(Application.persistentDataPath, fileName);

        private void Awake()
        {
            Load();
            Apply();
        }

        /// <summary>
        /// Загружает сохраненные настройки или создает их из дефолтных значений.
        /// </summary>
        public void Load()
        {
            if (!File.Exists(SavePath))
            {
                CurrentSettings = Clone(defaultSettings);
                Save();
                return;
            }

            Wrapper wrapper = JsonUtility.FromJson<Wrapper>(File.ReadAllText(SavePath));
            CurrentSettings = wrapper?.settings ?? Clone(defaultSettings);
            SettingsChanged?.Invoke(CurrentSettings);
        }

        /// <summary>
        /// Сохраняет настройки в локальный JSON-файл.
        /// </summary>
        public void Save()
        {
            Wrapper wrapper = new Wrapper { settings = CurrentSettings ?? Clone(defaultSettings) };
            Directory.CreateDirectory(Path.GetDirectoryName(SavePath) ?? Application.persistentDataPath);
            File.WriteAllText(SavePath, JsonUtility.ToJson(wrapper, true));
            SettingsChanged?.Invoke(CurrentSettings);
        }

        /// <summary>
        /// Применяет сохраненные настройки к текущему приложению.
        /// </summary>
        public void Apply()
        {
            if (CurrentSettings == null)
                CurrentSettings = Clone(defaultSettings);

            Resolution[] resolutions = Screen.resolutions;
            if (resolutions.Length > 0)
            {
                int index = Mathf.Clamp(CurrentSettings.resolutionIndex < 0 ? resolutions.Length - 1 : CurrentSettings.resolutionIndex, 0, resolutions.Length - 1);
                Resolution resolution = resolutions[index];
                Screen.SetResolution(resolution.width, resolution.height, CurrentSettings.fullscreen);
                CurrentSettings.resolutionIndex = index;
            }
            else
            {
                Screen.fullScreen = CurrentSettings.fullscreen;
            }

            QualitySettings.SetQualityLevel(Mathf.Clamp(CurrentSettings.qualityIndex, 0, QualitySettings.names.Length - 1), true);
            AudioListener.volume = 1f;
            Save();
        }

        /// <summary>
        /// Обновляет музыкальную громкость.
        /// </summary>
        public void SetMusicVolume(float value)
        {
            EnsureSettings();
            CurrentSettings.musicVolume = Mathf.Clamp01(value);
        }

        /// <summary>
        /// Обновляет громкость звуковых эффектов.
        /// </summary>
        public void SetEffectsVolume(float value)
        {
            EnsureSettings();
            CurrentSettings.effectsVolume = Mathf.Clamp01(value);
        }

        /// <summary>
        /// Переключает полноэкранный режим.
        /// </summary>
        public void SetFullscreen(bool fullscreen)
        {
            EnsureSettings();
            CurrentSettings.fullscreen = fullscreen;
        }

        /// <summary>
        /// Устанавливает индекс разрешения из списка Screen.resolutions.
        /// </summary>
        public void SetResolutionIndex(int index)
        {
            EnsureSettings();
            CurrentSettings.resolutionIndex = index;
        }

        /// <summary>
        /// Устанавливает индекс quality preset.
        /// </summary>
        public void SetQualityIndex(int index)
        {
            EnsureSettings();
            CurrentSettings.qualityIndex = index;
        }

        /// <summary>
        /// Возвращает подписи доступных разрешений для UI.
        /// </summary>
        public List<TMP_Dropdown.OptionData> BuildResolutionOptions()
        {
            List<TMP_Dropdown.OptionData> options = new List<TMP_Dropdown.OptionData>();
            foreach (Resolution resolution in Screen.resolutions)
                options.Add(new TMP_Dropdown.OptionData($"{resolution.width}x{resolution.height} @{resolution.refreshRateRatio.value:0}Hz"));

            if (options.Count == 0)
                options.Add(new TMP_Dropdown.OptionData("Текущее"));

            return options;
        }

        private void EnsureSettings()
        {
            if (CurrentSettings == null)
                CurrentSettings = Clone(defaultSettings);
        }

        private static GameSettingsData Clone(GameSettingsData source)
        {
            return new GameSettingsData
            {
                musicVolume = source.musicVolume,
                effectsVolume = source.effectsVolume,
                fullscreen = source.fullscreen,
                resolutionIndex = source.resolutionIndex,
                qualityIndex = source.qualityIndex
            };
        }
    }
}

using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ParliamentGame
{
    public sealed class SettingsPanelController : MonoBehaviour
    {
        [SerializeField] private GameSettingsService settingsService;
        [SerializeField] private Slider musicSlider;
        [SerializeField] private Slider effectsSlider;
        [SerializeField] private Toggle fullscreenToggle;
        [SerializeField] private TMP_Dropdown resolutionDropdown;
        [SerializeField] private TMP_Dropdown qualityDropdown;
        [SerializeField] private Button applyButton;

        private bool initialized;

        private void Start()
        {
            Initialize();
        }

        public void Initialize()
        {
            if (initialized)
                return;

            initialized = true;

            if (settingsService == null)
                return;

            if (resolutionDropdown != null)
            {
                resolutionDropdown.ClearOptions();
                resolutionDropdown.AddOptions(settingsService.BuildResolutionOptions());
            }

            if (qualityDropdown != null)
            {
                qualityDropdown.ClearOptions();
                foreach (string name in QualitySettings.names)
                    qualityDropdown.options.Add(new TMP_Dropdown.OptionData(name));
            }

            if (applyButton != null)
                applyButton.onClick.AddListener(ApplySettings);

            if (settingsService.CurrentSettings != null)
                SyncFromSettings(settingsService.CurrentSettings);
        }

        /// <summary>
        /// Применяет значения из UI в сервис настроек и сохраняет их.
        /// </summary>
        public void ApplySettings()
        {
            if (!initialized)
                Initialize();

            if (settingsService == null)
                return;

            if (musicSlider != null)
                settingsService.SetMusicVolume(musicSlider.value);

            if (effectsSlider != null)
                settingsService.SetEffectsVolume(effectsSlider.value);

            if (fullscreenToggle != null)
                settingsService.SetFullscreen(fullscreenToggle.isOn);

            if (resolutionDropdown != null)
                settingsService.SetResolutionIndex(resolutionDropdown.value);

            if (qualityDropdown != null)
                settingsService.SetQualityIndex(qualityDropdown.value);

            settingsService.Apply();
        }

        private void SyncFromSettings(GameSettingsData settings)
        {
            if (musicSlider != null)
                musicSlider.value = settings.musicVolume;

            if (effectsSlider != null)
                effectsSlider.value = settings.effectsVolume;

            if (fullscreenToggle != null)
                fullscreenToggle.isOn = settings.fullscreen;

            if (resolutionDropdown != null)
                resolutionDropdown.value = Mathf.Max(0, settings.resolutionIndex);

            if (qualityDropdown != null)
                qualityDropdown.value = Mathf.Max(0, settings.qualityIndex);
        }
    }
}

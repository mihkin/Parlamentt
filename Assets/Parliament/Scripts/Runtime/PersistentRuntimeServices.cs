using UnityEngine;

namespace ParliamentGame
{
    public sealed class PersistentRuntimeServices : MonoBehaviour
    {
        private static PersistentRuntimeServices instance;

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
        }
    }

    public sealed class PersistentAudioService : MonoBehaviour
    {
        [SerializeField] private AudioSource musicSource;
        [SerializeField] private AudioSource effectsSource;
        [SerializeField] private AudioClip backgroundMusicClip;
        [SerializeField] [Range(0f, 1f)] private float musicVolumeMultiplier = 1f;
        [SerializeField] [Range(0f, 1f)] private float effectsVolumeMultiplier = 1f;

        private GameSettingsService settingsService;

        public AudioSource EffectsSource
        {
            get
            {
                EnsureAudioSources();
                return effectsSource;
            }
        }

        public void Initialize(GameSettingsService gameSettingsService, AudioClip musicClip)
        {
            EnsureAudioSources();

            if (settingsService != gameSettingsService)
            {
                if (settingsService != null)
                    settingsService.SettingsChanged -= HandleSettingsChanged;

                settingsService = gameSettingsService;

                if (settingsService != null)
                    settingsService.SettingsChanged += HandleSettingsChanged;
            }

            if (musicClip != null)
                backgroundMusicClip = musicClip;

            ApplyVolumeSettings();
            RefreshMusicPlayback();
        }

        private void Awake()
        {
            EnsureAudioSources();
            ApplyVolumeSettings();
            RefreshMusicPlayback();
        }

        private void OnDestroy()
        {
            if (settingsService != null)
                settingsService.SettingsChanged -= HandleSettingsChanged;
        }

        private void HandleSettingsChanged(GameSettingsData settings)
        {
            ApplyVolumeSettings();
        }

        private void EnsureAudioSources()
        {
            AudioSource[] audioSources = GetComponents<AudioSource>();

            if (effectsSource == null)
            {
                effectsSource = audioSources.Length > 0
                    ? audioSources[0]
                    : gameObject.AddComponent<AudioSource>();
            }

            if (musicSource == null)
            {
                for (int i = 0; i < audioSources.Length; i++)
                {
                    if (audioSources[i] != null && audioSources[i] != effectsSource)
                    {
                        musicSource = audioSources[i];
                        break;
                    }
                }

                if (musicSource == null)
                    musicSource = gameObject.AddComponent<AudioSource>();
            }

            if (musicSource == effectsSource)
                musicSource = gameObject.AddComponent<AudioSource>();

            ConfigureEffectsSource(effectsSource);
            ConfigureMusicSource(musicSource);
        }

        private static void ConfigureEffectsSource(AudioSource source)
        {
            if (source == null)
                return;

            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
        }

        private static void ConfigureMusicSource(AudioSource source)
        {
            if (source == null)
                return;

            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 0f;
            source.ignoreListenerPause = true;
        }

        private void ApplyVolumeSettings()
        {
            EnsureAudioSources();

            float musicVolume = settingsService?.CurrentSettings != null
                ? settingsService.CurrentSettings.musicVolume
                : 1f;
            float effectsVolume = settingsService?.CurrentSettings != null
                ? settingsService.CurrentSettings.effectsVolume
                : 1f;

            musicSource.volume = Mathf.Clamp01(musicVolume * musicVolumeMultiplier);
            effectsSource.volume = Mathf.Clamp01(effectsVolume * effectsVolumeMultiplier);
        }

        private void RefreshMusicPlayback()
        {
            EnsureAudioSources();

            if (backgroundMusicClip == null)
            {
                if (musicSource.isPlaying)
                    musicSource.Stop();

                musicSource.clip = null;
                return;
            }

            bool clipChanged = musicSource.clip != backgroundMusicClip;
            if (clipChanged)
                musicSource.clip = backgroundMusicClip;

            if (clipChanged || !musicSource.isPlaying)
                musicSource.Play();
        }
    }
}

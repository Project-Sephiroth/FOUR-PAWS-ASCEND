using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

public enum SoundEffect
{
    UIClick = 0,
    CutScene,
    Elevator_Open, Elevator_Up,
    Jump, FootStepA, FootStepB, FootStepC,
    Lever, FloorButton,
    MovingFloor, Throw,
    HackingComplete
}

public enum BGM
{
    Intro, Lobby, Map, Malkuth, Yesod, Hod, Netzach, Credits
}

[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public sealed class SoundManager : MonoBehaviour
{
    [Serializable]
    private sealed class SoundEntry
    {
        public SoundEffect effect;
        public AudioClip clip;
    }

    [SerializeField] private SoundEntry[] effects = Array.Empty<SoundEntry>();

    [Serializable]
    private sealed class SceneMusic
    {
        public BGM track;
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 1f;
        public bool loop = true;
    }

    [SerializeField] private SceneMusic[] sceneBgm = Array.Empty<SceneMusic>();

    public static SoundManager Instance { get; private set; }
    private readonly Dictionary<SoundEffect, AudioClip> effectClips = new();
    private readonly Dictionary<BGM, SceneMusic> musicClips = new();
    [SerializeField] private AudioMixer mixer;
    [SerializeField] private AudioSource musicSource;
    [SerializeField] private List<AudioSource> effectSources = new();
    private readonly Dictionary<AudioSource, double> scheduledStarts = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null)
            return;

        SoundManager prefab = Resources.Load<SoundManager>("Audio/SoundManager");
        if (prefab != null)
            Instantiate(prefab);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        Init();

        DontDestroyOnLoad(gameObject);
    }

    private void Init()
    {
        foreach (var entry in effects)
            if (entry != null && entry.clip != null)
                effectClips.TryAdd(entry.effect, entry.clip);

        foreach (var entry in sceneBgm)
            if (entry != null && entry.clip != null)
                musicClips.TryAdd(entry.track, entry);

        if (effectClips.Count == 0 || musicClips.Count == 0)
            Debug.LogWarning($"SoundManager: {effectClips.Count} SFX and {musicClips.Count} BGM clips assigned. Check SoundManager.prefab.", this);
    }

    private void Start()
    {
        if (Instance != this)
            return;

        PlayerSettings.ApplyAudio();
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
        ApplySceneBgm(SceneManager.GetActiveScene());
    }

    private void OnActiveSceneChanged(Scene previous, Scene current) => ApplySceneBgm(current);

    private void ApplySceneBgm(Scene scene)
    {
        string trackName = scene.name == nameof(NetworkGameManager.SceneName.Title) ? nameof(BGM.Intro) : scene.name;
        
        if (!Enum.TryParse(trackName, out BGM track) || trackName != track.ToString() ||
            !musicClips.TryGetValue(track, out var music))
        {
            musicSource.Stop();
            musicSource.clip = null;
            return;
        }

        musicSource.volume = music.volume;
        musicSource.loop = music.loop;
        if (musicSource.clip == music.clip && IsOccupied(musicSource))
            return;

        musicSource.clip = music.clip;
        musicSource.Play();
    }

    private void OnDestroy()
    {
        if (Instance != this)
            return;

        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        Instance = null;
    }

    public static void Play(SoundEffect effect, float volume = 1f) => Instance?.PlayEffect(effect, volume);

    public void PlayEffect(SoundEffect effect) => PlayEffect(effect, 1f);

    private void PlayEffect(SoundEffect effect, float volume)
    {
        if (!effectClips.TryGetValue(effect, out var clip))
            return;

        AudioSource source = GetAvailableEffectSource();
        source.volume = Mathf.Clamp01(volume);
        source.clip = clip;
        if (effect == SoundEffect.Elevator_Up && clip.length > 1f)
        {
            double start = AudioSettings.dspTime + 0.05d;
            scheduledStarts[source] = start;
            source.PlayScheduled(start);
            source.SetScheduledEndTime(start + 1d);
        }
        else
            source.Play();
    }

    private AudioSource GetAvailableEffectSource()
    {
        foreach (AudioSource source in effectSources)
        {
            if (source.clip == null)
                return source;
        }

        var extraSource = gameObject.AddComponent<AudioSource>();
        extraSource.playOnAwake = false;
        extraSource.outputAudioMixerGroup = effectSources[0].outputAudioMixerGroup;
        effectSources.Add(extraSource);
        return extraSource;
    }

    private void LateUpdate()
    {
        if (Instance != this)
            return;

        ReleaseFinishedClip(musicSource);
        foreach (AudioSource source in effectSources)
            ReleaseFinishedClip(source);
    }

    private void ReleaseFinishedClip(AudioSource source)
    {
        if (scheduledStarts.TryGetValue(source, out double start))
        {
            if (AudioSettings.dspTime < start)
                return;
            scheduledStarts.Remove(source);
        }

        if (source.clip != null && !IsOccupied(source))
            source.clip = null;
    }

    private static bool IsOccupied(AudioSource source) =>
        source.isPlaying || AudioListener.pause || source.clip.loadState == AudioDataLoadState.Loading;

    public void SetVolumeLevels(float master, float music, float effects)
    {
        if (mixer == null)
            return;

        mixer.SetFloat("MasterVolume", ToDecibels(master));
        mixer.SetFloat("BgmVolume", ToDecibels(music));
        mixer.SetFloat("SfxVolume", ToDecibels(effects));
    }

    private static float ToDecibels(float volume) =>
        20f * Mathf.Log10(Mathf.Clamp(volume, 0.0001f, 1f));

    public void PlayUIClick() => PlayEffect(SoundEffect.UIClick);
}

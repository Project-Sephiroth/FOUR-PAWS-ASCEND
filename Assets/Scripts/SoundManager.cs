using System;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum SoundEffect
{
    UIClick = 0,
    ElevatorUp = 1,
    ElevatorDown = 2
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

    [SerializeField] private SoundEntry[] effects =
    {
        new SoundEntry { effect = SoundEffect.UIClick },
        new SoundEntry { effect = SoundEffect.ElevatorUp },
        new SoundEntry { effect = SoundEffect.ElevatorDown }
    };

    [Serializable]
    private sealed class SceneMusic
    {
        public string sceneName;
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 1f;
        public bool loop = true;
    }

    [SerializeField] private SceneMusic[] sceneBgm = Array.Empty<SceneMusic>();

    public static SoundManager Instance { get; private set; }
    private AudioSource source;
    private AudioSource musicSource;
    private float musicVolume = 1f;
    private float effectsVolume = 1f;
    private float sceneMusicVolume = 1f;

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
        source = GetComponent<AudioSource>();

        musicSource = gameObject.AddComponent<AudioSource>();
        musicSource.playOnAwake = false;

        DontDestroyOnLoad(gameObject);
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
    }

    private void Start()
    {
        if (Instance == this)
            ApplySceneBgm(SceneManager.GetActiveScene());
    }

    private void OnActiveSceneChanged(Scene previous, Scene current) => ApplySceneBgm(current);

    private void ApplySceneBgm(Scene scene)
    {
        SceneMusic music = Array.Find(sceneBgm, entry => entry != null &&
            entry.clip != null && string.Equals(entry.sceneName, scene.name, StringComparison.Ordinal));

        if (music == null)
        {
            musicSource.Stop();
            musicSource.clip = null;
            return;
        }

        sceneMusicVolume = music.volume;
        musicSource.volume = sceneMusicVolume * musicVolume;
        musicSource.loop = music.loop;
        if (musicSource.clip == music.clip)
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

    public static void Play(SoundEffect effect) => Instance?.PlayEffect(effect);

    public void PlayEffect(SoundEffect effect)
    {
        foreach (SoundEntry entry in effects)
        {
            if (entry.effect != effect || entry.clip == null)
                continue;

            source.PlayOneShot(entry.clip, effectsVolume);
            return;
        }
    }

    public void SetVolumeLevels(float music, float effects)
    {
        musicVolume = music;
        effectsVolume = effects;
        musicSource.volume = sceneMusicVolume * musicVolume;
    }

    public void PlayUIClick() => PlayEffect(SoundEffect.UIClick);
}

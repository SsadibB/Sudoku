using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

// Lives on a GameObject in the MainMenu scene. Survives scene loads via
// DontDestroyOnLoad, so SoundManager.Instance is reachable from any other
// scene as long as MainMenu was the first scene loaded this session.
public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance { get; private set; }

    [Header("Sound Library")]
    [SerializeField] private SoundLibrary library;

    [Header("Music")]
    [SerializeField] private AudioSource musicSource;

    [Header("SFX Pool")]
    [Tooltip("How many SFX can overlap/play at once before the pool grows.")]
    [SerializeField] private int sfxPoolSize = 8;

    private const string MusicVolumeKey = "MusicVolume";
    private const string SfxVolumeKey = "SfxVolume";
    private const string MusicMutedKey = "MusicMuted";
    private const string SfxMutedKey = "SfxMuted";

    private readonly List<AudioSource> sfxPool = new List<AudioSource>();

    private SoundLibrary.MusicEntry currentMusic;
    private Tween musicFadeTween;

    private float musicVolume = 1f;
    private float sfxVolume = 1f;
    private bool musicMuted;
    private bool sfxMuted;

    public bool IsMusicMuted => musicMuted;
    public bool IsSfxMuted => sfxMuted;
    public float MusicVolume => musicVolume;
    public float SfxVolume => sfxVolume;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        LoadVolumePrefs();
        BuildSfxPool();
    }

    private void LoadVolumePrefs()
    {
        musicVolume = PlayerPrefs.GetFloat(MusicVolumeKey, musicVolume);
        sfxVolume = PlayerPrefs.GetFloat(SfxVolumeKey, sfxVolume);
        musicMuted = PlayerPrefs.GetInt(MusicMutedKey, 0) == 1;
        sfxMuted = PlayerPrefs.GetInt(SfxMutedKey, 0) == 1;

        if (musicSource != null)
            musicSource.volume = musicMuted ? 0f : musicVolume;
    }

    private void BuildSfxPool()
    {
        for (int i = 0; i < sfxPoolSize; i++)
        {
            sfxPool.Add(CreateSfxSource());
        }
    }

    private AudioSource CreateSfxSource()
    {
        GameObject go = new GameObject("SFX Source");
        go.transform.SetParent(transform);

        AudioSource src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        return src;
    }

    // ---------------- Music ----------------

    public void PlayMusic(string id, bool fade = true, float fadeDuration = 1f)
    {
        if (library == null)
        {
            Debug.LogWarning("SoundManager: 'library' is not assigned in the Inspector — cannot play music.");
            return;
        }

        if (musicSource == null)
        {
            Debug.LogWarning("SoundManager: 'musicSource' is not assigned in the Inspector — cannot play music.");
            return;
        }

        SoundLibrary.MusicEntry track = library.GetMusic(id);
        if (track == null)
        {
            Debug.LogWarning($"SoundManager: no music entry with id '{id}' in the Sound Library.");
            return;
        }

        if (currentMusic == track && musicSource.isPlaying) return;

        currentMusic = track;
        musicFadeTween?.Kill();

        float targetVolume = musicMuted ? 0f : musicVolume * track.volume;

        if (fade && musicSource.isPlaying)
        {
            musicFadeTween = DOTween.Sequence()
                .Append(musicSource.DOFade(0f, fadeDuration * 0.5f))
                .AppendCallback(() => StartTrack(track))
                .Append(musicSource.DOFade(targetVolume, fadeDuration * 0.5f))
                .SetLink(gameObject);
        }
        else
        {
            StartTrack(track);
            musicSource.volume = targetVolume;
        }
    }

    private void StartTrack(SoundLibrary.MusicEntry track)
    {
        musicSource.clip = track.clip;
        musicSource.loop = track.loop;
        musicSource.Play();
    }

    public void StopMusic(bool fade = true, float fadeDuration = 1f)
    {
        if (musicSource == null) return;

        musicFadeTween?.Kill();
        currentMusic = null;

        if (fade)
        {
            musicFadeTween = musicSource.DOFade(0f, fadeDuration)
                .OnComplete(() => musicSource.Stop())
                .SetLink(gameObject);
        }
        else
        {
            musicSource.Stop();
        }
    }

    // ---------------- SFX ----------------

    public void PlaySFX(string id)
    {
        if (library == null)
        {
            Debug.LogWarning("SoundManager: 'library' is not assigned in the Inspector — cannot play SFX.");
            return;
        }
        if (sfxMuted) return;

        SoundLibrary.SFXEntry sfx = library.GetSFX(id);
        if (sfx == null)
        {
            Debug.LogWarning($"SoundManager: no SFX entry with id '{id}' in the Sound Library.");
            return;
        }

        if (sfx.clips == null || sfx.clips.Length == 0) return;

        AudioSource src = GetFreeSfxSource();
        if (src == null) return;

        AudioClip clip = sfx.clips[Random.Range(0, sfx.clips.Length)];
        src.pitch = Random.Range(sfx.minPitch, sfx.maxPitch);
        src.volume = sfxVolume * sfx.volume;
        src.PlayOneShot(clip);
    }

    private AudioSource GetFreeSfxSource()
    {
        foreach (AudioSource src in sfxPool)
        {
            if (!src.isPlaying) return src;
        }

        // Pool exhausted — grow by one rather than cutting off a sound
        // that's already playing.
        AudioSource extra = CreateSfxSource();
        sfxPool.Add(extra);
        return extra;
    }

    // ---------------- Volume / Mute ----------------

    public void SetMusicVolume(float volume)
    {
        musicVolume = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat(MusicVolumeKey, musicVolume);
        PlayerPrefs.Save();

        if (musicSource != null && !musicMuted)
            musicSource.volume = musicVolume * (currentMusic != null ? currentMusic.volume : 1f);
    }

    public void SetSfxVolume(float volume)
    {
        sfxVolume = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat(SfxVolumeKey, sfxVolume);
        PlayerPrefs.Save();
    }

    public void SetMusicMuted(bool muted)
    {
        musicMuted = muted;
        PlayerPrefs.SetInt(MusicMutedKey, muted ? 1 : 0);
        PlayerPrefs.Save();

        if (musicSource != null)
            musicSource.volume = muted ? 0f : musicVolume * (currentMusic != null ? currentMusic.volume : 1f);
    }

    public void SetSfxMuted(bool muted)
    {
        sfxMuted = muted;
        PlayerPrefs.SetInt(SfxMutedKey, muted ? 1 : 0);
        PlayerPrefs.Save();
    }
}
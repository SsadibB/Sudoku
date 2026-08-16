using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "SoundLibrary", menuName = "AudioMusicSfx/Sound Library")]
public class SoundLibrary : ScriptableObject
{
    [Serializable]
    public class MusicEntry
    {
        public string id;
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 1f;
        public bool loop = true;
    }

    [Serializable]
    public class SFXEntry
    {
        public string id;

        [Tooltip("One clip is picked at random each time this SFX plays. Add just one clip if you don't want variation.")]
        public AudioClip[] clips;

        [Range(0f, 1f)] public float volume = 1f;

        [Header("Pitch Variation (optional)")]
        [Tooltip("Random pitch is picked between min and max each time this SFX plays. Set both to 1 for no variation.")]
        public float minPitch = 1f;
        public float maxPitch = 1f;
    }

    [Header("Add as many of each as you need")]
    public List<MusicEntry> musicTracks = new List<MusicEntry>();
    public List<SFXEntry> sfxEntries = new List<SFXEntry>();

    private Dictionary<string, MusicEntry> musicLookup;
    private Dictionary<string, SFXEntry> sfxLookup;

    public MusicEntry GetMusic(string id)
    {
        BuildLookupsIfNeeded();
        musicLookup.TryGetValue(id, out MusicEntry entry);
        return entry;
    }

    public SFXEntry GetSFX(string id)
    {
        BuildLookupsIfNeeded();
        sfxLookup.TryGetValue(id, out SFXEntry entry);
        return entry;
    }

    private void OnEnable()
    {
        // Force a rebuild next lookup — covers the entries being edited in
        // the Inspector while the asset is loaded (e.g. in the editor).
        musicLookup = null;
        sfxLookup = null;
    }

    private void BuildLookupsIfNeeded()
    {
        if (musicLookup == null)
        {
            musicLookup = new Dictionary<string, MusicEntry>();
            foreach (MusicEntry entry in musicTracks)
            {
                if (entry != null && !string.IsNullOrEmpty(entry.id) && !musicLookup.ContainsKey(entry.id))
                    musicLookup[entry.id] = entry;
            }
        }

        if (sfxLookup == null)
        {
            sfxLookup = new Dictionary<string, SFXEntry>();
            foreach (SFXEntry entry in sfxEntries)
            {
                if (entry != null && !string.IsNullOrEmpty(entry.id) && !sfxLookup.ContainsKey(entry.id))
                    sfxLookup[entry.id] = entry;
            }
        }
    }
}
using System;
using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
[AddComponentMenu("Game/Game Audio Controller")]
public class GameAudioController : MonoBehaviour
{
    public static GameAudioController Instance { get; private set; }

    [Header("Lifetime")]
    [SerializeField] private bool makePersistentAcrossScenes = false;
    [SerializeField] private bool replaceExistingInstance = true;

    [Header("Audio Sources")]
    [SerializeField] private AudioSource ambientSource;
    [SerializeField] private AudioSource sfxSource;
    [SerializeField] private AudioSource uiSource;
    [SerializeField] private AudioSource footstepSource;

    [Header("Block Stack")]
    [Tooltip("Four block stack/landing sounds. The next sound will not repeat the previous one when possible.")]
    [SerializeField] private AudioClip[] blockStackClips = new AudioClip[4];
    [SerializeField, Range(0f, 1f)] private float blockStackVolume = 1f;

    [Header("Block In Water")]
    [SerializeField] private AudioClip[] blockWaterClips = new AudioClip[2];
    [SerializeField, Range(0f, 1f)] private float blockWaterVolume = 1f;

    [Header("Block Destroy")]
    [SerializeField] private AudioClip blockDestroyClip;
    [SerializeField, Range(0f, 1f)] private float blockDestroyVolume = 1f;

    [Header("Ambient")]
    [SerializeField] private AudioClip ambientClip;
    [SerializeField, Range(0f, 1f)] private float ambientVolume = 0.6f;
    [SerializeField] private bool playAmbientOnStart = true;

    [Header("UI")]
    [SerializeField] private AudioClip uiClickClip;
    [SerializeField, Range(0f, 1f)] private float uiClickVolume = 1f;

    [Header("Player Run")]
    [SerializeField] private AudioClip footstepClip;
    [SerializeField, Range(0f, 1f)] private float footstepVolume = 1f;
    [SerializeField, Range(0.5f, 1.5f)] private float footstepPitchMin = 0.95f;
    [SerializeField, Range(0.5f, 1.5f)] private float footstepPitchMax = 1.05f;

    [Header("Win / Defeat")]
    [SerializeField] private AudioClip victoryClip;
    [SerializeField, Range(0f, 1f)] private float victoryVolume = 1f;
    [SerializeField] private bool stopAmbientOnVictory = false;

    [SerializeField] private AudioClip defeatClip;
    [SerializeField, Range(0f, 1f)] private float defeatVolume = 1f;
    [SerializeField] private bool delayReloadForDefeatSound = true;
    [SerializeField, Min(0f)] private float maxDefeatReloadDelay = 1.5f;
    [SerializeField] private bool stopAmbientOnDefeat = false;

    private int lastBlockStackIndex = -1;
    private bool defeatReloadPending;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            if (!replaceExistingInstance)
            {
                Destroy(gameObject);
                return;
            }

            Destroy(Instance.gameObject);
        }

        Instance = this;

        if (makePersistentAcrossScenes)
            DontDestroyOnLoad(gameObject);

        EnsureSources();
        ConfigureSources();
    }

    private void Start()
    {
        if (playAmbientOnStart)
            PlayAmbient();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public static void PlayBlockStack()
    {
        if (Instance != null)
            Instance.PlayBlockStackInternal();
    }

    public static void PlayBlockWaterImpact()
    {
        if (Instance != null)
            Instance.PlayRandomOneShot(Instance.blockWaterClips, Instance.sfxSource, Instance.blockWaterVolume);
    }

    public static void PlayBlockDestroy()
    {
        if (Instance != null)
            Instance.PlayOneShot(Instance.blockDestroyClip, Instance.sfxSource, Instance.blockDestroyVolume);
    }

    public static void PlayUiClick()
    {
        if (Instance != null)
            Instance.PlayOneShot(Instance.uiClickClip, Instance.uiSource, Instance.uiClickVolume);
    }

    public static void PlayFootstep()
    {
        if (Instance == null)
            return;

        Instance.PlayOneShot(
            Instance.footstepClip,
            Instance.footstepSource,
            Instance.footstepVolume,
            Instance.footstepPitchMin,
            Instance.footstepPitchMax);
    }

    public static void PlayVictory()
    {
        if (Instance == null)
            return;

        if (Instance.stopAmbientOnVictory)
            Instance.StopAmbient();

        Instance.PlayOneShot(Instance.victoryClip, Instance.sfxSource, Instance.victoryVolume);
    }

    public static bool TryPlayDefeatBeforeReload(Action reloadAction)
    {
        if (Instance == null)
            return false;

        return Instance.TryPlayDefeatBeforeReloadInternal(reloadAction);
    }

    public void PlayAmbient()
    {
        if (ambientSource == null || ambientClip == null)
            return;

        ambientSource.clip = ambientClip;
        ambientSource.volume = ambientVolume;
        ambientSource.loop = true;

        if (!ambientSource.isPlaying)
            ambientSource.Play();
    }

    public void StopAmbient()
    {
        if (ambientSource != null)
            ambientSource.Stop();
    }

    private void PlayBlockStackInternal()
    {
        int index = PickRandomClipIndex(blockStackClips, lastBlockStackIndex);
        if (index < 0)
            return;

        lastBlockStackIndex = index;
        PlayOneShot(blockStackClips[index], sfxSource, blockStackVolume);
    }

    private bool TryPlayDefeatBeforeReloadInternal(Action reloadAction)
    {
        if (defeatReloadPending)
            return true;

        if (defeatClip == null || defeatVolume <= 0f || !delayReloadForDefeatSound)
            return false;

        defeatReloadPending = true;
        StartCoroutine(PlayDefeatAndReload(reloadAction));
        return true;
    }

    private IEnumerator PlayDefeatAndReload(Action reloadAction)
    {
        if (stopAmbientOnDefeat)
            StopAmbient();

        PlayOneShot(defeatClip, sfxSource, defeatVolume);

        float delay = Mathf.Min(defeatClip.length, maxDefeatReloadDelay);
        if (delay > 0f)
            yield return new WaitForSecondsRealtime(delay);

        reloadAction?.Invoke();
    }

    private void PlayRandomOneShot(AudioClip[] clips, AudioSource source, float volume)
    {
        int index = PickRandomClipIndex(clips, -1);
        if (index < 0)
            return;

        PlayOneShot(clips[index], source, volume);
    }

    private void PlayOneShot(AudioClip clip, AudioSource source, float volume)
    {
        PlayOneShot(clip, source, volume, 1f, 1f);
    }

    private void PlayOneShot(AudioClip clip, AudioSource source, float volume, float pitchMin, float pitchMax)
    {
        if (clip == null || source == null || volume <= 0f)
            return;

        float oldPitch = source.pitch;
        source.pitch = UnityEngine.Random.Range(
            Mathf.Min(pitchMin, pitchMax),
            Mathf.Max(pitchMin, pitchMax));

        source.PlayOneShot(clip, volume);
        source.pitch = oldPitch;
    }

    private static int PickRandomClipIndex(AudioClip[] clips, int previousIndex)
    {
        if (clips == null || clips.Length == 0)
            return -1;

        int validCount = 0;
        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i] != null)
                validCount++;
        }

        if (validCount == 0)
            return -1;

        if (validCount == 1)
        {
            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i] != null)
                    return i;
            }
        }

        int pick = UnityEngine.Random.Range(0, validCount - (IsValidPrevious(clips, previousIndex) ? 1 : 0));

        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i] == null)
                continue;

            if (i == previousIndex)
                continue;

            if (pick == 0)
                return i;

            pick--;
        }

        return -1;
    }

    private static bool IsValidPrevious(AudioClip[] clips, int previousIndex)
    {
        return previousIndex >= 0
               && previousIndex < clips.Length
               && clips[previousIndex] != null;
    }

    private void EnsureSources()
    {
        if (ambientSource == null)
            ambientSource = CreateSource("Ambient Audio");
        if (sfxSource == null)
            sfxSource = CreateSource("SFX Audio");
        if (uiSource == null)
            uiSource = CreateSource("UI Audio");
        if (footstepSource == null)
            footstepSource = CreateSource("Footstep Audio");
    }

    private AudioSource CreateSource(string sourceName)
    {
        GameObject go = new GameObject(sourceName);
        go.transform.SetParent(transform, false);
        return go.AddComponent<AudioSource>();
    }

    private void ConfigureSources()
    {
        Configure2DSource(ambientSource);
        Configure2DSource(sfxSource);
        Configure2DSource(uiSource);
        Configure2DSource(footstepSource);
    }

    private static void Configure2DSource(AudioSource source)
    {
        if (source == null)
            return;

        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.ignoreListenerPause = true;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (footstepPitchMin > footstepPitchMax)
            footstepPitchMax = footstepPitchMin;
    }
#endif
}

using UnityEngine;
using UnityEngine.UI;

public class MainMenuController : MonoBehaviour
{
    [Header("Scenes")]
    [SerializeField] private string firstLevelSceneName = "Level1";

    [Header("Audio Settings")]
    [SerializeField] private Button sfxButton;
    [SerializeField] private Button musicButton;
    [SerializeField] private Slider sfxSlider;
    [SerializeField] private Slider musicSlider;

    private void OnEnable()
    {
        if (sfxButton != null)
            sfxButton.onClick.AddListener(ToggleSfx);
        if (musicButton != null)
            musicButton.onClick.AddListener(ToggleMusic);
        if (sfxSlider != null)
            sfxSlider.onValueChanged.AddListener(GameAudioController.SetSfxVolume);
        if (musicSlider != null)
            musicSlider.onValueChanged.AddListener(GameAudioController.SetMusicVolume);

        GameAudioController.VolumeChanged += RefreshAudioSliders;
        RefreshAudioSliders();
    }

    private void OnDisable()
    {
        if (sfxButton != null)
            sfxButton.onClick.RemoveListener(ToggleSfx);
        if (musicButton != null)
            musicButton.onClick.RemoveListener(ToggleMusic);
        if (sfxSlider != null)
            sfxSlider.onValueChanged.RemoveListener(GameAudioController.SetSfxVolume);
        if (musicSlider != null)
            musicSlider.onValueChanged.RemoveListener(GameAudioController.SetMusicVolume);

        GameAudioController.VolumeChanged -= RefreshAudioSliders;
        PlayerPrefs.Save();
    }

    private void ToggleSfx()
    {
        GameAudioController.SetSfxVolume(GameAudioController.SfxVolume > 0f ? 0f : 1f);
    }

    private void ToggleMusic()
    {
        GameAudioController.SetMusicVolume(GameAudioController.MusicVolume > 0f ? 0f : 1f);
    }

    private void RefreshAudioSliders()
    {
        if (sfxSlider != null)
            sfxSlider.SetValueWithoutNotify(GameAudioController.SfxVolume);
        if (musicSlider != null)
            musicSlider.SetValueWithoutNotify(GameAudioController.MusicVolume);
    }

    private void Start()
    {
        Time.timeScale = 1f;
    }

    public void StartGame()
    {
        if (SceneTransitionManager.Instance == null)
        {
            Debug.LogError("SceneTransitionManager not found in scene.");
            return;
        }

        SceneTransitionManager.Instance.LoadScene(firstLevelSceneName);
    }

    public void QuitGame()
    {
        Application.Quit();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}

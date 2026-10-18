using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[AddComponentMenu("Game/UI/Audio Settings")]
public class AudioSettingsUI : MonoBehaviour
{
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

        GameAudioController.VolumeChanged += RefreshSliders;
        RefreshSliders();
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

        GameAudioController.VolumeChanged -= RefreshSliders;
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

    private void RefreshSliders()
    {
        if (sfxSlider != null)
            sfxSlider.SetValueWithoutNotify(GameAudioController.SfxVolume);
        if (musicSlider != null)
            musicSlider.SetValueWithoutNotify(GameAudioController.MusicVolume);
    }
}

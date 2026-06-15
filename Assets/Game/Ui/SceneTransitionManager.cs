using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneTransitionManager : MonoBehaviour
{
    public static SceneTransitionManager Instance { get; private set; }

    [Header("Fade")]
    [SerializeField] private CanvasGroup fadeCanvasGroup;
    [SerializeField] private float fadeDuration = 0.5f;
    [SerializeField] private bool fadeInOnStart = true;

    private bool isTransitioning;

    public bool IsTransitioning => isTransitioning;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (fadeCanvasGroup != null)
        {
            SetFadeAlpha(1f);
            fadeCanvasGroup.blocksRaycasts = true;
            fadeCanvasGroup.interactable = false;
        }
    }

    private void Start()
    {
        if (fadeCanvasGroup == null)
            return;

        if (fadeInOnStart)
        {
            StartCoroutine(FadeRoutine(1f, 0f));
        }
        else
        {
            SetFadeAlpha(0f);
            fadeCanvasGroup.blocksRaycasts = false;
        }
    }

    public void LoadScene(string sceneName)
    {
        if (isTransitioning)
            return;

        StartCoroutine(LoadSceneRoutine(sceneName));
    }

    public void ReloadCurrentScene()
    {
        string currentSceneName = SceneManager.GetActiveScene().name;
        LoadScene(currentSceneName);
    }

    private IEnumerator LoadSceneRoutine(string sceneName)
    {
        isTransitioning = true;

        Time.timeScale = 1f;

        yield return FadeRoutine(0f, 1f);

        AsyncOperation loadingOperation = SceneManager.LoadSceneAsync(sceneName);

        while (!loadingOperation.isDone)
        {
            yield return null;
        }

        yield return null;

        yield return FadeRoutine(1f, 0f);

        isTransitioning = false;
    }

    private IEnumerator FadeRoutine(float from, float to)
    {
        if (fadeCanvasGroup == null)
            yield break;

        fadeCanvasGroup.blocksRaycasts = true;
        fadeCanvasGroup.interactable = false;

        if (fadeDuration <= 0f)
        {
            SetFadeAlpha(to);
            fadeCanvasGroup.blocksRaycasts = to > 0.01f;
            yield break;
        }

        float timer = 0f;

        while (timer < fadeDuration)
        {
            timer += Time.unscaledDeltaTime;

            float progress = timer / fadeDuration;
            float alpha = Mathf.Lerp(from, to, progress);

            SetFadeAlpha(alpha);

            yield return null;
        }

        SetFadeAlpha(to);

        fadeCanvasGroup.blocksRaycasts = to > 0.01f;
    }

    private void SetFadeAlpha(float alpha)
    {
        fadeCanvasGroup.alpha = Mathf.Clamp01(alpha);
    }
}

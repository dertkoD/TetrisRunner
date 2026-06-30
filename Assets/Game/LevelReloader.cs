using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Перезагрузка текущей сцены по запросу. Гарантирует, что параллельные
/// вызовы (например, и игрок попал в DeathWater, и одновременно блок
/// раздавил его) не приведут к двойному <see cref="SceneManager.LoadScene"/>.
/// </summary>
public static class LevelReloader
{
    private static bool reloadScheduled;
    private static int subscribedFrame = -1;

    /// <summary>
    /// Перезагружает активную сцену. Безопасно вызывать многократно за один
    /// кадр — повторные вызовы игнорируются до завершения предыдущего ребута.
    /// </summary>
    public static void RequestReload()
    {
        if (reloadScheduled)
            return;

        reloadScheduled = true;

        if (GameAudioController.TryPlayDefeatBeforeReload(LoadActiveSceneNow))
            return;

        LoadActiveSceneNow();
    }

    private static void LoadActiveSceneNow()
    {
        // SceneManager.LoadScene на следующем кадре сбрасывает все статические
        // подписки на sceneLoaded ниже Awake, поэтому мы вешаем хук вручную,
        // чтобы reloadScheduled был выставлен в false ПОСЛЕ загрузки сцены.
        if (subscribedFrame != Time.frameCount)
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            subscribedFrame = Time.frameCount;
        }

        Scene active = SceneManager.GetActiveScene();
        SceneManager.LoadScene(active.buildIndex, LoadSceneMode.Single);
    }

    private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        reloadScheduled = false;
        SceneManager.sceneLoaded -= HandleSceneLoaded;
    }
}

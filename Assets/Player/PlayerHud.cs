using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD игрока: показывает HP массивом из 3-х (или N) Image и оставшийся запас
/// заморозки через Image.fillAmount.
///
/// Подключается к <see cref="PlayerHealth"/> и <see cref="PlayerBlockFreeze"/>
/// через события (никакого Update-poll). Если ссылки не назначены — попытается
/// найти их в сцене сама.
/// </summary>
[DisallowMultipleComponent]
public class PlayerHud : MonoBehaviour
{
    [Header("Refs (optional — иначе ищем сами)")]
    [SerializeField] private PlayerFacade playerFacade;
    [SerializeField] private PlayerHealth health;
    [SerializeField] private PlayerBlockFreeze blockFreeze;

    [Header("Health icons")]
    [Tooltip("Image-и сердечек/щитов. По умолчанию ожидается 3. Индекс 0 — самое первое HP, " +
             "индекс N-1 — самое последнее.")]
    [SerializeField] private Image[] healthIcons;

    [Tooltip("Если true — потерянные HP полностью скрываются (image.enabled = false). " +
             "Если false — становятся полупрозрачными (Lost Health Alpha).")]
    [SerializeField] private bool hideLostHealth = true;
    [SerializeField, Range(0f, 1f)] private float lostHealthAlpha = 0.25f;

    [Header("Freeze meter")]
    [Tooltip("Image с типом Filled (Horizontal/Vertical, любая FillMethod). " +
             "Значение fillAmount будет = current/max способности заморозки (1..0).")]
    [SerializeField] private Image freezeFillImage;

    [Tooltip("Дополнительно красить freezeFillImage в активном/неактивном цвете.")]
    [SerializeField] private bool tintFreezeImage = false;
    [SerializeField] private Color freezeIdleColor = Color.white;
    [SerializeField] private Color freezeActiveColor = new Color(0.4f, 0.85f, 1f, 1f);
    [SerializeField] private Color freezeEmptyColor = new Color(0.6f, 0.2f, 0.2f, 1f);

    private bool subscribedHealth;
    private bool subscribedFreeze;

    private void Awake()
    {
        if (playerFacade == null)
            playerFacade = FindFirstObjectByType<PlayerFacade>();

        if (health == null && playerFacade != null)
            health = playerFacade.Health;

        if (health == null)
            health = FindFirstObjectByType<PlayerHealth>();

        if (blockFreeze == null && playerFacade != null)
            blockFreeze = playerFacade.GetComponent<PlayerBlockFreeze>();

        if (blockFreeze == null)
            blockFreeze = FindFirstObjectByType<PlayerBlockFreeze>();
    }

    private void OnEnable()
    {
        SubscribeAll();
        RefreshAll();
    }

    private void OnDisable()
    {
        UnsubscribeAll();
    }

    private void SubscribeAll()
    {
        if (health != null && !subscribedHealth)
        {
            health.OnHealthChanged.AddListener(OnHealthChanged);
            subscribedHealth = true;
        }

        if (blockFreeze != null && !subscribedFreeze)
        {
            blockFreeze.OnBudgetChanged.AddListener(OnFreezeChanged);
            blockFreeze.OnActivated.AddListener(OnFreezeActivated);
            blockFreeze.OnDeactivated.AddListener(OnFreezeDeactivated);
            subscribedFreeze = true;
        }
    }

    private void UnsubscribeAll()
    {
        if (health != null && subscribedHealth)
        {
            health.OnHealthChanged.RemoveListener(OnHealthChanged);
            subscribedHealth = false;
        }

        if (blockFreeze != null && subscribedFreeze)
        {
            blockFreeze.OnBudgetChanged.RemoveListener(OnFreezeChanged);
            blockFreeze.OnActivated.RemoveListener(OnFreezeActivated);
            blockFreeze.OnDeactivated.RemoveListener(OnFreezeDeactivated);
            subscribedFreeze = false;
        }
    }

    private void RefreshAll()
    {
        if (health != null)
            OnHealthChanged(health.CurrentHealth, health.MaxHealth);

        if (blockFreeze != null)
        {
            OnFreezeChanged(blockFreeze.Budget, blockFreeze.MaxBudget);
            if (blockFreeze.IsActive) OnFreezeActivated();
            else OnFreezeDeactivated();
        }
    }

    private void OnHealthChanged(int current, int max)
    {
        if (healthIcons == null || healthIcons.Length == 0)
            return;

        for (int i = 0; i < healthIcons.Length; i++)
        {
            Image img = healthIcons[i];
            if (img == null) continue;

            bool alive = i < current;

            if (hideLostHealth)
            {
                img.enabled = alive;
            }
            else
            {
                img.enabled = true;
                Color c = img.color;
                c.a = alive ? 1f : lostHealthAlpha;
                img.color = c;
            }
        }
    }

    private void OnFreezeChanged(float current, float max)
    {
        if (freezeFillImage == null)
            return;

        freezeFillImage.fillAmount = max > 0f ? Mathf.Clamp01(current / max) : 0f;

        if (tintFreezeImage && !blockFreeze.IsActive)
        {
            // Если способность не активна — мигаем "пусто/готово" по запасу.
            freezeFillImage.color = current <= 0.001f ? freezeEmptyColor : freezeIdleColor;
        }
    }

    private void OnFreezeActivated()
    {
        if (tintFreezeImage && freezeFillImage != null)
            freezeFillImage.color = freezeActiveColor;
    }

    private void OnFreezeDeactivated()
    {
        if (tintFreezeImage && freezeFillImage != null)
            freezeFillImage.color = blockFreeze != null && blockFreeze.Budget > 0.001f
                ? freezeIdleColor
                : freezeEmptyColor;
    }
}

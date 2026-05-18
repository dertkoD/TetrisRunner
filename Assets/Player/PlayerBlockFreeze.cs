using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

/// <summary>
/// Способность "Заморозить блоки". Игрок удерживает кнопку — блоки в сетке
/// не двигаются (через <see cref="TetrisBlockSpawnManager.SetExternalFreeze"/>).
/// Доступное время ограничено (<see cref="PlayerConfigSO.FreezeMaxDuration"/>),
/// способность медленно восстанавливается, когда не используется
/// (<see cref="PlayerConfigSO.FreezeRechargeSecondsPerSecond"/>).
///
/// <see cref="BudgetFraction"/> от 0 до 1 удобно отдавать в Image.fillAmount
/// для UI-шкалы.
/// </summary>
[DisallowMultipleComponent]
public class PlayerBlockFreeze : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private PlayerConfigSO config;

    [Tooltip("Менеджер тетрис-блоков. Если пусто — найдём в сцене.")]
    [SerializeField] private TetrisBlockSpawnManager spawnManager;

    [Header("Events")]
    /// <summary>Аргументы: (current seconds, max seconds).</summary>
    public UnityEvent<float, float> OnBudgetChanged = new UnityEvent<float, float>();
    public UnityEvent OnActivated = new UnityEvent();
    public UnityEvent OnDeactivated = new UnityEvent();

    private InputAction freezeAction;
    private float budget;
    private float maxBudget;
    private bool requestActive;
    private bool isActive;
    private bool exhaustedSinceRelease;

    public float Budget => budget;
    public float MaxBudget => maxBudget;
    public float BudgetFraction => maxBudget > 0f ? Mathf.Clamp01(budget / maxBudget) : 0f;
    public bool IsActive => isActive;

    private void Awake()
    {
        if (config == null)
        {
            Debug.LogError($"{nameof(PlayerBlockFreeze)}: Config is not assigned.", this);
            enabled = false;
            return;
        }

        maxBudget = Mathf.Max(0f, config.FreezeMaxDuration);
        budget = maxBudget;

        freezeAction = config.FreezeAction != null ? config.FreezeAction.action : null;

        if (freezeAction == null)
            Debug.LogWarning(
                $"{nameof(PlayerBlockFreeze)}: Freeze action не задан в PlayerConfigSO — " +
                "способность работать не будет.", this);
    }

    private void Start()
    {
        if (spawnManager == null)
            spawnManager = FindFirstObjectByType<TetrisBlockSpawnManager>();

        OnBudgetChanged?.Invoke(budget, maxBudget);
    }

    private void OnEnable()
    {
        if (freezeAction == null)
            return;

        freezeAction.performed += OnFreezePressed;
        freezeAction.canceled += OnFreezeReleased;
        freezeAction.Enable();
    }

    private void OnDisable()
    {
        if (freezeAction != null)
        {
            freezeAction.performed -= OnFreezePressed;
            freezeAction.canceled -= OnFreezeReleased;
            freezeAction.Disable();
        }

        SetActive(false);
    }

    private void OnFreezePressed(InputAction.CallbackContext ctx)
    {
        requestActive = true;
    }

    private void OnFreezeReleased(InputAction.CallbackContext ctx)
    {
        requestActive = false;
        exhaustedSinceRelease = false;
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        bool canActivate = requestActive && !exhaustedSinceRelease;

        // Если запас полностью иссяк во время удержания — заставим игрока
        // сначала отпустить кнопку и подождать минимума, прежде чем снова
        // нажать. Иначе при микро-зарядке заморозка дёргается.
        if (canActivate && budget <= 0f)
        {
            exhaustedSinceRelease = true;
            canActivate = false;
        }

        if (canActivate)
        {
            budget = Mathf.Max(0f, budget - dt);
            SetActive(true);
        }
        else
        {
            float recharge = Mathf.Max(0f, config.FreezeRechargeSecondsPerSecond);
            if (recharge > 0f && budget < maxBudget)
                budget = Mathf.Min(maxBudget, budget + recharge * dt);

            // Когда запас восстановился до порога — даём возможность снова активировать.
            if (exhaustedSinceRelease && budget >= Mathf.Max(0f, config.FreezeMinToReactivate))
                exhaustedSinceRelease = false;

            SetActive(false);
        }

        OnBudgetChanged?.Invoke(budget, maxBudget);
    }

    private void SetActive(bool active)
    {
        if (isActive == active)
            return;

        isActive = active;

        if (spawnManager != null)
            spawnManager.SetExternalFreeze(active);

        if (active)
            OnActivated?.Invoke();
        else
            OnDeactivated?.Invoke();
    }
}

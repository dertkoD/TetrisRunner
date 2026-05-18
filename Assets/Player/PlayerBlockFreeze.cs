using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

/// <summary>
/// Способность "Заморозить блоки". Работает по принципу ТОГГЛА: одно нажатие
/// включает заморозку, повторное нажатие — выключает. Удерживать кнопку не нужно.
///
/// Пока заморозка активна, блоки в сетке не двигаются (через
/// <see cref="TetrisBlockSpawnManager.SetExternalFreeze"/>) и расходуется запас
/// (<see cref="PlayerConfigSO.FreezeMaxDuration"/> секунд). Когда запас иссякает,
/// заморозка автоматически отключается. Между активациями способность
/// восстанавливается со скоростью
/// <see cref="PlayerConfigSO.FreezeRechargeSecondsPerSecond"/> секунд за секунду.
/// Включить заново можно, когда запас вырос как минимум до
/// <see cref="PlayerConfigSO.FreezeMinToReactivate"/>.
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
    private bool toggleActive;
    private bool isActive;

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
        freezeAction.Enable();
    }

    private void OnDisable()
    {
        if (freezeAction != null)
        {
            freezeAction.performed -= OnFreezePressed;
            freezeAction.Disable();
        }

        toggleActive = false;
        SetActive(false);
    }

    private void OnFreezePressed(InputAction.CallbackContext ctx)
    {
        // Toggle. Каждое нажатие переключает: вкл → выкл и наоборот.
        if (toggleActive)
        {
            toggleActive = false;
            return;
        }

        // Нельзя включить, если запас слишком мал — заставим подождать перезарядки.
        float minToActivate = Mathf.Max(0f, config.FreezeMinToReactivate);
        if (budget <= minToActivate)
            return;

        toggleActive = true;
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        if (toggleActive)
        {
            // Заморозка активна: тратим запас. Когда дошли до нуля — автоматически выключаемся.
            budget -= dt;
            if (budget <= 0f)
            {
                budget = 0f;
                toggleActive = false;
            }

            SetActive(toggleActive);
        }
        else
        {
            // Заморозка не активна: восстанавливаем запас.
            float recharge = Mathf.Max(0f, config.FreezeRechargeSecondsPerSecond);
            if (recharge > 0f && budget < maxBudget)
                budget = Mathf.Min(maxBudget, budget + recharge * dt);

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

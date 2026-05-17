using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Здоровье игрока. Простые методы получения урона и хилинга,
/// плюс события для UI/анимаций и обработки смерти.
/// </summary>
public class PlayerHealth : MonoBehaviour
{
    [Header("Config")]
    [Tooltip("Конфиг игрока. Если задан — максимальное здоровье берётся отсюда. " +
             "Можно оставить пустым и проставить значения вручную ниже.")]
    [SerializeField] private PlayerConfigSO config;

    [Header("Manual Override (используется, если конфиг не задан)")]
    [SerializeField, Min(1)] private int manualMaxHealth = 3;

    [Header("Behaviour")]
    [Tooltip("Если true, при получении урона ниже нуля игрок умирает. " +
             "Если false — здоровье просто упирается в 0, но событие смерти не вызывается.")]
    [SerializeField] private bool dieAtZero = true;

    [Header("Events")]
    public UnityEvent<int, int> OnHealthChanged = new UnityEvent<int, int>();
    public UnityEvent<int> OnDamaged = new UnityEvent<int>();
    public UnityEvent<int> OnHealed = new UnityEvent<int>();
    public UnityEvent OnDied = new UnityEvent();

    private int currentHealth;
    private int maxHealth;
    private bool isDead;

    public int CurrentHealth => currentHealth;
    public int MaxHealth => maxHealth;
    public bool IsDead => isDead;

    private void Awake()
    {
        maxHealth = ResolveMaxHealth();
        currentHealth = maxHealth;
        isDead = false;
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }

    /// <summary>Наносит игроку урон. Возвращает true, если урон был применён.</summary>
    public bool TakeDamage(int amount)
    {
        if (isDead)
            return false;

        if (amount <= 0)
            return false;

        int before = currentHealth;
        currentHealth = Mathf.Max(0, currentHealth - amount);

        int applied = before - currentHealth;
        if (applied <= 0)
            return false;

        OnDamaged?.Invoke(applied);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);

        if (dieAtZero && currentHealth <= 0)
            Die();

        return true;
    }

    /// <summary>Восстанавливает здоровье игрока. Возвращает true, если хил был применён.</summary>
    public bool Heal(int amount)
    {
        if (isDead)
            return false;

        if (amount <= 0)
            return false;

        int before = currentHealth;
        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);

        int applied = currentHealth - before;
        if (applied <= 0)
            return false;

        OnHealed?.Invoke(applied);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
        return true;
    }

    /// <summary>Полностью восстанавливает здоровье и снимает флаг смерти.</summary>
    public void FullRestore()
    {
        maxHealth = ResolveMaxHealth();
        currentHealth = maxHealth;
        isDead = false;
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }

    private void Die()
    {
        isDead = true;
        OnDied?.Invoke();
    }

    private int ResolveMaxHealth()
    {
        if (config != null)
            return Mathf.Max(1, config.MaxHealth);

        return Mathf.Max(1, manualMaxHealth);
    }
}

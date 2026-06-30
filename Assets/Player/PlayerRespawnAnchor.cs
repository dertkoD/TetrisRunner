using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Точка возрождения игрока. По умолчанию <see cref="Respawn"/> телепортирует
/// игрока в <see cref="spawnPoint"/> (Transform, который дизайнер ставит на сцене).
/// Если spawnPoint не задан — используется fallback: последняя точка прыжка
/// (записывается из <see cref="PlayerStateMachine"/> при каждом удачном прыжке)
/// или стартовая позиция игрока.
///
/// Также при смерти игрока (<see cref="PlayerHealth.OnDied"/>) и/или при любом
/// получении урона (<see cref="PlayerHealth.OnDamaged"/>) перезагружает текущую
/// сцену — поведением управляют <see cref="reloadSceneOnDeath"/> и
/// <see cref="reloadSceneOnAnyDamage"/>.
/// </summary>
public class PlayerRespawnAnchor : MonoBehaviour
{
    [Header("Refs")]
    [Tooltip("Тело игрока. Если пусто, попробуем найти Rigidbody2D в родителях.")]
    [SerializeField] private Rigidbody2D body;

    [Tooltip("HP игрока. Используется, чтобы перезагрузить сцену при OnDied/OnDamaged. " +
             "Если пусто — найдётся через GetComponentInParent.")]
    [SerializeField] private PlayerHealth health;

    [Header("Spawn Point")]
    [Tooltip("Точка возрождения в сцене. Когда задана — Respawn() телепортирует именно сюда. " +
             "Если оставить пусто, используется fallback по last-jump или стартовой позиции.")]
    [SerializeField] private Transform spawnPoint;

    [Header("Death")]
    [Tooltip("Если true — при OnDied у PlayerHealth (HP опустился до 0) текущая сцена " +
             "перезагружается через SceneManager.LoadScene.")]
    [SerializeField] private bool reloadSceneOnDeath = true;

    [Tooltip("Если true — текущая сцена перезагружается при ЛЮБОМ получении урона " +
             "(а не только при смерти). Удобно, когда игроку нельзя получать урон " +
             "вообще — любое попадание мгновенно ресетит уровень.")]
    [SerializeField] private bool reloadSceneOnAnyDamage = true;

    [Header("Fallback Initial")]
    [Tooltip("Если true и spawnPoint не задан — стартовая позиция игрока становится первым " +
             "чекпоинтом, чтобы первая смерть до прыжка не отправляла его в (0;0).")]
    [SerializeField] private bool useStartPositionAsInitialCheckpoint = true;

    private Vector2 lastCheckpoint;
    private bool hasCheckpoint;
    private bool subscribedToDied;
    private bool subscribedToDamaged;
    private bool reloadScheduled;

    public bool HasCheckpoint => hasCheckpoint;
    public Vector2 LastCheckpoint => lastCheckpoint;
    public Transform SpawnPoint { get => spawnPoint; set => spawnPoint = value; }

    private void Awake()
    {
        if (body == null)
            body = GetComponentInParent<Rigidbody2D>();

        if (health == null)
            health = GetComponentInParent<PlayerHealth>();
    }

    private void OnEnable()
    {
        if (health == null)
            return;

        if (!subscribedToDied)
        {
            health.OnDied.AddListener(OnPlayerDied);
            subscribedToDied = true;
        }

        if (!subscribedToDamaged)
        {
            health.OnDamaged.AddListener(OnPlayerDamaged);
            subscribedToDamaged = true;
        }
    }

    private void OnDisable()
    {
        if (health == null)
            return;

        if (subscribedToDied)
        {
            health.OnDied.RemoveListener(OnPlayerDied);
            subscribedToDied = false;
        }

        if (subscribedToDamaged)
        {
            health.OnDamaged.RemoveListener(OnPlayerDamaged);
            subscribedToDamaged = false;
        }
    }

    private void Start()
    {
        if (useStartPositionAsInitialCheckpoint && !hasCheckpoint)
        {
            Vector2 pos = body != null ? body.position : (Vector2)transform.position;
            RecordCheckpoint(pos);
        }
    }

    /// <summary>Запоминает позицию как новый чекпоинт (fallback, если spawnPoint не задан).</summary>
    public void RecordCheckpoint(Vector2 worldPosition)
    {
        lastCheckpoint = worldPosition;
        hasCheckpoint = true;
    }

    /// <summary>
    /// Телепортирует игрока в точку возрождения. Приоритет:
    ///   1) spawnPoint (Transform из инспектора),
    ///   2) последний записанный checkpoint (last-jump или стартовая позиция).
    /// Возвращает true, если телепорт состоялся.
    /// </summary>
    public bool Respawn()
    {
        Vector2 target;

        if (spawnPoint != null)
        {
            target = spawnPoint.position;
        }
        else if (hasCheckpoint)
        {
            target = lastCheckpoint;
        }
        else
        {
            return false;
        }

        if (body != null)
        {
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
            body.position = target;
            body.transform.position = target;
        }
        else
        {
            transform.position = target;
        }

        return true;
    }

    private void OnPlayerDied()
    {
        if (!reloadSceneOnDeath)
            return;

        ReloadActiveScene();
    }

    private void OnPlayerDamaged(int amount)
    {
        if (!reloadSceneOnAnyDamage)
            return;

        if (amount <= 0)
            return;

        ReloadActiveScene();
    }

    private void ReloadActiveScene()
    {
        // Если параллельно прилетают и OnDamaged, и OnDied (когда удар добил
        // игрока), не дёргаем LoadScene дважды.
        if (reloadScheduled)
            return;

        reloadScheduled = true;
        LevelReloader.RequestReload();
    }
}

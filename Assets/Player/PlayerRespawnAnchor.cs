using UnityEngine;

/// <summary>
/// Запоминает, где находился игрок в последний раз, когда совершил прыжок,
/// и умеет вернуть его в эту точку (например, после попадания в Deathzone).
///
/// Точку обновляет PlayerStateMachine при каждом успешном прыжке.
/// </summary>
public class PlayerRespawnAnchor : MonoBehaviour
{
    [Header("Refs")]
    [Tooltip("Тело игрока. Если пусто, попробуем найти Rigidbody2D в родителях.")]
    [SerializeField] private Rigidbody2D body;

    [Header("Initial")]
    [Tooltip("Если true — стартовая позиция игрока становится первым чекпоинтом, " +
             "чтобы первая смерть до прыжка не отправляла его в (0;0).")]
    [SerializeField] private bool useStartPositionAsInitialCheckpoint = true;

    private Vector2 lastCheckpoint;
    private bool hasCheckpoint;

    public bool HasCheckpoint => hasCheckpoint;
    public Vector2 LastCheckpoint => lastCheckpoint;

    private void Awake()
    {
        if (body == null)
            body = GetComponentInParent<Rigidbody2D>();
    }

    private void Start()
    {
        if (useStartPositionAsInitialCheckpoint && !hasCheckpoint)
        {
            Vector2 pos = body != null ? body.position : (Vector2)transform.position;
            RecordCheckpoint(pos);
        }
    }

    /// <summary>Запоминает позицию как новый чекпоинт.</summary>
    public void RecordCheckpoint(Vector2 worldPosition)
    {
        lastCheckpoint = worldPosition;
        hasCheckpoint = true;
    }

    /// <summary>Телепортирует игрока в сохранённую точку. Возвращает true, если чекпоинт был.</summary>
    public bool Respawn()
    {
        if (!hasCheckpoint)
            return false;

        if (body != null)
        {
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
            body.position = lastCheckpoint;
            body.transform.position = lastCheckpoint;
        }
        else
        {
            transform.position = lastCheckpoint;
        }

        return true;
    }
}

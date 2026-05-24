using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Определяет, стоит ли игрок на земле. Раньше детектор полагался только на
/// нормали из <see cref="Collision2D"/>: контакты по углам блока часто давали
/// диагональные нормали с <c>y &lt; MinGroundNormalY</c>, поэтому стоя на краю
/// блока или вплотную к соседнему игрок временами «терял землю» и не прыгал.
///
/// Теперь рядом с collision-based детектором добавлена надёжная физическая
/// проба <see cref="Physics2D.OverlapBox"/>: на каждом FixedUpdate под нижней
/// гранью коллайдера игрока ищется любой не-триггерный коллайдер с подходящего
/// слоя <see cref="PlayerConfigSO.GroundLayers"/>. Если он есть — игрок считается
/// заземлённым, даже если коллизионные нормали этого ещё не подтвердили.
/// Это убирает «провисания» прыжка на краях и вплотную к другим блокам.
/// </summary>
public class PlayerGroundChecker : MonoBehaviour
{
    [Header("Config")]
    [SerializeField] private PlayerConfigSO config;

    [Header("Overlap Probe")]
    [Tooltip("Главный коллайдер игрока, по которому считается нижняя грань для пробы. " +
             "Если пусто — будет найден первый не-триггерный Collider2D на этом GameObject.")]
    [SerializeField] private Collider2D playerCollider;

    [Tooltip("На сколько мировых единиц проба выходит вниз под нижнюю грань коллайдера. " +
             "Слишком маленькое — будут false-negative на краях; слишком большое — игрок " +
             "будет считаться заземлённым, ещё паря в воздухе.")]
    [SerializeField, Min(0.005f)] private float probeDistance = 0.06f;

    [Tooltip("На сколько ужать ширину пробы относительно ширины коллайдера. 0 — точно по " +
             "ширине, 0.1 — пробa уже коллайдера на 10%. Узкая проба не цепляет соседние " +
             "стены и не ложно срабатывает, когда игрок просто прижимается к стене.")]
    [SerializeField, Range(0f, 0.5f)] private float probeWidthShrink = 0.1f;

    private readonly HashSet<Collider2D> groundContacts = new HashSet<Collider2D>();
    private readonly Collider2D[] probeBuffer = new Collider2D[8];

    private bool overlapProbeFoundGround;

    /// <summary>True, если игрок касается земли (по столкновениям или по физической пробе).</summary>
    public bool IsGrounded => groundContacts.Count > 0 || overlapProbeFoundGround;

    private void Awake()
    {
        ResolveColliderIfNeeded();
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        EvaluateCollision(collision);
    }

    private void OnCollisionStay2D(Collision2D collision)
    {
        EvaluateCollision(collision);
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        groundContacts.Remove(collision.collider);
    }

    private void OnDisable()
    {
        groundContacts.Clear();
        overlapProbeFoundGround = false;
    }

    private void FixedUpdate()
    {
        overlapProbeFoundGround = ProbeGroundUnderFeet();
    }

    /// <summary>
    /// Физическая проба под ногами. Возвращает true, если в узкой полоске ровно
    /// под нижней гранью коллайдера игрока есть хотя бы один не-триггерный
    /// коллайдер на одном из слоёв <see cref="PlayerConfigSO.GroundLayers"/>.
    /// </summary>
    private bool ProbeGroundUnderFeet()
    {
        if (config == null)
            return false;

        ResolveColliderIfNeeded();

        if (playerCollider == null || !playerCollider.enabled)
            return false;

        Bounds b = playerCollider.bounds;

        float width = Mathf.Max(0.01f, b.size.x * (1f - probeWidthShrink));
        float height = Mathf.Max(0.005f, probeDistance);

        Vector2 center = new Vector2(b.center.x, b.min.y - height * 0.5f);
        Vector2 size = new Vector2(width, height);

        int hitCount = Physics2D.OverlapBoxNonAlloc(center, size, 0f, probeBuffer, config.GroundLayers);

        for (int i = 0; i < hitCount; i++)
        {
            Collider2D c = probeBuffer[i];
            if (c == null) continue;
            if (c == playerCollider) continue;
            if (c.isTrigger) continue;
            if (c.transform.IsChildOf(transform)) continue;

            return true;
        }

        return false;
    }

    private void ResolveColliderIfNeeded()
    {
        if (playerCollider != null && playerCollider)
            return;

        Collider2D[] colliders = GetComponents<Collider2D>();
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider2D c = colliders[i];
            if (c == null) continue;
            if (c.isTrigger) continue;

            playerCollider = c;
            return;
        }

        if (playerCollider == null)
            playerCollider = GetComponentInChildren<Collider2D>();
    }

    private void EvaluateCollision(Collision2D collision)
    {
        Collider2D otherCollider = collision.collider;

        if (otherCollider == null)
            return;

        if (!IsGroundLayer(otherCollider.gameObject.layer))
        {
            groundContacts.Remove(otherCollider);
            return;
        }

        if (HasGroundNormal(collision))
            groundContacts.Add(otherCollider);
        else
            groundContacts.Remove(otherCollider);
    }

    private bool IsGroundLayer(int layer)
    {
        return (config.GroundLayers.value & (1 << layer)) != 0;
    }

    private bool HasGroundNormal(Collision2D collision)
    {
        for (int i = 0; i < collision.contactCount; i++)
        {
            ContactPoint2D contact = collision.GetContact(i);

            if (contact.normal.y >= config.MinGroundNormalY)
                return true;
        }

        return false;
    }

    private void OnDrawGizmosSelected()
    {
        if (playerCollider == null)
            return;

        Bounds b = playerCollider.bounds;
        float width = Mathf.Max(0.01f, b.size.x * (1f - probeWidthShrink));
        float height = Mathf.Max(0.005f, probeDistance);

        Vector3 center = new Vector3(b.center.x, b.min.y - height * 0.5f, 0f);
        Vector3 size = new Vector3(width, height, 0.01f);

        Gizmos.color = overlapProbeFoundGround ? new Color(0.3f, 1f, 0.3f, 0.6f) : new Color(1f, 0.3f, 0.3f, 0.5f);
        Gizmos.DrawWireCube(center, size);
    }
}

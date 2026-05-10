using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Слушает столкновения и определяет, прижат ли игрок к вертикальной стене.
/// Аналог PlayerGroundChecker, но фильтрует контакты по горизонтальной нормали.
/// </summary>
public class PlayerWallChecker : MonoBehaviour
{
    [Header("Config")]
    [SerializeField] private PlayerConfigSO config;

    private readonly HashSet<Collider2D> wallContacts = new HashSet<Collider2D>();

    private float wallNormalX;

    /// <summary>Игрок касается какой-то стены прямо сейчас.</summary>
    public bool IsTouchingWall => wallContacts.Count > 0;

    /// <summary>Знак нормали стены по X. +1 — стена слева (нормаль вправо), -1 — стена справа.</summary>
    public float WallNormalX => wallNormalX;

    private void OnCollisionEnter2D(Collision2D collision) => Evaluate(collision);
    private void OnCollisionStay2D(Collision2D collision) => Evaluate(collision);

    private void OnCollisionExit2D(Collision2D collision)
    {
        wallContacts.Remove(collision.collider);

        if (wallContacts.Count == 0)
            wallNormalX = 0f;
    }

    private void OnDisable()
    {
        wallContacts.Clear();
        wallNormalX = 0f;
    }

    private void Evaluate(Collision2D collision)
    {
        Collider2D otherCollider = collision.collider;

        if (otherCollider == null)
            return;

        if (config == null)
            return;

        if (!IsWallLayer(otherCollider.gameObject.layer))
        {
            wallContacts.Remove(otherCollider);
            return;
        }

        if (TryFindWallNormal(collision, out Vector2 normal))
        {
            wallContacts.Add(otherCollider);
            wallNormalX = Mathf.Sign(normal.x);
        }
        else
        {
            wallContacts.Remove(otherCollider);
        }
    }

    private bool IsWallLayer(int layer)
    {
        LayerMask mask = config.EffectiveWallLayers;
        return (mask.value & (1 << layer)) != 0;
    }

    private bool TryFindWallNormal(Collision2D collision, out Vector2 normal)
    {
        for (int i = 0; i < collision.contactCount; i++)
        {
            ContactPoint2D contact = collision.GetContact(i);

            if (Mathf.Abs(contact.normal.x) >= config.MinWallNormalX)
            {
                normal = contact.normal;
                return true;
            }
        }

        normal = Vector2.zero;
        return false;
    }
}

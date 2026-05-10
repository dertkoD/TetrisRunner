using System.Collections.Generic;
using UnityEngine;

public class PlayerGroundChecker : MonoBehaviour
{
    [Header("Config")]
    [SerializeField] private PlayerConfigSO config;

    private readonly HashSet<Collider2D> groundContacts = new HashSet<Collider2D>();

    public bool IsGrounded => groundContacts.Count > 0;

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
}

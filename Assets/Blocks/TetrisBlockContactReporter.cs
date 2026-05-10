using UnityEngine;

public class TetrisBlockContactReporter : MonoBehaviour
{
    private TetrisBlockConfigSO config;
    private TetrisBlockController controller;

    private bool initialized;

    public void Initialize(TetrisBlockConfigSO config, TetrisBlockController controller)
    {
        this.config = config;
        this.controller = controller;
        initialized = true;
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        EvaluateCollision(collision);
    }

    private void OnCollisionStay2D(Collision2D collision)
    {
        EvaluateCollision(collision);
    }

    private void EvaluateCollision(Collision2D collision)
    {
        if (!initialized)
            return;

        if (collision.collider == null)
            return;

        int otherLayer = collision.collider.gameObject.layer;

        bool touchedGround = IsInLayerMask(otherLayer, config.GroundLayers);
        bool touchedBlock = IsInLayerMask(otherLayer, config.BlockLayers);

        if (!touchedGround && !touchedBlock)
            return;

        if (config.RequireBottomContactToLock && !HasBottomContact(collision))
            return;

        controller.NotifyTouchedLockTarget();
    }

    private bool IsInLayerMask(int layer, LayerMask mask)
    {
        return (mask.value & (1 << layer)) != 0;
    }

    private bool HasBottomContact(Collision2D collision)
    {
        for (int i = 0; i < collision.contactCount; i++)
        {
            ContactPoint2D contact = collision.GetContact(i);

            if (contact.normal.y >= config.MinLockNormalY)
                return true;
        }

        return false;
    }
}

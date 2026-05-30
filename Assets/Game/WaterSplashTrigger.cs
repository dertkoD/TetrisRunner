using UnityEngine;

public class WaterSplashTrigger : MonoBehaviour
{
    public Simple2DWater water;
    public float splashMultiplier = 0.08f;
    public float minVelocity = 2f;

    void OnTriggerEnter2D(Collider2D other)
    {
        Rigidbody2D rb = other.attachedRigidbody;
        if (rb == null) return;

        float impactVelocity = -rb.linearVelocity.y;

        if (impactVelocity < minVelocity) return;

        float force = -impactVelocity * rb.mass * splashMultiplier;
        water.Splash(other.bounds.center.x, force);
    }
}

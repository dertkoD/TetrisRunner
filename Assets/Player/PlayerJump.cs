using UnityEngine;

public class PlayerJump : MonoBehaviour
{
    public void PerformJump(Rigidbody2D body, PlayerConfigSO config)
    {
        Vector2 velocity = body.linearVelocity;
        velocity.y = config.JumpVelocity;
        body.linearVelocity = velocity;
    }

    public void CutJump(Rigidbody2D body, PlayerConfigSO config)
    {
        Vector2 velocity = body.linearVelocity;

        if (velocity.y <= 0f)
            return;

        velocity.y *= config.JumpCutMultiplier;
        body.linearVelocity = velocity;
    }

    public void LimitFallSpeed(Rigidbody2D body, PlayerConfigSO config)
    {
        Vector2 velocity = body.linearVelocity;

        if (velocity.y >= -config.MaxFallSpeed)
            return;

        velocity.y = -config.MaxFallSpeed;
        body.linearVelocity = velocity;
    }
}

using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    public void Move(
        Rigidbody2D body,
        Transform bodyTransform,
        PlayerConfigSO config,
        float inputX,
        bool isGrounded)
    {
        float targetVelocityX = inputX * config.MaxMoveSpeed;
        float currentVelocityX = body.linearVelocity.x;

        bool hasMoveInput = Mathf.Abs(inputX) > 0.01f;
        float speedChangeRate = hasMoveInput
            ? config.Acceleration
            : config.Deceleration;

        if (!isGrounded)
            speedChangeRate *= config.AirControlMultiplier;

        float newVelocityX = Mathf.MoveTowards(
            currentVelocityX,
            targetVelocityX,
            speedChangeRate * Time.fixedDeltaTime
        );

        body.linearVelocity = new Vector2(newVelocityX, body.linearVelocity.y);

        Flip(bodyTransform, config, inputX);
    }

    private void Flip(Transform bodyTransform, PlayerConfigSO config, float inputX)
    {
        if (!config.FlipByScale)
            return;

        if (Mathf.Abs(inputX) <= 0.01f)
            return;

        Vector3 scale = bodyTransform.localScale;
        float absX = Mathf.Abs(scale.x);

        if (absX <= Mathf.Epsilon)
            absX = 1f;

        scale.x = inputX > 0f ? absX : -absX;
        bodyTransform.localScale = scale;
    }
}

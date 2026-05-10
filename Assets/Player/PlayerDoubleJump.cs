using UnityEngine;

/// <summary>
/// Способность "двойного прыжка". На земле счётчик доступных воздушных
/// прыжков восполняется (Refill), в воздухе их можно потратить (TryJump).
/// </summary>
public class PlayerDoubleJump : MonoBehaviour
{
    private int airJumpsRemaining;

    public int AirJumpsRemaining => airJumpsRemaining;

    public void Refill(PlayerConfigSO config)
    {
        if (config == null)
        {
            airJumpsRemaining = 0;
            return;
        }

        airJumpsRemaining = Mathf.Max(0, config.AirJumpCount);
    }

    public bool TryJump(Rigidbody2D body, PlayerConfigSO config)
    {
        if (body == null || config == null)
            return false;

        if (!config.DoubleJumpEnabled)
            return false;

        if (airJumpsRemaining <= 0)
            return false;

        airJumpsRemaining--;

        body.linearVelocity = new Vector2(
            body.linearVelocity.x,
            config.AirJumpVelocity
        );

        return true;
    }
}

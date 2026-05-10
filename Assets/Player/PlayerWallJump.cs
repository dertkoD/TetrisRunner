using UnityEngine;

/// <summary>
/// Способность отталкиваться от стен (как в Hollow Knight).
/// Игрок прижат к стене → нажимает прыжок → улетает по диагонали в
/// противоположную от стены сторону. На время лок-аута горизонтальный
/// инпут игнорируется, чтобы нельзя было сразу прилипнуть обратно.
/// Количество wall-jump'ов за один полёт ограничено (config.WallJumpCount)
/// и восполняется при приземлении (Refill).
/// </summary>
public class PlayerWallJump : MonoBehaviour
{
    private float lockoutTimer;
    private float lastKickDirectionX;
    private int wallJumpsRemaining;

    /// <summary>true пока лок-аут после wall-jump ещё активен.</summary>
    public bool IsLockedOut => lockoutTimer > 0f;

    /// <summary>Направление последнего отталкивания по X (+1 — вправо, -1 — влево).</summary>
    public float LastKickDirectionX => lastKickDirectionX;

    /// <summary>Сколько wall-jump'ов доступно сейчас.</summary>
    public int WallJumpsRemaining => wallJumpsRemaining;

    public void Tick(float deltaTime)
    {
        if (lockoutTimer > 0f)
        {
            lockoutTimer -= deltaTime;
            if (lockoutTimer < 0f) lockoutTimer = 0f;
        }
    }

    /// <summary>Восполнить количество доступных wall-jump'ов. Вызывается при приземлении.</summary>
    public void Refill(PlayerConfigSO config)
    {
        if (config == null)
        {
            wallJumpsRemaining = 0;
            return;
        }

        wallJumpsRemaining = Mathf.Max(0, config.WallJumpCount);
    }

    public bool TryJump(Rigidbody2D body, PlayerConfigSO config, PlayerWallChecker wallChecker)
    {
        if (body == null || config == null || wallChecker == null)
            return false;

        if (!config.WallJumpEnabled)
            return false;

        if (!wallChecker.IsTouchingWall)
            return false;

        if (lockoutTimer > 0f)
            return false;

        if (wallJumpsRemaining <= 0)
            return false;

        float kickDirectionX = wallChecker.WallNormalX;

        if (Mathf.Abs(kickDirectionX) < 0.01f)
            kickDirectionX = 1f;

        body.linearVelocity = new Vector2(
            kickDirectionX * config.WallJumpHorizontalVelocity,
            config.WallJumpVelocity
        );

        lockoutTimer = config.WallJumpLockoutTime;
        lastKickDirectionX = kickDirectionX;
        wallJumpsRemaining--;

        return true;
    }
}

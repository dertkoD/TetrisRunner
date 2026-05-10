using UnityEngine;

public class TetrisBlockMovement : MonoBehaviour
{
    private float horizontalStepTimer;
    private float fallStepTimer;
    private int previousStepDirection;

    public void Initialize()
    {
        horizontalStepTimer = 0f;
        fallStepTimer = 0f;
        previousStepDirection = 0;
    }

    public void Move(Rigidbody2D body, TetrisBlockConfigSO config, float horizontalInput)
    {
        if (config.FreeFall)
            MoveWithFreeFall(body, config, horizontalInput);
        else
            MoveWithClassicSteps(body, config, horizontalInput);
    }

    public void LimitFallSpeed(Rigidbody2D body, TetrisBlockConfigSO config)
    {
        Vector2 velocity = body.linearVelocity;

        if (velocity.y >= -config.MaxFallSpeed)
            return;

        velocity.y = -config.MaxFallSpeed;
        body.linearVelocity = velocity;
    }

    public void SnapToGrid(Rigidbody2D body, TetrisBlockConfigSO config)
    {
        Vector2 position = body.position;
        float rotation = body.rotation;

        if (config.SnapPositionWhenStacking)
        {
            float cell = config.GridCellSize;

            position.x = Mathf.Round(position.x / cell) * cell;
            position.y = Mathf.Round(position.y / cell) * cell;
        }

        if (config.SnapRotationWhenStacking)
        {
            float step = config.RotationStepDegrees;

            if (Mathf.Abs(step) > Mathf.Epsilon)
                rotation = Mathf.Round(rotation / step) * step;
        }

        body.position = position;
        body.rotation = rotation;
    }

    private void MoveWithFreeFall(Rigidbody2D body, TetrisBlockConfigSO config, float horizontalInput)
    {
        float targetVelocityX = horizontalInput * config.FreeFallHorizontalSpeed;

        Vector2 velocity = body.linearVelocity;

        velocity.x = Mathf.MoveTowards(
            velocity.x,
            targetVelocityX,
            config.FreeFallHorizontalAcceleration * Time.fixedDeltaTime
        );

        body.linearVelocity = velocity;
    }

    private void MoveWithClassicSteps(Rigidbody2D body, TetrisBlockConfigSO config, float horizontalInput)
    {
        Vector2 nextPosition = body.position;

        int direction = GetStepDirection(horizontalInput);

        if (direction == 0)
        {
            horizontalStepTimer = 0f;
            previousStepDirection = 0;
        }
        else
        {
            if (direction != previousStepDirection)
            {
                horizontalStepTimer = 0f;
                previousStepDirection = direction;
            }

            horizontalStepTimer -= Time.fixedDeltaTime;

            if (horizontalStepTimer <= 0f)
            {
                nextPosition.x += direction * config.HorizontalStepDistance;
                horizontalStepTimer = config.HorizontalStepRepeatTime;
            }
        }

        fallStepTimer -= Time.fixedDeltaTime;

        if (fallStepTimer <= 0f)
        {
            nextPosition.y -= config.FallStepDistance;
            fallStepTimer = config.FallStepInterval;
        }

        body.MovePosition(nextPosition);
    }

    private int GetStepDirection(float horizontalInput)
    {
        if (horizontalInput > 0.1f)
            return 1;

        if (horizontalInput < -0.1f)
            return -1;

        return 0;
    }
    
    public bool MoveOnGrid(
        Rigidbody2D body,
        TetrisBlockConfigSO config,
        TetrisGridBoard board,
        TetrisBlockCells blockCells,
        float horizontalInput)
    {
        if (body == null || board == null || blockCells == null)
            return false;

        if (blockCells.CurrentOffsets == null)
            return false;

        Vector2Int pivotCell = board.WorldToCell(body.position);
        Vector2Int nextPivotCell = pivotCell;

        int direction = GetStepDirection(horizontalInput);

        if (direction == 0)
        {
            horizontalStepTimer = 0f;
            previousStepDirection = 0;
        }
        else
        {
            if (direction != previousStepDirection)
            {
                horizontalStepTimer = 0f;
                previousStepDirection = direction;
            }

            horizontalStepTimer -= Time.fixedDeltaTime;

            if (horizontalStepTimer <= 0f)
            {
                Vector2Int horizontalOffset = new Vector2Int(direction, 0);
                Vector2Int horizontalTarget = nextPivotCell + horizontalOffset;

                if (board.CanPlaceOffsets(horizontalTarget, blockCells.CurrentOffsets))
                {
                    nextPivotCell = horizontalTarget;
                }

                horizontalStepTimer = config.HorizontalStepRepeatTime;
            }
        }

        bool blockedDown = false;

        fallStepTimer -= Time.fixedDeltaTime;

        if (fallStepTimer <= 0f)
        {
            Vector2Int downTarget = nextPivotCell + Vector2Int.down;

            if (board.CanPlaceOffsets(downTarget, blockCells.CurrentOffsets))
            {
                nextPivotCell = downTarget;
            }
            else
            {
                blockedDown = true;
            }

            fallStepTimer = config.FallStepInterval;
        }

        if (nextPivotCell != pivotCell)
        {
            body.MovePosition(board.CellToWorld(nextPivotCell));
        }

        body.rotation = 0f;

        return blockedDown;
    }
}

using UnityEngine;

public enum TetrisBlockMoveResult
{
    Moving,
    BlockedDown,
    FellOffBoard,
}

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

    public TetrisBlockMoveResult MoveOnGrid(
        Rigidbody2D body,
        TetrisBlockConfigSO config,
        TetrisGridBoard board,
        TetrisBlockCells blockCells,
        Vector2 moveInput)
    {
        if (body == null || board == null || blockCells == null)
            return TetrisBlockMoveResult.Moving;

        if (blockCells.CurrentOffsets == null || blockCells.CurrentOffsets.Length == 0)
            return TetrisBlockMoveResult.Moving;

        Vector2Int pivotCell = board.WorldToCell(body.position);
        Vector2Int nextPivotCell = pivotCell;

        int direction = GetStepDirection(moveInput.x);

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

        // Soft-drop: при удержании "вниз" блок падает быстрее.
        bool softDropping = moveInput.y < -0.1f;
        float softDropMultiplier = Mathf.Max(1f, config != null ? config.SoftDropMultiplier : 1f);
        float fallStepTickSpeed = softDropping ? softDropMultiplier : 1f;

        fallStepTimer -= Time.fixedDeltaTime * fallStepTickSpeed;

        bool blockedDown = false;
        bool fellOff = false;

        if (fallStepTimer <= 0f)
        {
            Vector2Int downTarget = nextPivotCell + Vector2Int.down;

            if (board.CanPlaceOffsets(downTarget, blockCells.CurrentOffsets))
            {
                nextPivotCell = downTarget;
            }
            else if (WouldFallOffBottom(board, downTarget, blockCells.CurrentOffsets))
            {
                // Все клетки в новой позиции либо ушли ниже сетки, либо
                // пусты — значит под блоком нет ни ground, ни другого блока.
                fellOff = true;
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

        if (fellOff)
            return TetrisBlockMoveResult.FellOffBoard;

        if (blockedDown)
            return TetrisBlockMoveResult.BlockedDown;

        return TetrisBlockMoveResult.Moving;
    }

    private static bool WouldFallOffBottom(
        TetrisGridBoard board,
        Vector2Int pivot,
        Vector2Int[] offsets)
    {
        if (board == null || offsets == null || offsets.Length == 0)
            return false;

        bool hasCellBelowBoard = false;

        for (int i = 0; i < offsets.Length; i++)
        {
            Vector2Int cell = pivot + offsets[i];

            if (board.IsInside(cell))
            {
                // Если внутри сетки в этой позиции уже что-то есть — это
                // обычный лок, а не падение вниз.
                if (board.IsOccupied(cell))
                    return false;

                continue;
            }

            // Клетка вне сетки. Нас интересует только случай, когда фигура
            // целиком/частично провалилась под нижнюю границу — выход вбок
            // не должен считаться падением (этого и не должно случаться при
            // спуске на одну клетку, но проверим на всякий случай).
            if (cell.y >= 0)
                return false;

            hasCellBelowBoard = true;
        }

        return hasCellBelowBoard;
    }
}

using UnityEngine;

public enum TSpinResult { None, Mini, Full }

/// <summary>
/// 조작 중인 테트로미노. 테트로미노 프리팹 7개의 루트에 각각 붙인다.
/// 자식으로 미노 스프라이트가 정확히 4개 있어야 한다.
/// </summary>
public class Block : MonoBehaviour
{
    [Header("Data")]
    public BlockType blockType;

    [Header("Timing (스폰 시 TetrisManager가 덮어씀)")]
    public float fallInterval = 1f;
    public float softDropFactor = 20f;
    public float lockDelay = 0.5f;
    public int maxLockResets = 15;
    public float das = 0.167f;
    public float arr = 0.033f;

    private Board board;
    private TetrisManager manager;
    private readonly Transform[] minos = new Transform[4];

    private Vector2Int position;
    private Vector2Int[] cells;
    private int rotation;

    private float fallTimer, lockTimer, shiftTimer;
    private int shiftDir, lockResets, lowestY, lastKickIndex;
    private bool dasCharged, grounded, lastMoveWasRotation, locked, ready;

    public bool IsLocked => locked;
    public Vector2Int Position => position;
    public int Rotation => rotation;

    // ================= 초기화 =================

    public void Init(Board board, TetrisManager manager, BlockType type)
    {
        this.board = board;
        this.manager = manager;
        blockType = type;

        transform.localScale = Vector3.one;   // 부모 스케일은 항상 1
        transform.rotation = Quaternion.identity;

        int count = 0;
        foreach (Transform child in transform)
            if (count < 4) minos[count++] = child;

        if (count < 4)
        {
            Debug.LogError($"[Block] {type} 프리팹의 자식 미노가 {count}개입니다. 4개여야 합니다.", this);
            return;
        }

        for (int i = 0; i < 4; i++) board.NormalizeMino(minos[i]);   // 셀 크기에 맞춰 스케일

        rotation = 0;
        cells = SRS.GetCells(type, rotation);
        position = SRS.SpawnOrigin(type);
        lowestY = position.y;

        if (Collides(position, cells))   // Block Out
        {
            manager.GameOver();
            return;
        }

        ready = true;
        Sync();
        TryMove(Vector2Int.down);         // 가이드라인: 등장 직후 한 칸 하강
    }

    private void Update()
    {
        if (!ready || locked) return;

        HandleShift();
        HandleRotate();
        HandleDrop();
        HandleHold();

        if (!locked) HandleLockDelay();
    }

    // ================= 입력 =================

    private void HandleShift()
    {
        int dir = 0;
        if (Input.GetKey(KeyCode.LeftArrow)) dir -= 1;
        if (Input.GetKey(KeyCode.RightArrow)) dir += 1;   // 동시 입력은 상쇄

        if (dir == 0)
        {
            shiftDir = 0;
            shiftTimer = 0f;
            dasCharged = false;
            return;
        }

        if (dir != shiftDir)              // 누른 순간 1칸
        {
            shiftDir = dir;
            shiftTimer = 0f;
            dasCharged = false;
            TryMove(new Vector2Int(dir, 0));
            return;
        }

        shiftTimer += Time.deltaTime;

        if (!dasCharged)                  // DAS 대기
        {
            if (shiftTimer < das) return;
            shiftTimer -= das;
            dasCharged = true;
            TryMove(new Vector2Int(dir, 0));
        }

        if (arr <= 0f)                    // ARR 0 = 벽까지 즉시
        {
            while (TryMove(new Vector2Int(dir, 0))) { }
            shiftTimer = 0f;
            return;
        }

        while (shiftTimer >= arr)         // ARR 반복
        {
            shiftTimer -= arr;
            if (!TryMove(new Vector2Int(dir, 0))) break;
        }
    }

    private void HandleRotate()
    {
        if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.X))
            TryRotate(1);
        else if (Input.GetKeyDown(KeyCode.Z) || Input.GetKeyDown(KeyCode.LeftControl))
            TryRotate(-1);
    }

    private void HandleDrop()
    {
        if (Input.GetKeyDown(KeyCode.Space)) { HardDrop(); return; }

        bool soft = Input.GetKey(KeyCode.DownArrow);
        float interval = soft ? fallInterval / Mathf.Max(1f, softDropFactor) : fallInterval;

        fallTimer += Time.deltaTime;
        if (fallTimer < interval) return;
        fallTimer = 0f;

        if (!TryMove(Vector2Int.down)) return;   // 소프트드롭은 논로킹

        lastMoveWasRotation = false;
        if (soft) manager.AddDropScore(1);       // 소프트드롭 1점/칸

        if (position.y < lowestY)                // 새 최저행 → 리셋 횟수 회복
        {
            lowestY = position.y;
            lockResets = 0;
        }
    }

    private void HandleHold()
    {
        if (Input.GetKeyDown(KeyCode.C) || Input.GetKeyDown(KeyCode.LeftShift))
            manager.RequestHold(this, blockType);
    }

    private void HandleLockDelay()
    {
        grounded = Collides(position + Vector2Int.down, cells);

        if (!grounded)
        {
            lockTimer = 0f;
            return;
        }

        lockTimer += Time.deltaTime;
        if (lockTimer >= lockDelay) Lock();
    }

    // ================= 동작 =================

    private bool TryMove(Vector2Int delta)
    {
        Vector2Int next = position + delta;
        if (Collides(next, cells)) return false;

        position = next;
        if (delta.y == 0) lastMoveWasRotation = false;
        Sync();
        ResetLockTimer();
        return true;
    }

    private bool TryRotate(int dir)
    {
        int from = rotation;
        int to = ((rotation + dir) % 4 + 4) % 4;

        Vector2Int[] next = SRS.GetCells(blockType, to);
        Vector2Int[] kicks = SRS.GetKicks(blockType, from, to);

        for (int i = 0; i < kicks.Length; i++)
        {
            Vector2Int test = position + kicks[i];
            if (Collides(test, next)) continue;

            position = test;
            rotation = to;
            cells = next;
            lastMoveWasRotation = true;
            lastKickIndex = i;
            Sync();
            ResetLockTimer();
            return true;
        }

        return false;   // 5개 후보 전부 실패
    }

    private void HardDrop()
    {
        int dropped = 0;
        while (!Collides(position + Vector2Int.down, cells))
        {
            position += Vector2Int.down;
            dropped++;
        }

        lastMoveWasRotation = false;
        Sync();
        manager.AddDropScore(dropped * 2);   // 하드드롭 2점/칸
        Lock();
    }

    private void ResetLockTimer()
    {
        if (!grounded) return;
        if (lockResets >= maxLockResets) return;   // 리셋 15회 제한
        lockResets++;
        lockTimer = 0f;
    }

    private void Lock()
    {
        if (locked) return;
        locked = true;

        TSpinResult tspin = DetectTSpin();

        bool allAboveVisible = true;
        for (int i = 0; i < 4; i++)
        {
            Vector2Int c = position + cells[i];
            if (c.y < Board.VisibleHeight) allAboveVisible = false;
            board.Set(c, minos[i]);       // 미노를 보드로 이관
        }

        int lines = board.ClearLines();
        bool perfect = lines > 0 && board.IsEmpty();

        Destroy(gameObject);              // 빈 부모 정리

        if (allAboveVisible)              // Lock Out
        {
            manager.GameOver();
            return;
        }

        manager.OnPieceLocked(lines, tspin, perfect);
    }

    // ================= 3-corner T 판정 =================

    private TSpinResult DetectTSpin()
    {
        if (blockType != BlockType.T_Block || !lastMoveWasRotation) return TSpinResult.None;

        Vector2Int[] front = rotation switch
        {
            0 => new[] { new Vector2Int(-1, 1), new Vector2Int(1, 1) },
            1 => new[] { new Vector2Int(1, 1), new Vector2Int(1, -1) },
            2 => new[] { new Vector2Int(-1, -1), new Vector2Int(1, -1) },
            _ => new[] { new Vector2Int(-1, 1), new Vector2Int(-1, -1) },
        };
        Vector2Int[] corners =
        {
            new(-1, 1), new(1, 1), new(-1, -1), new(1, -1)
        };

        int total = 0, frontFilled = 0;
        foreach (var o in corners)
        {
            if (!board.IsBlocked(position + o)) continue;   // 벽·바닥도 채워진 것으로 취급
            total++;
            foreach (var f in front)
                if (f == o) frontFilled++;
        }

        if (total < 3) return TSpinResult.None;
        if (frontFilled == 2) return TSpinResult.Full;

        // 앞 1 + 뒤 2 → 미니. 단 마지막 킥이 5번째 후보면 정식 T-Spin
        return lastKickIndex == 4 ? TSpinResult.Full : TSpinResult.Mini;
    }

    // ================= 유틸 =================

    private bool Collides(Vector2Int origin, Vector2Int[] shape)
    {
        for (int i = 0; i < 4; i++)
            if (board.IsBlocked(origin + shape[i])) return true;
        return false;
    }

    private void Sync()
    {
        for (int i = 0; i < 4; i++)
            if (minos[i] != null) minos[i].position = board.CellToWorld(position + cells[i]);
    }

    public int GetDropDistance()
    {
        if (!ready) return 0;

        int d = 0;
        while (!Collides(position + Vector2Int.down * (d + 1), cells)) d++;
        return d;
    }

    public Vector2Int[] GetGhostCells()
    {
        int d = GetDropDistance();
        var result = new Vector2Int[4];
        for (int i = 0; i < 4; i++) result[i] = position + cells[i] + Vector2Int.down * d;
        return result;
    }
}

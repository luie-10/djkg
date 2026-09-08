using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TetrisManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Board board;
    [SerializeField] private TetrisAudio audioController;

    [Header("Block Prefab Registry")]
    public List<BlockSet> tetrisBlocks = new List<BlockSet>();

    [Header("Next & Hold")]
    [Range(1, 6)] public int nextCount = 5;
    public bool allowHold = true;

    [Header("Timings (60FPS 기준 권장값)")]
    public float areDelay = 0.100f;          // ARE 6프레임
    public float lineClearDelay = 0.400f;
    public float lockDelay = 0.5f;           // 20G 미만에서 0.5초
    public int maxLockResets = 15;
    public float das = 0.167f;               // 10프레임
    public float arr = 0.033f;               // 2프레임
    public float softDropFactor = 20f;

    [Header("Level")]
    public int startLevel = 1;
    public int linesPerLevel = 10;
    public int maxLevel = 15;

    [Header("Garbage (Versus)")]
    public bool enableGarbage = false;
    [Range(0f, 1f)] public float garbageAlignChance = 0.7f;

    [Header("Randomizer")]
    [Tooltip("0이면 매번 랜덤. 네트워크 대전에서는 서버가 준 시드를 넣는다")]
    public int randomSeed = 0;

    // ---------- 이벤트 (UI / 오디오가 구독) ----------
    public event System.Action<int> ScoreChanged;
    public event System.Action<int> LevelChanged;
    public event System.Action<int> LinesChanged;
    public event System.Action<int> ComboChanged;
    public event System.Action<bool> BackToBackChanged;
    public event System.Action<int, TSpinResult, bool> LineCleared;
    public event System.Action GameStarted;
    public event System.Action<int> GameEnded;

    // ---------- 런타임 상태 ----------
    private readonly Dictionary<BlockType, GameObject> blockPrefabDict = new();
    private readonly Queue<BlockType> nextQueue = new();
    private readonly List<BlockType> bag = new();
    private System.Random rng;

    private Block activePiece;
    private Coroutine spawnRoutine;
    private bool gameOver;
    private bool canHold = true;

    private int score;
    private int level;
    private int totalLines;
    private int combo = -1;
    private bool backToBack;

    private int pendingGarbage;
    private int outgoingGarbage;

    // ---------- 읽기 전용 ----------
    public BlockType? currentHoldBlock { get; private set; }
    public int Score => score;
    public int Level => level;
    public int TotalLines => totalLines;
    public int ComboCount => Mathf.Max(0, combo);
    public bool BackToBackActive => backToBack;
    public bool IsGameOver => gameOver;
    public Block ActivePiece => activePiece;
    public int PendingGarbage => pendingGarbage;

    // ================= 초기화 =================

    private void Awake()
    {
        foreach (BlockSet set in tetrisBlocks)
        {
            if (set.blockPrefab == null)
            {
                Debug.LogError($"[TetrisManager] {set.blockType} 프리팹이 비어 있습니다.", this);
                continue;
            }

            if (!blockPrefabDict.ContainsKey(set.blockType))
                blockPrefabDict.Add(set.blockType, set.blockPrefab);
            else
                Debug.LogWarning($"[TetrisManager] {set.blockType} 이(가) 중복 등록되었습니다.", this);
        }

        foreach (BlockType type in System.Enum.GetValues(typeof(BlockType)))
            if (!blockPrefabDict.ContainsKey(type))
                Debug.LogError($"[TetrisManager] {type} 프리팹이 등록되지 않았습니다.", this);

        if (board == null)
        {
            board = FindFirstObjectByType<Board>();   // FindObjectOfType은 obsolete
            if (board == null) Debug.LogError("[TetrisManager] 씬에 Board가 없습니다.", this);
        }

        if (audioController == null) audioController = FindFirstObjectByType<TetrisAudio>();
    }

    private void Start() => NewGame();

    // ================= 사운드 =================

    /// <summary>Block이 조작 효과음을 요청할 때 사용.</summary>
    public void PlaySfx(TetrisSfx sfx)
    {
        if (audioController != null) audioController.Play(sfx);
    }

    // ================= 게임 흐름 =================

    public void NewGame()
    {
        if (spawnRoutine != null) StopCoroutine(spawnRoutine);
        spawnRoutine = null;

        if (activePiece != null) Destroy(activePiece.gameObject);
        activePiece = null;

        gameOver = false;
        score = 0;
        totalLines = 0;
        level = Mathf.Max(1, startLevel);
        combo = -1;
        backToBack = false;
        currentHoldBlock = null;
        canHold = true;
        pendingGarbage = 0;
        outgoingGarbage = 0;

        rng = (randomSeed != 0)
            ? new System.Random(randomSeed)
            : new System.Random(System.Environment.TickCount);

        board.RecalculateCellSize();
        board.Clear();
        bag.Clear();
        InitNextQueue();

        GameStarted?.Invoke();
        ScoreChanged?.Invoke(score);
        LinesChanged?.Invoke(totalLines);
        ComboChanged?.Invoke(0);
        BackToBackChanged?.Invoke(false);

        spawnRoutine = StartCoroutine(SpawnAfterDelay(0f));
    }

    public void GameOver()
    {
        if (gameOver) return;
        gameOver = true;

        if (spawnRoutine != null) StopCoroutine(spawnRoutine);
        spawnRoutine = null;

        if (activePiece != null) Destroy(activePiece.gameObject);
        activePiece = null;

        Debug.Log($"[TetrisManager] Game Over — Score:{score} Lines:{totalLines} Level:{level}");
        GameEnded?.Invoke(score);
    }

    // ================= 7-Bag =================

    private void RefillBag()
    {
        bag.Clear();
        bag.AddRange((BlockType[])System.Enum.GetValues(typeof(BlockType)));

        for (int i = bag.Count - 1; i > 0; i--)          // Fisher-Yates
        {
            int r = rng.Next(0, i + 1);                  // 시드 기반 → 재현 가능
            (bag[i], bag[r]) = (bag[r], bag[i]);
        }
    }

    private BlockType DrawFromBag()
    {
        if (bag.Count == 0) RefillBag();
        BlockType t = bag[0];
        bag.RemoveAt(0);
        return t;
    }

    private void InitNextQueue()
    {
        nextQueue.Clear();
        while (nextQueue.Count < nextCount + 1) nextQueue.Enqueue(DrawFromBag());
    }

    private BlockType GetNextBlockType()
    {
        if (nextQueue.Count == 0) InitNextQueue();

        BlockType next = nextQueue.Dequeue();
        nextQueue.Enqueue(DrawFromBag());
        return next;
    }

    /// <summary>UI용. index 0이 가장 먼저 나올 피스.</summary>
    public BlockType? PeekNext(int index)
    {
        if (index < 0 || index >= nextCount) return null;

        int i = 0;
        foreach (BlockType t in nextQueue)
        {
            if (i == index) return t;
            i++;
        }
        return null;
    }

    // ================= 스폰 & 홀드 =================

    private IEnumerator SpawnAfterDelay(float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);

        spawnRoutine = null;
        if (gameOver) yield break;

        ApplyPendingGarbage();
        if (gameOver) yield break;

        SpawnBlock(GetNextBlockType());
    }

    public Block SpawnBlock(BlockType type)
    {
        if (gameOver) return null;

        if (!blockPrefabDict.TryGetValue(type, out GameObject prefab) || prefab == null)
        {
            Debug.LogError($"[TetrisManager] {type} 프리팹이 없어 스폰할 수 없습니다.", this);
            return null;
        }

        GameObject go = Instantiate(prefab, Vector3.zero, Quaternion.identity);

        Block piece = go.GetComponent<Block>();
        if (piece == null)
        {
            Debug.LogError($"[TetrisManager] {type} 프리팹에 Block 컴포넌트가 없습니다.", go);
            Destroy(go);
            return null;
        }

        piece.fallInterval = GetFallInterval();
        piece.softDropFactor = softDropFactor;
        piece.lockDelay = lockDelay;
        piece.maxLockResets = maxLockResets;
        piece.das = das;
        piece.arr = arr;

        activePiece = piece;
        piece.Init(board, this, type);

        return gameOver ? null : piece;
    }

    /// <summary>홀드는 락다운될 때까지 재사용 불가.</summary>
    public void RequestHold(Block current, BlockType currentType)
    {
        if (!allowHold || !canHold || gameOver) return;
        if (current == null || current != activePiece || current.IsLocked) return;

        canHold = false;
        PlaySfx(TetrisSfx.Hold);

        Destroy(current.gameObject);
        activePiece = null;

        if (currentHoldBlock == null)
        {
            currentHoldBlock = currentType;
            SpawnBlock(GetNextBlockType());
        }
        else
        {
            BlockType swap = currentHoldBlock.Value;
            currentHoldBlock = currentType;
            SpawnBlock(swap);
        }
    }

    // ================= 락다운 후 처리 =================

    public void OnPieceLocked(int lines, TSpinResult tspin, bool perfectClear)
    {
        activePiece = null;
        if (gameOver) return;

        canHold = true;

        int gained = CalcScore(lines, tspin, perfectClear);
        if (gained > 0)
        {
            score += gained;
            ScoreChanged?.Invoke(score);
        }

        bool prevB2B = backToBack;
        UpdateBackToBack(lines, tspin);
        if (prevB2B != backToBack) BackToBackChanged?.Invoke(backToBack);

        int prevCombo = combo;
        combo = (lines > 0) ? combo + 1 : -1;
        if (prevCombo != combo) ComboChanged?.Invoke(ComboCount);

        if (lines > 0)
        {
            totalLines += lines;
            LinesChanged?.Invoke(totalLines);
            LineCleared?.Invoke(lines, tspin, perfectClear);   // 터지는 소리 + 팝업

            int newLevel = Mathf.Clamp(startLevel + totalLines / linesPerLevel, startLevel, maxLevel);
            if (newLevel != level)
            {
                level = newLevel;
                LevelChanged?.Invoke(level);
            }

            EnqueueGarbage(lines, tspin, perfectClear);
        }

        float delay = areDelay + (lines > 0 ? lineClearDelay : 0f);
        spawnRoutine = StartCoroutine(SpawnAfterDelay(delay));
    }

    public void AddDropScore(int points)
    {
        if (points <= 0) return;
        score += points;
        ScoreChanged?.Invoke(score);
    }

    // ================= 스코어링 =================

    private int CalcScore(int lines, TSpinResult tspin, bool perfectClear)
    {
        int baseScore;
        bool difficult = false;

        if (tspin == TSpinResult.Full)
        {
            switch (lines)
            {
                case 0: baseScore = 400; break;
                case 1: baseScore = 800; difficult = true; break;
                case 2: baseScore = 1200; difficult = true; break;
                default: baseScore = 1600; difficult = true; break;
            }
        }
        else if (tspin == TSpinResult.Mini)
        {
            switch (lines)
            {
                case 0: baseScore = 100; break;
                case 1: baseScore = 200; difficult = true; break;
                default: baseScore = 400; difficult = true; break;
            }
        }
        else
        {
            switch (lines)
            {
                case 0: return 0;
                case 1: baseScore = 100; break;
                case 2: baseScore = 300; break;
                case 3: baseScore = 500; break;
                default: baseScore = 800; difficult = true; break;   // Tetris
            }
        }

        float total = baseScore;
        if (difficult && backToBack) total *= 1.5f;                   // B2B ×1.5

        if (lines > 0 && combo >= 0) total += 50 * (combo + 1);       // 50 × 콤보 수

        if (perfectClear && lines > 0)
        {
            int pc = lines switch
            {
                1 => 800,
                2 => 1200,
                3 => 1800,
                _ => backToBack ? 3200 : 2000,
            };
            total += pc;
        }

        return Mathf.RoundToInt(total * level);                       // 레벨은 클리어 전 값
    }

    private void UpdateBackToBack(int lines, TSpinResult tspin)
    {
        if (lines == 0) return;   // 라인 없는 T-Spin은 체인 유지
        backToBack = (lines >= 4) || (tspin != TSpinResult.None);
    }

    /// <summary>레벨별 낙하 간격(초). 공식 수식 미공개라 커뮤니티 근사식 사용.</summary>
    public float GetFallInterval()
    {
        float t = Mathf.Pow(0.8f - (level - 1) * 0.007f, level - 1);
        return Mathf.Max(0.0166f, t);
    }

    // ================= 가비지 =================

    private void EnqueueGarbage(int lines, TSpinResult tspin, bool perfectClear)
    {
        if (!enableGarbage) return;

        int attack = tspin != TSpinResult.None
            ? lines switch { 1 => 2, 2 => 4, _ => 6 }
            : lines switch { 2 => 1, 3 => 2, 4 => 4, _ => 0 };

        if (backToBack && attack > 0) attack += 1;
        if (combo >= 0) attack += ComboBonus(combo + 1);
        if (perfectClear) attack += 10;

        int cancel = Mathf.Min(pendingGarbage, attack);
        pendingGarbage -= cancel;
        attack -= cancel;

        outgoingGarbage += attack;
    }

    private int ComboBonus(int comboCount) => comboCount switch
    {
        <= 1 => 0,
        2 or 3 => 1,
        4 or 5 => 2,
        6 or 7 => 3,
        _ => 4,
    };

    public void ReceiveGarbage(int lines)
    {
        if (!enableGarbage || lines <= 0) return;
        pendingGarbage += lines;
    }

    private void ApplyPendingGarbage()
    {
        if (!enableGarbage || pendingGarbage <= 0) return;

        int hole = rng.Next(0, Board.Width);

        for (int i = 0; i < pendingGarbage; i++)
        {
            if (i > 0 && rng.NextDouble() > garbageAlignChance)
                hole = rng.Next(0, Board.Width);

            if (!board.InsertGarbageRow(hole))
            {
                pendingGarbage = 0;
                GameOver();
                return;
            }
        }

        pendingGarbage = 0;
    }

    public int TakeOutgoingGarbage()
    {
        int g = outgoingGarbage;
        outgoingGarbage = 0;
        return g;
    }
}

public enum BlockType
{
    I_Block,
    J_Block,
    L_Block,
    O_Block,
    S_Block,
    T_Block,
    Z_Block
}

[System.Serializable]
public struct BlockSet
{
    public BlockType blockType;
    public GameObject blockPrefab;
}

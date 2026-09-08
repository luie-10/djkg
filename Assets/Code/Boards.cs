using UnityEngine;

/// <summary>
/// 필드 데이터 모델. 씬의 빈 GameObject에 붙이고 Position을 필드 좌하단에 맞춘다.
/// 가시 20행 + 상단 버퍼 20행 = 내부 40행.
/// 셀 크기와 미노 스케일을 런타임에 자동 계산하므로 스프라이트 PPU를 손댈 필요가 없다.
/// </summary>
public class Board : MonoBehaviour
{
    public const int Width = 10;
    public const int VisibleHeight = 20;
    public const int TotalHeight = 40;

    public enum CellSizeMode
    {
        FitCamera,   // 카메라 Orthographic Size에 20행을 맞춘다 (Size 5 → 0.5)
        FromSprite,  // 스프라이트 실제 월드 크기를 그대로 한 칸으로 쓴다 (PPU 존중)
        Manual       // 직접 입력
    }

    [Header("Layout")]
    [SerializeField] private CellSizeMode cellSizeMode = CellSizeMode.FitCamera;
    [SerializeField] private Camera targetCamera;
    [Tooltip("FromSprite 모드에서 셀 크기 측정에 쓸 미노 스프라이트")]
    [SerializeField] private Sprite minoSprite;
    [SerializeField] private float manualCellSize = 0.5f;

    [Tooltip("미노 스프라이트를 한 칸 크기에 딱 맞게 자동 스케일")]
    [SerializeField] private bool autoScaleMinos = true;

    [Header("Garbage (대전용, 선택)")]
    [SerializeField] private GameObject garbageMinoPrefab;

    private float cellSize = 0.5f;
    public float CellSize => cellSize;

    private readonly Transform[,] cells = new Transform[Width, TotalHeight];

    // ================= 초기화 =================

    private void Awake()
    {
        if (targetCamera == null) targetCamera = Camera.main;
        RecalculateCellSize();
    }

    public void RecalculateCellSize()
    {
        switch (cellSizeMode)
        {
            case CellSizeMode.FitCamera:
                if (targetCamera == null)
                {
                    Debug.LogWarning("[Board] 카메라가 없어 Manual 값을 사용합니다.", this);
                    cellSize = manualCellSize;
                    break;
                }
                if (!targetCamera.orthographic)
                    Debug.LogWarning("[Board] 카메라가 Orthographic이 아닙니다.", targetCamera);

                // orthographicSize = 화면 세로 절반 → 전체 세로 = Size * 2
                cellSize = (targetCamera.orthographicSize * 2f) / VisibleHeight;
                break;

            case CellSizeMode.FromSprite:
                if (minoSprite == null)
                {
                    Debug.LogWarning("[Board] minoSprite가 비어 있어 Manual 값을 사용합니다.", this);
                    cellSize = manualCellSize;
                    break;
                }
                // Sprite.bounds는 PPU가 반영된 월드 단위 크기
                cellSize = minoSprite.bounds.size.x;
                break;

            default:
                cellSize = manualCellSize;
                break;
        }

        if (cellSize <= 0.0001f)
        {
            Debug.LogError($"[Board] 계산된 셀 크기가 비정상입니다({cellSize}). 0.5로 대체합니다.", this);
            cellSize = 0.5f;
        }
    }

    /// <summary>스프라이트를 한 칸 크기에 정확히 맞춘다. PPU 값과 무관하게 동작.</summary>
    public void NormalizeMino(Transform mino)
    {
        if (!autoScaleMinos || mino == null) return;

        SpriteRenderer sr = mino.GetComponent<SpriteRenderer>();
        if (sr == null) sr = mino.GetComponentInChildren<SpriteRenderer>();
        if (sr == null || sr.sprite == null) return;

        if (sr.drawMode != SpriteDrawMode.Simple)
            Debug.LogWarning("[Board] Sprite Renderer의 Draw Mode를 Simple로 두세요.", sr);

        Vector2 raw = sr.sprite.bounds.size;   // 스케일 1일 때의 월드 크기
        if (raw.x <= 0.0001f || raw.y <= 0.0001f) return;

        if (Mathf.Abs(raw.x - raw.y) > 0.0001f)
            Debug.LogWarning($"[Board] 미노 스프라이트가 정사각형이 아닙니다({raw.x} x {raw.y}).", sr);

        mino.localScale = new Vector3(cellSize / raw.x, cellSize / raw.y, 1f);
    }

    // ================= 좌표 변환 =================

    public Vector3 CellToWorld(Vector2Int c) =>
        transform.position + new Vector3((c.x + 0.5f) * cellSize,
                                        (c.y + 0.5f) * cellSize, 0f);

    // ================= 충돌 판정 =================

    /// <summary>벽·바닥·기존 블록이면 true. 버퍼존 위쪽 하늘은 비어 있는 것으로 취급.</summary>
    public bool IsBlocked(Vector2Int c)
    {
        if (c.x < 0 || c.x >= Width || c.y < 0) return true;
        if (c.y >= TotalHeight) return false;
        return cells[c.x, c.y] != null;
    }

    public bool IsInside(Vector2Int c) =>
        c.x >= 0 && c.x < Width && c.y >= 0 && c.y < TotalHeight;

    // ================= 블록 고정 =================

    public void Set(Vector2Int c, Transform mino)
    {
        if (!IsInside(c) || mino == null) return;

        cells[c.x, c.y] = mino;
        mino.SetParent(transform, true);   // true = 월드 스케일 보존
        mino.position = CellToWorld(c);
    }

    // ================= 라인 클리어 =================

    /// <summary>지운 줄 수를 반환.</summary>
    public int ClearLines()
    {
        int cleared = 0;

        for (int y = 0; y < TotalHeight; y++)
        {
            if (!IsRowFull(y)) continue;

            DeleteRow(y);
            cleared++;

            for (int above = y + 1; above < TotalHeight; above++) MoveRowDown(above);

            y--;   // 내려온 줄을 다시 검사
        }

        return cleared;
    }

    public bool IsEmpty()
    {
        for (int y = 0; y < TotalHeight; y++)
            for (int x = 0; x < Width; x++)
                if (cells[x, y] != null) return false;
        return true;
    }

    /// <summary>재시작용.</summary>
    public void Clear()
    {
        for (int y = 0; y < TotalHeight; y++)
            for (int x = 0; x < Width; x++)
            {
                if (cells[x, y] != null) Destroy(cells[x, y].gameObject);
                cells[x, y] = null;
            }
    }

    private bool IsRowFull(int y)
    {
        for (int x = 0; x < Width; x++)
            if (cells[x, y] == null) return false;
        return true;
    }

    private void DeleteRow(int y)
    {
        for (int x = 0; x < Width; x++)
        {
            if (cells[x, y] != null) Destroy(cells[x, y].gameObject);
            cells[x, y] = null;
        }
    }

    private void MoveRowDown(int y)
    {
        if (y <= 0) return;

        for (int x = 0; x < Width; x++)
        {
            if (cells[x, y] == null) continue;

            cells[x, y - 1] = cells[x, y];
            cells[x, y] = null;
            cells[x, y - 1].position = CellToWorld(new Vector2Int(x, y - 1));
        }
    }

    // ================= 가비지 =================

    /// <summary>
    /// 한 줄 밀어 올리고 holeX 한 칸만 빈 가비지 줄을 삽입.
    /// 최상단이 이미 차서 밀 수 없으면 false (탑아웃).
    /// </summary>
    public bool InsertGarbageRow(int holeX)
    {
        for (int x = 0; x < Width; x++)
            if (cells[x, TotalHeight - 1] != null) return false;

        for (int y = TotalHeight - 1; y > 0; y--)
            for (int x = 0; x < Width; x++)
            {
                cells[x, y] = cells[x, y - 1];
                cells[x, y - 1] = null;
                if (cells[x, y] != null)
                    cells[x, y].position = CellToWorld(new Vector2Int(x, y));
            }

        if (garbageMinoPrefab == null)
        {
            Debug.LogWarning("[Board] garbageMinoPrefab이 없어 가비지가 보이지 않습니다.", this);
            return true;
        }

        for (int x = 0; x < Width; x++)
        {
            if (x == holeX) continue;

            GameObject m = Instantiate(garbageMinoPrefab);
            NormalizeMino(m.transform);
            Set(new Vector2Int(x, 0), m.transform);
        }
        return true;
    }

    // ================= 에디터 보조 =================

    [ContextMenu("Fit Camera To Board")]
    private void FitCameraToBoard()
    {
        if (targetCamera == null) targetCamera = Camera.main;
        if (targetCamera == null) return;

        RecalculateCellSize();

        targetCamera.orthographic = true;
        targetCamera.orthographicSize = VisibleHeight * cellSize * 0.5f;
        targetCamera.transform.position = transform.position + new Vector3(
            Width * cellSize * 0.5f,
            VisibleHeight * cellSize * 0.5f,
            targetCamera.transform.position.z - transform.position.z);

        Debug.Log($"[Board] cellSize={cellSize}, camera size={targetCamera.orthographicSize}");
    }

    private void OnDrawGizmos()
    {
        float s = Application.isPlaying ? cellSize : PreviewCellSize();
        Vector3 o = transform.position;

        Gizmos.color = Color.cyan;
        Vector3 vis = new Vector3(Width * s, VisibleHeight * s, 0f);
        Gizmos.DrawWireCube(o + vis * 0.5f, vis);

        Gizmos.color = new Color(1f, 1f, 0f, 0.35f);
        Gizmos.DrawWireCube(o + new Vector3(vis.x * 0.5f, vis.y * 1.5f, 0f), vis);
    }

    private float PreviewCellSize()
    {
        Camera cam = targetCamera != null ? targetCamera : Camera.main;

        return cellSizeMode switch
        {
            CellSizeMode.FitCamera => cam != null ? (cam.orthographicSize * 2f) / VisibleHeight : manualCellSize,
            CellSizeMode.FromSprite => minoSprite != null ? minoSprite.bounds.size.x : manualCellSize,
            _ => manualCellSize,
        };
    }
}

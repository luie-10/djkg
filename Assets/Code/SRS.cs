using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Super Rotation System 데이터 테이블.
/// MonoBehaviour가 아니므로 씬/프리팹에 붙이지 않는다. 파일만 두면 된다.
/// 킥 테이블 값 출처: https://tetris.wiki/Super_Rotation_System
/// 스폰 규칙 출처: https://tetris.wiki/Tetris_Guideline (21~22행, 중앙 정렬 후 좌측 반올림)
/// </summary>
public static class SRS
{
    // ---------- 피스 모양 ----------

    // 3칸 폭 피스(J,L,S,T,Z): "중심 미노"를 원점으로 한 상대 좌표 (스폰 상태)
    private static readonly Dictionary<BlockType, Vector2Int[]> Spawn3x3 = new()
    {
        { BlockType.T_Block, new[]{ new Vector2Int(-1,0), new Vector2Int(0,0), new Vector2Int(1,0), new Vector2Int( 0,1) } },
        { BlockType.J_Block, new[]{ new Vector2Int(-1,0), new Vector2Int(0,0), new Vector2Int(1,0), new Vector2Int(-1,1) } },
        { BlockType.L_Block, new[]{ new Vector2Int(-1,0), new Vector2Int(0,0), new Vector2Int(1,0), new Vector2Int( 1,1) } },
        { BlockType.S_Block, new[]{ new Vector2Int(-1,0), new Vector2Int(0,0), new Vector2Int(0,1), new Vector2Int( 1,1) } },
        { BlockType.Z_Block, new[]{ new Vector2Int( 0,0), new Vector2Int(1,0), new Vector2Int(-1,1), new Vector2Int(0,1) } },
    };

    // I 피스: 4x4 바운딩 박스의 좌하단을 원점으로 한 상대 좌표
    private static readonly Vector2Int[][] IStates =
    {
        new[]{ new Vector2Int(0,2), new Vector2Int(1,2), new Vector2Int(2,2), new Vector2Int(3,2) }, // 0
        new[]{ new Vector2Int(2,3), new Vector2Int(2,2), new Vector2Int(2,1), new Vector2Int(2,0) }, // R
        new[]{ new Vector2Int(0,1), new Vector2Int(1,1), new Vector2Int(2,1), new Vector2Int(3,1) }, // 2
        new[]{ new Vector2Int(1,3), new Vector2Int(1,2), new Vector2Int(1,1), new Vector2Int(1,0) }, // L
    };

    // O 피스: 회전해도 제자리 그대로이므로 상태 구분 없음
    private static readonly Vector2Int[] OCells =
    {
        new(0,0), new(1,0), new(0,1), new(1,1)
    };

    private static readonly Vector2Int[] NoKick = { Vector2Int.zero };

    /// <summary>
    /// 해당 회전 상태의 셀 목록을 "새 배열"로 반환한다.
    /// 호출자가 반환값을 직접 수정해도 내부 테이블이 오염되지 않는다.
    /// </summary>
    public static Vector2Int[] GetCells(BlockType type, int rot)
    {
        rot = ((rot % 4) + 4) % 4;

        var result = new Vector2Int[4];

        if (type == BlockType.O_Block)
        {
            System.Array.Copy(OCells, result, 4);
            return result;
        }

        if (type == BlockType.I_Block)
        {
            System.Array.Copy(IStates[rot], result, 4);
            return result;
        }

        if (!Spawn3x3.TryGetValue(type, out Vector2Int[] baseCells))
        {
            Debug.LogError($"[SRS] 정의되지 않은 BlockType: {type}");
            return result;
        }

        for (int i = 0; i < 4; i++)
        {
            Vector2Int c = baseCells[i];
            for (int r = 0; r < rot; r++) c = new Vector2Int(c.y, -c.x);   // 시계방향
            result[i] = c;
        }
        return result;
    }

    /// <summary>
    /// 스폰 좌표. 피스는 21~22행(0-index 20,21)에 놓이고
    /// 폭에 따라 중앙 정렬 후 좌측으로 반올림된다.
    /// I: x 3~6 / row 21,  O: x 4~5 / row 21~22,  3칸 폭: x 3~5 / row 21~22
    /// </summary>
    public static Vector2Int SpawnOrigin(BlockType type) => type switch
    {
        BlockType.I_Block => new Vector2Int(3, 18),
        BlockType.O_Block => new Vector2Int(4, 20),
        _ => new Vector2Int(4, 20),
    };

    // ---------- 킥 테이블 ----------

    private static readonly Vector2Int[,][] JlstzKicks = BuildJlstz();
    private static readonly Vector2Int[,][] IKicks = BuildI();

    /// <summary>
    /// from -> to 회전에 시도할 오프셋 목록. 항상 null이 아닌 배열을 반환한다.
    /// </summary>
    public static Vector2Int[] GetKicks(BlockType type, int from, int to)
    {
        from = ((from % 4) + 4) % 4;
        to = ((to % 4) + 4) % 4;

        if (type == BlockType.O_Block || from == to) return NoKick;

        Vector2Int[,][] table = (type == BlockType.I_Block) ? IKicks : JlstzKicks;
        Vector2Int[] kicks = table[from, to];

        if (kicks == null)
        {
            // 180도 회전. SRS 표준 문서에는 180도 킥 정의가 없으므로 제자리만 허용.
            return NoKick;
        }
        return kicks;
    }

    private static Vector2Int[] K(params int[] v)
    {
        var a = new Vector2Int[v.Length / 2];
        for (int i = 0; i < a.Length; i++) a[i] = new Vector2Int(v[i * 2], v[i * 2 + 1]);
        return a;
    }

    // 상태 인덱스: 0=스폰, 1=R, 2=180, 3=L
    private static Vector2Int[,][] BuildJlstz()
    {
        var t = new Vector2Int[4, 4][];
        t[0, 1] = K(0, 0, -1, 0, -1, 1, 0, -2, -1, -2);   // 0 -> R
        t[1, 0] = K(0, 0, 1, 0, 1, -1, 0, 2, 1, 2);       // R -> 0
        t[1, 2] = K(0, 0, 1, 0, 1, -1, 0, 2, 1, 2);       // R -> 2
        t[2, 1] = K(0, 0, -1, 0, -1, 1, 0, -2, -1, -2);   // 2 -> R
        t[2, 3] = K(0, 0, 1, 0, 1, 1, 0, -2, 1, -2);      // 2 -> L
        t[3, 2] = K(0, 0, -1, 0, -1, -1, 0, 2, -1, 2);    // L -> 2
        t[3, 0] = K(0, 0, -1, 0, -1, -1, 0, 2, -1, 2);    // L -> 0
        t[0, 3] = K(0, 0, 1, 0, 1, 1, 0, -2, 1, -2);      // 0 -> L
        // t[0,2] / t[2,0] / t[1,3] / t[3,1] = 180도. 표준 미정의 -> GetKicks에서 NoKick 처리.
        return t;
    }

    private static Vector2Int[,][] BuildI()
    {
        var t = new Vector2Int[4, 4][];
        t[0, 1] = K(0, 0, -2, 0, 1, 0, -2, -1, 1, 2);
        t[1, 0] = K(0, 0, 2, 0, -1, 0, 2, 1, -1, -2);
        t[1, 2] = K(0, 0, -1, 0, 2, 0, -1, 2, 2, -1);
        t[2, 1] = K(0, 0, 1, 0, -2, 0, 1, -2, -2, 1);
        t[2, 3] = K(0, 0, 2, 0, -1, 0, 2, 1, -1, -2);
        t[3, 2] = K(0, 0, -2, 0, 1, 0, -2, -1, 1, 2);
        t[3, 0] = K(0, 0, 1, 0, -2, 0, 1, -2, -2, 1);
        t[0, 3] = K(0, 0, -1, 0, 2, 0, -1, 2, 2, -1);
        return t;
    }
}

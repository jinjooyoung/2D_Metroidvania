using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 상승기류 타일맵. 타일이 그려진 칸부터 위로 liftHeight(6)유닛까지 플레이어를 띄움.
/// 콜라이더 없이 타일 위치만 조회하므로 타일을 그리기만 하면 바로 동작.
/// </summary>
[RequireComponent(typeof(Tilemap))]
public class UpdraftTilemap : MonoBehaviour
{
    [Tooltip("타일 윗면으로부터 몇 유닛까지 띄우는지")]
    public float liftHeight = 6f;
    [Tooltip("상승 목표 속도 (m/s)")]
    public float liftSpeed = 8f;
    public float liftAcceleration = 60f;

    static readonly List<UpdraftTilemap> active = new List<UpdraftTilemap>();

    Tilemap tm;

    void Awake() => tm = GetComponent<Tilemap>();
    void OnEnable() { if (tm == null) tm = GetComponent<Tilemap>(); active.Add(this); }
    void OnDisable() => active.Remove(this);

    /// <summary>발 위치(+좌우 폭)가 어느 상승기류 기둥 안에 있으면 그 존을 반환</summary>
    public static UpdraftTilemap FindLift(Vector2 feet, float halfWidth)
    {
        foreach (UpdraftTilemap z in active)
        {
            if (z.Contains(feet) || z.Contains(feet + Vector2.right * halfWidth) || z.Contains(feet + Vector2.left * halfWidth))
                return z;
        }
        return null;
    }

    public bool Contains(Vector2 p)
    {
        Vector3Int c = tm.WorldToCell(p);
        int range = Mathf.CeilToInt(liftHeight) + 1;
        for (int dy = 0; dy <= range; dy++)
        {
            Vector3Int cell = new Vector3Int(c.x, c.y - dy, 0);
            if (!tm.HasTile(cell)) continue;
            float bottom = tm.CellToWorld(cell).y;
            float top = bottom + tm.cellSize.y;
            if (p.y >= bottom && p.y < top + liftHeight) return true;
        }
        return false;
    }

    void OnDrawGizmos()
    {
        Tilemap t = tm != null ? tm : GetComponent<Tilemap>();
        if (t == null) return;
        foreach (Vector3Int pos in t.cellBounds.allPositionsWithin)
        {
            if (!t.HasTile(pos)) continue;
            Vector3 b = t.CellToWorld(pos);
            Vector3 size = new Vector3(t.cellSize.x, t.cellSize.y + liftHeight, 0f);
            Gizmos.color = new Color(0.4f, 0.85f, 1f, 0.12f);
            Gizmos.DrawCube(b + size * 0.5f, size);
            Gizmos.color = new Color(0.4f, 0.85f, 1f, 0.6f);
            Gizmos.DrawWireCube(b + size * 0.5f, size);
        }
    }
}

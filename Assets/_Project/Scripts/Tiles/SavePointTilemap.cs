using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 저장 지점(쉼터) 타일맵 (트리거).
/// - 처음엔 흰색, 한 번이라도 닿으면 초록색으로 바뀌고 방문 순서대로 번호(1, 2, 3...)가 매겨짐
/// - 닿으면 리스폰 위치 저장 + 체력 회복
/// - 방문한(초록) 저장 지점 주변 1유닛 이내에서 숫자키 N → N번째로 방문한 저장 지점으로 순간이동
/// </summary>
[RequireComponent(typeof(Tilemap))]
public class SavePointTilemap : MonoBehaviour
{
    public bool healOnSave = true;

    [Header("색")]
    public Color unvisitedColor = new Color(1f, 1f, 1f, 0.9f);
    public Color visitedColor = new Color(0.294f, 0.878f, 0.478f, 0.95f);   // #4BE07A

    [Header("순간이동")]
    [Tooltip("방문한 저장 지점 칸에서 이 거리(유닛) 안에 있어야 숫자키 이동 가능")]
    public float teleportRange = 1f;

    /// <summary>씬의 대표 저장 지점 타일맵 (HUD 표시용)</summary>
    public static SavePointTilemap Main { get; private set; }

    Tilemap tm;
    Tilemap visual;     // 색을 바꿀 타일맵 (Level 방식이면 Level 타일맵)
    readonly List<Vector3Int> visited = new List<Vector3Int>();

    public IReadOnlyList<Vector3Int> Visited => visited;

    void Awake()
    {
        tm = GetComponent<Tilemap>();
        if (Main == null) Main = this;

        Grid grid = GetComponentInParent<Grid>();
        LevelTilemap lt = grid != null ? grid.GetComponentInChildren<LevelTilemap>() : null;
        if (lt != null) visual = lt.Source;
        else if (GetComponent<TilemapRenderer>() != null) visual = tm;
    }

    void Start()
    {
        tm.CompressBounds();
        foreach (Vector3Int p in tm.cellBounds.allPositionsWithin)
            if (tm.HasTile(p)) SetColor(p, unvisitedColor);
    }

    void OnDestroy()
    {
        if (Main == this) Main = null;
    }

    // ───────── 방문 ─────────
    void OnTriggerEnter2D(Collider2D other)
    {
        PlayerController p = other.GetComponentInParent<PlayerController>();
        if (p == null || p.IsDead) return;

        if (!FindCell(other.bounds, out Vector3Int cell)) return;

        if (!visited.Contains(cell))
        {
            visited.Add(cell);
            SetColor(cell, visitedColor);
        }
        if (GameManager.Instance != null) GameManager.Instance.SetSavePoint(SpawnOf(cell), healOnSave);
    }

    // ───────── 순간이동 ─────────
    void Update()
    {
        if (visited.Count == 0) return;
        int key = NumberKeyDown();
        if (key < 1 || key > visited.Count) return;

        GameManager gm = GameManager.Instance;
        if (gm == null || gm.player == null || gm.player.IsDead) return;
        if (!IsNearVisited(gm.player, out Vector3Int current)) return;

        Vector3Int target = visited[key - 1];
        if (target == current) return;

        gm.player.TeleportTo(SpawnOf(target));
        Camera cam = Camera.main;
        if (cam != null)
        {
            CameraFollow follow = cam.GetComponent<CameraFollow>();
            if (follow != null) follow.Snap();
        }
        // 도착하면 저장 지점 트리거에 닿으므로 저장·회복은 자동 처리됨
    }

    /// <summary>플레이어가 방문한 저장 지점 근처(teleportRange 이내)에 있는지</summary>
    public bool IsNearVisited(PlayerController player, out Vector3Int cell)
    {
        cell = default;
        Collider2D col = player.GetComponent<Collider2D>();
        Bounds pb = col != null ? col.bounds : new Bounds(player.transform.position, Vector3.one);
        foreach (Vector3Int c in visited)
        {
            Vector3 min = tm.CellToWorld(c) - new Vector3(teleportRange, teleportRange, 0f);
            Vector3 size = new Vector3(tm.cellSize.x + teleportRange * 2f, tm.cellSize.y + teleportRange * 2f, 100f);
            Bounds area = new Bounds(new Vector3(min.x + size.x * 0.5f, min.y + size.y * 0.5f, pb.center.z), size);
            if (area.Intersects(pb)) { cell = c; return true; }
        }
        return false;
    }

    /// <summary>저장 지점 칸의 바닥 중앙 (리스폰·이동 위치)</summary>
    public Vector2 SpawnOf(Vector3Int cell)
    {
        Vector3 c = tm.GetCellCenterWorld(cell);
        return new Vector2(c.x, c.y - tm.cellSize.y * 0.5f);
    }

    bool FindCell(Bounds b, out Vector3Int cell)
    {
        Vector3Int min = tm.WorldToCell(b.min);
        Vector3Int max = tm.WorldToCell(b.max);
        for (int x = min.x; x <= max.x; x++)
            for (int y = min.y; y <= max.y; y++)
            {
                Vector3Int p = new Vector3Int(x, y, 0);
                if (tm.HasTile(p)) { cell = p; return true; }
            }
        cell = default;
        return false;
    }

    void SetColor(Vector3Int cell, Color color)
    {
        if (visual == null) return;
        visual.SetTileFlags(cell, TileFlags.None);
        visual.SetColor(cell, color);
    }

    static int NumberKeyDown()
    {
        for (int i = 1; i <= 9; i++)
            if (Input.GetKeyDown(KeyCode.Alpha0 + i) || Input.GetKeyDown(KeyCode.Keypad0 + i))
                return i;
        return -1;
    }
}

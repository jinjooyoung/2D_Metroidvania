using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 저장 지점 타일맵 (트리거). 플레이어가 닿으면 해당 칸 바닥을 리스폰 위치로 저장하고 체력 회복.
/// </summary>
[RequireComponent(typeof(Tilemap))]
public class SavePointTilemap : MonoBehaviour
{
    public bool healOnSave = true;

    Tilemap tm;

    void Awake() => tm = GetComponent<Tilemap>();

    void OnTriggerEnter2D(Collider2D other)
    {
        PlayerController p = other.GetComponentInParent<PlayerController>();
        if (p == null || p.IsDead || GameManager.Instance == null) return;

        Vector2 spawn = p.transform.position;
        Bounds b = other.bounds;
        Vector3Int min = tm.WorldToCell(b.min);
        Vector3Int max = tm.WorldToCell(b.max);
        for (int x = min.x; x <= max.x; x++)
        for (int y = min.y; y <= max.y; y++)
        {
            Vector3Int cell = new Vector3Int(x, y, 0);
            if (!tm.HasTile(cell)) continue;
            Vector3 c = tm.GetCellCenterWorld(cell);
            spawn = new Vector2(c.x, c.y - tm.cellSize.y * 0.5f);   // 저장 타일 칸의 바닥
            x = max.x + 1;
            break;
        }

        GameManager.Instance.SetSavePoint(spawn, healOnSave);
    }
}

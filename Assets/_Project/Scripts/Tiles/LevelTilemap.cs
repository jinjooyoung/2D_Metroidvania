using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 레벨 디자인용 단일 타일맵.
/// 여기에 바닥/발판/가시/상승기류/저장지점을 전부 그리면,
/// 타일 종류를 보고 [자동] 충돌·기능 레이어로 분배(동기화)함.
/// 등록되지 않은 타일은 장식(충돌 없음)으로 취급.
/// </summary>
[RequireComponent(typeof(Tilemap))]
public class LevelTilemap : MonoBehaviour
{
    [Header("타일 종류 등록 (같은 종류로 쓸 타일을 여러 개 넣어도 됨)")]
    public TileBase[] solidTiles;       // 벽·바닥·천장
    public TileBase[] oneWayTiles;      // 통과 발판
    public TileBase[] spikeTiles;       // 가시 함정
    public TileBase[] updraftTiles;     // 상승기류
    public TileBase[] savePointTiles;   // 저장 지점

    [Header("[자동] 레이어 - 여기에 직접 그려도 Level로 옮겨짐")]
    public Tilemap solidLayer;
    public Tilemap oneWayLayer;
    public Tilemap spikeLayer;
    public Tilemap updraftLayer;
    public Tilemap savePointLayer;

    Tilemap source;
    public Tilemap Source => source != null ? source : (source = GetComponent<Tilemap>());

    void Awake() => Sync();   // 플레이 시작 시 한 번 더 맞춰줌 (이미 같으면 변경 없음)

    public void Sync()
    {
        Source.CompressBounds();
        SyncLayer(solidLayer, solidTiles);
        SyncLayer(oneWayLayer, oneWayTiles);
        SyncLayer(spikeLayer, spikeTiles);
        SyncLayer(updraftLayer, updraftTiles);
        SyncLayer(savePointLayer, savePointTiles);
    }

    void SyncLayer(Tilemap dst, TileBase[] kinds)
    {
        if (dst == null || dst == Source) return;
        dst.CompressBounds();
        BoundsInt a = Source.cellBounds, b = dst.cellBounds;
        int xMin = Mathf.Min(a.xMin, b.xMin), xMax = Mathf.Max(a.xMax, b.xMax);
        int yMin = Mathf.Min(a.yMin, b.yMin), yMax = Mathf.Max(a.yMax, b.yMax);

        for (int x = xMin; x < xMax; x++)
        for (int y = yMin; y < yMax; y++)
        {
            Vector3Int p = new Vector3Int(x, y, 0);
            TileBase t = Source.GetTile(p);
            TileBase want = IsKind(t, kinds) ? t : null;
            if (dst.GetTile(p) != want) dst.SetTile(p, want);
        }
    }

    public static bool IsKind(TileBase t, TileBase[] kinds)
    {
        if (t == null || kinds == null) return false;
        foreach (TileBase k in kinds) if (k == t) return true;
        return false;
    }

    public bool IsGeneratedLayer(Tilemap tm) =>
        tm != null && (tm == solidLayer || tm == oneWayLayer || tm == spikeLayer || tm == updraftLayer || tm == savePointLayer);

    public TileBase[] KindsOf(Tilemap layer)
    {
        if (layer == solidLayer) return solidTiles;
        if (layer == oneWayLayer) return oneWayTiles;
        if (layer == spikeLayer) return spikeTiles;
        if (layer == updraftLayer) return updraftTiles;
        if (layer == savePointLayer) return savePointTiles;
        return null;
    }
}

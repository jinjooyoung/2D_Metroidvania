using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 끌림 기믹 타일맵. 각 타일 중심에서 반경 radius(5) 원 안의 엔티티(플레이어·몬스터)를
/// pullSpeed(3.5 m/s)로 타일 쪽으로 끌어당김.
/// 서서 걷기(8) → 순 탈출 4.5 m/s, 기어가기(4) → 순 탈출 0.5 m/s로 겨우 탈출 가능.
/// 끌림은 "속도 덧셈"으로 적용되므로 가시·낭떠러지 쪽으로 엔티티를 끌고 가는 함정으로 쓸 수 있음.
/// </summary>
[RequireComponent(typeof(Tilemap))]
public class PullTilemap : MonoBehaviour
{
    [Tooltip("끌어당기는 원형 범위 반경 (타일 중심 기준, m)")]
    public float radius = 5f;
    [Tooltip("끌어당기는 속도 (m/s)")]
    public float pullSpeed = 3.5f;

    static readonly List<PullTilemap> active = new List<PullTilemap>();

    Tilemap tm;
    readonly List<Vector2> centers = new List<Vector2>();

    void Awake() => tm = GetComponent<Tilemap>();

    void OnEnable()
    {
        if (tm == null) tm = GetComponent<Tilemap>();
        Rebuild();
        active.Add(this);
    }

    void OnDisable() => active.Remove(this);

    void Start() => Rebuild();   // Level 동기화(Awake) 이후 한 번 더 갱신

    /// <summary>타일 위치 캐시 갱신 (런타임에 타일을 바꿨다면 호출)</summary>
    public void Rebuild()
    {
        if (tm == null) tm = GetComponent<Tilemap>();
        centers.Clear();
        tm.CompressBounds();
        foreach (Vector3Int p in tm.cellBounds.allPositionsWithin)
            if (tm.HasTile(p)) centers.Add(tm.GetCellCenterWorld(p));
    }

    /// <summary>
    /// 해당 위치에 작용하는 끌림 속도. 범위 안에서 가장 가까운 끌림 타일 쪽으로 pullSpeed.
    /// 범위 밖이면 Vector2.zero.
    /// </summary>
    public static Vector2 GetPull(Vector2 position)
    {
        float bestSqr = float.MaxValue;
        Vector2 bestCenter = Vector2.zero;
        float speed = 0f;

        foreach (PullTilemap z in active)
        {
            float r2 = z.radius * z.radius;
            foreach (Vector2 c in z.centers)
            {
                float d2 = (c - position).sqrMagnitude;
                if (d2 <= r2 && d2 < bestSqr) { bestSqr = d2; bestCenter = c; speed = z.pullSpeed; }
            }
        }
        if (speed <= 0f) return Vector2.zero;

        Vector2 dir = bestCenter - position;
        float dist = dir.magnitude;
        if (dist < 0.05f) return Vector2.zero;
        // 중심 0.3m 안에서는 서서히 줄여 떨림 방지
        return dir / dist * speed * Mathf.Clamp01(dist / 0.3f);
    }

    void OnDrawGizmos()
    {
        Tilemap t = tm != null ? tm : GetComponent<Tilemap>();
        if (t == null) return;
        foreach (Vector3Int p in t.cellBounds.allPositionsWithin)
        {
            if (!t.HasTile(p)) continue;
            Vector3 c = t.GetCellCenterWorld(p);
            Gizmos.color = new Color(0.78f, 0.64f, 1f, 0.8f);
            Gizmos.DrawWireSphere(c, radius);
            Gizmos.color = new Color(0.78f, 0.64f, 1f, 0.35f);
            Gizmos.DrawSphere(c, 0.3f);
        }
    }
}

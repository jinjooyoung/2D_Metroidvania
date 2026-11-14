using UnityEngine;

/// <summary>
/// 즉사 구역 (낭떠러지 밑 검은 칸).
/// 플레이어가 닿으면 즉시 마지막 저장 지점에서 리스폰(정신력 전부 회복), 몬스터가 떨어지면 비활성화.
/// 세부 맵 에디터가 검은 칸(K) 가로 묶음마다 하나씩 만든다.
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
public class KillZone : MonoBehaviour
{
    void Reset() => GetComponent<BoxCollider2D>().isTrigger = true;

    void OnTriggerEnter2D(Collider2D other)
    {
        PlayerController p = other.GetComponentInParent<PlayerController>();
        if (p != null)
        {
            if (p.IsDead) return;
            Vector2 spawn = GameManager.Instance != null ? GameManager.Instance.SavePoint : (Vector2)p.transform.position;
            p.Respawn(spawn);
            return;
        }
        EnemyAI e = other.GetComponentInParent<EnemyAI>();
        if (e != null) e.gameObject.SetActive(false);
    }
}

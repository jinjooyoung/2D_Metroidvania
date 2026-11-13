using UnityEngine;

/// <summary>
/// 닿으면 피해를 주는 오브젝트 (가시 함정 타일맵 등).
/// 대상의 Health 무적 시간 덕분에 계속 밟고 있어도 무적 시간마다 1번씩만 피해.
/// </summary>
public class DamageOnTouch : MonoBehaviour
{
    public int damage = 10;
    public LayerMask targetMask = ~0;

    void OnCollisionEnter2D(Collision2D c) => TryDamage(c);
    void OnCollisionStay2D(Collision2D c) => TryDamage(c);
    void OnTriggerEnter2D(Collider2D o) => TryDamage(o, (Vector2)o.bounds.center + Vector2.down);
    void OnTriggerStay2D(Collider2D o) => TryDamage(o, (Vector2)o.bounds.center + Vector2.down);

    void TryDamage(Collision2D c)
    {
        Vector2 point = c.contactCount > 0 ? c.GetContact(0).point : (Vector2)c.collider.bounds.center + Vector2.down;
        TryDamage(c.collider, point);
    }

    void TryDamage(Collider2D other, Vector2 sourcePoint)
    {
        if ((targetMask.value & (1 << other.gameObject.layer)) == 0) return;
        Health h = other.GetComponentInParent<Health>();
        if (h != null) h.TakeDamage(damage, sourcePoint);
    }
}

using UnityEngine;

/// <summary>따뜻한 기억 (회복 아이템). 닿으면 정신력 회복 후 사라짐.</summary>
[RequireComponent(typeof(Collider2D))]
public class HealPickup : MonoBehaviour
{
    public int amount = 30;

    void OnTriggerEnter2D(Collider2D other)
    {
        PlayerController p = other.GetComponentInParent<PlayerController>();
        if (p == null || p.IsDead) return;
        p.Health.Heal(amount);
        gameObject.SetActive(false);
    }
}

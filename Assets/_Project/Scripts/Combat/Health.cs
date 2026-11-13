using System;
using UnityEngine;

/// <summary>
/// 플레이어/몬스터 공용 체력. 증감, 피격 무적, 깜빡임, 사망 이벤트.
/// </summary>
public class Health : MonoBehaviour
{
    public int maxHealth = 100;
    [SerializeField] int currentHealth = 100;

    [Tooltip("피격 후 무적 시간(초)")]
    public float invincibleTime = 1f;

    [Tooltip("무적 중 깜빡일 렌더러 (비워두면 자식 전체, 이름에 Attack이 들어간 것 제외)")]
    public SpriteRenderer[] blinkRenderers;

    public int Current => currentHealth;
    public bool IsDead { get; private set; }
    public bool IsInvincible => invincibleTimer > 0f;

    /// <summary>(받은 피해량, 공격 위치)</summary>
    public event Action<int, Vector2> Damaged;
    public event Action<int> Healed;
    public event Action Died;
    public event Action Changed;

    float invincibleTimer;
    float[] baseAlpha;

    void Awake()
    {
        currentHealth = maxHealth;

        if (blinkRenderers == null || blinkRenderers.Length == 0)
        {
            var list = new System.Collections.Generic.List<SpriteRenderer>();
            foreach (var r in GetComponentsInChildren<SpriteRenderer>())
                if (!r.name.Contains("Attack")) list.Add(r);
            blinkRenderers = list.ToArray();
        }
        baseAlpha = new float[blinkRenderers.Length];
        for (int i = 0; i < blinkRenderers.Length; i++)
            baseAlpha[i] = blinkRenderers[i] != null ? blinkRenderers[i].color.a : 1f;
    }

    void Update()
    {
        if (invincibleTimer > 0f)
        {
            invincibleTimer -= Time.deltaTime;
            bool dim = invincibleTimer > 0f && Mathf.FloorToInt(Time.time * 20f) % 2 == 0;
            SetAlpha(dim ? 0.3f : 1f);
        }
    }

    /// <summary>피해를 받음. 실제로 피해가 들어갔으면 true.</summary>
    public bool TakeDamage(int amount, Vector2 sourcePosition)
    {
        if (IsDead || amount <= 0 || invincibleTimer > 0f) return false;

        currentHealth = Mathf.Max(0, currentHealth - amount);
        invincibleTimer = invincibleTime;

        Damaged?.Invoke(amount, sourcePosition);
        Changed?.Invoke();

        if (currentHealth == 0)
        {
            IsDead = true;
            Died?.Invoke();
        }
        return true;
    }

    public void Heal(int amount)
    {
        if (IsDead || amount <= 0) return;
        int before = currentHealth;
        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        if (currentHealth != before)
        {
            Healed?.Invoke(currentHealth - before);
            Changed?.Invoke();
        }
    }

    public void ResetHealth()
    {
        IsDead = false;
        currentHealth = maxHealth;
        invincibleTimer = 0f;
        SetAlpha(1f);
        Changed?.Invoke();
    }

    void SetAlpha(float mul)
    {
        if (baseAlpha == null) return;
        for (int i = 0; i < blinkRenderers.Length; i++)
        {
            if (blinkRenderers[i] == null) continue;
            Color c = blinkRenderers[i].color;
            c.a = baseAlpha[i] * mul;
            blinkRenderers[i].color = c;
        }
    }

    // 인스펙터 컴포넌트 우클릭(⋮) 메뉴에서 바로 테스트
    [ContextMenu("테스트: 10 데미지")]
    void DebugDamage() { invincibleTimer = 0f; TakeDamage(10, (Vector2)transform.position + Vector2.left * 0.1f); }

    [ContextMenu("테스트: 10 회복")]
    void DebugHeal() { Heal(10); }
}

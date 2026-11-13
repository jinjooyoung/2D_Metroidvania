using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 플레이어 근접 공격 (마우스 좌클릭).
/// 바라보는 방향 앞쪽, 허리 높이를 중심으로 가로 2m × 세로 1m 직사각형 범위.
/// 범위와 겹친 Health 보유 엔티티에 데미지.
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class PlayerCombat : MonoBehaviour
{
    [Header("공격")]
    public int damage = 10;
    public float cooldown = 0.5f;
    public Vector2 attackSize = new Vector2(2f, 1f);
    public LayerMask targetMask;

    [Header("연출 (게임 화면에 잠깐 표시)")]
    public SpriteRenderer attackFlash;
    public float flashTime = 0.1f;
    public Color flashColor = new Color(1f, 1f, 1f, 0.45f);

    public float CooldownRemaining => Mathf.Max(0f, cooldownTimer);

    PlayerController pc;
    float cooldownTimer, flashTimer;
    readonly HashSet<Health> hitBuffer = new HashSet<Health>();

    void Awake()
    {
        pc = GetComponent<PlayerController>();
        SetFlashAlpha(0f);
    }

    void Update()
    {
        cooldownTimer -= Time.deltaTime;

        if (flashTimer > 0f)
        {
            flashTimer -= Time.deltaTime;
            if (flashTimer <= 0f) SetFlashAlpha(0f);
        }

        if (pc.IsDead) return;
        if (Input.GetMouseButtonDown(0) && cooldownTimer <= 0f) DoAttack();
    }

    /// <summary>현재 공격 박스(월드 좌표)</summary>
    public void GetAttackBox(out Vector2 center, out Vector2 size)
    {
        PlayerController p = pc != null ? pc : GetComponent<PlayerController>();
        float waist = p.CurrentHeight * 0.5f;                          // 허리 높이 (서있을 때 1m)
        float forward = p.width * 0.5f + attackSize.x * 0.5f;          // 몸 앞쪽부터 2m
        center = (Vector2)transform.position + new Vector2(p.Facing * forward, waist);
        size = attackSize;
    }

    void DoAttack()
    {
        cooldownTimer = cooldown;
        GetAttackBox(out Vector2 center, out Vector2 size);

        hitBuffer.Clear();
        foreach (Collider2D c in Physics2D.OverlapBoxAll(center, size, 0f, targetMask))
        {
            Health h = c.GetComponentInParent<Health>();
            if (h == null || h.gameObject == gameObject || !hitBuffer.Add(h)) continue;
            h.TakeDamage(damage, transform.position);
        }

        if (attackFlash != null)
        {
            // Visual 자식이므로 좌우 반전은 자동
            attackFlash.transform.localPosition = new Vector3(pc.width * 0.5f + attackSize.x * 0.5f, pc.CurrentHeight * 0.5f, 0f);
            attackFlash.transform.localScale = new Vector3(attackSize.x, attackSize.y, 1f);
            SetFlashAlpha(1f);
            flashTimer = flashTime;
        }
    }

    void SetFlashAlpha(float a)
    {
        if (attackFlash == null) return;
        Color c = flashColor;
        c.a *= a;
        attackFlash.color = c;
    }

    void OnDrawGizmos()
    {
        GetAttackBox(out Vector2 center, out Vector2 size);
        bool active = Application.isPlaying && flashTimer > 0f;
        Gizmos.color = new Color(1f, 0.85f, 0.1f, active ? 0.55f : 0.2f);
        Gizmos.DrawCube(center, size);
        Gizmos.color = new Color(1f, 0.85f, 0.1f, 0.9f);
        Gizmos.DrawWireCube(center, size);
    }
}

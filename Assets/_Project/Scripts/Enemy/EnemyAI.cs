using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 기본 몬스터: 순찰 → 플레이어 감지 시 추적 → 공격 범위 안이면 예비동작 후 공격.
/// 체력은 Health 컴포넌트(기본 30), 공격력/범위/쿨타임은 여기서 설정.
/// 공격 범위는 반투명 빨간 기즈모로 표시.
/// </summary>
[RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D), typeof(Health))]
public class EnemyAI : MonoBehaviour
{
    public enum State { Idle, Patrol, Chase, Windup }

    public static readonly List<EnemyAI> All = new List<EnemyAI>();

    [Header("이동")]
    public float moveSpeed = 2.5f;
    public bool patrolWhenIdle = true;
    public float patrolSpeed = 1.2f;

    [Header("감지")]
    public float detectRange = 6f;
    [Tooltip("이 거리보다 멀어지면 추적 포기")]
    public float loseRange = 9f;
    public bool requireLineOfSight = true;

    [Header("공격")]
    public int attackDamage = 10;
    public Vector2 attackSize = new Vector2(1.4f, 1.4f);
    [Tooltip("발 기준 오프셋. x는 바라보는 방향으로 자동 반전")]
    public Vector2 attackOffset = new Vector2(1.15f, 0.7f);
    [Tooltip("공격 판정 전 예비동작 시간 (0이면 즉시)")]
    public float attackWindup = 0.3f;
    public float attackCooldown = 1.5f;
    public bool hitInterruptsAttack = true;

    [Header("피격")]
    public Vector2 knockbackVelocity = new Vector2(4f, 3f);
    public float hitStunTime = 0.25f;

    [Header("레이어")]
    public LayerMask playerMask;
    public LayerMask groundMask;     // 벽 판정
    public LayerMask ledgeMask;      // 낭떠러지 판정 (바닥 + 발판)

    [Header("비주얼")]
    public Transform visual;
    public SpriteRenderer body;
    public SpriteRenderer attackArea;
    public Color windupColor = new Color(1f, 0.9f, 0.3f);

    [SerializeField] State state = State.Patrol;

    public Health Health { get; private set; }

    Rigidbody2D rb;
    BoxCollider2D col;
    Transform player;
    Health playerHealth;
    Collider2D playerCollider;
    int facing = 1;
    float cooldownTimer, windupTimer, stunTimer, attackFlashTimer;
    Color bodyBaseColor = Color.white;
    float appliedPullX;     // 지난 스텝에 더한 끌림 속도 (누적 방지)

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<BoxCollider2D>();
        Health = GetComponent<Health>();
        rb.freezeRotation = true;

        if (visual != null && visual.localScale.x < 0f) facing = -1;
        if (body != null) bodyBaseColor = body.color;
        if (attackArea != null)
        {
            attackArea.transform.localPosition = attackOffset;
            attackArea.transform.localScale = new Vector3(attackSize.x, attackSize.y, 1f);
        }
        state = patrolWhenIdle ? State.Patrol : State.Idle;
    }

    void OnEnable()
    {
        All.Add(this);
        Health.Damaged += OnDamaged;
        Health.Died += OnDied;
    }

    void OnDisable()
    {
        All.Remove(this);
        Health.Damaged -= OnDamaged;
        Health.Died -= OnDied;
    }

    void Start() => FindPlayer();

    void FindPlayer()
    {
        GameObject go = null;
        if (GameManager.Instance != null && GameManager.Instance.player != null) go = GameManager.Instance.player.gameObject;
        if (go == null) go = GameObject.FindWithTag("Player");
        if (go == null) return;
        player = go.transform;
        playerHealth = go.GetComponent<Health>();
        playerCollider = go.GetComponent<Collider2D>();
    }

    // ───────── AI ─────────
    void FixedUpdate()
    {
        if (Health.IsDead) return;
        float dt = Time.fixedDeltaTime;
        cooldownTimer -= dt;

        if (stunTimer > 0f) { stunTimer -= dt; appliedPullX = 0f; return; }   // 넉백 유지
        if (player == null) FindPlayer();

        Vector2 v = rb.GetVelocity();
        v.x -= appliedPullX;
        appliedPullX = 0f;
        bool playerAlive = player != null && (playerHealth == null || !playerHealth.IsDead);

        // 예비동작 → 공격
        if (state == State.Windup)
        {
            v.x = 0f;
            windupTimer -= dt;
            if (windupTimer <= 0f)
            {
                PerformAttack();
                cooldownTimer = attackCooldown;
                state = State.Chase;
            }
            ApplyPullAndSet(v);
            return;
        }

        float dist = playerAlive ? Vector2.Distance(Center, PlayerCenter) : float.MaxValue;
        if (state != State.Chase)
        {
            if (playerAlive && dist <= detectRange && (!requireLineOfSight || CanSeePlayer()))
                state = State.Chase;
        }
        else if (!playerAlive || dist > loseRange)
        {
            state = patrolWhenIdle ? State.Patrol : State.Idle;
        }

        switch (state)
        {
            case State.Chase:
                float dx = PlayerCenter.x - Center.x;
                if (Mathf.Abs(dx) > 0.15f) facing = dx > 0f ? 1 : -1;

                if (PlayerInAttackBox())
                {
                    v.x = 0f;
                    if (cooldownTimer <= 0f) { state = State.Windup; windupTimer = attackWindup; }
                }
                else v.x = CanMoveForward() ? facing * moveSpeed : 0f;
                break;

            case State.Patrol:
                if (!CanMoveForward()) facing = -facing;
                v.x = facing * patrolSpeed;
                break;

            default:
                v.x = 0f;
                break;
        }

        ApplyPullAndSet(v);
    }

    /// <summary>끌림 기믹: 수평 끌림 속도를 더해 적용</summary>
    void ApplyPullAndSet(Vector2 v)
    {
        Vector2 pull = PullTilemap.GetPull(Center);
        appliedPullX = pull.x;
        v.x += pull.x;
        rb.SetVelocity(v);
    }

    void Update()
    {
        if (visual != null) visual.localScale = new Vector3(facing, 1f, 1f);
        if (attackFlashTimer > 0f) attackFlashTimer -= Time.deltaTime;

        if (body != null)
        {
            Color c = state == State.Windup ? windupColor : bodyBaseColor;
            c.a = body.color.a;                       // 알파는 Health 깜빡임이 관리
            body.color = c;
        }

        if (attackArea != null)
        {
            float a = attackFlashTimer > 0f ? 0.6f
                    : state == State.Windup ? 0.2f + 0.15f * Mathf.Sin(Time.time * 40f)
                    : 0f;
            attackArea.color = new Color(1f, 0.1f, 0.1f, a);
        }
    }

    void PerformAttack()
    {
        attackFlashTimer = 0.12f;
        foreach (Collider2D c in Physics2D.OverlapBoxAll(AttackCenter(facing), attackSize, 0f, playerMask))
        {
            Health h = c.GetComponentInParent<Health>();
            if (h != null && h != Health) { h.TakeDamage(attackDamage, Center); break; }
        }
    }

    // ───────── 판정 헬퍼 ─────────
    Vector2 Center => (Vector2)transform.position + col.offset;
    Vector2 PlayerCenter => playerCollider != null ? (Vector2)playerCollider.bounds.center : (Vector2)player.position + Vector2.up;

    Vector2 AttackCenter(int f) => (Vector2)transform.position + new Vector2(attackOffset.x * f, attackOffset.y);

    bool PlayerInAttackBox() => Physics2D.OverlapBox(AttackCenter(facing), attackSize, 0f, playerMask) != null;

    bool CanSeePlayer()
    {
        Vector2 eye = (Vector2)transform.position + Vector2.up * (col.size.y * 0.8f);
        return Physics2D.Linecast(eye, PlayerCenter, groundMask).collider == null;
    }

    bool CanMoveForward()
    {
        Vector2 p = rb.position;
        float hw = col.size.x * 0.5f;
        bool groundAhead = Physics2D.OverlapBox(p + new Vector2(facing * (hw + 0.15f), -0.1f), new Vector2(0.1f, 0.2f), 0f, ledgeMask) != null;
        bool wallAhead = Physics2D.OverlapBox(p + new Vector2(facing * (hw + 0.08f), col.size.y * 0.5f), new Vector2(0.1f, col.size.y * 0.6f), 0f, groundMask) != null;
        return groundAhead && !wallAhead;
    }

    // ───────── 피격 / 사망 ─────────
    void OnDamaged(int amount, Vector2 source)
    {
        float dir = Mathf.Sign(rb.position.x - source.x);
        if (dir == 0f) dir = -facing;
        rb.SetVelocity(new Vector2(dir * knockbackVelocity.x, knockbackVelocity.y));
        appliedPullX = 0f;
        stunTimer = hitStunTime;
        if (hitInterruptsAttack && state == State.Windup) state = State.Chase;
        if (state != State.Windup) state = State.Chase;        // 맞으면 바로 어그로
    }

    void OnDied()
    {
        Destroy(gameObject);
    }

    // ───────── 기즈모 ─────────
    void OnDrawGizmos()
    {
        int f = Application.isPlaying ? facing : (visual != null && visual.localScale.x < 0f ? -1 : 1);
        Vector2 c = AttackCenter(f);
        Gizmos.color = new Color(1f, 0f, 0f, state == State.Windup && Application.isPlaying ? 0.5f : 0.25f);
        Gizmos.DrawCube(c, attackSize);                      // 반투명 빨간 공격 범위
        Gizmos.color = new Color(1f, 0f, 0f, 0.9f);
        Gizmos.DrawWireCube(c, attackSize);
    }

    void OnDrawGizmosSelected()
    {
        Vector3 center = transform.position + Vector3.up * 0.7f;
        Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.8f);
        Gizmos.DrawWireSphere(center, detectRange);          // 감지 범위
        Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.25f);
        Gizmos.DrawWireSphere(center, loseRange);            // 추적 포기 범위
    }
}

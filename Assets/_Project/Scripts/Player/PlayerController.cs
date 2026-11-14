using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 메트로베니아 플레이어 이동.
/// 1유닛 = 1m, 피벗 = 발바닥(콜라이더 아래쪽 중앙).
///
/// 조작: A/D 이동, W 또는 Space 점프, S 웅크리기(바닥) / 발판 내려가기(발판), Shift 대쉬
/// 대쉬·벽타기·더블점프는 GameManager의 bool 값으로 사용 가능 여부가 결정됨.
/// </summary>
[RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D), typeof(Health))]
public class PlayerController : MonoBehaviour
{
    [Header("크기 (1유닛 = 1m)")]
    public float width = 0.8f;
    public float standHeight = 2f;
    public float crouchHeight = 1f;
    [Tooltip("콜라이더 높이를 키보다 이만큼 작게 → 키와 딱 같은 높이의 틈(1칸, 2칸)을 통과 가능")]
    public float colliderSkin = 0.1f;

    [Header("이동")]
    public float moveSpeed = 4f;            // 4 m/s
    public float crouchMoveSpeed = 2f;      // 웅크린 상태 2 m/s

    [Header("점프")]
    public float jumpHeight = 2f;           // 발 기준 2m
    public float crouchJumpHeight = 1f;     // 웅크린 상태 1m
    [Tooltip("최고점까지 걸리는 시간. 중력은 높이/시간으로 자동 계산")]
    public float timeToApex = 0.4f;
    [Tooltip("점프 높이 여유분. 높이 N의 발판에 발이 확실히 올라가도록 살짝 더 뜀")]
    public float jumpHeightMargin = 0.05f;
    public float maxFallSpeed = 20f;
    public float coyoteTime = 0.1f;         // 발판 끝에서 살짝 늦게 눌러도 점프 허용
    public float jumpBufferTime = 0.1f;     // 착지 직전 입력 미리 받기
    [Tooltip("켜면 점프키를 빨리 떼면 낮게 뜀. 레벨 측정용으로는 끄는 걸 권장")]
    public bool variableJumpHeight = false;
    [Range(0f, 1f)] public float jumpCutMultiplier = 0.5f;

    [Header("대쉬")]
    public float dashSpeed = 8f;            // 8 m/s
    public float dashDuration = 0.5f;       // 0.5초 → 4m
    public float dashCooldown = 1f;

    [Header("벽타기")]
    public float wallSlideSpeed = 2f;
    [Tooltip("벽 쪽(또는 무입력)으로 점프: 벽에서 살짝 떨어지는 수평 속도")]
    public float wallJumpHorizontalSpeed = 6f;
    [Tooltip("벽 쪽 점프: 떨어지는 시간 (이후 자동으로 다시 벽으로 붙음)")]
    public float wallJumpInputLockTime = 0.15f;
    [Tooltip("벽 쪽 점프: 다시 벽으로 돌아가는 최대 시간")]
    public float wallJumpReturnTime = 0.25f;
    [Tooltip("벽 반대 방향키 + 점프: 그 방향으로 뛰어나가는 수평 속도")]
    public float wallJumpAwaySpeed = 8f;
    [Tooltip("벽 반대 방향 점프 직후 입력을 무시하는 시간")]
    public float wallJumpAwayLockTime = 0.1f;
    [Tooltip("켜면 벽에 매달린 상태에서 S로 벽을 놓음 (기본: 끔 → 방향키로는 벽에서 떨어지지 않음)")]
    public bool releaseWallWithDown = false;

    [Header("웅크리기")]
    [Tooltip("공중에서 웅크림이 강제로 풀리기까지 필요한 체공 시간 (+ 머리 위 2m 공간 필요)")]
    public float crouchForceStandAirTime = 1.5f;

    [Header("통과 발판")]
    public float dropThroughTime = 0.3f;

    [Header("피격")]
    public Vector2 knockbackVelocity = new Vector2(6f, 6f);
    public float hitStunTime = 0.25f;

    [Header("충돌 레이어")]
    public LayerMask groundMask;            // 벽/바닥/천장/가시
    public LayerMask oneWayMask;            // 통과 발판

    [Header("비주얼 (좌우 반전 / 웅크리기 표시)")]
    public Transform visual;
    public Transform body;
    public Transform eye;

    // ───────── 상태 (읽기 전용) ─────────
    public int Facing { get; private set; } = 1;
    public bool IsGrounded { get; private set; }
    public bool IsCrouching { get; private set; }
    public bool IsDashing { get; private set; }
    public bool IsWallSliding { get; private set; }     // 벽에 매달려 미끄러지는 중
    public bool IsInUpdraft { get; private set; }
    public bool IsBeingPulled { get; private set; }
    public bool IsDead { get; private set; }
    public float CurrentHeight => IsCrouching ? crouchHeight : standHeight;
    public float DashCooldownRemaining => Mathf.Max(0f, dashCooldownTimer);
    /// <summary>마지막 점프의 실제 상승 높이(발 기준) - 레벨 측정용</summary>
    public float LastJumpHeight { get; private set; }
    public Health Health => health != null ? health : (health = GetComponent<Health>());

    Rigidbody2D rb;
    BoxCollider2D col;
    Health health;

    float gravity, jumpVelocity, crouchJumpVelocity;

    // 입력
    float moveInput;
    bool downHeld, jumpHeld, dashRequested;
    float jumpBufferTimer;

    // 타이머
    float coyoteTimer, timeSinceGrounded, dashTimer, dashCooldownTimer;
    float wallJumpLockTimer, hitStunTimer, dropTimer;
    float wallReturnTimer;
    int wallReturnDir;
    int wallClingDir;               // 매달린 벽 방향 (-1 왼쪽, 1 오른쪽, 0 없음)
    Vector2 appliedPull;            // 지난 스텝에 더한 끌림 속도 (다음 스텝에서 빼서 누적 방지)
    readonly Collider2D[] wallHits = new Collider2D[8];
    ContactFilter2D wallFilter;

    bool doubleJumpAvailable = true;
    bool jumpCutAvailable;
    int dashDir = 1;
    bool trackingJump;
    float jumpStartY, jumpPeakY;
    Vector2 spawnPoint;
    readonly List<Collider2D> ignoredPlatforms = new List<Collider2D>();

    float GravityScale => gravity / Mathf.Max(0.01f, Mathf.Abs(Physics2D.gravity.y));

    // ════════════════════════════════════════════════

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<BoxCollider2D>();
        health = GetComponent<Health>();
        rb.freezeRotation = true;
        spawnPoint = transform.position;

        RecalculateJumpPhysics();
        ApplyCrouchShape(false);
    }

    void OnEnable()
    {
        Health.Damaged += OnDamaged;
        Health.Died += OnDied;
    }

    void OnDisable()
    {
        Health.Damaged -= OnDamaged;
        Health.Died -= OnDied;
    }

    void OnValidate()
    {
        timeToApex = Mathf.Max(0.05f, timeToApex);
        RecalculateJumpPhysics();   // 플레이 중 인스펙터에서 점프 수치를 바꾸면 즉시 반영
    }

    void RecalculateJumpPhysics()
    {
        gravity = 2f * jumpHeight / (timeToApex * timeToApex);

        // 물리 스텝(0.02초) 단위 계산 때문에 생기는 높이 손실(≈ v·dt/2)을 보정
        // 이산 적분 최고점: (v0 - g·dt/2)² / 2g = h  →  v0 = √(2gh) + g·dt/2
        float dt = Time.fixedDeltaTime;
        jumpVelocity = Mathf.Sqrt(2f * gravity * (jumpHeight + jumpHeightMargin)) + gravity * dt * 0.5f;
        crouchJumpVelocity = Mathf.Sqrt(2f * gravity * (crouchJumpHeight + jumpHeightMargin)) + gravity * dt * 0.5f;
        if (rb != null && !IsDashing) rb.gravityScale = GravityScale;
    }

    // ───────── 입력 ─────────
    void Update()
    {
        if (IsDead)
        {
            moveInput = 0f; downHeld = jumpHeld = false;
        }
        else
        {
            moveInput = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
            downHeld = Input.GetKey(KeyCode.S);
            jumpHeld = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.Space);

            if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.Space))
                jumpBufferTimer = jumpBufferTime;

            if (Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift))
                dashRequested = true;
        }

        if (visual != null) visual.localScale = new Vector3(Facing, 1f, 1f);
    }

    // ───────── 물리 ─────────
    enum WallContact { None, Climbable, Slippery }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        TickTimers(dt);

        // 지난 스텝의 끌림 속도를 빼서 "플레이어 자신의 속도"만 남김
        Vector2 v = rb.GetVelocity() - appliedPull;
        appliedPull = Vector2.zero;

        if (trackingJump)
        {
            jumpPeakY = Mathf.Max(jumpPeakY, rb.position.y);
            if (v.y <= 0f) { LastJumpHeight = jumpPeakY - jumpStartY; trackingJump = false; }
        }

        // 1. 감지
        bool groundedSolid = CheckSolidGround(v.y);
        Collider2D oneWay = v.y > 0.1f ? null : ProbeOneWay();
        IsGrounded = groundedSolid || oneWay != null;
        WallContact wallLeft = CheckWall(-1);
        WallContact wallRight = CheckWall(1);
        int climbWall = wallRight == WallContact.Climbable ? 1 : (wallLeft == WallContact.Climbable ? -1 : 0);

        if (IsGrounded)
        {
            coyoteTimer = coyoteTime;
            timeSinceGrounded = 0f;
            doubleJumpAvailable = true;          // 바닥 착지 → 더블점프 재충전
        }
        else timeSinceGrounded += dt;

        // 2. 사망 / 피격 경직
        if (IsDead || hitStunTimer > 0f)
        {
            if (IsDead) v.x = 0f;
            v.y = Mathf.Max(v.y, -maxFallSpeed);
            rb.SetVelocity(v);
            dashRequested = false;
            ReleaseWall();
            return;
        }

        // 3. 대쉬 진행 중 (붙을 수 없는 벽 포함, 어떤 벽이든 막히면 종료)
        if (IsDashing)
        {
            dashTimer -= dt;
            bool blocked = (dashDir > 0 ? wallRight : wallLeft) != WallContact.None;
            if (dashTimer > 0f && !blocked)
            {
                rb.SetVelocity(new Vector2(dashDir * dashSpeed, 0f));
                dashRequested = false;
                return;
            }
            EndDash();
            v.y = 0f;
        }

        // 4. 웅크리기
        UpdateCrouch(groundedSolid);

        // 5. 통과 발판 위에서 S → 아래로
        if (oneWay != null && !groundedSolid && downHeld)
        {
            StartDropThrough(oneWay);
            IsGrounded = false;
            coyoteTimer = 0f;
            v.y = Mathf.Min(v.y, -2f);
        }

        // 6. 벽 매달리기 상태 갱신
        bool wallAbility = GameManager.HasWallJump && !IsGrounded;
        if (wallClingDir != 0)
        {
            WallContact w = wallClingDir > 0 ? wallRight : wallLeft;
            if (!wallAbility || w != WallContact.Climbable || (releaseWallWithDown && downHeld))
                ReleaseWall();
        }
        if (wallClingDir == 0 && wallAbility && climbWall != 0 && v.y <= 0f)
        {
            wallClingDir = climbWall;            // 떨어지는 중 벽에 닿으면 매달림
            wallReturnTimer = 0f;
        }
        IsWallSliding = wallClingDir != 0;

        // 7. 수평 이동
        if (IsWallSliding)
        {
            // 매달린 동안 방향키는 무시: 벽에 밀착한 채 미끄러지기만 함
            v.x = wallClingDir * 1f;
            v.y = Mathf.Max(v.y, -wallSlideSpeed);
            Facing = wallClingDir;               // 벽을 바라봄
            doubleJumpAvailable = true;          // 벽에 붙음 → 더블점프 재충전
        }
        else if (wallJumpLockTimer <= 0f)
        {
            bool returning = wallReturnTimer > 0f;
            if (returning && moveInput != 0f && Mathf.Sign(moveInput) == -wallReturnDir) { wallReturnTimer = 0f; returning = false; }
            if (returning && climbWall == wallReturnDir) { wallReturnTimer = 0f; returning = false; }

            if (returning)
            {
                v.x = wallReturnDir * moveSpeed; // 벽 쪽 점프 후 자동으로 다시 벽으로
            }
            else
            {
                v.x = moveInput * (IsCrouching ? crouchMoveSpeed : moveSpeed);
                if (moveInput != 0f) Facing = moveInput > 0f ? 1 : -1;
            }
        }

        // 8. 대쉬 시작 (매달린 상태면 벽 반대쪽으로)
        if (dashRequested && GameManager.HasDash && !IsCrouching && dashCooldownTimer <= 0f)
        {
            dashRequested = false;
            int dir = IsWallSliding ? -wallClingDir
                    : (moveInput != 0f ? (moveInput > 0f ? 1 : -1) : Facing);
            ReleaseWall();
            StartDash(dir);
            rb.SetVelocity(new Vector2(dashDir * dashSpeed, 0f));
            return;
        }
        dashRequested = false;

        // 9. 점프 (지면 → 벽 → 더블 순서)
        float jv = IsCrouching ? crouchJumpVelocity : jumpVelocity;
        int jumpWall = IsWallSliding ? wallClingDir : (wallAbility ? climbWall : 0);
        if (jumpBufferTimer > 0f)
        {
            if (coyoteTimer > 0f)
            {
                DoJump(ref v, jv);
                coyoteTimer = 0f;
            }
            else if (jumpWall != 0)
            {
                DoJump(ref v, jv);
                bool away = moveInput != 0f && Mathf.Sign(moveInput) == -jumpWall;
                if (away)
                {
                    // 벽 반대 방향키 + 점프 → 그 방향을 바라보며 뛰어나감
                    v.x = -jumpWall * wallJumpAwaySpeed;
                    Facing = -jumpWall;
                    wallJumpLockTimer = wallJumpAwayLockTime;
                    wallReturnTimer = 0f;
                }
                else
                {
                    // 벽 쪽 / 무입력 + 점프 → 살짝 떨어졌다가 다시 벽에 붙으며 상승
                    v.x = -jumpWall * wallJumpHorizontalSpeed;
                    Facing = jumpWall;
                    wallJumpLockTimer = wallJumpInputLockTime;
                    wallReturnTimer = wallJumpInputLockTime + wallJumpReturnTime;
                    wallReturnDir = jumpWall;
                }
                ReleaseWall();
            }
            else if (GameManager.HasDoubleJump && doubleJumpAvailable)
            {
                DoJump(ref v, jv);
                doubleJumpAvailable = false;
            }
        }

        if (variableJumpHeight && jumpCutAvailable && !jumpHeld && v.y > 0f)
        {
            v.y *= jumpCutMultiplier;
            jumpCutAvailable = false;
        }
        if (v.y <= 0f) jumpCutAvailable = false;

        // 10. 상승기류
        UpdraftTilemap zone = UpdraftTilemap.FindLift(rb.position, width * 0.4f);
        IsInUpdraft = zone != null;
        if (zone != null)
        {
            v.y = Mathf.MoveTowards(v.y, zone.liftSpeed, zone.liftAcceleration * dt);
            jumpCutAvailable = false;
        }

        v.y = Mathf.Max(v.y, -maxFallSpeed);

        // 11. 끌림 기믹: 자신의 속도에 끌림 속도를 더함 (바닥에서는 위로 띄우지 않음)
        Vector2 pull = PullTilemap.GetPull(rb.position + Vector2.up * (CurrentHeight * 0.5f));
        if (IsGrounded && pull.y > 0f) pull.y = 0f;
        IsBeingPulled = pull != Vector2.zero;
        appliedPull = pull;

        rb.SetVelocity(v + pull);
    }

    void ReleaseWall()
    {
        wallClingDir = 0;
        IsWallSliding = false;
    }

    void TickTimers(float dt)
    {
        jumpBufferTimer -= dt;
        coyoteTimer -= dt;
        dashCooldownTimer -= dt;
        wallJumpLockTimer -= dt;
        wallReturnTimer -= dt;
        hitStunTimer -= dt;
        if (dropTimer > 0f)
        {
            dropTimer -= dt;
            if (dropTimer <= 0f) RestorePlatforms();
        }
    }

    void DoJump(ref Vector2 v, float velocity)
    {
        v.y = velocity;
        jumpBufferTimer = 0f;
        jumpCutAvailable = true;
        trackingJump = true;
        jumpStartY = jumpPeakY = rb.position.y;
    }

    // ───────── 감지 ─────────
    bool CheckSolidGround(float vy)
    {
        if (vy > 0.1f) return false;
        Vector2 center = rb.position + Vector2.down * 0.05f;
        return Physics2D.OverlapBox(center, new Vector2(width * 0.9f, 0.1f), 0f, groundMask) != null;
    }

    /// <summary>발 아래 통과 발판. 발판 안쪽(통과 중)에서 쏜 레이는 거리 0이므로 제외됨.</summary>
    Collider2D ProbeOneWay()
    {
        if (dropTimer > 0f) return null;
        float hw = width * 0.45f;
        for (int i = -1; i <= 1; i++)
        {
            Vector2 origin = rb.position + new Vector2(i * hw, 0.05f);
            RaycastHit2D hit = Physics2D.Raycast(origin, Vector2.down, 0.15f, oneWayMask);
            if (hit.collider != null && hit.distance > 0.001f) return hit.collider;
        }
        return null;
    }

    /// <summary>dir 방향 벽 감지. WallSurface.climbable = false인 벽만 있으면 Slippery</summary>
    WallContact CheckWall(int dir)
    {
        float h = CurrentHeight;
        Vector2 center = rb.position + new Vector2(dir * (width * 0.5f + 0.05f), h * 0.5f);
        wallFilter.useLayerMask = true;
        wallFilter.layerMask = groundMask;
        wallFilter.useTriggers = false;
        int n = Physics2D.OverlapBox(center, new Vector2(0.1f, h * 0.7f), 0f, wallFilter, wallHits);
        if (n == 0) return WallContact.None;
        for (int i = 0; i < n; i++)
        {
            WallSurface ws = wallHits[i].GetComponent<WallSurface>();
            if (ws == null || ws.climbable) return WallContact.Climbable;
        }
        return WallContact.Slippery;
    }

    /// <summary>웅크린 상태에서 일어설 공간(머리 위 standHeight까지)이 있는지</summary>
    public bool HasHeadroom()
    {
        // 웅크린 콜라이더 위쪽 ~ 선 콜라이더 위쪽 구간이 비어있는지
        float bottom = crouchHeight - colliderSkin + 0.02f;
        float top = standHeight - colliderSkin - 0.02f;
        Vector2 center = rb.position + new Vector2(0f, (bottom + top) * 0.5f);
        Vector2 size = new Vector2(width * 0.9f, Mathf.Max(0.02f, top - bottom));
        return Physics2D.OverlapBox(center, size, 0f, groundMask) == null;
    }

    // ───────── 웅크리기 ─────────
    void UpdateCrouch(bool groundedSolid)
    {
        if (!IsCrouching)
        {
            if (downHeld && groundedSolid) ApplyCrouchShape(true);   // 발판 위에서는 웅크리지 않음
            return;
        }

        if (IsGrounded)
        {
            if (!downHeld && HasHeadroom()) ApplyCrouchShape(false);
        }
        else if (timeSinceGrounded >= crouchForceStandAirTime && HasHeadroom())
        {
            ApplyCrouchShape(false);   // 땅에서 떨어진 지 1.5초 + 2m 공간 확보 → 강제 해제
        }
    }

    void ApplyCrouchShape(bool crouch)
    {
        IsCrouching = crouch;
        float h = CurrentHeight;
        if (col != null)
        {
            float ch = Mathf.Max(0.1f, h - colliderSkin);     // 발바닥은 그대로, 머리 쪽만 살짝 작게
            col.size = new Vector2(width, ch);
            col.offset = new Vector2(0f, ch * 0.5f);
        }
        if (body != null)
        {
            body.localScale = new Vector3(width, h, 1f);
            body.localPosition = new Vector3(0f, h * 0.5f, 0f);
        }
        if (eye != null) eye.localPosition = new Vector3(width * 0.28f, h - 0.35f, 0f);
    }

    // ───────── 대쉬 ─────────
    void StartDash(int dir)
    {
        dashDir = dir;
        Facing = dir;
        IsDashing = true;
        IsWallSliding = false;
        dashTimer = dashDuration;
        dashCooldownTimer = dashCooldown;
        rb.gravityScale = 0f;
        jumpCutAvailable = false;
    }

    void EndDash()
    {
        IsDashing = false;
        if (rb != null) rb.gravityScale = GravityScale;
    }

    // ───────── 통과 발판 ─────────
    void StartDropThrough(Collider2D platform)
    {
        Physics2D.IgnoreCollision(col, platform, true);
        if (!ignoredPlatforms.Contains(platform)) ignoredPlatforms.Add(platform);
        dropTimer = dropThroughTime;
    }

    void RestorePlatforms()
    {
        foreach (Collider2D c in ignoredPlatforms)
            if (c != null) Physics2D.IgnoreCollision(col, c, false);
        ignoredPlatforms.Clear();
        dropTimer = 0f;
    }

    // ───────── 피격 / 사망 / 리스폰 ─────────
    void OnDamaged(int amount, Vector2 source)
    {
        if (IsDashing) EndDash();
        float dx = rb.position.x - source.x;
        float dir = Mathf.Abs(dx) < 0.05f ? -Facing : Mathf.Sign(dx);
        rb.SetVelocity(new Vector2(dir * knockbackVelocity.x, knockbackVelocity.y));
        hitStunTimer = hitStunTime;
        wallJumpLockTimer = 0f;
        wallReturnTimer = 0f;
        appliedPull = Vector2.zero;
        ReleaseWall();
        jumpCutAvailable = false;
    }

    void OnDied()
    {
        IsDead = true;
        if (IsDashing) EndDash();
        if (GameManager.Instance != null) GameManager.Instance.OnPlayerDied(this);
        else Invoke(nameof(RespawnAtStart), 1f);
    }

    void RespawnAtStart() => Respawn(spawnPoint);

    /// <summary>체력 변화 없이 위치만 옮김 (저장 지점 순간이동 등)</summary>
    public void TeleportTo(Vector2 position)
    {
        RestorePlatforms();
        if (IsDashing) EndDash();
        ReleaseWall();
        rb.position = position;
        transform.position = position;
        rb.SetVelocity(Vector2.zero);
        appliedPull = Vector2.zero;
        wallJumpLockTimer = 0f;
        wallReturnTimer = 0f;
        hitStunTimer = 0f;
        if (IsCrouching && HasHeadroom()) ApplyCrouchShape(false);
    }

    public void Respawn(Vector2 position)
    {
        RestorePlatforms();
        EndDash();
        rb.position = position;
        transform.position = position;
        rb.SetVelocity(Vector2.zero);
        ApplyCrouchShape(false);
        IsDead = false;
        hitStunTimer = 0f;
        dashCooldownTimer = 0f;
        wallJumpLockTimer = 0f;
        doubleJumpAvailable = true;
        wallReturnTimer = 0f;
        appliedPull = Vector2.zero;
        ReleaseWall();
        Health.ResetHealth();
    }

    public string StateText
    {
        get
        {
            if (IsDead) return "사망";
            string s = IsDashing ? "대쉬" : IsWallSliding ? "벽 매달림" : IsGrounded ? "지면" : "공중";
            if (IsCrouching) s += " / 웅크림";
            if (IsInUpdraft) s += " / 상승기류";
            if (IsBeingPulled) s += " / 끌림";
            if (!IsGrounded && !IsDashing) s += doubleJumpAvailable && GameManager.HasDoubleJump ? " (더블점프 가능)" : "";
            return s;
        }
    }

    // ───────── 기즈모: 바닥/벽/머리 감지 영역 ─────────
    void OnDrawGizmosSelected()
    {
        Vector2 p = Application.isPlaying && rb != null ? rb.position : (Vector2)transform.position;
        float h = CurrentHeight;
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(p + Vector2.down * 0.05f, new Vector2(width * 0.9f, 0.1f));
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(p + new Vector2(width * 0.5f + 0.05f, h * 0.5f), new Vector2(0.1f, h * 0.7f));
        Gizmos.DrawWireCube(p + new Vector2(-(width * 0.5f + 0.05f), h * 0.5f), new Vector2(0.1f, h * 0.7f));
        if (IsCrouching)
        {
            float bottom = crouchHeight - colliderSkin, top = standHeight - colliderSkin;
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireCube(p + new Vector2(0f, (bottom + top) * 0.5f), new Vector2(width * 0.9f, top - bottom));
        }
    }
}

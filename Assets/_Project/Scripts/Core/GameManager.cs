using System.Collections;
using UnityEngine;

/// <summary>
/// 중앙 관리자.
/// - 이동 능력(대쉬 / 벽타기 / 더블점프) 획득 여부를 bool로 관리 → 플레이 중 인스펙터에서 바로 토글 가능
/// - 저장 지점 / 사망 후 리스폰 처리
/// </summary>
[DefaultExecutionOrder(-100)]
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("이동 능력 획득 여부 (플레이 중 실시간 변경 가능)")]
    public bool hasDash = true;
    public bool hasWallJump = true;      // 벽 슬라이드 + 벽 점프
    public bool hasDoubleJump = true;

    // 다른 스크립트는 이 값만 읽으면 됨. GameManager가 씬에 없으면 전부 허용.
    public static bool HasDash       => Instance == null || Instance.hasDash;
    public static bool HasWallJump   => Instance == null || Instance.hasWallJump;
    public static bool HasDoubleJump => Instance == null || Instance.hasDoubleJump;

    [Header("플레이어 / 리스폰")]
    public PlayerController player;
    public float respawnDelay = 1f;
    [SerializeField] Vector2 savePoint;

    public Vector2 SavePoint => savePoint;
    public float LastSaveTime { get; private set; } = -100f;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // 플레이어와 몬스터는 서로 밀치지 않고 통과 (피해는 공격 판정으로만)
        int p = LayerMask.NameToLayer("Player");
        int e = LayerMask.NameToLayer("Enemy");
        if (p >= 0 && e >= 0) Physics2D.IgnoreLayerCollision(p, e, true);

        if (player == null)
        {
            GameObject go = GameObject.FindWithTag("Player");
            if (go != null) player = go.GetComponent<PlayerController>();
        }
    }

    void Start()
    {
        if (player != null) savePoint = player.transform.position;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void SetSavePoint(Vector2 position, bool heal)
    {
        savePoint = position;
        LastSaveTime = Time.time;
        if (heal && player != null) player.Health.Heal(player.Health.maxHealth);
    }

    public void OnPlayerDied(PlayerController p)
    {
        StartCoroutine(RespawnRoutine(p));
    }

    IEnumerator RespawnRoutine(PlayerController p)
    {
        yield return new WaitForSeconds(respawnDelay);
        p.Respawn(savePoint);
    }
}

using UnityEngine;

/// <summary>
/// 레벨 테스트용 화면 표시: 체력, 능력 상태, 쿨타임, 몬스터 체력.
/// </summary>
public class DebugHUD : MonoBehaviour
{
    public bool show = true;
    public bool showEnemyHealth = true;

    PlayerCombat combat;

    void OnGUI()
    {
        GameManager gm = GameManager.Instance;
        if (!show || gm == null || gm.player == null) return;

        PlayerController p = gm.player;
        Health h = p.Health;
        if (combat == null) combat = p.GetComponent<PlayerCombat>();

        GUILayout.BeginArea(new Rect(10, 10, 360, 220), GUI.skin.box);

        GUILayout.Label($"HP  {h.Current} / {h.maxHealth}");
        float ratio = h.maxHealth > 0 ? (float)h.Current / h.maxHealth : 0f;
        Rect bar = GUILayoutUtility.GetRect(340f, 12f);
        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(bar, Texture2D.whiteTexture);
        GUI.color = Color.Lerp(Color.red, Color.green, ratio);
        GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * ratio, bar.height), Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUILayout.Label($"능력   대쉬[{OX(gm.hasDash)}]   벽타기[{OX(gm.hasWallJump)}]   더블점프[{OX(gm.hasDoubleJump)}]");
        float atk = combat != null ? combat.CooldownRemaining : 0f;
        GUILayout.Label($"대쉬 쿨타임 {p.DashCooldownRemaining:0.00}s    공격 쿨타임 {atk:0.00}s");
        GUILayout.Label($"상태: {p.StateText}");
        GUILayout.Label($"마지막 점프 높이: {p.LastJumpHeight:0.00}m (설정 {p.jumpHeight}m)");
        if (Time.time - gm.LastSaveTime < 1.5f) GUILayout.Label("[ 저장됨 - 체력 회복 ]");
        GUILayout.Label("A/D 이동 · W/Space 점프 · S 웅크리기/발판 내려가기\nShift 대쉬 · 마우스 좌클릭 공격");

        GUILayout.EndArea();

        if (showEnemyHealth && Camera.main != null)
        {
            foreach (EnemyAI e in EnemyAI.All)
            {
                Health eh = e.Health;
                Vector3 sp = Camera.main.WorldToScreenPoint(e.transform.position + Vector3.up * 1.8f);
                if (sp.z < 0f) continue;
                GUI.Label(new Rect(sp.x - 30f, Screen.height - sp.y - 10f, 80f, 20f), $"{eh.Current}/{eh.maxHealth}");
            }
        }
    }

    static string OX(bool b) => b ? "O" : "X";
}

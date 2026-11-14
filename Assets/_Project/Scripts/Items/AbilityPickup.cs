using UnityEngine;

/// <summary>
/// 능력 조각. 플레이어가 닿으면 GameManager의 해당 능력을 켠다 (벽타기 1F / 더블점프 4I / 대쉬 41I).
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class AbilityPickup : MonoBehaviour
{
    public enum Ability { WallJump, DoubleJump, Dash }
    public Ability ability;

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.GetComponentInParent<PlayerController>() == null) return;
        GameManager gm = GameManager.Instance;
        if (gm != null)
        {
            switch (ability)
            {
                case Ability.WallJump:   gm.hasWallJump = true; break;
                case Ability.DoubleJump: gm.hasDoubleJump = true; break;
                case Ability.Dash:       gm.hasDash = true; break;
            }
        }
        Debug.Log("[능력 획득] " + ability);
        gameObject.SetActive(false);
    }
}

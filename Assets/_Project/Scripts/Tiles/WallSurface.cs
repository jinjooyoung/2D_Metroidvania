using UnityEngine;

/// <summary>
/// 벽 속성. 벽(충돌) 타일맵에 붙이며, climbable = false면 벽타기(매달리기·벽 점프)가 불가능한 벽.
/// 컴포넌트가 없는 벽은 붙을 수 있는 벽으로 취급.
/// 예) 낙하 지점의 벽: 떨어지는 도중 벽에 붙어 멈추지 않도록 할 때 사용.
/// </summary>
public class WallSurface : MonoBehaviour
{
    [Tooltip("false = 붙을 수 없는 벽 (벽 슬라이드·벽 점프 불가, 바닥·천장으로는 정상 동작)")]
    public bool climbable = true;
}

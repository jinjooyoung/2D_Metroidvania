using UnityEngine;

/// <summary>
/// Unity 2022(velocity) / Unity 6(linearVelocity) 양쪽에서 컴파일되도록 감싼 확장 메서드.
/// </summary>
public static class Rigidbody2DCompat
{
    public static Vector2 GetVelocity(this Rigidbody2D rb)
    {
#if UNITY_6000_0_OR_NEWER
        return rb.linearVelocity;
#else
        return rb.velocity;
#endif
    }

    public static void SetVelocity(this Rigidbody2D rb, Vector2 v)
    {
#if UNITY_6000_0_OR_NEWER
        rb.linearVelocity = v;
#else
        rb.velocity = v;
#endif
    }
}

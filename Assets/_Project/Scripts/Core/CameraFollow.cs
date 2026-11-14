using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform target;
    public Vector2 offset = new Vector2(0f, 1.5f);   // 발 기준 피벗이므로 살짝 위를 바라봄
    public float smoothTime = 0.12f;

    Vector3 velocity;

    /// <summary>순간이동 등에서 카메라를 즉시 대상 위치로 옮김</summary>
    public void Snap()
    {
        if (target == null) return;
        transform.position = new Vector3(target.position.x + offset.x, target.position.y + offset.y, transform.position.z);
        velocity = Vector3.zero;
    }

    void LateUpdate()
    {
        if (target == null) return;
        Vector3 goal = new Vector3(target.position.x + offset.x, target.position.y + offset.y, transform.position.z);
        transform.position = Vector3.SmoothDamp(transform.position, goal, ref velocity, smoothTime);
    }
}

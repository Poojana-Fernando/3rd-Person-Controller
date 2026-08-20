using UnityEngine;

public class BulletTracer : MonoBehaviour
{
    [SerializeField] private float speed = 120f;
    [SerializeField] private float lifetime = 2f;

    private Vector3 _destination;
    private float _elapsed;

    public void Launch(Vector3 start, Vector3 end, float travelSpeed)
    {
        transform.position = start;
        _destination = end;
        speed = Mathf.Max(1f, travelSpeed);
        _elapsed = 0f;

        Vector3 direction = end - start;
        if (direction.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(direction.normalized);
    }

    private void Update()
    {
        _elapsed += Time.deltaTime;
        if (_elapsed >= lifetime)
        {
            Destroy(gameObject);
            return;
        }

        transform.position = Vector3.MoveTowards(transform.position, _destination, speed * Time.deltaTime);
        if ((transform.position - _destination).sqrMagnitude < 0.01f)
            Destroy(gameObject);
    }
}

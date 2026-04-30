using UnityEngine;

public class Bullet : MonoBehaviour
{
    void Awake() => Destroy(gameObject, 4f);

    void OnCollisionEnter(Collision col)
    {
        var ast = col.gameObject.GetComponent<Asteroid>();
        if (ast != null)
        {
            ast.Hit();
            Destroy(gameObject);
        }
    }
}

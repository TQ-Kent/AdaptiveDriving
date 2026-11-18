using F1Racing.Vehicle;
using UnityEngine;

namespace F1Racing.Track
{
    /// <summary>One box in a <see cref="PushableBoxField"/> stack; reports car hits to the field.</summary>
    [RequireComponent(typeof(Rigidbody))]
    public class PushableBox : MonoBehaviour
    {
        public PushableBoxField field;
        public int stackIndex;

        private void OnCollisionEnter(Collision collision)
        {
            if (field == null || collision.rigidbody == null) return;
            var car = collision.rigidbody.GetComponent<F1CarController>();
            if (car == null) return;
            Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
            field.OnStackHit(stackIndex, point, collision.rigidbody.linearVelocity);
        }
    }
}

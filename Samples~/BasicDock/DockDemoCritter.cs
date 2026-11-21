using UnityEngine;
using UnityEngine.EventSystems;

namespace DesktopDock.Samples
{
    /// <summary>Wanders along the ground; hops, changes colour and pays out coins when clicked.</summary>
    public sealed class DockDemoCritter : MonoBehaviour, IPointerClickHandler
    {
        private DockDemo _demo;
        private float _t;           // -1..1 along the ground
        private float _dir = 1f;
        private float _speed;
        private float _hop;
        private float _hopVelocity;
        private SpriteRenderer _sr;
        private Transform _shadow;

        public void Init(DockDemo demo, float t)
        {
            _demo = demo;
            _t = t;
            _dir = Random.value < 0.5f ? -1f : 1f;
            _speed = Random.Range(0.4f, 1.2f);
            _sr = GetComponent<SpriteRenderer>();

            var shadow = new GameObject(name + " Shadow").AddComponent<SpriteRenderer>();
            shadow.sprite = DockDemo.Shadow;
            shadow.sortingOrder = -1;
            _shadow = shadow.transform;
        }

        /// <summary>A small hop, for example when the critter arrives.</summary>
        public void Hop(float velocity = 7f) => _hopVelocity = velocity;

        private void OnDestroy()
        {
            if (_shadow != null) Destroy(_shadow.gameObject);
        }

        private void Update()
        {
            float halfLength = Mathf.Max(0.5f, _demo.HalfLength - 0.5f);
            _t += _dir * _speed * Time.deltaTime / halfLength;
            if (_t > 1f) { _t = 1f; _dir = -1f; }
            if (_t < -1f) { _t = -1f; _dir = 1f; }
            if (Random.value < 0.002f) _dir = -_dir;

            _hopVelocity -= 20f * Time.deltaTime;
            _hop = Mathf.Max(0f, _hop + _hopVelocity * Time.deltaTime);
            if (_hop <= 0f) _hopVelocity = 0f;

            _demo.PlaceOnGround(transform, _t * halfLength, transform.localScale.y * 0.5f + _hop);

            // The shadow stays on the ground and shrinks while the critter is in the air.
            _demo.PlaceOnGround(_shadow, _t * halfLength, 0.08f);
            float s = 1f / (1f + _hop);
            _shadow.localScale = new Vector3(1.8f * s, 1f * s, 1f);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            Hop();
            _sr.color = Color.HSVToRGB(Random.value, 0.55f, 0.95f);
            _demo.Boop();
        }
    }
}

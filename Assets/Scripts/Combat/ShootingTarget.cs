using UnityEngine;

namespace BodycamHorror.Combat
{
    /// <summary>
    /// Pop-up practice target. Wobbles and flashes when hit, falls over away from the shot when its
    /// health runs out, then stands back up. Put it on the pivot at the base; colliders live on children.
    /// </summary>
    public class ShootingTarget : MonoBehaviour, IDamageable
    {
        public float maxHealth = 100f;
        public float resetDelay = 3f;
        public float fallAngle = 85f;
        public float fallSpeed = 9f;
        public float wobblePerHit = 7f;
        public Color hitFlashColor = new Color(1f, 0.15f, 0.1f);
        public float hitFlashTime = 0.08f;

        public bool IsDown { get; private set; }

        float health, downTimer, flashTimer, fall;
        bool flashing;
        Quaternion baseRotation;
        Vector3 fallAxis = Vector3.right, wobbleAxis = Vector3.right;
        float wobble, wobbleVelocity;
        Renderer[] renderers;
        Color[] baseColors;
        MaterialPropertyBlock block;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        void Awake()
        {
            health = maxHealth;
            baseRotation = transform.localRotation;
            renderers = GetComponentsInChildren<Renderer>();
            baseColors = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                var m = renderers[i].sharedMaterial;
                baseColors[i] = m && m.HasProperty(BaseColorId) ? m.GetColor(BaseColorId) : Color.white;
            }
            block = new MaterialPropertyBlock();
        }

        public void TakeDamage(in DamageInfo info)
        {
            flashTimer = hitFlashTime;
            flashing = true;
            Vector3 flatDir = Vector3.ProjectOnPlane(info.direction, Vector3.up);
            if (flatDir.sqrMagnitude < 0.0001f) flatDir = -transform.forward;
            flatDir.Normalize();
            // Rotating about this axis tips the top along the bullet's travel.
            Vector3 axisWorld = Vector3.Cross(Vector3.up, flatDir);
            Vector3 axisLocal = transform.parent ? transform.parent.InverseTransformDirection(axisWorld) : axisWorld;

            if (IsDown) return;
            wobbleAxis = axisLocal;
            wobbleVelocity += wobblePerHit * 25f;
            health -= info.amount;
            if (health <= 0f)
            {
                IsDown = true;
                fallAxis = axisLocal;
                downTimer = resetDelay;
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            if (IsDown)
            {
                fall = Mathf.MoveTowards(fall, 1f, fallSpeed * dt * (0.3f + fall));   // accelerates as it tips
                downTimer -= dt;
                if (downTimer <= 0f) { IsDown = false; health = maxHealth; }
            }
            else fall = Mathf.MoveTowards(fall, 0f, dt * 1.5f);

            // damped wobble
            wobbleVelocity += (-wobble * 260f - wobbleVelocity * 12f) * dt;
            wobble += wobbleVelocity * dt;

            float eased = fall * fall * (3f - 2f * fall);
            transform.localRotation = Quaternion.AngleAxis(eased * fallAngle, fallAxis)
                                      * Quaternion.AngleAxis(wobble, wobbleAxis) * baseRotation;

            if (flashing)
            {
                flashTimer -= dt;
                float f = Mathf.Clamp01(flashTimer / hitFlashTime);
                flashing = flashTimer > 0f;   // one last pass writes the base colour back
                for (int i = 0; i < renderers.Length; i++)
                {
                    renderers[i].GetPropertyBlock(block);
                    block.SetColor(BaseColorId, Color.Lerp(baseColors[i], hitFlashColor, f));
                    renderers[i].SetPropertyBlock(block);
                }
            }
        }
    }
}

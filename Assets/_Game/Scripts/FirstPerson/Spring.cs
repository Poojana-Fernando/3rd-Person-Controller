using UnityEngine;

namespace Game.FirstPerson
{
    /// <summary>Critically damped spring for smooth, frame-rate independent motion.</summary>
    public struct SpringFloat
    {
        public float Current;
        public float Velocity;
        public float Stiffness;
        public float Damping;

        public SpringFloat(float stiffness, float damping)
        {
            Current = 0f;
            Velocity = 0f;
            Stiffness = stiffness;
            Damping = damping;
        }

        public float Update(float target, float deltaTime)
        {
            if (deltaTime <= 0f)
                return Current;

            float omega = Stiffness;
            float zeta = Damping;
            float f = omega * omega * (target - Current) - 2f * zeta * omega * Velocity;
            Velocity += f * deltaTime;
            Current += Velocity * deltaTime;
            return Current;
        }

        public void Impulse(float amount) => Velocity += amount;

        public void Reset(float value = 0f)
        {
            Current = value;
            Velocity = 0f;
        }
    }

    public struct SpringVector3
    {
        public Vector3 Current;
        public Vector3 Velocity;
        public float Stiffness;
        public float Damping;

        public SpringVector3(float stiffness, float damping)
        {
            Current = Vector3.zero;
            Velocity = Vector3.zero;
            Stiffness = stiffness;
            Damping = damping;
        }

        public Vector3 Update(Vector3 target, float deltaTime)
        {
            if (deltaTime <= 0f)
                return Current;

            Vector3 displacement = target - Current;
            Vector3 force = displacement * (Stiffness * Stiffness) - Velocity * (2f * Damping * Stiffness);
            Velocity += force * deltaTime;
            Current += Velocity * deltaTime;
            return Current;
        }

        public void Impulse(Vector3 amount) => Velocity += amount;

        public void Reset(Vector3 value = default)
        {
            Current = value;
            Velocity = Vector3.zero;
        }
    }
}

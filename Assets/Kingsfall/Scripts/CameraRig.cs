using UnityEngine;

namespace Kingsfall
{
    /// <summary>Orbit camera around the board with cinematic overrides and trauma-based shake.</summary>
    public class CameraRig : MonoBehaviour
    {
        public static CameraRig I { get; private set; }

        public Camera Cam;
        public Vector3 Target = new Vector3(0, 0.2f, 0);
        public float Yaw = 0f, Pitch = 52f, Distance = 11.5f;
        public float MinDistance = 6f, MaxDistance = 18f;
        public bool AutoOrbit;
        public bool UserControl = true;

        float yawTarget, pitchTarget, distTarget;
        float trauma;
        float baseFov = 42f;

        // Cinematic override
        float cineWeight, cineWeightTarget;
        Vector3 cinePos, cineLook;
        Vector3 cinePosTarget, cineLookTarget;
        float cineFov = 36f, cineFovTarget = 36f;
        float cineSharp = 4f;
        public float FocusDistance { get; private set; } = 11f;

        void Awake()
        {
            I = this;
            if (!Cam) Cam = GetComponent<Camera>();
            yawTarget = Yaw; pitchTarget = Pitch; distTarget = Distance;
            baseFov = Cam.fieldOfView;
        }

        public void SetSide(int colour, bool instant = false)
        {
            yawTarget = colour == 0 ? 0f : 180f;
            // take the short way round
            while (yawTarget - Yaw > 180f) yawTarget -= 360f;
            while (yawTarget - Yaw < -180f) yawTarget += 360f;
            pitchTarget = 52f;
            distTarget = 11.5f;
            if (instant) { Yaw = yawTarget; Pitch = pitchTarget; Distance = distTarget; }
        }

        public void SetView(float yaw, float pitch, float distance, bool instant = false)
        {
            yawTarget = yaw; pitchTarget = pitch; distTarget = distance;
            if (instant) { Yaw = yaw; Pitch = pitch; Distance = distance; }
        }

        public void AddTrauma(float amount) => trauma = Mathf.Clamp01(trauma + amount);

        /// <summary>Blend toward a cinematic shot. sharpness = how quickly it tracks (per second).</summary>
        public void Cinematic(Vector3 position, Vector3 lookAt, float fov = 34f, float sharpness = 4f, bool snap = false)
        {
            cinePosTarget = position;
            cineLookTarget = lookAt;
            cineFovTarget = fov;
            cineSharp = sharpness;
            if (cineWeightTarget < 0.5f || snap)
            {
                if (cineWeight < 0.01f || snap) { cinePos = position; cineLook = lookAt; cineFov = fov; }
            }
            cineWeightTarget = 1f;
        }

        public void EndCinematic() => cineWeightTarget = 0f;
        public bool InCinematic => cineWeightTarget > 0.5f;

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;

            if (UserControl && cineWeightTarget < 0.5f)
            {
                if (Input.GetMouseButton(1) || Input.GetMouseButton(2))
                {
                    yawTarget += Input.GetAxisRaw("Mouse X") * 3.2f;
                    pitchTarget = Mathf.Clamp(pitchTarget - Input.GetAxisRaw("Mouse Y") * 2.4f, 18f, 82f);
                }
                float scroll = Input.mouseScrollDelta.y;
                if (Mathf.Abs(scroll) > 0.01f) distTarget = Mathf.Clamp(distTarget - scroll * 0.9f, MinDistance, MaxDistance);
                if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) yawTarget += 70f * dt;
                if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) yawTarget -= 70f * dt;
                if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) pitchTarget = Mathf.Clamp(pitchTarget + 45f * dt, 18f, 82f);
                if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) pitchTarget = Mathf.Clamp(pitchTarget - 45f * dt, 18f, 82f);
            }
            if (AutoOrbit) yawTarget += 6f * dt;

            float k = 1f - Mathf.Exp(-dt * 6f);
            Yaw = Mathf.Lerp(Yaw, yawTarget, k);
            Pitch = Mathf.Lerp(Pitch, pitchTarget, k);
            Distance = Mathf.Lerp(Distance, distTarget, k);

            Quaternion orbitRot = Quaternion.Euler(Pitch, Yaw, 0);
            Vector3 orbitPos = Target + orbitRot * new Vector3(0, 0, -Distance);
            Quaternion orbitLook = Quaternion.LookRotation(Target - orbitPos);

            cineWeight = Mathf.MoveTowards(cineWeight, cineWeightTarget, dt * 1.6f);
            float kc = 1f - Mathf.Exp(-dt * cineSharp);
            cinePos = Vector3.Lerp(cinePos, cinePosTarget, kc);
            cineLook = Vector3.Lerp(cineLook, cineLookTarget, kc);
            cineFov = Mathf.Lerp(cineFov, cineFovTarget, kc);

            float w = Ease.InOutCubic(cineWeight);
            Vector3 pos = Vector3.Lerp(orbitPos, cinePos, w);
            Vector3 cineDir = cineLook - cinePos;
            Quaternion cineRot = cineDir.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(cineDir) : orbitLook;
            Quaternion rot = w > 0f ? Quaternion.Slerp(orbitLook, cineRot, w) : orbitLook;
            float fov = Mathf.Lerp(baseFov, cineFov, w);

            // Keep the camera above the floor and out of the board.
            pos.y = Mathf.Max(pos.y, 0.35f);

            // Shake
            trauma = Mathf.Max(0f, trauma - dt * 1.4f);
            float shake = trauma * trauma;
            if (shake > 0f)
            {
                float t = Time.unscaledTime * 22f;
                pos += rot * new Vector3(Mathf.PerlinNoise(t, 1.3f) - 0.5f, Mathf.PerlinNoise(2.1f, t) - 0.5f, 0) * shake * 0.5f;
                rot *= Quaternion.Euler(0, 0, (Mathf.PerlinNoise(t, 7.7f) - 0.5f) * shake * 6f);
            }

            transform.SetPositionAndRotation(pos, rot);
            Cam.fieldOfView = fov;
            FocusDistance = Vector3.Distance(pos, Vector3.Lerp(Target, cineLook, w));
        }
    }
}

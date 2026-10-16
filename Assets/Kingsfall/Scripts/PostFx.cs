using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Kingsfall
{
    /// <summary>Drives the global volume: cinematic depth of field, impact pulses and slow motion.</summary>
    public class PostFx : MonoBehaviour
    {
        public static PostFx I { get; private set; }
        public Volume Volume;

        DepthOfField dof;
        ChromaticAberration chroma;
        LensDistortion lens;
        Bloom bloom;
        ColorAdjustments colour;
        Vignette vignette;
        float baseBloom, baseVignette, baseExposure;

        float pulse;          // 0..1 impact pulse
        float dofWeight, dofTarget;
        float slowTimer, slowScale = 1f;
        float exposureKick;
        public float BaseTimeScale = 1f;

        void Awake()
        {
            I = this;
            var profile = Volume.profile; // runtime instance
            profile.TryGet(out dof);
            profile.TryGet(out chroma);
            profile.TryGet(out lens);
            profile.TryGet(out bloom);
            profile.TryGet(out colour);
            profile.TryGet(out vignette);
            if (bloom) baseBloom = bloom.intensity.value;
            if (vignette) baseVignette = vignette.intensity.value;
            if (colour) baseExposure = colour.postExposure.value;
        }

        public void Impact(float strength)
        {
            pulse = Mathf.Clamp01(Mathf.Max(pulse, strength));
        }

        public void ExposureKick(float amount) => exposureKick = Mathf.Max(exposureKick, amount);

        public void SlowMo(float scale, float realSeconds)
        {
            slowScale = scale;
            slowTimer = realSeconds;
        }

        public void CinematicFocus(bool on) => dofTarget = on ? 1f : 0f;

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            pulse = Mathf.MoveTowards(pulse, 0f, dt * 2.2f);
            exposureKick = Mathf.MoveTowards(exposureKick, 0f, dt * 3f);
            dofWeight = Mathf.MoveTowards(dofWeight, dofTarget, dt * 2f);

            if (slowTimer > 0f)
            {
                slowTimer -= dt;
                Time.timeScale = Mathf.Lerp(Time.timeScale, slowScale * BaseTimeScale, 1f - Mathf.Exp(-dt * 20f));
            }
            else Time.timeScale = Mathf.Lerp(Time.timeScale, BaseTimeScale, 1f - Mathf.Exp(-dt * 6f));
            Time.fixedDeltaTime = 0.02f * Mathf.Max(Time.timeScale, 0.05f);

            if (chroma) chroma.intensity.value = 0.06f + pulse * 0.9f;
            if (lens) lens.intensity.value = -pulse * 0.28f;
            if (bloom) bloom.intensity.value = baseBloom + pulse * 1.2f + exposureKick * 2f;
            if (colour) colour.postExposure.value = baseExposure + exposureKick;
            if (vignette) vignette.intensity.value = baseVignette + dofWeight * 0.1f;

            if (dof)
            {
                bool active = dofWeight > 0.01f;
                dof.active = active;
                if (active && CameraRig.I)
                {
                    dof.mode.value = DepthOfFieldMode.Bokeh;
                    dof.focusDistance.value = CameraRig.I.FocusDistance;
                    dof.aperture.value = Mathf.Lerp(16f, 3.2f, dofWeight);
                    dof.focalLength.value = Mathf.Lerp(20f, 70f, dofWeight);
                }
            }
        }
    }
}

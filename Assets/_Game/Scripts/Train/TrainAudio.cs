
using UnityEngine;
using UnityEngine.Audio;

namespace Game.Train
{
    [DisallowMultipleComponent]
    public class TrainAudio : MonoBehaviour
    {
        public enum MixerChannel
        {
            Train,
            Engine,
            Track,
            Brakes,
            Horn
        }

        public const string TrackCutoffParam = "TrackCutoff";

        [Header("Audio Mixer")]
        [SerializeField] private AudioMixer mixer;

        [SerializeField] private float trackCutoffSlow = 800f;
        [SerializeField] private float trackCutoffFast = 12000f;

        [Header("Audio Sources")]

        [Tooltip("Assign multiple track AudioSources positioned around the train wheels.")]
        [SerializeField] private AudioSource[] trackSources;

        [SerializeField] private AudioSource engineSource;
        [SerializeField] private AudioSource hornSource;
        [SerializeField] private AudioSource slidingSource;

        [Header("Engine Sound")]

        [Tooltip("Engine pitch when train is stationary.")]
        [SerializeField] private float engineIdlePitch = 0.8f;

        [Tooltip("Engine pitch at maximum train speed.")]
        [SerializeField] private float engineMaxPitch = 1.6f;

        [Tooltip("How quickly engine pitch responds to speed.")]
        [SerializeField] private float enginePitchSmoothTime = 0.5f;

        [Tooltip("Optional engine pitch curve. X = speed percentage, Y = pitch interpolation.")]
        [SerializeField] private AnimationCurve enginePitchCurve =
            AnimationCurve.Linear(0f, 0f, 1f, 1f);

        private float _enginePitchVelocity;

        [Header("Track Sound")]

        [SerializeField] private float trackMinPitch = 0.6f;
        [SerializeField] private float trackMaxPitch = 1.4f;

        [Range(0.01f, 1f)]
        [SerializeField] private float trackFullVolumeAt = 0.25f;

        [SerializeField] private float trackStartSpeed = 0.1f;
        [SerializeField] private float trackFadeTime = 0.3f;

        private float[] _trackMaxVolumes;
        private float _trackVolume;

        [Header("Sliding Sound")]

        [Range(0f, 1f)]
        [SerializeField] private float slideLockThreshold = 0.5f;

        [SerializeField] private float slideMinSpeed = 0.3f;
        [SerializeField] private float slideFullVolumeSpeed = 6f;

        [SerializeField] private float slideMinPitch = 0.85f;
        [SerializeField] private float slideMaxPitch = 1.15f;

        [SerializeField] private float slideFadeInTime = 0.15f;
        [SerializeField] private float slideFadeOutTime = 0.4f;

        private float _slideMaxVolume = 1f;
        private float _slideVolume;

        private TrainSplineDriver _driver;
        private float _nextHornTime;
        private const float HornCooldown = 0.5f;

        private void Awake()
        {
            _driver = GetComponent<TrainSplineDriver>();

            if (_driver == null)
                _driver = GetComponentInParent<TrainSplineDriver>();

            FindMissingSources();

            InitializeTrackSources();
            InitializeEngine();
            InitializeHorn();
            InitializeSliding();
        }

        private void Start()
        {
            KeepEngineRunning();
        }

        // --------------------------------------------------
        // INITIALIZATION
        // --------------------------------------------------

        private void FindMissingSources()
        {
            AudioSource[] sources =
                GetComponentsInChildren<AudioSource>(true);

            if (trackSources == null || trackSources.Length == 0)
            {
                System.Collections.Generic.List<AudioSource> tracks =
                    new System.Collections.Generic.List<AudioSource>();

                foreach (AudioSource source in sources)
                {
                    if (source.gameObject.name == "TrackSound")
                        tracks.Add(source);
                }

                trackSources = tracks.ToArray();
            }

            if (engineSource == null)
                engineSource = FindSource(sources, "EngineSound");

            if (hornSource == null)
                hornSource = FindSource(sources, "Horn") ?? FindSource(sources, "horn");

            if (slidingSource == null)
                slidingSource = FindSource(sources, "TrainSliding");
        }

        private void InitializeTrackSources()
        {
            if (trackSources == null)
                trackSources = new AudioSource[0];

            _trackMaxVolumes = new float[trackSources.Length];

            for (int i = 0; i < trackSources.Length; i++)
            {
                AudioSource source = trackSources[i];

                if (source == null)
                    continue;

                _trackMaxVolumes[i] = source.volume;

                source.loop = true;
                source.playOnAwake = false;
                source.volume = 0f;
            }
        }

        private void InitializeEngine()
        {
            if (engineSource == null)
                return;

            engineSource.loop = true;
            engineSource.playOnAwake = false;

            engineSource.pitch = engineIdlePitch;
        }

        private void InitializeHorn()
        {
            if (hornSource == null)
                return;

            hornSource.loop = false;
            hornSource.playOnAwake = false;
        }

        private void InitializeSliding()
        {
            if (slidingSource == null)
                return;

            _slideMaxVolume = slidingSource.volume;

            slidingSource.loop = true;
            slidingSource.playOnAwake = false;
            slidingSource.volume = 0f;
        }

        // --------------------------------------------------
        // HORN
        // --------------------------------------------------

        public void PlayHorn()
        {
            if (hornSource == null || !hornSource.isActiveAndEnabled)
                return;

            if (Time.time < _nextHornTime)
                return;
            _nextHornTime = Time.time + HornCooldown;

            AudioClip clip = ResolveClip(hornSource);
            if (clip != null)
            {
                hornSource.PlayOneShot(clip);
                return;
            }

            if (hornSource.resource != null)
                hornSource.Play();
        }

        // --------------------------------------------------
        // MIXER
        // --------------------------------------------------

        public void SetVolume(MixerChannel channel, float volume01)
        {
            if (mixer == null)
                return;

            float db = volume01 <= 0.0001f
                ? -80f
                : 20f * Mathf.Log10(Mathf.Clamp01(volume01));

            mixer.SetFloat(channel + "Volume", db);
        }

        // --------------------------------------------------
        // UPDATE
        // --------------------------------------------------

        private void Update()
        {
            KeepEngineRunning();

            float speed = _driver != null
                ? Mathf.Abs(_driver.Velocity)
                : 0f;

            float maxSpeed = _driver != null
                ? Mathf.Max(0.1f, _driver.MaxSpeed)
                : 1f;

            float wheelLock = _driver != null
                ? _driver.WheelLock
                : 0f;

            float speed01 = Mathf.Clamp01(speed / maxSpeed);

            UpdateEngine(speed01);
            UpdateTrack(speed, speed01);
            UpdateSliding(speed, wheelLock);
        }

        // --------------------------------------------------
        // ENGINE
        // --------------------------------------------------

        private void KeepEngineRunning()
        {
            if (engineSource != null &&
                engineSource.isActiveAndEnabled &&
                !engineSource.isPlaying)
            {
                engineSource.Play();
            }
        }

        private void UpdateEngine(float speed01)
        {
            if (engineSource == null)
                return;

            float curveValue = enginePitchCurve.Evaluate(speed01);

            float targetPitch = Mathf.Lerp(
                engineIdlePitch,
                engineMaxPitch,
                Mathf.Clamp01(curveValue)
            );

            engineSource.pitch = Mathf.SmoothDamp(
                engineSource.pitch,
                targetPitch,
                ref _enginePitchVelocity,
                Mathf.Max(0.01f, enginePitchSmoothTime)
            );
        }

        // --------------------------------------------------
        // TRACK AUDIO - MULTIPLE SOURCES
        // --------------------------------------------------

        private void UpdateTrack(float speed, float speed01)
        {
            if (trackSources == null)
                return;

            float target = speed > trackStartSpeed
                ? Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.Clamp01(speed01 / trackFullVolumeAt)
                )
                : 0f;

            _trackVolume = Mathf.MoveTowards(
                _trackVolume,
                target,
                Time.deltaTime / Mathf.Max(0.01f, trackFadeTime)
            );

            float pitch = Mathf.Lerp(
                trackMinPitch,
                trackMaxPitch,
                speed01
            );

            for (int i = 0; i < trackSources.Length; i++)
            {
                AudioSource source = trackSources[i];

                if (source == null || !source.isActiveAndEnabled)
                    continue;

                source.pitch = pitch;

                source.volume = _trackMaxVolumes[i] * _trackVolume;

                if (_trackVolume > 0.001f)
                {
                    if (!source.isPlaying)
                        source.Play();
                }
                else if (source.isPlaying)
                {
                    source.Pause();
                }
            }

            UpdateTrackMixer(speed01);
        }

        private void UpdateTrackMixer(float speed01)
        {
            if (mixer == null)
                return;

            float cutoff = Mathf.Exp(
                Mathf.Lerp(
                    Mathf.Log(Mathf.Max(10f, trackCutoffSlow)),
                    Mathf.Log(Mathf.Max(10f, trackCutoffFast)),
                    speed01
                )
            );

            mixer.SetFloat(TrackCutoffParam, cutoff);
        }

        // --------------------------------------------------
        // SLIDING AUDIO
        // --------------------------------------------------

        private void UpdateSliding(float speed, float wheelLock)
        {
            if (slidingSource == null ||
                !slidingSource.isActiveAndEnabled)
                return;

            float target = 0f;

            if (wheelLock > slideLockThreshold &&
                speed > slideMinSpeed)
            {
                float lock01 = Mathf.InverseLerp(
                    slideLockThreshold,
                    1f,
                    wheelLock
                );

                float speed01 = Mathf.Clamp01(
                    speed / Mathf.Max(0.1f, slideFullVolumeSpeed)
                );

                target = _slideMaxVolume *
                    Mathf.Lerp(0.4f, 1f, lock01) *
                    Mathf.Lerp(0.35f, 1f, speed01);
            }

            float fadeTime = target > _slideVolume
                ? slideFadeInTime
                : slideFadeOutTime;

            _slideVolume = Mathf.MoveTowards(
                _slideVolume,
                target,
                _slideMaxVolume * Time.deltaTime /
                Mathf.Max(0.01f, fadeTime)
            );

            slidingSource.volume = _slideVolume;

            slidingSource.pitch = Mathf.Lerp(
                slideMinPitch,
                slideMaxPitch,
                Mathf.Clamp01(
                    speed / Mathf.Max(0.1f, slideFullVolumeSpeed)
                )
            );

            if (_slideVolume > 0.001f)
            {
                if (!slidingSource.isPlaying)
                    slidingSource.Play();
            }
            else if (slidingSource.isPlaying)
            {
                slidingSource.Stop();
            }
        }

        // --------------------------------------------------
        // HELPERS
        // --------------------------------------------------

        private AudioSource FindSource(
            AudioSource[] sources,
            string childName)
        {
            foreach (AudioSource source in sources)
            {
                if (source != null &&
                    string.Equals(source.gameObject.name, childName, System.StringComparison.OrdinalIgnoreCase))
                {
                    return source;
                }
            }

            return null;
        }

        private static AudioClip ResolveClip(AudioSource source)
        {
            if (source == null)
                return null;
            if (source.clip != null)
                return source.clip;
            return source.resource as AudioClip;
        }
    }
}

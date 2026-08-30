using Motiong.vCharacterController;
using UnityEngine;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
public class PlayerFootstepSystem : MonoBehaviour
{
    [Header("Audio")]
    [SerializeField] private AudioSource footstepAudioSource;
    [SerializeField] private float volume = 1f;

    [Header("Timing")]
    [Tooltip("Minimum horizontal speed before footsteps play.")]
    [SerializeField] private float footstepSensitivity = 0.35f;
    [Tooltip("Seconds between steps while walking (aim/strafe, walk-by-default, or low input).")]
    [SerializeField] private float walkStepInterval = 0.5f;
    [Tooltip("Seconds between steps while running (normal free locomotion).")]
    [FormerlySerializedAs("walkStepInterval")]
    [SerializeField] private float runStepInterval = 0.38f;
    [Tooltip("Seconds between steps while sprinting.")]
    [SerializeField] private float sprintStepInterval = 0.28f;
    [Tooltip("Animator input magnitude at or below this uses the walk interval (when not sprinting).")]
    [SerializeField] private float walkInputMagnitudeMax = 0.55f;

    [Header("Surface Tags")]
    [SerializeField] private string grassTag = "Grass";
    [SerializeField] private string concreteTag = "Concrete";
    [SerializeField] private string waterTag = "Water";
    [SerializeField] private string metalTag = "Metal";
    [SerializeField] private string gravelTag = "Gravel";
    [SerializeField] private string woodTag = "Wood";

    [Header("Surface Clips")]
    [SerializeField] private AudioClip[] soundGrass;
    [SerializeField] private AudioClip[] soundConcrete;
    [SerializeField] private AudioClip[] soundWater;
    [SerializeField] private AudioClip[] soundMetal;
    [SerializeField] private AudioClip[] soundGravel;
    [SerializeField] private AudioClip[] soundWood;

    [Header("Player Clips")]
    [SerializeField] private AudioClip jumpClip;
    [SerializeField] private AudioClip landClip;

    private vThirdPersonController _controller;
    private ParkourController _parkour;
    private Rigidbody _rigidbody;
    private float _stepTimer;
    private bool _wasGrounded = true;
    private int _lastClipIndex = -1;

    private void Awake()
    {
        _controller = GetComponent<vThirdPersonController>();
        _parkour = GetComponent<ParkourController>();
        _rigidbody = GetComponent<Rigidbody>();
        EnsureAudioSource();
    }

    private void Update()
    {
        if (_controller == null)
            return;

        if (_parkour != null && _parkour.IsInParkourAction)
        {
            _stepTimer = 0f;
            return;
        }

        UpdateJumpLand();
        UpdateFootsteps();
    }

    private void EnsureAudioSource()
    {
        if (footstepAudioSource != null)
            return;

        Transform existing = transform.Find("footstepAudio");
        if (existing != null)
            footstepAudioSource = existing.GetComponent<AudioSource>();

        if (footstepAudioSource != null)
            return;

        GameObject go = new GameObject("footstepAudio");
        go.transform.SetParent(transform, false);
        footstepAudioSource = go.AddComponent<AudioSource>();
        footstepAudioSource.playOnAwake = false;
        footstepAudioSource.spatialBlend = 1f;
        footstepAudioSource.loop = false;
    }

    private void UpdateJumpLand()
    {
        bool grounded = _controller.isGrounded;

        if (_wasGrounded && !grounded && !_controller.isSliding)
            PlayOneShot(jumpClip);

        if (!_wasGrounded && grounded && !_controller.isSliding)
            PlayOneShot(landClip);

        _wasGrounded = grounded;
    }

    private void UpdateFootsteps()
    {
        if (!_controller.isGrounded || _controller.isSliding || _controller.isJumping)
        {
            _stepTimer = 0f;
            return;
        }

        float speed = GetHorizontalSpeed();
        if (speed < footstepSensitivity)
        {
            _stepTimer = 0f;
            return;
        }

        float interval = Mathf.Max(0.05f, GetStepInterval());

        _stepTimer += Time.deltaTime;
        if (_stepTimer < interval)
            return;

        _stepTimer = 0f;
        PlaySurfaceStep();
    }

    private float GetStepInterval()
    {
        if (_controller.isSprinting)
            return sprintStepInterval;

        if (IsWalkingLocomotion())
            return walkStepInterval;

        return runStepInterval;
    }

    private bool IsWalkingLocomotion()
    {
        if (_controller.forceWalkLocomotion || _controller.suppressUpperBodyLocomotion)
            return true;

        vThirdPersonMotor.vMovementSpeed speed =
            _controller.isStrafing ? _controller.strafeSpeed : _controller.freeSpeed;
        if (speed != null && speed.walkByDefault)
            return true;

        return Mathf.Abs(_controller.inputMagnitude) <= walkInputMagnitudeMax;
    }

    private float GetHorizontalSpeed()
    {
        if (_rigidbody != null)
        {
            Vector3 velocity = _rigidbody.linearVelocity;
            velocity.y = 0f;
            return velocity.magnitude;
        }

        return Mathf.Abs(_controller.inputMagnitude) * Mathf.Max(0.01f, _controller.moveSpeed);
    }

    private void PlaySurfaceStep()
    {
        AudioClip[] clips = ResolveSurfaceClips();
        AudioClip clip = PickRandomClip(clips);
        PlayOneShot(clip);
    }

    private AudioClip[] ResolveSurfaceClips()
    {
        Collider groundCollider = _controller.groundHit.collider;
        string tag = groundCollider != null ? groundCollider.tag : string.Empty;

        if (!string.IsNullOrEmpty(tag))
        {
            if (tag == grassTag && HasClips(soundGrass))
                return soundGrass;
            if (tag == concreteTag && HasClips(soundConcrete))
                return soundConcrete;
            if (tag == waterTag && HasClips(soundWater))
                return soundWater;
            if (tag == metalTag && HasClips(soundMetal))
                return soundMetal;
            if (tag == gravelTag && HasClips(soundGravel))
                return soundGravel;
            if (tag == woodTag && HasClips(soundWood))
                return soundWood;
        }

        // Untagged / unknown → concrete fallback, then any available pack.
        if (HasClips(soundConcrete))
            return soundConcrete;
        if (HasClips(soundGrass))
            return soundGrass;
        if (HasClips(soundGravel))
            return soundGravel;
        if (HasClips(soundWood))
            return soundWood;
        if (HasClips(soundMetal))
            return soundMetal;
        if (HasClips(soundWater))
            return soundWater;
        return null;
    }

    private static bool HasClips(AudioClip[] clips)
    {
        if (clips == null || clips.Length == 0)
            return false;

        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i] != null)
                return true;
        }

        return false;
    }

    private AudioClip PickRandomClip(AudioClip[] clips)
    {
        if (!HasClips(clips))
            return null;

        if (clips.Length == 1)
            return clips[0];

        int index = Random.Range(0, clips.Length);
        if (clips.Length > 1 && index == _lastClipIndex)
            index = (index + 1) % clips.Length;

        _lastClipIndex = index;
        return clips[index];
    }

    private void PlayOneShot(AudioClip clip)
    {
        if (clip == null)
            return;

        EnsureAudioSource();
        if (footstepAudioSource == null)
            return;

        footstepAudioSource.PlayOneShot(clip, volume);
    }
}

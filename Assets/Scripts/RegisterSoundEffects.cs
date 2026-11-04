using UnityEngine;

/// <summary>
/// Optional UI sounds for the register. Assign clips on the scene component;
/// the AudioSource is created automatically when the scene starts.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
[AddComponentMenu("Club Register/Register Sound Effects")]
public sealed class RegisterSoundEffects : MonoBehaviour
{
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip buttonTapClip;
    [SerializeField] private AudioClip productTapClip;
    [SerializeField, Range(0f, 1f)] private float volume = 0.75f;

    private void Awake()
    {
        if(audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }

        if(audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }

        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.spatialBlend = 0f;
    }

    public void PlayButtonTap()
    {
        Play(buttonTapClip);
    }

    public void PlayProductTap()
    {
        Play(productTapClip != null ? productTapClip : buttonTapClip);
    }

    private void Play(AudioClip clip)
    {
        if(clip != null && audioSource != null)
        {
            audioSource.PlayOneShot(clip, Mathf.Clamp01(volume));
        }
    }
}

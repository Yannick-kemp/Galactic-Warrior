using System;
using System.Collections;
using UnityEngine;

namespace Assets.Scripts.Characteres.WarriorController
{
    public partial class Warrior : CharacterController
    {

        #region sound

        [Header("Attack3 Cast SFX")]
        [SerializeField] private AudioClip attack3CastClip; // assign laser-futur.mp3
        [SerializeField, Range(0f, 1f)] private float attack3CastVolume = 0.95f;
        [SerializeField] private Vector2 attack3CastPitchRange = new Vector2(0.98f, 1.02f);
        [SerializeField] private bool attack3CastPlayOncePerFrame = true;

        private int _lastAttack3CastSfxFrame = -1;

        [SerializeField] private bool attack2FadeOutInsteadOfHardStop = true;
        [SerializeField, Min(0f)] private float attack2FadeOutSeconds = 0.06f;

        // --- Add these fields near your other [Header("SFX")] fields ---

        [Header("Attack2 Hit SFX (on enemy hit only)")]
        [SerializeField] private AudioClip attack2HitClip;   // assign kick-182227.mp3 in Inspector
        [SerializeField, Range(0f, 1f)] private float attack2HitVolume = 0.9f;
        [SerializeField] private Vector2 attack2HitPitchRange = new Vector2(0.96f, 1.04f);
        [SerializeField] private bool attack2HitPlayOncePerFrame = true;

        private int _lastAttack2HitSfxFrame = -1;

        // ... Keep your existing Attack2 and SFX variables ...

        [Header("Voice SFX")]
        [SerializeField] private AudioSource _voiceSource; // New dedicated source
        [SerializeField, Range(0f, 1f)] private float voiceVolume = 1.0f;

        private void EnsureVoiceSource()
        {
            if (_voiceSource != null) return;

            // Adds a third AudioSource to the Warrior GameObject
            _voiceSource = gameObject.AddComponent<AudioSource>();
            _voiceSource.playOnAwake = false;
            _voiceSource.loop = false;
            _voiceSource.spatialBlend = 0f; // 2D Sound
        }

        /// <summary>
        /// Plays a voice clip. Use this for grunts or dialogue.
        /// Using .Play() ensures new voice lines interrupt old ones (so they don't talk over themselves).
        /// </summary>
        public void PlayVoice(AudioClip clip)
        {
            if (clip == null) return;
            EnsureVoiceSource();

            _voiceSource.clip = clip;
            _voiceSource.volume = voiceVolume;
            _voiceSource.Play();
        }
        // --- Add this method in the same partial (Warrior.Audio.cs) ---

        private void PlayAttack2HitSfx()
        {
            if (attack2HitClip == null) return;
            EnsureSfxSource();

            if (attack2HitPlayOncePerFrame && _lastAttack2HitSfxFrame == Time.frameCount)
                return;

            _lastAttack2HitSfxFrame = Time.frameCount;

            _sfxSource.pitch = UnityEngine.Random.Range(attack2HitPitchRange.x, attack2HitPitchRange.y);
            _sfxSource.PlayOneShot(attack2HitClip, attack2HitVolume);
        }

        private AudioSource _attack2Source;
        private Coroutine _attack2FadeRoutine;

        private void EnsureSfxSource()
        {
            if (_sfxSource != null) return;

            _sfxSource = GetComponent<AudioSource>();
            if (_sfxSource == null) _sfxSource = gameObject.AddComponent<AudioSource>();

            _sfxSource.playOnAwake = false;
            _sfxSource.loop = false;
            _sfxSource.spatialBlend = 0f; // 2D
        }

        private void EnsureAttack2Source()
        {
            if (_attack2Source != null) return;

            // Dedicated AudioSource so Stop() doesn't cut jump/hit sounds
            _attack2Source = gameObject.AddComponent<AudioSource>();
            _attack2Source.playOnAwake = false;
            _attack2Source.loop = false;
            _attack2Source.spatialBlend = 0f; // 2D
        }
        private void PlayAttack1HitSfx(bool isSpecialHit = false)
        {
            if (attack1HitClip == null) return;
            EnsureSfxSource();

            if (attack1HitPlayOncePerFrame)
            {
                if (_lastAttack1HitSfxFrame == Time.frameCount) return;
                _lastAttack1HitSfxFrame = Time.frameCount;
            }

            // Always play the Hit SFX on the SFX Source
            _sfxSource.pitch = UnityEngine.Random.Range(attack1HitPitchRange.x, attack1HitPitchRange.y);
            _sfxSource.PlayOneShot(attack1HitClip, attack1HitVolume);

            // Only play the "Yeeah" if this is the special hit (Frame 27 / Kick)
            if (isSpecialHit && yeeahClip != null)
            {
                PlayVoice(yeeahClip);
            }
            else if (attack2Grunt != null) // Otherwise play the normal grunt
            {
                PlayVoice(attack2Grunt);
            }
        }

        private void PlayJumpSfx()
        {
            if (jumpClip == null) return;
            EnsureSfxSource();

            if (jumpPlayOncePerFrame)
            {
                if (_lastJumpSfxFrame == Time.frameCount) return;
                _lastJumpSfxFrame = Time.frameCount;
            }

            _sfxSource.pitch = UnityEngine.Random.Range(jumpPitchRange.x, jumpPitchRange.y);
            _sfxSource.PlayOneShot(jumpClip, jumpVolume);
        }

        private void PlayAttack1MissSfx()
        {
            if (attack1MissClip == null) return;
            EnsureSfxSource();

            if (attack1MissPlayOncePerFrame)
            {
                if (_lastAttack1MissSfxFrame == Time.frameCount) return;
                _lastAttack1MissSfxFrame = Time.frameCount;
            }

            // Layer 1: The "Whoosh" on SFX Source
            _sfxSource.pitch = UnityEngine.Random.Range(attack1MissPitchRange.x, attack1MissPitchRange.y);
            _sfxSource.PlayOneShot(attack1MissClip, attack1MissVolume);

            // Layer 2: The effort grunt on Voice Source
            if (attack2Grunt != null)
            {
                PlayVoice(attack2Grunt);
            }
        }

        //START Attack2 sound (stoppable)
        private void StartAttack2Sfx()
        {
            if (attack2Clip == null) return;

            EnsureAttack2Source();

            if (_attack2FadeRoutine != null)
            {
                StopCoroutine(_attack2FadeRoutine);
                _attack2FadeRoutine = null;
            }

            // restart cleanly
            _attack2Source.Stop();
            _attack2Source.clip = attack2Clip;
            _attack2Source.volume = attack2Volume;
            _attack2Source.pitch = UnityEngine.Random.Range(attack2PitchRange.x, attack2PitchRange.y);
            _attack2Source.Play();
        }

        //  STOP Attack2 sound exactly at animation end
        private void StopAttack2Sfx()
        {
            if (_attack2Source == null) return;

            if (!attack2FadeOutInsteadOfHardStop || attack2FadeOutSeconds <= 0f)
            {
                _attack2Source.Stop();
                return;
            }

            if (_attack2FadeRoutine != null) StopCoroutine(_attack2FadeRoutine);
            _attack2FadeRoutine = StartCoroutine(FadeOutAndStop(_attack2Source, attack2FadeOutSeconds));
        }

        private IEnumerator FadeOutAndStop(AudioSource src, float seconds)
        {
            float startVol = src.volume;
            float t = 0f;

            while (t < seconds && src != null && src.isPlaying)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / seconds);
                src.volume = Mathf.Lerp(startVol, 0f, k);
                yield return null;
            }

            if (src != null)
            {
                src.Stop();
                src.volume = startVol; // restore for next time
            }

            _attack2FadeRoutine = null;
        }
        // Warrior.Audio.cs (inside the Warrior class)
        [Header("Attack2 Hit Grunt (on enemy hit only)")]
        [SerializeField] private AudioClip attack2Grunt; // Assign in Inspector

        public void AE_Attack2_SfxBegin()
        {
            StartAttack2Sfx();      // Plays the "Whoosh"
            PlayVoice(attack2Grunt); // Plays the "Hyaah!"
        }
        public void AE_Attack2_SfxEnd() => StopAttack2Sfx();

        [Header("Attack 1 Special Voice")]
        [SerializeField] private AudioClip yeeahClip; // Assign yeeah.mp3 here in the Inspector


        private void PlayAttack3CastSfx()
        {
            if (attack3CastClip == null) return;

            EnsureSfxSource();

            if (attack3CastPlayOncePerFrame && _lastAttack3CastSfxFrame == Time.frameCount)
                return;

            _lastAttack3CastSfxFrame = Time.frameCount;

            _sfxSource.pitch = UnityEngine.Random.Range(
                attack3CastPitchRange.x,
                attack3CastPitchRange.y);

            _sfxSource.PlayOneShot(attack3CastClip, attack3CastVolume);
        }
        #endregion
    }
}

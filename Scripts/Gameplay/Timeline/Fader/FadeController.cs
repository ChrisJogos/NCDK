using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace NCDK.Timeline {
    [RequireComponent(typeof(Image), typeof(Canvas))]
    public class FadeController : MonoBehaviour {

        [SerializeField] Image _fadeImage;
        [SerializeField] Canvas _canvas;

        /// <summary>
        /// Alpha written by the last <see cref="SetColor"/> call. PlayableHelper reads it to decide
        /// whether the teardown frame needs an opaque cover: covering a cutscene that ends transparent
        /// would invent a black frame that the author never asked for.
        /// </summary>
        public float LastAlpha { get; private set; }

        void OnValidate() {
            GetReferences();
        }

        void Reset() {
            GetReferences();
        }

        void Awake() {
            GetReferences();
        }

        void GetReferences() {
            TryGetComponent(out _fadeImage);
            TryGetComponent(out _canvas);
        }

        public void SetColor(Color c, float alpha) {
            LastAlpha = alpha;
            _canvas.enabled = alpha > 0;
            c.a = alpha;
            _fadeImage.color = c;
        }

        /// <summary>
        /// Ramps the overlay from its current alpha to <paramref name="targetAlpha"/> using unscaled time.
        /// </summary>
        /// <remarks>
        /// Used after a cutscene stops: the overlay outlives the Timeline, so whoever takes over the scene
        /// decides when (and how fast) the cover leaves the screen. Colours are not blended - the overlay is
        /// always black, only the alpha moves.
        /// </remarks>
        public IEnumerator FadeTo(float targetAlpha, float duration) {
            if (duration <= 0f) {
                SetColor(Color.black, targetAlpha);
                yield break;
            }

            var startAlpha = LastAlpha;
            var elapsed = 0f;
            while (elapsed < duration) {
                SetColor(Color.black, Mathf.Lerp(startAlpha, targetAlpha, elapsed / duration));
                yield return null;
                elapsed += Time.unscaledDeltaTime;
            }

            SetColor(Color.black, targetAlpha);
        }

    }
}

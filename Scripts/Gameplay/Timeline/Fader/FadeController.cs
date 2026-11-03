using System;
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

    }
}

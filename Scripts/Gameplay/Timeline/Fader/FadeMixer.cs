using UnityEngine;
using UnityEngine.Playables;

namespace NCDK.Timeline {
    public class FadeMixer : PlayableBehaviour {

        FadeController _fadeController;

        // Last value actually written. The alpha is held when no clip is playing: deriving it from
        // "nothing is playing" would write zero and flash the scene through for a frame at the end of
        // every cutscene, because the last clip is already finished when the graph is still evaluating.
        Color _lastColor = Color.black;
        float _lastAlpha;
        bool _hasWritten;

        // Flip to true while investigating a fade gap; the log names the frame and the held value.
        const bool DebugHolds = false;

        public override void ProcessFrame(Playable playable, FrameData info, object playerData) {
            base.ProcessFrame(playable, info, playerData);

            _fadeController = playerData as FadeController;
            if (_fadeController == null) return;

            var count = playable.GetInputCount();

            var targetColor = Color.clear;
            float targetAlpha = 0f;
            int playingInputs = 0;

            for (var i = 0; i < count; i++) {
                var input = playable.GetInput(i);
                var weight = playable.GetInputWeight(i);

                if (!input.IsValid() || input.GetPlayState() != PlayState.Playing) continue;

                var data = ((ScriptPlayable<FadeDataPlayable>) input).GetBehaviour();
                if (data == null) continue;

                playingInputs++;
                targetAlpha += data.MaxAlpha * weight;

                if (weight > 0.001f) {
                    targetColor = targetColor == Color.clear ? data.Color : Color.Lerp(targetColor, data.Color, weight);
                }
            }

            if (playingInputs == 0) {
                // No clip owns the fade right now (gap at the tail, or clip already finished). Keep what
                // was last written instead of turning the overlay off.
                if (DebugHolds) {
                    Debug.Log($"[FadeMixer] no clip playing at t={playable.GetTime():F3}s; holding color={_lastColor} alpha={_lastAlpha:F3}", _fadeController);
                }

                if (_hasWritten) _fadeController.SetColor(_lastColor, _lastAlpha);
                return;
            }

            _lastColor = targetColor == Color.clear ? Color.black : targetColor;
            _lastAlpha = targetAlpha;
            _hasWritten = true;

            _fadeController.SetColor(_lastColor, _lastAlpha);
        }

        public override void OnGraphStop (Playable playable) {
            // Restore the last value after the graph is torn down: Timeline restores its bindings and the
            // fade must not come back transparent before PlayableHelper covers the teardown frame.
            if (_hasWritten && _fadeController != null) {
                _fadeController.SetColor(_lastColor, _lastAlpha);
            }
        }
    }
}

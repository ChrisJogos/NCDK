using UnityEngine;
using UnityEngine.Playables;

namespace NCDK.Timeline {
    public class FadeMixer : PlayableBehaviour {

        FadeController _fadeController;

        public override void ProcessFrame(Playable playable, FrameData info, object playerData) {
            base.ProcessFrame(playable, info, playerData);

            _fadeController = playerData as FadeController;
            if (_fadeController == null) return;

            var count = playable.GetInputCount();

            var targetColor = Color.clear;
            float targetAlpha = 0f;
            bool anyPlaying = false;
            for (var i = 0; i < count; i++) {
                var input = playable.GetInput(i);
                var weight = playable.GetInputWeight(i);

                if (!input.IsValid() || input.GetPlayState() != PlayState.Playing) continue;

                var data = ((ScriptPlayable<FadeDataPlayable>) input).GetBehaviour();
                if (data == null) continue;

                anyPlaying = true;
                targetAlpha += data.MaxAlpha * weight;
                
                if (weight > 0.001f) { 
                    targetColor = targetColor == Color.clear ? data.Color : Color.Lerp(targetColor, data.Color, weight);
                }
            }

            // At the end of the graph the fade-out clip has already finished, so "no clip playing" is not
            // "make the screen transparent": writing alpha 0 here was the flash at the last frame of every
            // fade-to-black cutscene ('first week complete', week intros). Keep what the clips produced.
            // A gap in the middle of a cutscene still means transparent.
            if (!anyPlaying && IsAtGraphEnd(playable)) return;

            _fadeController.SetColor(targetColor == Color.clear ? Color.black : targetColor, targetAlpha);
        }

        /// <summary>True when the playhead is on the last frames of the whole timeline.</summary>
        static bool IsAtGraphEnd(Playable playable) {
            var root = playable.GetGraph().GetRootPlayable(0);
            if (!root.IsValid()) return false;

            double duration = root.GetDuration();
            if (double.IsNaN(duration) || duration <= 0) return false;

            return root.GetTime() >= duration - 0.05;
        }

        public override void OnGraphStop (Playable playable) {
            // Fade cleanup is handled by PlayableHelper.OnStoppedPlaying() after disabling GameObjects,
            // so the fade stays active until the cutscene is fully cleaned up.
        }
    }
}
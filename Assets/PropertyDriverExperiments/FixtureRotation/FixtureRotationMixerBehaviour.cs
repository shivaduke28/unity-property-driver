using UnityEngine;
using UnityEngine.Playables;

namespace PropertyDriverExperiments.Timeline
{
    /// <summary>
    /// weight が最大のクリップの回転を信号として <see cref="FixtureRotation.SetTargetRotation"/> に送る。
    /// 時間は進めない（補間は target 側の LateUpdate が Time.deltaTime で行う）。
    /// クリップの無い区間は最後の信号をホールドする。グラフ破棄時に <see cref="FixtureRotation.ClearTarget"/>。
    /// </summary>
    public class FixtureRotationMixerBehaviour : PlayableBehaviour
    {
        FixtureRotation binding;

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            var target = playerData as FixtureRotation;
            if (target != binding)
            {
                if (binding != null) binding.ClearTarget();
                binding = target;
            }
            if (binding == null) return;

            var bestWeight = 0f;
            FixtureRotationBehaviour best = null;
            var inputCount = playable.GetInputCount();
            for (var i = 0; i < inputCount; i++)
            {
                var weight = playable.GetInputWeight(i);
                if (weight <= bestWeight) continue;
                bestWeight = weight;
                best = ((ScriptPlayable<FixtureRotationBehaviour>)playable.GetInput(i)).GetBehaviour();
            }

            if (best != null)
                binding.SetTargetRotation(Quaternion.Euler(best.eulerAngles));
        }

        public override void OnPlayableDestroy(Playable playable)
        {
            if (binding != null) binding.ClearTarget();
            binding = null;
        }
    }
}

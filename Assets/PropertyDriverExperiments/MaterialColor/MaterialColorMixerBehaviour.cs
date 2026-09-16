using UnityEngine;
using UnityEngine.Playables;

namespace PropertyDriverExperiments.Timeline
{
    /// <summary>
    /// クリップの色を weight で加重平均して <see cref="MaterialColorTarget.SetColor"/> に渡す。
    /// クリップの無い区間とグラフ破棄時は <see cref="MaterialColorTarget.ResetColor"/> で戻す。
    /// </summary>
    public class MaterialColorMixerBehaviour : PlayableBehaviour
    {
        MaterialColorTarget binding;

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            var target = playerData as MaterialColorTarget;
            if (target != binding)
            {
                if (binding != null) binding.ResetColor();
                binding = target;
            }
            if (binding == null) return;

            var accum = Color.clear;
            var totalWeight = 0f;
            var inputCount = playable.GetInputCount();
            for (var i = 0; i < inputCount; i++)
            {
                var weight = playable.GetInputWeight(i);
                if (weight <= 0f) continue;

                var input = (ScriptPlayable<MaterialColorBehaviour>)playable.GetInput(i);
                accum += input.GetBehaviour().color * weight;
                totalWeight += weight;
            }

            if (totalWeight <= 0f)
            {
                binding.ResetColor();
                return;
            }

            binding.SetColor(Color.Lerp(binding.BaseColor, accum, totalWeight));
        }

        public override void OnPlayableDestroy(Playable playable)
        {
            if (binding != null) binding.ResetColor();
            binding = null;
        }
    }
}

using UnityEngine.Playables;

namespace PropertyDriverExperiments.Timeline
{
    /// <summary>
    /// クリップの値を入力 weight で加重平均し、binding の target に素の代入で書き込む。
    /// クリップの無い区間（総 weight 0）では何も書かない。
    /// </summary>
    public class DrivenValueMixerBehaviour : PlayableBehaviour
    {
        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            var target = playerData as DrivenTarget;
            if (target == null) return;

            var accum = 0f;
            var totalWeight = 0f;
            var inputCount = playable.GetInputCount();
            for (var i = 0; i < inputCount; i++)
            {
                var weight = playable.GetInputWeight(i);
                if (weight <= 0f) continue;

                var input = (ScriptPlayable<DrivenValueBehaviour>)playable.GetInput(i);
                accum += input.GetBehaviour().value * weight;
                totalWeight += weight;
            }

            if (totalWeight <= 0f) return;

            target.Value = accum / totalWeight;
        }
    }
}

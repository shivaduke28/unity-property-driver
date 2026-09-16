using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace PropertyDriverExperiments.Timeline
{
    /// <summary>
    /// クリップの色を <see cref="MaterialColorTarget"/> に渡すトラック。
    /// MaterialPropertyBlock の控え・適用・復元は target 側の責務で、このトラックは色を混ぜて渡すだけ。
    /// シリアライズされる値には触らないので、Timeline に申告するプロパティは無い。
    /// </summary>
    [TrackColor(1f, 0.5f, 0.2f)]
    [TrackBindingType(typeof(MaterialColorTarget))]
    [TrackClipType(typeof(MaterialColorClip))]
    public class MaterialColorTrack : TrackAsset
    {
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            return ScriptPlayable<MaterialColorMixerBehaviour>.Create(graph, inputCount);
        }
    }
}

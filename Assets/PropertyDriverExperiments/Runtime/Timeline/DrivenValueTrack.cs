using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace PropertyDriverExperiments.Timeline
{
    /// <summary>クリップから <see cref="DrivenTarget.Value"/> を駆動する。</summary>
    [TrackColor(0.2f, 0.7f, 1f)]
    [TrackBindingType(typeof(DrivenTarget))]
    [TrackClipType(typeof(DrivenValueClip))]
    public class DrivenValueTrack : DrivenTrack<DrivenTarget>
    {
        protected override string[] DrivenPropertyPaths { get; } = { DrivenTarget.ValuePath };

        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            return ScriptPlayable<DrivenValueMixerBehaviour>.Create(graph, inputCount);
        }
    }
}

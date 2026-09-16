using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace PropertyDriverExperiments.Timeline
{
    /// <summary>
    /// クリップの回転を信号として <see cref="FixtureRotation"/> に送るトラック。
    /// 補間は target 側の責務。このトラックはプレビュー中に target の Transform を一時的に動かす側なので、
    /// <see cref="GatherProperties"/> で m_LocalRotation を申告し、登録と復元は Timeline のプレビュー driver に任せる。
    /// </summary>
    [TrackColor(0.8f, 0.3f, 0.9f)]
    [TrackBindingType(typeof(FixtureRotation))]
    [TrackClipType(typeof(FixtureRotationClip))]
    public class FixtureRotationTrack : TrackAsset
    {
        public const string LocalRotationPath = "m_LocalRotation";

        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            return ScriptPlayable<FixtureRotationMixerBehaviour>.Create(graph, inputCount);
        }

        public override void GatherProperties(PlayableDirector director, IPropertyCollector driver)
        {
            if (director.GetGenericBinding(this) is not FixtureRotation binding) return;

            driver.AddFromName<Transform>(binding.gameObject, LocalRotationPath);
        }
    }
}

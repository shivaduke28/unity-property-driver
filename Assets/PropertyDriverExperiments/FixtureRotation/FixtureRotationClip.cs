using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace PropertyDriverExperiments.Timeline
{
    [Serializable]
    public class FixtureRotationClip : PlayableAsset, ITimelineClipAsset
    {
        public FixtureRotationBehaviour template = new FixtureRotationBehaviour();

        public ClipCaps clipCaps => ClipCaps.None;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            return ScriptPlayable<FixtureRotationBehaviour>.Create(graph, template);
        }
    }
}

using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace PropertyDriverExperiments.Timeline
{
    [Serializable]
    public class DrivenValueClip : PlayableAsset, ITimelineClipAsset
    {
        public DrivenValueBehaviour template = new();

        public ClipCaps clipCaps => ClipCaps.Blending;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            return ScriptPlayable<DrivenValueBehaviour>.Create(graph, template);
        }
    }
}

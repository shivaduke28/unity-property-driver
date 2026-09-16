using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace PropertyDriverExperiments.Timeline
{
    [Serializable]
    public class MaterialColorClip : PlayableAsset, ITimelineClipAsset
    {
        public MaterialColorBehaviour template = new();

        public ClipCaps clipCaps => ClipCaps.Blending;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            return ScriptPlayable<MaterialColorBehaviour>.Create(graph, template);
        }
    }
}

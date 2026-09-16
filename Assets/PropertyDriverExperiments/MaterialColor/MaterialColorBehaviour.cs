using System;
using UnityEngine;
using UnityEngine.Playables;

namespace PropertyDriverExperiments.Timeline
{
    [Serializable]
    public class MaterialColorBehaviour : PlayableBehaviour
    {
        public Color color = Color.white;
    }
}

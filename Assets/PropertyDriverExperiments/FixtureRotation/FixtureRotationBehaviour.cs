using System;
using UnityEngine;
using UnityEngine.Playables;

namespace PropertyDriverExperiments.Timeline
{
    [Serializable]
    public class FixtureRotationBehaviour : PlayableBehaviour
    {
        /// <summary>ターゲットのローカル回転（オイラー角、度）。</summary>
        public Vector3 eulerAngles;
    }
}

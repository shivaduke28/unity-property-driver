using UnityEngine;

namespace PropertyDriverExperiments
{
    /// <summary>
    /// スライダー値をシーンの外（このアセット）に持つ。編集で dirty になるのはこのアセットだけで、シーンは汚れない。
    /// </summary>
    [CreateAssetMenu(menuName = "PropertyDriverExperiments/Slider Driver Settings")]
    public class SliderDriverSettings : ScriptableObject
    {
        [Range(0f, 1f)] public float t;
    }
}

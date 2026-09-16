using UnityEngine;

namespace PropertyDriverExperiments
{
    /// <summary>
    /// データのみの driver。<see cref="SliderDriverSettings.t"/> を [min, max] に写像し、
    /// <see cref="target"/> のどのプロパティを駆動するかを表す。
    /// スライダー値は <see cref="settings"/>（ScriptableObject アセット）側に持つので、スライダー操作でシーンは汚れない。
    /// 実際の登録と書き込みは Editor アセンブリのカスタムインスペクタが行い、ランタイムでは何もしない。
    /// </summary>
    public class SliderDriver : MonoBehaviour
    {
        public const string LocalPositionPropertyPath = "m_LocalPosition";

        public DrivenTarget target;
        public bool driveTransformPosition;
        public float min;
        public float max = 10f;
        public SliderDriverSettings settings;

        public float T => settings != null ? settings.t : 0f;
        public float DrivenValue => Mathf.Lerp(min, max, T);
    }
}

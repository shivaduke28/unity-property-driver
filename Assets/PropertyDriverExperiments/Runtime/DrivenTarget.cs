using UnityEngine;

namespace PropertyDriverExperiments
{
    /// <summary>
    /// <see cref="SliderDriver"/> と Timeline トラックに駆動される側のコンポーネント。
    /// [DrivenProperty] を付けた field は、シリアライズパスが DrivenTarget.ValuePath などとして生成される。
    /// </summary>
    public partial class DrivenTarget : MonoBehaviour
    {
        [SerializeField, DrivenProperty] float value;
        [SerializeField, DrivenProperty] Vector3 offset;
        [SerializeField] Color color = Color.white;

        public float Value
        {
            get => value;
            set => this.value = value;
        }

        public Vector3 Offset => offset;
        public Color Color => color;
    }
}

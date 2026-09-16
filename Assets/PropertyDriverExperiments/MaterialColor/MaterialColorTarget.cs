using UnityEngine;

namespace PropertyDriverExperiments
{
    /// <summary>
    /// Renderer の MaterialPropertyBlock で色を上書きするコンポーネント。
    /// 上書き前の block を控えておき、<see cref="ResetColor"/> でそれに戻す。
    /// MaterialPropertyBlock はシリアライズされないので、ここで扱う状態はシーンにも material にも残らない。
    /// </summary>
    [RequireComponent(typeof(Renderer))]
    public class MaterialColorTarget : MonoBehaviour
    {
        [SerializeField] string propertyName = "_BaseColor";

        Renderer cachedRenderer;
        int propertyId;
        MaterialPropertyBlock originalBlock;
        MaterialPropertyBlock workBlock;
        bool overriding;

        Renderer Renderer => cachedRenderer != null ? cachedRenderer : cachedRenderer = GetComponent<Renderer>();

        /// <summary>上書きしていないときの色。控えた block に無ければ sharedMaterial の値。</summary>
        public Color BaseColor
        {
            get
            {
                EnsureBlocks();
                if (overriding && originalBlock.HasColor(propertyId)) return originalBlock.GetColor(propertyId);
                if (!overriding)
                {
                    Renderer.GetPropertyBlock(workBlock);
                    if (workBlock.HasColor(propertyId)) return workBlock.GetColor(propertyId);
                }
                var material = Renderer.sharedMaterial;
                return material != null && material.HasColor(propertyId) ? material.GetColor(propertyId) : Color.white;
            }
        }

        /// <summary>現在上書き中かどうか。</summary>
        public bool IsOverriding => overriding;

        /// <summary>色を上書きする。最初の呼び出しで現在の block を控える。</summary>
        public void SetColor(Color color)
        {
            EnsureBlocks();
            if (!overriding)
            {
                Renderer.GetPropertyBlock(originalBlock);
                overriding = true;
            }

            // 控えた block を土台にして色だけ上書きする。他のプロパティは保持される
            workBlock.Clear();
            Renderer.GetPropertyBlock(workBlock);
            workBlock.SetColor(propertyId, color);
            Renderer.SetPropertyBlock(workBlock);
        }

        /// <summary>上書きをやめ、<see cref="SetColor"/> 前の block に戻す。</summary>
        public void ResetColor()
        {
            if (!overriding) return;
            Renderer.SetPropertyBlock(originalBlock);
            overriding = false;
        }

        void EnsureBlocks()
        {
            if (originalBlock != null) return;
            originalBlock = new MaterialPropertyBlock();
            workBlock = new MaterialPropertyBlock();
            propertyId = Shader.PropertyToID(propertyName);
        }
    }
}

using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace PropertyDriverExperiments.Timeline
{
    /// <summary>
    /// mixer が書くシリアライズプロパティを Timeline に申告するトラック基底。
    /// 申告したプロパティは Timeline ウィンドウのプレビュー中だけ driven として登録され、終了時に元の値へ戻る。
    /// 派生トラックは書くパスを必ず列挙する。パスには [DrivenProperty] から生成された定数を使うこと。
    /// </summary>
    public abstract class DrivenTrack<TBinding> : TrackAsset where TBinding : Component
    {
        /// <summary>このトラックの mixer が書く、binding のシリアライズプロパティパス。</summary>
        protected abstract string[] DrivenPropertyPaths { get; }

        public override void GatherProperties(PlayableDirector director, IPropertyCollector driver)
        {
            if (director.GetGenericBinding(this) is not TBinding binding) return;

            foreach (var path in DrivenPropertyPaths)
                driver.AddFromName(binding, path);
        }
    }
}

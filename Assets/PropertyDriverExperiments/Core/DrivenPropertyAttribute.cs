using System;

namespace PropertyDriverExperiments
{
    /// <summary>
    /// Unity がシリアライズする field を「駆動可能」として印付ける。
    /// PropertyDriver.Generator がこの field を含む partial 型に、シリアライズ上のプロパティパスを持つ
    /// <c>const string {Name}Path</c> と、それらを列挙した <c>static readonly string[] DrivenPropertyPaths</c> を生成する。
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class DrivenPropertyAttribute : Attribute
    {
    }
}

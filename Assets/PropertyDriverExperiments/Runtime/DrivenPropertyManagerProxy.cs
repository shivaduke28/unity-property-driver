using System;
using System.Reflection;
using Object = UnityEngine.Object;

namespace PropertyDriverExperiments
{
    /// <summary>
    /// internal な <c>UnityEngine.DrivenPropertyManager</c> の reflection ラッパー。
    /// 登録したプロパティは「driver に駆動されている」扱いになり、登録時点の値がネイティブ側に控えられる。
    /// 登録中はシーン保存時にその控えた値が書き出され、解除時にはその値へ戻る。Inspector では青背景で表示される。
    /// </summary>
    public static class DrivenPropertyManagerProxy
    {
        static readonly Type ManagerType =
            typeof(Object).Assembly.GetType("UnityEngine.DrivenPropertyManager", throwOnError: true);

        static readonly Action<Object, Object, string> RegisterPropertyImpl = Bind<Action<Object, Object, string>>("RegisterProperty");
        static readonly Action<Object, Object, string> TryRegisterPropertyImpl = Bind<Action<Object, Object, string>>("TryRegisterProperty");
        static readonly Action<Object, Object, string> UnregisterPropertyImpl = Bind<Action<Object, Object, string>>("UnregisterProperty");
        static readonly Action<Object> UnregisterPropertiesImpl = Bind<Action<Object>>("UnregisterProperties");

        /// <summary><paramref name="target"/> の <paramref name="propertyPath"/> を <paramref name="driver"/> が駆動していると登録する。存在しないパスはエラーログを出して無視される。</summary>
        public static void RegisterProperty(Object driver, Object target, string propertyPath) => RegisterPropertyImpl(driver, target, propertyPath);

        /// <summary><see cref="RegisterProperty"/> の Try 版。Unity 内部の実装差は未検証。</summary>
        public static void TryRegisterProperty(Object driver, Object target, string propertyPath) => TryRegisterPropertyImpl(driver, target, propertyPath);

        public static void UnregisterProperty(Object driver, Object target, string propertyPath) => UnregisterPropertyImpl(driver, target, propertyPath);

        /// <summary><paramref name="driver"/> が登録した全プロパティを解除する。値は登録時点のものに戻る。</summary>
        public static void UnregisterProperties(Object driver) => UnregisterPropertiesImpl(driver);

        static T Bind<T>(string name) where T : Delegate
        {
            var method = ManagerType.GetMethod(name, BindingFlags.Public | BindingFlags.Static);
            if (method == null) throw new MissingMethodException(ManagerType.FullName, name);
            return (T)Delegate.CreateDelegate(typeof(T), method);
        }
    }
}

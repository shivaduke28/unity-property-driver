using System;
using System.Reflection;
using UnityEditor;
using Object = UnityEngine.Object;

namespace PropertyDriverExperiments.Editor
{
    /// <summary>internal な <c>UnityEditor.DrivenPropertyManagerInternal</c> の問い合わせ API の reflection ラッパー。</summary>
    public static class DrivenPropertyEditorProxy
    {
        static readonly Type InternalType =
            typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.DrivenPropertyManagerInternal", throwOnError: true);

        static readonly Func<Object, string, bool> IsDrivenImpl = Bind<Func<Object, string, bool>>("IsDriven");
        static readonly Func<Object, Object, string, bool> IsDrivingImpl = Bind<Func<Object, Object, string, bool>>("IsDriving");

        /// <summary><paramref name="target"/> の <paramref name="propertyPath"/> を誰かが駆動中なら true。複合型は末端パス（例: m_LocalPosition.x）で問い合わせること。</summary>
        public static bool IsDriven(Object target, string propertyPath) => target != null && IsDrivenImpl(target, propertyPath);

        /// <summary><paramref name="driver"/> が <paramref name="target"/> の <paramref name="propertyPath"/> を駆動中なら true。</summary>
        public static bool IsDriving(Object driver, Object target, string propertyPath) =>
            driver != null && target != null && IsDrivingImpl(driver, target, propertyPath);

        static T Bind<T>(string name) where T : Delegate
        {
            var method = InternalType.GetMethod(name, BindingFlags.Public | BindingFlags.Static);
            if (method == null) throw new MissingMethodException(InternalType.FullName, name);
            return (T)Delegate.CreateDelegate(typeof(T), method);
        }
    }
}

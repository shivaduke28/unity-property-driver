using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PropertyDriverExperiments.Editor
{
    /// <summary>
    /// <see cref="SliderDriver"/> のインスペクタ。
    /// Register / Unregister は明示的なボタン。登録中はスライダー操作で即座に target へ書き込み、
    /// 未登録ならスライダーはアセットの値を変えるだけ。登録状態・値の同期・dirty を常時表示する。
    /// </summary>
    [CustomEditor(typeof(SliderDriver))]
    public class SliderDriverEditor : UnityEditor.Editor
    {
        SerializedObject settingsObject;

        SliderDriver Driver => (SliderDriver)target;

        void OnDisable()
        {
            settingsObject?.Dispose();
            settingsObject = null;
        }

        public override void OnInspectorGUI()
        {
            var driver = Driver;

            EditorGUILayout.LabelField("Driver (scene data)", EditorStyles.boldLabel);
            DrawDefaultInspector();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Slider (asset data)", EditorStyles.boldLabel);
            DrawSlider(driver);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Registration", EditorStyles.boldLabel);
            DrawRegistration(driver);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Values", EditorStyles.boldLabel);
            DrawValues(driver);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Dirty", EditorStyles.boldLabel);
            DrawDirty(driver);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);
            DrawActions(driver);
        }

        // ---- スライダー ----------------------------------------------------------

        void DrawSlider(SliderDriver driver)
        {
            if (driver.settings == null)
            {
                EditorGUILayout.HelpBox("Assign a SliderDriverSettings asset.", MessageType.Info);
                return;
            }

            if (settingsObject == null || settingsObject.targetObject != driver.settings)
            {
                settingsObject?.Dispose();
                settingsObject = new SerializedObject(driver.settings);
            }

            settingsObject.Update();
            EditorGUILayout.Slider(settingsObject.FindProperty(nameof(SliderDriverSettings.t)), 0f, 1f, new GUIContent("t"));
            var changed = settingsObject.ApplyModifiedProperties();

            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.FloatField("Driven value = lerp(min, max, t)", driver.DrivenValue);

            if (changed && IsRegistered(driver))
                Apply(driver);
        }

        static bool IsRegistered(SliderDriver driver) =>
            driver.target != null &&
            DrivenPropertyEditorProxy.IsDriving(driver, driver.target, DrivenTarget.ValuePath);

        // ---- 登録状態 ------------------------------------------------------------

        static void DrawRegistration(SliderDriver driver)
        {
            if (driver.target == null)
            {
                EditorGUILayout.HelpBox("No target assigned.", MessageType.Info);
                return;
            }

            DrawRegistrationRow("target." + DrivenTarget.ValuePath, driver, driver.target, DrivenTarget.ValuePath, wanted: true);
            DrawRegistrationRow("target.transform.m_LocalPosition.x", driver, driver.target.transform, SliderDriver.LocalPositionPropertyPath + ".x", wanted: driver.driveTransformPosition);
        }

        static void DrawRegistrationRow(string label, Object driver, Object target, string propertyPath, bool wanted)
        {
            var driving = DrivenPropertyEditorProxy.IsDriving(driver, target, propertyPath);
            var driven = DrivenPropertyEditorProxy.IsDriven(target, propertyPath);

            string state;
            if (driving) state = wanted ? "driven by this" : "driven by this (not wanted: press Unregister)";
            else if (driven) state = "driven by OTHER";
            else state = wanted ? "free (press Register)" : "free";

            EditorGUILayout.LabelField(label, state);
        }

        // ---- 値 ------------------------------------------------------------------

        static void DrawValues(SliderDriver driver)
        {
            if (driver.target == null) return;

            var driven = driver.DrivenValue;
            DrawValueRow("target." + DrivenTarget.ValuePath, driver.target.Value, driven);
            if (driver.driveTransformPosition)
                DrawValueRow("target.transform.localPosition.x", driver.target.transform.localPosition.x, driven);
        }

        static void DrawValueRow(string label, float current, float expected)
        {
            var sync = Mathf.Approximately(current, expected) ? "in sync" : "stale (not registered when slider moved)";
            EditorGUILayout.LabelField(label, $"{current} ({sync})");
        }

        // ---- dirty ---------------------------------------------------------------

        static void DrawDirty(SliderDriver driver)
        {
            var scene = driver.gameObject.scene;
            EditorGUILayout.LabelField("scene", scene.IsValid() ? scene.isDirty.ToString() : "n/a");
            EditorGUILayout.LabelField("driver", EditorUtility.IsDirty(driver).ToString());
            if (driver.target != null)
            {
                EditorGUILayout.LabelField("target", EditorUtility.IsDirty(driver.target).ToString());
                EditorGUILayout.LabelField("target.transform", EditorUtility.IsDirty(driver.target.transform).ToString());
            }
            EditorGUILayout.LabelField("settings asset", driver.settings != null ? EditorUtility.IsDirty(driver.settings).ToString() : "n/a");
        }

        // ---- 操作 ----------------------------------------------------------------

        static void DrawActions(SliderDriver driver)
        {
            using (new EditorGUI.DisabledScope(driver.target == null))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("Register", "DrivenPropertyManager.RegisterProperty for each wanted property, then write the current driven value."))) { Register(driver); Apply(driver); }
                if (GUILayout.Button(new GUIContent("Unregister", "DrivenPropertyManager.UnregisterProperties(driver). Target values revert to the values at registration."))) Unregister(driver);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("Save scene", "EditorSceneManager.SaveScene(driver.gameObject.scene)"))) EditorSceneManager.SaveScene(driver.gameObject.scene);
                if (GUILayout.Button(new GUIContent("Save assets", "AssetDatabase.SaveAssets()"))) AssetDatabase.SaveAssets();
            }
        }

        public static void Register(SliderDriver driver)
        {
            if (driver.target == null) return;

            DrivenPropertyManagerProxy.RegisterProperty(driver, driver.target, DrivenTarget.ValuePath);
            if (driver.driveTransformPosition)
                DrivenPropertyManagerProxy.RegisterProperty(driver, driver.target.transform, SliderDriver.LocalPositionPropertyPath);
        }

        public static void Unregister(SliderDriver driver)
        {
            DrivenPropertyManagerProxy.UnregisterProperties(driver);
        }

        /// <summary>DrivenValue を target に素のセッターで書き込む。登録中に呼ぶこと。</summary>
        static void Apply(SliderDriver driver)
        {
            if (driver.target == null) return;

            var v = driver.DrivenValue;
            driver.target.Value = v;
            if (driver.driveTransformPosition)
                driver.target.transform.localPosition = new Vector3(v, 0f, 0f);
        }
    }
}

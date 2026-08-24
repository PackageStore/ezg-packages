#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace EZG.TechnicalArt.VisualRoadBuilder
{
    /// <summary>
    /// Inspector cho <see cref="RoadPartLibrary"/>: gom field theo 5 tab (Road / Highway / Building / Road2 / Path).
    /// Chỉ tiêu đề khối — mô tả chi tiết nằm ở [Tooltip] của từng field.
    /// </summary>
    [CustomEditor(typeof(RoadPartLibrary))]
    public sealed class RoadPartLibraryEditor : UnityEditor.Editor
    {
        private const string TabKey = "RoadPartLibrary.tab";
        private static readonly string[] Tabs = { "Road", "Highway", "Building", "Road2", "Path" };

        private GUIStyle _titleStyle;

        private void OnEnable() => MigrateLegacyPath();

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            Prop("roadPlanAtlas");
            EditorGUILayout.Space(4f);

            var tab = Mathf.Clamp(SessionState.GetInt(TabKey, 0), 0, Tabs.Length - 1);
            var next = GUILayout.Toolbar(tab, Tabs, GUILayout.Height(24f));
            if (next != tab) SessionState.SetInt(TabKey, next);
            EditorGUILayout.Space(6f);

            switch (next)
            {
                case 0: DrawRoad(); break;
                case 1: DrawHighway(); break;
                case 2: DrawBuilding(); break;
                case 3: DrawRoad2(); break;
                case 4: DrawPath(); break;
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawRoad()
        {
            Section("Ô modular 0.5x0.5");
            Prop("road1x1_side");
            Prop("road1x1_side_rim");

            Section("Mảnh giao");
            Prop("road1x1_curve");
            Prop("road1x1_curve_rim");
            Prop("road1x1_center");

            Section("Mảnh cua");
            Prop("road2x2_turn");
            Prop("road2x2_turn_rim");

            Section("Cua nhỏ (2 mảnh giao lệch 1.5 ô)");
            Prop("road1x1_turn");
            Prop("road1x1_turn_rim");

            Section("Mảnh nối");
            Prop("hway_to_road");
        }

        private void DrawHighway()
        {
            Section("Ô modular cao tốc 0.5x1");
            Prop("hway1x2_side");
            Prop("hway1x2_side_rim");
        }

        private void DrawBuilding()
        {
            Section("Station (4x4 ô)");
            Prop("stationPrefab");

            Section("Parking slot (4x2 ô)");
            Prop("parkingPrefab");
        }

        private void DrawRoad2()
        {
            Section("Mặt cắt rộng x1.5");
            Prop("road2_center_filler");

            Section("Bo góc / giao");
            Prop("road2_curve");
            Prop("road2_curve_rim");

            Section("Cua lớn 3x3");
            Prop("road3x3_turn");
            Prop("road3x3_turn_rim");

            Section("Mảnh nối");
            Prop("hway_to_road2");
        }

        private void DrawPath()
        {
            Section("Path (không rim, bốc biến thể theo trọng số)");
            VariantList("path_side_variants", "Side");
            VariantList("path_center_variants", "Center");
            VariantList("path_curve_variants", "Curve");
            VariantList("path_turn_variants", "Turn");
        }

        /// <summary>Danh sách biến thể của 1 slot path: mỗi dòng = prefab + slider trọng số + nút xoá.
        /// Mọi thao tác đều đi qua <see cref="SetWeight"/> / <see cref="Normalize"/> nên tổng LUÔN = 1.
        /// Chỉ hiện tên slot; mô tả nằm ở [Tooltip] của field, hover mới hiện (giống tab Road).</summary>
        private void VariantList(string propertyPath, string title)
        {
            var list = serializedObject.FindProperty(propertyPath);
            if (list == null) return;

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(new GUIContent(title, list.tooltip), EditorStyles.miniBoldLabel);

            if (list.arraySize > 0 && Mathf.Abs(WeightSum(list) - 1f) > 0.001f) Normalize(list);

            int removeAt = -1;
            for (int i = 0; i < list.arraySize; i++)
            {
                var element = list.GetArrayElementAtIndex(i);
                var prefab = element.FindPropertyRelative("prefab");
                var weight = element.FindPropertyRelative("weight");

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(prefab, GUIContent.none);

                    EditorGUI.BeginChangeCheck();
                    float next = EditorGUILayout.Slider(weight.floatValue, 0f, 1f, GUILayout.MaxWidth(180f));
                    if (EditorGUI.EndChangeCheck()) SetWeight(list, i, next);

                    if (GUILayout.Button("−", GUILayout.Width(22f))) removeAt = i;
                }
            }

            if (removeAt >= 0)
            {
                list.DeleteArrayElementAtIndex(removeAt);
                Normalize(list);
            }

            if (GUILayout.Button("+ Thêm biến thể", GUILayout.Height(18f)))
            {
                int added = list.arraySize;
                list.InsertArrayElementAtIndex(added);
                var element = list.GetArrayElementAtIndex(added);
                element.FindPropertyRelative("prefab").objectReferenceValue = null;
                element.FindPropertyRelative("weight").floatValue = 0f;
                SetWeight(list, added, 1f / list.arraySize);
            }
        }

        /// <summary>Đặt trọng số cho 1 biến thể rồi chia phần còn lại (1 − value) cho các biến thể khác
        /// THEO TỈ LỆ hiện có — các slider khác tự tụt/lên tương ứng, tổng vẫn = 1.</summary>
        private static void SetWeight(SerializedProperty list, int index, float value)
        {
            int n = list.arraySize;
            if (n <= 0) return;
            value = Mathf.Clamp01(value);
            if (n == 1) { Weight(list, 0).floatValue = 1f; return; }

            float others = 0f;
            for (int i = 0; i < n; i++)
                if (i != index) others += Mathf.Max(0f, Weight(list, i).floatValue);

            float rest = 1f - value;
            for (int i = 0; i < n; i++)
            {
                if (i == index) continue;
                var w = Weight(list, i);
                w.floatValue = others > 0.0001f
                    ? Mathf.Max(0f, w.floatValue) * rest / others
                    : rest / (n - 1);
            }
            Weight(list, index).floatValue = value;
        }

        private static void Normalize(SerializedProperty list)
        {
            int n = list.arraySize;
            if (n <= 0) return;
            float sum = WeightSum(list);
            for (int i = 0; i < n; i++)
            {
                var w = Weight(list, i);
                w.floatValue = sum > 0.0001f ? Mathf.Max(0f, w.floatValue) / sum : 1f / n;
            }
        }

        private static float WeightSum(SerializedProperty list)
        {
            float sum = 0f;
            for (int i = 0; i < list.arraySize; i++) sum += Mathf.Max(0f, Weight(list, i).floatValue);
            return sum;
        }

        private static SerializedProperty Weight(SerializedProperty list, int index) =>
            list.GetArrayElementAtIndex(index).FindPropertyRelative("weight");

        /// <summary>Chuyển 4 field 1-prefab đời cũ (path_side/center/curve/turn) vào list biến thể
        /// tương ứng rồi set null — chạy 1 lần khi mở inspector, giữ nguyên prefab đã gán trong asset.</summary>
        private void MigrateLegacyPath()
        {
            serializedObject.Update();
            bool moved = false;
            moved |= MoveLegacy("path_side", "path_side_variants");
            moved |= MoveLegacy("path_center", "path_center_variants");
            moved |= MoveLegacy("path_curve", "path_curve_variants");
            moved |= MoveLegacy("path_turn", "path_turn_variants");
            if (moved) serializedObject.ApplyModifiedProperties();
        }

        private bool MoveLegacy(string legacyPath, string listPath)
        {
            var legacy = serializedObject.FindProperty(legacyPath);
            var list = serializedObject.FindProperty(listPath);
            if (legacy == null || list == null || legacy.objectReferenceValue == null) return false;

            bool present = false;
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).FindPropertyRelative("prefab").objectReferenceValue
                    == legacy.objectReferenceValue) present = true;

            if (!present)
            {
                int added = list.arraySize;
                list.InsertArrayElementAtIndex(added);
                var element = list.GetArrayElementAtIndex(added);
                element.FindPropertyRelative("prefab").objectReferenceValue = legacy.objectReferenceValue;
                element.FindPropertyRelative("weight").floatValue = 0f;
                SetWeight(list, added, 1f / list.arraySize);
            }

            legacy.objectReferenceValue = null;
            return true;
        }

        private void Section(string title)
        {
            _titleStyle ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField(title, _titleStyle);
            EditorGUILayout.Space(2f);
        }

        private void Prop(string propertyPath)
        {
            var prop = serializedObject.FindProperty(propertyPath);
            if (prop != null) EditorGUILayout.PropertyField(prop);
        }
    }
}
#endif

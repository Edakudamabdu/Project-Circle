using System.Collections.Generic;
using System.IO;
using ProjectCircle.Systems;
using UnityEditor;
using UnityEngine;

namespace ProjectCircle.EditorTools
{
    /// <summary>
    /// Inspector for <see cref="SceneDoor"/>: picks the target scene from the build list (no typing scene names),
    /// warns when it isn't in the build, and has a button that checks every link in the game.
    /// </summary>
    [CustomEditor(typeof(SceneDoor))]
    [CanEditMultipleObjects]
    public sealed class SceneDoorEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            SerializedProperty target = serializedObject.FindProperty("targetScene");

            DrawPropertiesExcluding(serializedObject, "m_Script", "targetScene");

            // Target scene as a dropdown of the build's scenes.
            var names = new List<string> { "(none)" };
            foreach (var s in EditorBuildSettings.scenes) if (s.enabled) names.Add(Path.GetFileNameWithoutExtension(s.path));
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Leads To (scene)", EditorStyles.boldLabel);
            if (!target.hasMultipleDifferentValues)
            {
                int index = string.IsNullOrEmpty(target.stringValue) ? 0 : names.IndexOf(target.stringValue);
                if (index < 0)
                {
                    EditorGUILayout.HelpBox($"Target scene '{target.stringValue}' isn't in the build's scene list.", MessageType.Error);
                    EditorGUILayout.PropertyField(target, new GUIContent("Target Scene (typed)"));
                }
                else
                {
                    int picked = EditorGUILayout.Popup("Target Scene", index, names.ToArray());
                    if (picked != index) target.stringValue = picked == 0 ? "" : names[picked];
                }
            }
            else EditorGUILayout.PropertyField(target);

            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space(6);
            EditorGUILayout.HelpBox("Green circle: where the circle arrives. Yellow line: camera stop (room-edge openings). " +
                                    "After moving rooms or doors around, check every link:", MessageType.Info);
            if (GUILayout.Button("Check All Scene Links")) SceneLinkChecker.CheckMenu();
        }
    }
}

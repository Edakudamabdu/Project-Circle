using UnityEditor;
using UnityEngine;

namespace ProjectCircle.Player.EditorTools
{
    /// <summary>
    /// Custom Inspector for <see cref="PlayerMovement"/> that draws the assigned
    /// <see cref="PlayerMovementSettings"/> asset inline, so jump height, speeds, dash, etc.
    /// can be tuned directly on the Player object. Edits are written to the asset, so they
    /// survive leaving Play Mode.
    /// Lives in an Editor folder, so it is stripped from builds.
    /// </summary>
    [CustomEditor(typeof(PlayerMovement))]
    public sealed class PlayerMovementEditor : Editor
    {
        private Editor settingsEditor;
        private bool showSettings = true;

        public override void OnInspectorGUI()
        {
            // Live read-out of the current state while the game is running.
            if (Application.isPlaying)
            {
                var movement = (PlayerMovement)target;
                EditorGUILayout.LabelField("Current State", movement.State.ToString(), EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Grounded", movement.IsGrounded ? "Yes" : "No");
                EditorGUILayout.Space();
            }

            DrawDefaultInspector();

            SerializedProperty settingsProp = serializedObject.FindProperty("settings");
            Object settings = settingsProp != null ? settingsProp.objectReferenceValue : null;
            if (settings == null)
            {
                EditorGUILayout.HelpBox("Assign a Player Movement Settings asset to tune movement.", MessageType.Warning);
                return;
            }

            EditorGUILayout.Space();
            showSettings = EditorGUILayout.Foldout(showSettings, "Movement Tuning (" + settings.name + ")", true, EditorStyles.foldoutHeader);
            if (!showSettings)
            {
                return;
            }

            // Re-uses the cached editor unless the assigned asset changed.
            CreateCachedEditor(settings, null, ref settingsEditor);

            EditorGUILayout.HelpBox("These values live in the settings asset. Changes made in Play Mode are kept.", MessageType.None);
            using (new EditorGUI.IndentLevelScope())
            {
                settingsEditor.OnInspectorGUI();
            }
        }

        /// <summary>Keeps the state read-out updating every frame in Play Mode.</summary>
        public override bool RequiresConstantRepaint() => Application.isPlaying;

        private void OnDisable()
        {
            if (settingsEditor != null)
            {
                DestroyImmediate(settingsEditor);
            }
        }
    }
}

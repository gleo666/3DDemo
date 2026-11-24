using UnityEditor;
using UnityEngine;

namespace DialogueSystem.EditorTools
{
    [CustomPropertyDrawer(typeof(EventVariable))]
    public class EventVariableDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            float line = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;

            Rect headerRect = new Rect(position.x, position.y, position.width, line);
            EditorGUI.LabelField(headerRect, label, EditorStyles.boldLabel);

            SerializedProperty nameProperty = property.FindPropertyRelative("Name");
            SerializedProperty typeProperty = property.FindPropertyRelative("Type");
            SerializedProperty boolProperty = property.FindPropertyRelative("BoolValue");
            SerializedProperty numberProperty = property.FindPropertyRelative("NumberValue");

            Rect nameRect = new Rect(position.x, position.y + line + spacing, position.width, line);
            EditorGUI.PropertyField(nameRect, nameProperty, new GUIContent("Name"));

            float secondRowY = position.y + (line + spacing) * 2f;
            float typeWidth = position.width * 0.45f;

            Rect typeRect = new Rect(position.x, secondRowY, typeWidth, line);
            EditorGUI.PropertyField(typeRect, typeProperty, new GUIContent("Type"));

            Rect valueRect = new Rect(position.x + typeWidth + 4f, secondRowY,
                position.width - typeWidth - 4f, line);

            if ((EventVariableType)typeProperty.enumValueIndex == EventVariableType.Bool)
                EditorGUI.PropertyField(valueRect, boolProperty, new GUIContent("Value"));
            else
                EditorGUI.PropertyField(valueRect, numberProperty, new GUIContent("Value"));

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing) * 3f;
        }
    }
}

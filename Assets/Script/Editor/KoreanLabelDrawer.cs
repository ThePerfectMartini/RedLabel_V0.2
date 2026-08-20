using UnityEditor;
using UnityEngine;

/// <summary>
/// KoreanLabelAttribute가 붙은 필드를 그릴 때 라벨만 지정된 한글 문자열로 바꿔서 그린다.
/// Tooltip 어트리뷰트가 같이 붙어있으면 그 툴팁도 그대로 유지한다.
/// </summary>
[CustomPropertyDrawer(typeof(KoreanLabelAttribute))]
public class KoreanLabelDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        var koreanLabel = (KoreanLabelAttribute)attribute;
        GUIContent content = new GUIContent(koreanLabel.Label, property.tooltip);
        EditorGUI.PropertyField(position, property, content, true);
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        return EditorGUI.GetPropertyHeight(property, label, true);
    }
}

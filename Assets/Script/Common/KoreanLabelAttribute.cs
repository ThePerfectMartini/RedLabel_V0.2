using UnityEngine;

/// <summary>
/// 인스펙터에 표시될 라벨을 한글로 지정하는 어트리뷰트. 실제 필드/변수명에는 영향 없음.
/// 드로어(KoreanLabelDrawer)는 Editor 폴더에 있어서, 이 어트리뷰트 자체는 런타임 코드에서도 안전하게 참조 가능.
/// </summary>
public class KoreanLabelAttribute : PropertyAttribute
{
    public readonly string Label;

    public KoreanLabelAttribute(string label)
    {
        Label = label;
    }
}

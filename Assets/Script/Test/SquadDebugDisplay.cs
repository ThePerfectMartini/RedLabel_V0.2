using UnityEngine;

/// <summary>
/// TEMP: 스쿼드 상태(멤버 수 · 현재 공격 인원)를 화면 좌상단에 표시.
/// 아무 오브젝트에나 붙이면 된다 — SquadController.Instance만 읽는다. 제거는 파일 삭제 + 컴포넌트 제거.
/// </summary>
public class SquadDebugDisplay : MonoBehaviour
{
    void OnGUI()
    {
        SquadController s = SquadController.Instance;
        if (s == null) return;

        GUI.Label(new Rect(10f, 10f, 400f, 20f),
            $"Squad — 멤버 {s.MemberCount}명 · 공격 {s.AttackerCount}/{s.maxSimultaneousAttackers}");
    }
}

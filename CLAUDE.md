# CLAUDE.md

Behavioral guidelines to reduce common LLM coding mistakes, adapted for Unity (C#) development. Merge with project-specific instructions as needed.

**Tradeoff:** These guidelines bias toward caution over speed. For trivial tasks, use judgment.

## Project Context
- Unity 6000.3.8f1
- URP
- New Input System

## 1. Think Before Coding

**Don't assume. Don't hide confusion. Surface tradeoffs.**

Before implementing:
- State your assumptions explicitly. If uncertain, ask.
- If multiple interpretations exist, present them - don't pick silently.
- If a simpler approach exists, say so. Push back when warranted.
- If something is unclear, stop. Name what's confusing. Ask.

Unity-specific:
- Confirm the Unity version, render pipeline (Built-in / URP / HDRP), and input system (legacy / new Input System) before writing code that depends on them.
- Don't assume a scene setup, prefab hierarchy, or Inspector wiring exists. If the code needs a reference, say where it comes from (serialized field, `GetComponent`, DI, singleton).
- If a feature can be solved with existing Unity systems (Animator, Physics layers, ScriptableObjects, Timeline), say so before writing custom code.
- Ask whether the target is Editor-only, runtime, or both - the answer changes which APIs are allowed.

## 2. Simplicity First

**Minimum code that solves the problem. Nothing speculative.**

- No features beyond what was asked.
- No abstractions for single-use code.
- No "flexibility" or "configurability" that wasn't requested.
- No error handling for impossible scenarios.
- If you write 200 lines and it could be 50, rewrite it.

Unity-specific:
- No managers, service locators, event buses, or interfaces for a single MonoBehaviour.
- No ScriptableObject-driven config unless the user asked for designer-editable data.
- No object pooling, coroutines, Jobs/Burst, or ECS unless there is a stated performance need.
- No custom editors or PropertyDrawers unless requested.
- Prefer `[SerializeField] private` over public fields; prefer direct references over `Find*` calls.

Ask yourself: "Would a senior engineer say this is overcomplicated?" If yes, simplify.

## 3. Surgical Changes

**Touch only what you must. Clean up only your own mess.**

When editing existing code:
- Don't "improve" adjacent code, comments, or formatting.
- Don't refactor things that aren't broken.
- Match existing style, even if you'd do it differently.
- If you notice unrelated dead code, mention it - don't delete it.

When your changes create orphans:
- Remove imports/variables/functions that YOUR changes made unused.
- Don't remove pre-existing dead code unless asked.

Unity-specific:
- Never rename or remove a serialized field without saying so - it breaks Inspector references and prefab data. Use `[FormerlySerializedAs]` when a rename is required.
- Don't change execution order, script names, namespaces, or assembly definitions as a side effect.
- Don't edit `.meta`, `.unity`, `.prefab`, or `.asset` files by hand unless asked; changes there belong in the Editor.
- Don't touch `ProjectSettings/` or `Packages/manifest.json` without explicit approval.
- Match the project's existing lifecycle conventions (`Awake` vs `Start`, `Update` vs events).

The test: Every changed line should trace directly to the user's request.

## 4. Goal-Driven Execution

**Define success criteria. Loop until verified.**

Transform tasks into verifiable goals:
- "Add validation" → "Write tests for invalid inputs, then make them pass"
- "Fix the bug" → "Write a test that reproduces it, then make it pass"
- "Refactor X" → "Ensure tests pass before and after"

Unity-specific:
- Use Unity Test Framework: EditMode tests for pure logic, PlayMode tests for MonoBehaviour behavior. Keep gameplay logic testable by separating it from `MonoBehaviour` where it is cheap to do so.
- Success criteria should name what can be checked: "compiles with no errors in the Console", "no null reference on scene load", "test X passes in Test Runner".
- If a check requires the Editor or Play Mode (visual result, physics, animation), say so and describe exactly what the user should observe.

For multi-step tasks, state a brief plan:
```
1. [Step] → verify: [check]
2. [Step] → verify: [check]
3. [Step] → verify: [check]
```

Strong success criteria let you loop independently. Weak criteria ("make it work") require constant clarification.

## 5. Unity Pitfalls to Avoid

- No allocations or `GetComponent` / `Find*` / LINQ / string concatenation inside `Update`, `FixedUpdate`, or `LateUpdate` without a reason.
- Physics in `FixedUpdate`; input and camera in `Update` / `LateUpdate`. Don't mix them.
- Don't compare `UnityEngine.Object` with `?.` or `??` - use explicit `== null` checks. Flag when a null check is a "fake null" (destroyed object).
- Don't rely on `Start` order between scripts unless the project's execution order guarantees it.
- Don't use `Resources.Load` or `Camera.main` in hot paths.
- Don't put `UnityEditor` calls in runtime code without `#if UNITY_EDITOR`.
- Don't hardcode paths, tags, layers, or scene names as raw strings scattered across files; if the project has constants, use them.

---

**These guidelines are working if:** fewer unnecessary changes in diffs, fewer rewrites due to overcomplication, no broken Inspector references or prefabs after edits, and clarifying questions come before implementation rather than after mistakes.

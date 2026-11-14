# Project Rules & Context: 2D Hollow Knight-Inspired Platformer

- **Engine:** Unity 6 (2D URP).
- **Genre:** 2D Metroidvania / Action-Platformer inspired by *Hollow Knight* (tight controls, responsive combat, exploration).
- **Protagonist:** The main character is a circle (not a ball) driven by 2D Rigidbody2D physics. Squash & stretch is a subtle, fun touch only — keep it understated. The dash is a quickstep (a short, snappy slide with no rolling).
- **Target Platform:** PC (Keyboard/Mouse and Gamepad support).
- **Input System:** Exclusively use Unity's **New Input System** (`UnityEngine.InputSystem`). Never use legacy `Input.GetAxis` or `Input.GetKey`.
- **Art & UI:** Use simple placeholder primitives/shapes and standard uGUI Canvas placeholders (TextMeshPro, basic Image/Slider bars). **Do not generate images, sprites, or SVG assets.**
- **Developer Profile:** Experienced game developer, beginner specifically to Unity's Editor and APIs. Skip basic programming theory, but explicitly state Unity-specific Editor steps (Components, Inspector settings, Rigidbody2D constraints, Layers).
- **Coding Standards:** Write clean, production-grade C# readable by an intermediate developer with clear XML summaries (`/// <summary>`) and inline comments. Use Unity 6 APIs (`Rigidbody2D.linearVelocity`, `FindAnyObjectByType<T>()` — `FindFirstObjectByType<T>()` is obsolete in Unity 6.5). Keep scripts modular (`[SerializeField] private`, cache components in `Awake()`, physics in `FixedUpdate()`, C# events for decoupled UI).
- **Folder Structure:** Keep all game assets under `Assets/_Project/` (`Scripts/Player`, `Scripts/Combat`, `Scripts/Enemies`, `Scripts/Systems`, `Scripts/UI`, `Prefabs`, `ScriptableObjects`, `Settings`, `Scenes`).

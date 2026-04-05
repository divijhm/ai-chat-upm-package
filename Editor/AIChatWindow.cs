using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// AI Chat Window - Editor plugin stub for AI-driven scene control.
/// Provides a chat interface, mode selector, and a scene-switcher button.
/// </summary>
public partial class AIChatWindow : EditorWindow
{
    private const string ApiKeyPrefKey = "AIChatWindow.ApiKey";
    private const bool DebugPasteLogs = true;

    // -- Chat mode -----------------------------------------------------------
    private enum ChatMode { AssetGeneration, Agent, Selection }

    private static readonly string[] ChatModeLabels =
    {
        "Asset Generation",
        "Agent",
        "Selection"
    };

    private ChatMode currentMode = ChatMode.Agent;

    // -- Asset-generation model tiers ----------------------------------------
    // Format: (display label, internal id, usage multiplier label)
    private static readonly (string Label, string Id, string Tier)[] AssetModels =
    {
        // -- Budget  (x0.33) -------------------------------------------------
        ("Stable Diffusion 1.5   ×0.33",  "sd15",         "×0.33"),
        ("Shap-E                 ×0.33",  "shape",        "×0.33"),
        ("Point-E                ×0.33",  "pointe",       "×0.33"),
        // -- Standard  (x1) --------------------------------------------------
        ("TripoSR                ×1",     "triposr",      "×1"),
        ("Zero123++              ×1",     "zero123pp",    "×1"),
        ("DreamGaussian          ×1",     "dreamgaussian","×1"),
        // -- Premium  (x3) ---------------------------------------------------
        ("InstantMesh            ×3",     "instantmesh",  "×3"),
        ("CraftsMan              ×3",     "craftsman",    "×3"),
        ("Wonder3D               ×3",     "wonder3d",     "×3"),
    };

    private static readonly string[] AssetModelLabels;

    // Build the label array once from AssetModels
    static AIChatWindow()
    {
        AssetModelLabels = new string[AssetModels.Length];
        for (int i = 0; i < AssetModels.Length; i++)
            AssetModelLabels[i] = AssetModels[i].Label;
    }

    private int selectedModelIndex = 0;

    // -- Chat state ----------------------------------------------------------
    private string inputText = "";
    private Vector2 scrollPos;
    private readonly List<ChatMessage> messages = new List<ChatMessage>();
    private readonly List<ImageAttachment> pendingAttachments = new List<ImageAttachment>();

    // -- Scene switching -----------------------------------------------------
    private const string SCENE_A = "Assets/Scenes/SampleScene.unity";
    private const string SCENE_B = "Assets/Scenes/DemoScene.unity";
    private bool isOnSceneA = true;

    // -- Asset generation loading state --------------------------------------
    private static readonly string[] WeaponPrefabPaths =
    {
        "Assets/Stylized Modular Weapons/Prefabs/Your Weapon Example 1.prefab",
        "Assets/Stylized Modular Weapons/Prefabs/Your Weapon Example 2.prefab",
        "Assets/Stylized Modular Weapons/Prefabs/Your Weapon Example 3.prefab",
        "Assets/Stylized Modular Weapons/Prefabs/Your Weapon Example 4.prefab",
    };

    private bool isGenerating = false;
    private double generateStartTime;
    private int generatingMsgIdx = -1;
    private double lastDotTime;
    private int dotCount = 1;

    // -- Agent mode state machine --------------------------------------------
    private enum AgentPhase { None, ChangingScene, EnvironmentReveal, AdjustingScene, Done }
    private enum AgentFlow { UnityScene, EnvironmentFree }
    private enum EnvironmentChoiceKind { None, Trees, Rocks, Flowers }

    private sealed class RevealStage
    {
        public string Message;
        public List<GameObject> PrimaryObjects;
        public int PrimaryBatchSize;
        public float PrimaryDelay;
        public List<GameObject> SecondaryObjects;
        public int SecondaryBatchSize;
        public float SecondaryDelay;
        public bool HasSecondary => SecondaryObjects != null && SecondaryObjects.Count > 0;

        public RevealStage(string message, List<GameObject> objects, int batchSize, float delay)
        {
            Message = message;
            PrimaryObjects = objects;
            PrimaryBatchSize = batchSize;
            PrimaryDelay = delay;
            SecondaryObjects = null;
            SecondaryBatchSize = 0;
            SecondaryDelay = 0f;
        }

        public RevealStage(
            string message,
            List<GameObject> primaryObjects,
            int primaryBatchSize,
            float primaryDelay,
            List<GameObject> secondaryObjects,
            int secondaryBatchSize,
            float secondaryDelay)
        {
            Message = message;
            PrimaryObjects = primaryObjects;
            PrimaryBatchSize = primaryBatchSize;
            PrimaryDelay = primaryDelay;
            SecondaryObjects = secondaryObjects;
            SecondaryBatchSize = secondaryBatchSize;
            SecondaryDelay = secondaryDelay;
        }
    }

    private AgentPhase agentPhase = AgentPhase.None;
    private AgentFlow agentFlow = AgentFlow.UnityScene;
    private double agentStepTime;
    private int agentStepIdx;
    private int agentBotMsgIdx = -1;
    private List<GameObject> _initChildren = new List<GameObject>();
    private List<GameObject> _toreachChildren = new List<GameObject>();
    private readonly List<RevealStage> _envRevealStages = new List<RevealStage>();
    private Transform _envRevealRoot;
    private int _envRevealObjectIdx;
    private int _envRevealSecondaryObjectIdx;
    private bool _envStageAnnounced;
    private double _envPrimaryLastTickTime;
    private double _envSecondaryLastTickTime;
    private readonly HashSet<string> _envChoicePromptedStages = new HashSet<string>();
    private bool _envChoiceAwaiting;
    private EnvironmentChoiceKind _envChoiceAwaitingKind = EnvironmentChoiceKind.None;

    private const float ToggleDelay = 0.15f;
    private const float ToolDelay = 2.0f;
    private const float EnvironmentCompleteDelay = 0.2f;
    private const float EnvironmentDelayScale = 2.0f;

    private static readonly string[] EnvTerrainKeywords = { "terrain", "ground", "plane" };
    private static readonly string[] EnvGrassKeywords = { "grass" };
    private static readonly string[] EnvTreeKeywords = { "tree", "pine", "oak" };
    private static readonly string[] EnvRockKeywords = { "rock", "stone", "boulder", "cliff" };
    private static readonly string[] EnvFlowerKeywords = { "flower", "poppy", "mushroom" };
    private static readonly string[] EnvWaterKeywords = { "water", "ocean", "lake", "pond" };

    private static readonly string[] AdjustToolPaths =
    {
        "Tools/Cyberpunk Patch - Brightness & Floor",
        "Tools/Neon Glow Fix",
        "Tools/Neon Reset (Dark base + Glow)",
        "Tools/Neon Signs & Text Fix",
        "Tools/Setup Cyberpunk Scene V3",
    };

    private static readonly string[] AdjustToolMessages =
    {
        "Adjusting scene...",
        "Adjusting lighting...",
        "Rethinking the environment...",
        "Fine-tuning the neon signs...",
        "Applying final cyberpunk atmosphere...",
    };

    private static readonly string[] TreeChoicePrefabPaths =
    {
        "Assets/Polytope Studio/Lowpoly_Environments/Prefabs/Trees/PT_Pine_Tree_03_green_cut.prefab",
        "Assets/Darth_Artisan/Free_Trees/Prefabs/Fir_Tree.prefab",
        "Assets/Darth_Artisan/Free_Trees/Prefabs/Oak_Tree.prefab",
        "Assets/Darth_Artisan/Free_Trees/Prefabs/Palm_Tree.prefab",
    };

    private static readonly string[] RockChoicePrefabPaths =
    {
        "Assets/Polytope Studio/Lowpoly_Environments/Prefabs/Rocks/PT_River_Rock_Pile_02.prefab",
        "Assets/LowlyPoly/Free Stylized Rocks/Prefab/Stone_02.prefab",
        "Assets/LowlyPoly/Free Stylized Rocks/Prefab/Stone_03.prefab",
        "Assets/LowlyPoly/Free Stylized Rocks/Prefab/Stone_04.prefab",
    };

    private static readonly string[] FlowerChoicePrefabPaths =
    {
        "Assets/Polytope Studio/Lowpoly_Environments/Prefabs/Flowers/PT_Poppy_02.prefab",
        "Assets/Emilulz_Assets/DEMOLowPolyFlowers/Prefabs/SM_Daisy_Single.prefab",
        "Assets/Emilulz_Assets/DEMOLowPolyFlowers/Prefabs/SM_Dandelion_Small.prefab",
        "Assets/Emilulz_Assets/DEMOLowPolyFlowers/Prefabs/SM_Hyacinth_PastellBlue_Big.prefab",
    };

    // -- Styles (lazy-initialised) -------------------------------------------
    private GUIStyle bubbleStyleUser;
    private GUIStyle bubbleStyleBot;
    private GUIStyle inputAreaStyle;
    private GUIStyle tierBudgetStyle;
    private GUIStyle tierStandardStyle;
    private GUIStyle tierPremiumStyle;
    private GUIStyle modeBadgeStyle;
    private bool stylesInitialised;
    private string apiKeyInput = "";
    private string apiKeyError = "";

    // -- Tier colour map -----------------------------------------------------
    private static readonly Color ColBudget = new Color(0.35f, 0.75f, 0.35f);
    private static readonly Color ColStandard = new Color(0.25f, 0.55f, 0.95f);
    private static readonly Color ColPremium = new Color(0.85f, 0.50f, 0.15f);

    // -- Entry point ---------------------------------------------------------
    [MenuItem("AI Chat/Open Chat Window")]
    public static void ShowWindow()
    {
        var win = GetWindow<AIChatWindow>("AI Chat");
        win.minSize = new Vector2(420, 560);
        win.Show();
    }

    [MenuItem("AI Chat/Clear API Key")]
    public static void ClearApiKey()
    {
        EditorPrefs.DeleteKey(ApiKeyPrefKey);
        Debug.Log("API key cleared. Restart the window to test the input screen.");
    }

    // -- Editor update subscription -----------------------------------------
    private void OnEnable()
    {
        apiKeyInput = EditorPrefs.GetString(ApiKeyPrefKey, "");
        EditorApplication.update += OnEditorUpdate;
    }

    private void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
        DestroyRuntimeAttachments(pendingAttachments);
        pendingAttachments.Clear();

        foreach (var msg in messages)
        {
            if (msg.ImageAttachments != null)
                DestroyRuntimeAttachments(msg.ImageAttachments);
        }
    }

    private void OnEditorUpdate()
    {
        // -- Agent phase pump ------------------------------------------------
        if (agentPhase != AgentPhase.None)
        {
            OnEditorAgentUpdate();
            return;
        }

        if (!isGenerating)
        {
            // Keep repainting while Unity loads asset preview thumbnails
            if (AssetPreview.IsLoadingAssetPreviews()) Repaint();
            return;
        }

        double now = EditorApplication.timeSinceStartup;

        // Cycle loading dots every 0.4 s
        if (now - lastDotTime >= 0.4)
        {
            lastDotTime = now;
            dotCount = (dotCount % 3) + 1;
            if (generatingMsgIdx >= 0 && generatingMsgIdx < messages.Count)
            {
                string dots = new string('.', dotCount);
                messages[generatingMsgIdx].Text = "Generating assets" + dots;
            }
            Repaint();
        }

        // After 2 seconds, show weapon cards in the chat window
        if (now - generateStartTime >= 2.0)
        {
            isGenerating = false;
            ShowWeaponAssets();
        }
    }

    // -- Agent phase state machine -------------------------------------------
    private void OnEditorAgentUpdate()
    {
        double now = EditorApplication.timeSinceStartup;

        if (agentPhase == AgentPhase.ChangingScene)
        {
            // -- Bootstrap: collect children & enable wireframe ---------------
            if (agentStepIdx == -1)
            {
                if (agentFlow == AgentFlow.EnvironmentFree)
                {
                    var envRoot = GameObject.Find("envscene");
                    _envRevealRoot = envRoot != null ? envRoot.transform : null;
                    _envChoicePromptedStages.Clear();
                    _envChoiceAwaiting = false;
                    _envChoiceAwaitingKind = EnvironmentChoiceKind.None;
                    BuildEnvironmentRevealStages(envRoot);
                    agentPhase = AgentPhase.EnvironmentReveal;
                    agentStepIdx = 0;
                    _envRevealObjectIdx = 0;
                    _envRevealSecondaryObjectIdx = 0;
                    _envStageAnnounced = false;
                    agentStepTime = now;
                    return;
                }

                var initGO = GameObject.Find("init");
                _initChildren.Clear();
                if (initGO != null)
                    foreach (Transform t in initGO.transform) _initChildren.Add(t.gameObject);

                var toreachGO = GameObject.Find("toreach");
                _toreachChildren.Clear();
                if (toreachGO != null)
                    foreach (Transform t in toreachGO.transform) _toreachChildren.Add(t.gameObject);

                SetWireframeMode(true);
                agentStepIdx = 0;
                agentStepTime = now;
                return;
            }

            // -- Interleave: each tick hides one init child AND shows one toreach child
            // agentStepIdx counts how many pairs/singles have been processed.
            // We continue until both lists are exhausted.
            int maxSteps = Mathf.Max(_initChildren.Count, _toreachChildren.Count);
            if (agentStepIdx < maxSteps)
            {
                if (now - agentStepTime >= ToggleDelay)
                {
                    if (agentStepIdx < _initChildren.Count)
                        _initChildren[agentStepIdx].SetActive(false);

                    if (agentStepIdx < _toreachChildren.Count)
                        _toreachChildren[agentStepIdx].SetActive(true);

                    agentStepIdx++;
                    agentStepTime = now;
                    Repaint();
                }
                return;
            }

            // -- All toggles done: restore shaded view, move to adjusting -----
            SetWireframeMode(false);
            agentPhase = AgentPhase.AdjustingScene;
            agentStepIdx = 0;
            agentStepTime = now;
            Repaint();
        }
        else if (agentPhase == AgentPhase.EnvironmentReveal)
        {
            if (agentStepIdx < _envRevealStages.Count)
            {
                var stage = _envRevealStages[agentStepIdx];

                if (_envChoiceAwaiting)
                    return;

                if (TryQueueEnvironmentChoicePrompt(stage))
                {
                    Repaint();
                    return;
                }

                if (!_envStageAnnounced)
                {
                    AddBotMsg(stage.Message);
                    _envStageAnnounced = true;
                    _envRevealObjectIdx = 0;
                    _envRevealSecondaryObjectIdx = 0;
                    _envPrimaryLastTickTime = now;
                    _envSecondaryLastTickTime = now;
                    agentStepTime = now;
                    Repaint();
                    return;
                }

                bool progressed = false;

                if (_envRevealObjectIdx < stage.PrimaryObjects.Count
                    && now - _envPrimaryLastTickTime >= stage.PrimaryDelay)
                {
                    int batch = Mathf.Max(1, stage.PrimaryBatchSize);
                    int endIdx = Mathf.Min(stage.PrimaryObjects.Count, _envRevealObjectIdx + batch);

                    while (_envRevealObjectIdx < endIdx)
                    {
                        var go = stage.PrimaryObjects[_envRevealObjectIdx];
                        ActivateWithParents(go, _envRevealRoot);
                        _envRevealObjectIdx++;
                    }

                    _envPrimaryLastTickTime = now;
                    progressed = true;
                }

                if (stage.HasSecondary
                    && _envRevealSecondaryObjectIdx < stage.SecondaryObjects.Count
                    && now - _envSecondaryLastTickTime >= stage.SecondaryDelay)
                {
                    int batch = Mathf.Max(1, stage.SecondaryBatchSize);
                    int endIdx = Mathf.Min(stage.SecondaryObjects.Count, _envRevealSecondaryObjectIdx + batch);

                    while (_envRevealSecondaryObjectIdx < endIdx)
                    {
                        var go = stage.SecondaryObjects[_envRevealSecondaryObjectIdx];
                        ActivateWithParents(go, _envRevealRoot);
                        _envRevealSecondaryObjectIdx++;
                    }

                    _envSecondaryLastTickTime = now;
                    progressed = true;
                }

                if (progressed)
                {
                    Repaint();
                }

                bool primaryDone = _envRevealObjectIdx >= stage.PrimaryObjects.Count;
                bool secondaryDone = !stage.HasSecondary
                    || _envRevealSecondaryObjectIdx >= stage.SecondaryObjects.Count;

                if (primaryDone && secondaryDone)
                {
                    agentStepIdx++;
                    _envRevealObjectIdx = 0;
                    _envRevealSecondaryObjectIdx = 0;
                    _envStageAnnounced = false;
                    agentStepTime = now;
                }
            }
            else
            {
                if (now - agentStepTime >= EnvironmentCompleteDelay * EnvironmentDelayScale)
                {
                    AddBotMsg("Your scene is ready!");
                    agentPhase = AgentPhase.Done;
                    agentStepTime = now;
                    Repaint();
                }
            }
        }
        else if (agentPhase == AgentPhase.AdjustingScene)
        {
            if (agentFlow != AgentFlow.UnityScene)
            {
                agentPhase = AgentPhase.Done;
                return;
            }

            if (agentStepIdx < AdjustToolPaths.Length)
            {
                if (now - agentStepTime >= ToolDelay)
                {
                    AddBotMsg(AdjustToolMessages[agentStepIdx]);
                    EditorApplication.ExecuteMenuItem(AdjustToolPaths[agentStepIdx]);
                    agentStepIdx++;
                    agentStepTime = now;
                    Repaint();
                }
            }
            else
            {
                if (now - agentStepTime >= ToolDelay)
                {
                    AddBotMsg("Your scene is ready!");
                    agentPhase = AgentPhase.Done;
                    agentStepTime = now;
                    Repaint();
                }
            }
        }
        else if (agentPhase == AgentPhase.Done)
        {
            agentPhase = AgentPhase.None;
            _envChoiceAwaiting = false;
            _envChoiceAwaitingKind = EnvironmentChoiceKind.None;
        }
    }

    private void UpdateBotMsg(string text)
    {
        if (agentBotMsgIdx >= 0 && agentBotMsgIdx < messages.Count)
            messages[agentBotMsgIdx].Text = text;
    }

    private void AddBotMsg(string text)
    {
        messages.Add(new ChatMessage(text, isUser: false));
        agentBotMsgIdx = messages.Count - 1;
        scrollPos.y = float.MaxValue;
    }

    private static void SetWireframeMode(bool enable)
    {
        SceneView sv = SceneView.lastActiveSceneView;
        if (sv == null) return;
        sv.cameraMode = SceneView.GetBuiltinCameraMode(
            enable ? DrawCameraMode.TexturedWire : DrawCameraMode.Textured);
        sv.Repaint();
    }

    private static bool IsEnvironmentFreeScene(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName)) return false;

        string normalized = sceneName
            .Replace("_", string.Empty)
            .Replace(" ", string.Empty)
            .ToLowerInvariant();

        return normalized == "environmentfree";
    }

    private void BuildEnvironmentRevealStages(GameObject envRoot)
    {
        _envRevealStages.Clear();
        if (envRoot == null) return;

        var allObjects = new List<GameObject>();
        CollectDescendantsPreOrder(envRoot.transform, allObjects);

        var terrain = new List<GameObject>();
        var grass = new List<GameObject>();
        var trees = new List<GameObject>();
        var rocks = new List<GameObject>();
        var flowers = new List<GameObject>();
        var water = new List<GameObject>();
        var lights = new List<GameObject>();
        var remaining = new List<GameObject>();

        for (int i = 0; i < allObjects.Count; i++)
        {
            var go = allObjects[i];
            if (go == null) continue;

            // Reset reveal targets so objects can be rebuilt one by one.
            go.SetActive(false);

            if (go.GetComponent<Light>() != null)
            {
                lights.Add(go);
                continue;
            }

            string n = go.name.ToLowerInvariant();
            bool isGrass = ContainsAny(n, EnvGrassKeywords);
            bool isTree = ContainsAny(n, EnvTreeKeywords);
            bool isRock = ContainsAny(n, EnvRockKeywords);
            bool isFlower = ContainsAny(n, EnvFlowerKeywords);
            bool isTerrain = ContainsAny(n, EnvTerrainKeywords);

            if (IsWaterObject(go, n))
            {
                water.Add(go);
            }
            else if (isGrass)
            {
                grass.Add(go);
            }
            else if (isTree)
            {
                trees.Add(go);
            }
            else if (isRock)
            {
                rocks.Add(go);
            }
            else if (isFlower)
            {
                flowers.Add(go);
            }
            else if (isTerrain)
            {
                terrain.Add(go);
            }
            else
            {
                remaining.Add(go);
            }
        }

        // Water must appear at the very end as part of finishing touches.
        remaining.AddRange(water);

        AddRevealStage("Populating terrain...", terrain, batchSize: 1, delay: ScaledEnvDelay(0.12f));
        AddRevealStage("Setting up lighting...", lights, batchSize: 1, delay: ScaledEnvDelay(0.02f));
        AddDualRevealStage(
            "Adding trees and grass...",
            primaryObjects: grass,
            primaryBatchSize: 140,
            primaryDelay: ScaledEnvDelay(0.0009f),
            secondaryObjects: trees,
            secondaryBatchSize: 10,
            secondaryDelay: ScaledEnvDelay(0.001f));
        AddRevealStage("Adding rocks...", rocks, batchSize: 2, delay: ScaledEnvDelay(0.01f));
        AddRevealStage("Adding flowers...", flowers, batchSize: 4, delay: ScaledEnvDelay(0.01f));
        AddRevealStage("Applying finishing touches...", remaining, batchSize: 5, delay: ScaledEnvDelay(0.01f));
    }

    private static float ScaledEnvDelay(float baseDelay)
    {
        return baseDelay * EnvironmentDelayScale;
    }

    private static void CollectDescendantsPreOrder(Transform root, List<GameObject> output)
    {
        if (root == null || output == null) return;

        for (int i = 0; i < root.childCount; i++)
        {
            var child = root.GetChild(i);
            output.Add(child.gameObject);
            CollectDescendantsPreOrder(child, output);
        }
    }

    private static void ActivateWithParents(GameObject go, Transform stopAt)
    {
        if (go == null) return;

        Transform t = go.transform;
        while (t != null)
        {
            if (!t.gameObject.activeSelf)
                t.gameObject.SetActive(true);

            if (t == stopAt) break;
            t = t.parent;
        }
    }

    private void AddRevealStage(string message, List<GameObject> objects, int batchSize, float delay)
    {
        if (objects == null || objects.Count == 0) return;
        _envRevealStages.Add(new RevealStage(message, objects, batchSize, delay));
    }

    private void AddDualRevealStage(
        string message,
        List<GameObject> primaryObjects,
        int primaryBatchSize,
        float primaryDelay,
        List<GameObject> secondaryObjects,
        int secondaryBatchSize,
        float secondaryDelay)
    {
        bool hasPrimary = primaryObjects != null && primaryObjects.Count > 0;
        bool hasSecondary = secondaryObjects != null && secondaryObjects.Count > 0;
        if (!hasPrimary && !hasSecondary) return;

        if (!hasPrimary)
        {
            AddRevealStage(message, secondaryObjects, secondaryBatchSize, secondaryDelay);
            return;
        }

        if (!hasSecondary)
        {
            AddRevealStage(message, primaryObjects, primaryBatchSize, primaryDelay);
            return;
        }

        _envRevealStages.Add(new RevealStage(
            message,
            primaryObjects,
            primaryBatchSize,
            primaryDelay,
            secondaryObjects,
            secondaryBatchSize,
            secondaryDelay));
    }

    private bool TryQueueEnvironmentChoicePrompt(RevealStage stage)
    {
        if (stage == null || string.IsNullOrWhiteSpace(stage.Message))
            return false;

        if (_envChoicePromptedStages.Contains(stage.Message))
            return false;

        EnvironmentChoiceKind kind = GetEnvironmentChoiceForStage(stage.Message);
        if (kind == EnvironmentChoiceKind.None)
            return false;

        _envChoicePromptedStages.Add(stage.Message);

        var cards = BuildEnvironmentChoiceCards(kind);
        if (cards.Count == 0)
            return false;

        var promptMsg = new ChatMessage(GetEnvironmentChoicePromptText(kind), isUser: false)
        {
            AssetCards = cards,
            AssetCardColumns = 2,
            AssetCardAction = AssetCardAction.SelectionPrompt,
            ChoiceKind = kind,
        };

        messages.Add(promptMsg);
        _envChoiceAwaiting = true;
        _envChoiceAwaitingKind = kind;
        scrollPos.y = float.MaxValue;
        return true;
    }

    private static EnvironmentChoiceKind GetEnvironmentChoiceForStage(string stageMessage)
    {
        switch (stageMessage)
        {
            case "Adding trees and grass...":
                return EnvironmentChoiceKind.Trees;
            case "Adding rocks...":
                return EnvironmentChoiceKind.Rocks;
            case "Adding flowers...":
                return EnvironmentChoiceKind.Flowers;
            default:
                return EnvironmentChoiceKind.None;
        }
    }

    private static string GetEnvironmentChoicePromptText(EnvironmentChoiceKind kind)
    {
        switch (kind)
        {
            case EnvironmentChoiceKind.Trees:
                return "Which tree type would you want?";
            case EnvironmentChoiceKind.Rocks:
                return "Which rock type would you want?";
            case EnvironmentChoiceKind.Flowers:
                return "Which flower type would you want?";
            default:
                return "Pick a prefab to continue.";
        }
    }

    private static string GetEnvironmentChoiceConfirmedText(EnvironmentChoiceKind kind, string selectedName)
    {
        switch (kind)
        {
            case EnvironmentChoiceKind.Trees:
                return "Tree type selected: " + selectedName + ". Continuing scene generation...";
            case EnvironmentChoiceKind.Rocks:
                return "Rock type selected: " + selectedName + ". Continuing scene generation...";
            case EnvironmentChoiceKind.Flowers:
                return "Flower type selected: " + selectedName + ". Continuing scene generation...";
            default:
                return "Choice selected. Continuing scene generation...";
        }
    }

    private List<AssetCard> BuildEnvironmentChoiceCards(EnvironmentChoiceKind kind)
    {
        string[] prefabPaths;
        switch (kind)
        {
            case EnvironmentChoiceKind.Trees:
                prefabPaths = TreeChoicePrefabPaths;
                break;
            case EnvironmentChoiceKind.Rocks:
                prefabPaths = RockChoicePrefabPaths;
                break;
            case EnvironmentChoiceKind.Flowers:
                prefabPaths = FlowerChoicePrefabPaths;
                break;
            default:
                prefabPaths = null;
                break;
        }

        var cards = new List<AssetCard>();
        if (prefabPaths == null) return cards;

        for (int i = 0; i < prefabPaths.Length && cards.Count < 4; i++)
        {
            string path = prefabPaths[i];
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;

            AssetPreview.GetAssetPreview(prefab);
            cards.Add(new AssetCard(prefab.name, path));
        }

        return cards;
    }

    private void ConfirmEnvironmentChoice(ChatMessage message, AssetCard selectedCard)
    {
        if (message == null || selectedCard == null) return;
        if (!_envChoiceAwaiting || message.ChoiceKind != _envChoiceAwaitingKind) return;

        if (message.AssetCards != null)
        {
            foreach (var card in message.AssetCards)
                card.Selected = ReferenceEquals(card, selectedCard);
        }

        message.Text = GetEnvironmentChoiceConfirmedText(message.ChoiceKind, selectedCard.Name);
        message.AssetCardAction = AssetCardAction.None;
        message.ChoiceKind = EnvironmentChoiceKind.None;

        _envChoiceAwaiting = false;
        _envChoiceAwaitingKind = EnvironmentChoiceKind.None;
        agentStepTime = EditorApplication.timeSinceStartup;
        scrollPos.y = float.MaxValue;
        Repaint();
    }

    private static bool ContainsAny(string value, string[] keywords)
    {
        for (int i = 0; i < keywords.Length; i++)
        {
            if (value.Contains(keywords[i]))
                return true;
        }
        return false;
    }

    private static bool IsWaterObject(GameObject go, string lowerName)
    {
        if (ContainsAny(lowerName, EnvWaterKeywords))
            return true;

        var renderer = go.GetComponent<Renderer>();
        var material = renderer != null ? renderer.sharedMaterial : null;
        if (material == null) return false;

        string matName = material.name.ToLowerInvariant();
        return matName.Contains("water");
    }
}

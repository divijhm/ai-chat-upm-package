using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.Collections.Generic;

public partial class AIChatWindow
{
    private enum AssetCardAction { InstantiatePrefab, SelectionPrompt, None }

    // -- Show 4 weapon assets as cards inside the chat window ------------------
    private void ShowWeaponAssets()
    {
        var cards = new List<AssetCard>();

        foreach (var path in WeaponPrefabPaths)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) continue;
            // Kick off async preview generation
            AssetPreview.GetAssetPreview(prefab);
            cards.Add(new AssetCard(prefab.name, path));
        }

        string headerText = cards.Count > 0
            ? $"Here are {cards.Count} weapon assets from your project:"
            : "Asset generation complete, but no prefabs could be loaded.";

        var resultMsg = new ChatMessage(headerText, isUser: false);
        if (cards.Count > 0) resultMsg.AssetCards = cards;

        if (generatingMsgIdx >= 0 && generatingMsgIdx < messages.Count)
            messages[generatingMsgIdx] = resultMsg;
        else
            messages.Add(resultMsg);

        generatingMsgIdx = -1;
        scrollPos.y = float.MaxValue;
        Repaint();
    }

    // -- Chat helpers ---------------------------------------------------------
    private void SendMessage(string text, List<ImageAttachment> attachments)
    {
        var sentAttachments = new List<ImageAttachment>(attachments);
        messages.Add(new ChatMessage(text, isUser: true, sentAttachments));

        inputText = "";
        attachments.Clear();
        GUI.FocusControl("ChatInput");

        // -- Asset Generation mode: show animated loading, then spawn weapons --
        if (currentMode == ChatMode.AssetGeneration)
        {
            messages.Add(new ChatMessage("Generating assets.", isUser: false));
            generatingMsgIdx = messages.Count - 1;
            isGenerating = true;
            generateStartTime = EditorApplication.timeSinceStartup;
            lastDotTime = generateStartTime;
            dotCount = 1;
            scrollPos.y = float.MaxValue;
            Repaint();
            return;
        }

        // -- Agent mode: run the scene-change + tool sequence ----------------
        if (currentMode == ChatMode.Agent)
        {
            string activeSceneName = EditorSceneManager.GetActiveScene().name;
            bool isEnvironmentFlow = IsEnvironmentFreeScene(activeSceneName);

            agentFlow = isEnvironmentFlow
                ? AgentFlow.EnvironmentFree
                : AgentFlow.UnityScene;

            messages.Add(new ChatMessage(
                isEnvironmentFlow ? "Generating scene from images..." : "Changing scene...",
                isUser: false));
            agentBotMsgIdx = messages.Count - 1;
            agentPhase = AgentPhase.ChangingScene;
            agentStepIdx = -1;
            agentStepTime = EditorApplication.timeSinceStartup;
            scrollPos.y = float.MaxValue;
            Repaint();
            return;
        }

        // Switch scene on every send (stub behaviour - simulates AI taking action)
        string targetScene = isOnSceneA ? SCENE_B : SCENE_A;
        string targetName = isOnSceneA ? "DemoScene" : "SampleScene";
        SwitchScene(targetScene);

        string aiReply = GenerateStubReply(text, targetName);
        messages.Add(new ChatMessage(aiReply, isUser: false));

        scrollPos.y = float.MaxValue;
        Repaint();
    }

    private string GenerateStubReply(string userText, string switchedTo)
    {
        string lower = userText.ToLower();

        if (lower.Contains("hello") || lower.Contains("hi"))
            return $"Hello! I've switched the scene to \"{switchedTo}\" as a demo action.";

        if (lower.Contains("help"))
            return "Send any message and I'll switch the active scene as a proof-of-concept. Real AI editing is coming soon!";

        return currentMode switch
        {
            ChatMode.AssetGeneration =>
                $"[Asset Generation — {AssetModels[selectedModelIndex].Id} / {AssetModels[selectedModelIndex].Tier}] Switched scene to \"{switchedTo}\". Asset generation coming soon!",
            ChatMode.Agent =>
                $"[Agent] Switched scene to \"{switchedTo}\". Full scene-editing capabilities coming soon!",
            ChatMode.Selection =>
                $"[Selection] Switched scene to \"{switchedTo}\". Selection-based editing coming soon!",
            _ => $"Switched scene to \"{switchedTo}\"."
        };
    }

    private void SwitchScene(string scenePath)
    {
        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            isOnSceneA = scenePath == SCENE_A;
            Repaint();
        }
    }

    // -- Data types -----------------------------------------------------------
    private class AssetCard
    {
        public string Name;
        public string Path;
        public bool AddedToScene;
        public bool Selected;
        public AssetCard(string name, string path) { Name = name; Path = path; }
    }

    private class ImageAttachment
    {
        public Texture2D Texture;
        public string Name;
        public bool IsRuntime;

        public ImageAttachment(Texture2D texture, string name, bool isRuntime)
        {
            Texture = texture;
            Name = name;
            IsRuntime = isRuntime;
        }
    }

    // Mutable wrapper so we can update loading messages in-place
    private class ChatMessage
    {
        public string Text;
        public bool IsUser;
        public List<AssetCard> AssetCards; // non-null for asset result messages
        public int AssetCardColumns;
        public AssetCardAction AssetCardAction;
        public EnvironmentChoiceKind ChoiceKind;
        public List<ImageAttachment> ImageAttachments;
        public ChatMessage(string text, bool isUser, List<ImageAttachment> imageAttachments = null)
        {
            Text = text;
            IsUser = isUser;
            AssetCardColumns = 1;
            AssetCardAction = AssetCardAction.InstantiatePrefab;
            ChoiceKind = EnvironmentChoiceKind.None;
            ImageAttachments = imageAttachments;
        }
    }
}

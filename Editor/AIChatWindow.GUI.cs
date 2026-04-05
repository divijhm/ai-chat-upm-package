using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

public partial class AIChatWindow
{
    // -- Initialise styles once ----------------------------------------------
    private void InitStyles()
    {
        if (stylesInitialised) return;

        bubbleStyleUser = new GUIStyle(EditorStyles.helpBox)
        {
            wordWrap = true,
            alignment = TextAnchor.MiddleRight,
            fontSize = 12,
            padding = new RectOffset(8, 8, 6, 6)
        };
        bubbleStyleUser.normal.textColor = new Color(0.15f, 0.45f, 0.85f);

        bubbleStyleBot = new GUIStyle(EditorStyles.helpBox)
        {
            wordWrap = true,
            alignment = TextAnchor.MiddleLeft,
            fontSize = 12,
            padding = new RectOffset(8, 8, 6, 6)
        };
        bubbleStyleBot.normal.textColor = new Color(0.15f, 0.65f, 0.3f);

        inputAreaStyle = new GUIStyle(EditorStyles.textArea)
        {
            wordWrap = true,
            fontSize = 12
        };

        // Tier badge styles
        tierBudgetStyle = MakeBadgeStyle(ColBudget);
        tierStandardStyle = MakeBadgeStyle(ColStandard);
        tierPremiumStyle = MakeBadgeStyle(ColPremium);

        modeBadgeStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 10,
            fontStyle = FontStyle.Bold,
            padding = new RectOffset(6, 6, 2, 2)
        };

        stylesInitialised = true;
    }

    private static GUIStyle MakeBadgeStyle(Color textColor)
    {
        var s = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 10,
            fontStyle = FontStyle.Bold,
            padding = new RectOffset(4, 4, 2, 2)
        };
        s.normal.textColor = textColor;
        return s;
    }

    // Fallback for when badge styles haven't been initialised yet
    private GUIStyle SafeBadge => tierBudgetStyle ?? EditorStyles.miniLabel;

    // -- Main GUI ------------------------------------------------------------
    private void OnGUI()
    {
        InitStyles();

        if (!HasApiKey())
        {
            DrawApiKeyGate();
            return;
        }

        // Handle image paste early so focused text controls don't consume
        // the paste event before we can inspect clipboard image data.
        HandleClipboardPaste();

        Color prevBg = GUI.backgroundColor;

        // -- Header ---------------------------------------------------------
        EditorGUILayout.Space(6);
        GUILayout.Label("DAMN 3D Editor", EditorStyles.boldLabel);
        DrawHorizontalLine();

        // -- Chat history ----------------------------------------------------
        // Let IMGUI allocate the remaining vertical space so bottom controls
        // (mode, attachments, input) do not overflow out of the window.
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.ExpandHeight(true));

        foreach (var msg in messages)
        {
            GUIStyle style = msg.IsUser ? bubbleStyleUser : bubbleStyleBot;
            string prefix = msg.IsUser ? "You: " : "AI:  ";

            if (msg.IsUser)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();

                EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.MaxWidth(position.width * 0.78f));
                if (!string.IsNullOrWhiteSpace(msg.Text))
                    GUILayout.Label(prefix + msg.Text, style, GUILayout.MaxWidth(position.width * 0.72f));

                DrawMessageAttachments(msg.ImageAttachments);
                EditorGUILayout.EndVertical();

                EditorGUILayout.EndHorizontal();
            }
            else if (msg.AssetCards != null && msg.AssetCards.Count > 0)
            {
                DrawAssetCardMessage(msg);
            }
            else
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.MaxWidth(position.width * 0.78f));

                if (!string.IsNullOrWhiteSpace(msg.Text))
                    GUILayout.Label(prefix + msg.Text, style, GUILayout.MaxWidth(position.width * 0.72f));

                DrawMessageAttachments(msg.ImageAttachments);
                EditorGUILayout.EndVertical();

                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.Space(3);
        }

        EditorGUILayout.EndScrollView();

        // Keep composer controls pinned to the bottom like a chat UI.
        GUILayout.FlexibleSpace();

        DrawHorizontalLine();

        // -- Mode selector row (above input) ---------------------------------
        DrawModeSelector(prevBg);

        DrawHorizontalLine();

        // -- Attachment preview row (only when images are attached) --------
        if (pendingAttachments.Count > 0)
            DrawAttachmentComposer();

        // -- Input row -------------------------------------------------------
        EditorGUILayout.BeginHorizontal();

        GUI.SetNextControlName("ChatInput");
        inputText = EditorGUILayout.TextArea(inputText, inputAreaStyle,
            GUILayout.Height(46), GUILayout.ExpandWidth(true));
        Rect inputRect = GUILayoutUtility.GetLastRect();

        EditorGUILayout.BeginVertical(GUILayout.Width(64));
        GUI.backgroundColor = new Color(0.3f, 0.75f, 0.3f);
        bool sendPressed = GUILayout.Button("Send", GUILayout.Height(46));
        GUI.backgroundColor = prevBg;
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space(4);

        HandleDragAndDropImages(inputRect);

        // -- Send on button or Ctrl/Cmd+Enter -------------------------------
        bool ctrlEnter = Event.current.type == EventType.KeyDown
                      && Event.current.keyCode == KeyCode.Return
                      && (Event.current.control || Event.current.command);

        bool hasText = !string.IsNullOrWhiteSpace(inputText);
        bool hasAttachments = pendingAttachments.Count > 0;

        if ((sendPressed || ctrlEnter) && (hasText || hasAttachments))
        {
            SendMessage(inputText.Trim(), pendingAttachments);
            if (ctrlEnter) Event.current.Use();
        }
    }

    private bool HasApiKey()
    {
        return !string.IsNullOrWhiteSpace(EditorPrefs.GetString(ApiKeyPrefKey, ""));
    }

    private void DrawApiKeyGate()
    {
        EditorGUILayout.Space(8);
        GUILayout.Label("DAMN 3D Editor", EditorStyles.boldLabel);
        DrawHorizontalLine();

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        GUILayout.Label("API Key Required", EditorStyles.boldLabel);
        GUILayout.Label("Enter your API key to unlock the editor window.", EditorStyles.wordWrappedMiniLabel);

        EditorGUILayout.Space(6);
        GUI.SetNextControlName("ApiKeyInput");
        apiKeyInput = EditorGUILayout.PasswordField("API Key", apiKeyInput);

        if (!string.IsNullOrEmpty(apiKeyError))
            EditorGUILayout.HelpBox(apiKeyError, MessageType.Error);

        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();

        if (GUILayout.Button("Save Key", GUILayout.Width(100), GUILayout.Height(24)))
        {
            SaveApiKey();
        }

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();

        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return)
        {
            SaveApiKey();
            Event.current.Use();
        }
    }

    private void SaveApiKey()
    {
        string trimmed = (apiKeyInput ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            apiKeyError = "API key cannot be empty.";
            Repaint();
            return;
        }

        EditorPrefs.SetString(ApiKeyPrefKey, trimmed);
        apiKeyInput = trimmed;
        apiKeyError = "";
        GUI.FocusControl(null);
        Repaint();
    }

    // -- Mode selector UI ----------------------------------------------------
    // IMPORTANT: every branch must draw EXACTLY the same number of controls
    // so that IMGUI's Layout and Repaint passes stay in sync.
    private void DrawModeSelector(Color prevBg)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.BeginHorizontal();

        // -- Mode label + popup (always rendered) ----------------------------
        EditorGUILayout.LabelField("Mode", GUILayout.Width(38));
        int newMode = EditorGUILayout.Popup((int)currentMode, ChatModeLabels, GUILayout.Height(20));
        // Defer the state change to the end of the current event to keep
        // Layout / Repaint control counts identical within one frame.
        if (newMode != (int)currentMode)
            EditorApplication.delayCall += () =>
            {
                currentMode = (ChatMode)newMode;
                messages.Clear();
                isGenerating = false;
                generatingMsgIdx = -1;
                scrollPos = Vector2.zero;
                Repaint();
            };

        GUILayout.Space(6);

        // -- Model picker (only shown in Asset Generation mode) ---------------
        if (currentMode == ChatMode.AssetGeneration)
        {
            EditorGUILayout.LabelField("Model", GUILayout.Width(40));
            int newModel = EditorGUILayout.Popup(selectedModelIndex, AssetModelLabels, GUILayout.Height(20));
            if (newModel != selectedModelIndex)
                selectedModelIndex = newModel;

            string tier = AssetModels[selectedModelIndex].Tier;
            GUIStyle badgeStyle;
            string tierLabel;
            if (tier == "\u00d70.33") { badgeStyle = tierBudgetStyle ?? SafeBadge; tierLabel = "BUDGET"; }
            else if (tier == "\u00d71") { badgeStyle = tierStandardStyle ?? SafeBadge; tierLabel = "STANDARD"; }
            else { badgeStyle = tierPremiumStyle ?? SafeBadge; tierLabel = "PREMIUM"; }
            GUILayout.Label(tierLabel, badgeStyle, GUILayout.Width(58));
        }

        EditorGUILayout.EndHorizontal();

        // -- Hint line --------------------------------------------------------
        EditorGUILayout.Space(2);
        string hint;
        if (currentMode == ChatMode.AssetGeneration)
            hint = $"Generate assets · {AssetModels[selectedModelIndex].Id}  ({AssetModels[selectedModelIndex].Tier} usage)";
        else if (currentMode == ChatMode.Agent)
            hint = "Agent mode — AI can read and modify the active scene.";
        else
            hint = "Selection mode — AI operates on the currently selected objects.";
        EditorGUILayout.LabelField(hint, EditorStyles.miniLabel);

        EditorGUILayout.EndVertical();
    }

    private void DrawAttachmentComposer()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.BeginHorizontal();
        GUILayout.Label("Attachments", EditorStyles.miniBoldLabel);
        GUILayout.FlexibleSpace();
        GUILayout.Label("Paste image with Ctrl/Cmd+V", EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();

        if (pendingAttachments.Count == 0)
        {
            GUILayout.Label("No images attached", EditorStyles.miniLabel);
        }
        else
        {
            EditorGUILayout.BeginHorizontal();
            for (int i = 0; i < pendingAttachments.Count; i++)
            {
                var attachment = pendingAttachments[i];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(78));

                Rect thumbRect = GUILayoutUtility.GetRect(64, 64, GUILayout.Width(64), GUILayout.Height(64));
                if (attachment.Texture != null)
                    GUI.DrawTexture(thumbRect, attachment.Texture, ScaleMode.ScaleToFit);
                else
                    EditorGUI.DrawRect(thumbRect, new Color(0.25f, 0.25f, 0.25f, 0.9f));

                Rect removeRect = new Rect(thumbRect.xMax - 18, thumbRect.y + 2, 16, 16);
                if (GUI.Button(removeRect, "x", EditorStyles.miniButton))
                {
                    if (attachment.IsRuntime && attachment.Texture != null)
                        DestroyImmediate(attachment.Texture);

                    pendingAttachments.RemoveAt(i);
                    Repaint();
                    i--;
                    EditorGUILayout.EndVertical();
                    continue;
                }

                var nameStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    wordWrap = true,
                    alignment = TextAnchor.MiddleCenter
                };
                GUILayout.Label(attachment.Name, nameStyle, GUILayout.Width(64));
                EditorGUILayout.EndVertical();
                GUILayout.Space(4);
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(4);
    }

    private void DrawAssetCardMessage(ChatMessage msg)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        GUILayout.Label("AI:  " + msg.Text, bubbleStyleBot);
        EditorGUILayout.Space(4);

        int columns = Mathf.Clamp(msg.AssetCardColumns, 1, 4);
        if (columns <= 1)
        {
            EditorGUILayout.BeginHorizontal();
            foreach (var card in msg.AssetCards)
            {
                DrawAssetCard(msg, card);
                GUILayout.Space(4);
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }
        else
        {
            for (int i = 0; i < msg.AssetCards.Count; i += columns)
            {
                EditorGUILayout.BeginHorizontal();
                for (int c = 0; c < columns; c++)
                {
                    int idx = i + c;
                    if (idx < msg.AssetCards.Count)
                        DrawAssetCard(msg, msg.AssetCards[idx]);
                    else
                        GUILayout.Space(88);

                    GUILayout.Space(4);
                }

                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(2);
            }
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.EndVertical();
    }

    private void DrawAssetCard(ChatMessage msg, AssetCard card)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(88));

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(card.Path);
        Texture2D thumb = prefab != null ? AssetPreview.GetAssetPreview(prefab) : null;
        if (thumb == null && prefab != null) thumb = AssetPreview.GetMiniThumbnail(prefab);

        Rect thumbRect = GUILayoutUtility.GetRect(80, 80,
            GUILayout.Width(80), GUILayout.Height(80));

        if (thumbRect.Contains(Event.current.mousePosition))
            EditorGUI.DrawRect(thumbRect, new Color(1f, 1f, 1f, 0.08f));

        if (thumb != null)
            GUI.DrawTexture(thumbRect, thumb, ScaleMode.ScaleToFit);
        else
            EditorGUI.DrawRect(thumbRect, new Color(0.25f, 0.25f, 0.25f, 0.8f));

        if (card.Selected)
        {
            DrawAssetCardBadge(thumbRect, "Selected ✓", new Color(0.10f, 0.45f, 0.90f, 0.88f));
        }
        else if (card.AddedToScene)
        {
            DrawAssetCardBadge(thumbRect, "Added ✓", new Color(0.10f, 0.60f, 0.10f, 0.85f));
        }

        if (Event.current.type == EventType.MouseDown && thumbRect.Contains(Event.current.mousePosition))
        {
            Event.current.Use();
            HandleAssetCardClick(msg, card, prefab);
        }

        var nameStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            wordWrap = true,
            alignment = TextAnchor.MiddleCenter
        };
        GUILayout.Label(card.Name, nameStyle, GUILayout.Width(80));

        EditorGUILayout.EndVertical();
    }

    private void HandleAssetCardClick(ChatMessage msg, AssetCard card, GameObject prefab)
    {
        if (msg.AssetCardAction == AssetCardAction.SelectionPrompt)
        {
            ConfirmEnvironmentChoice(msg, card);
            return;
        }

        if (msg.AssetCardAction != AssetCardAction.InstantiatePrefab || prefab == null)
            return;

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.transform.position = Vector3.zero;
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Selection.activeGameObject = instance;
        card.AddedToScene = true;
        Repaint();
    }

    private static void DrawAssetCardBadge(Rect thumbRect, string text, Color background)
    {
        var badgeStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            fontSize = 9
        };
        badgeStyle.normal.textColor = Color.white;

        var badgeRect = new Rect(thumbRect.x, thumbRect.yMax - 16, thumbRect.width, 16);
        EditorGUI.DrawRect(badgeRect, background);
        GUI.Label(badgeRect, text, badgeStyle);
    }

    private void DrawMessageAttachments(List<ImageAttachment> attachments)
    {
        if (attachments == null || attachments.Count == 0) return;

        EditorGUILayout.Space(3);
        EditorGUILayout.BeginHorizontal();

        foreach (var attachment in attachments)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(88));

            Rect thumbRect = GUILayoutUtility.GetRect(80, 80, GUILayout.Width(80), GUILayout.Height(80));
            if (attachment.Texture != null)
                GUI.DrawTexture(thumbRect, attachment.Texture, ScaleMode.ScaleToFit);
            else
                EditorGUI.DrawRect(thumbRect, new Color(0.25f, 0.25f, 0.25f, 0.9f));

            var nameStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                wordWrap = true,
                alignment = TextAnchor.MiddleCenter
            };
            GUILayout.Label(attachment.Name, nameStyle, GUILayout.Width(80));
            EditorGUILayout.EndVertical();
            GUILayout.Space(4);
        }

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();
    }

    private static void DrawHorizontalLine()
    {
        EditorGUILayout.Space(4);
        Rect r = EditorGUILayout.GetControlRect(false, 1);
        EditorGUI.DrawRect(r, new Color(0.4f, 0.4f, 0.4f, 0.5f));
        EditorGUILayout.Space(4);
    }
}

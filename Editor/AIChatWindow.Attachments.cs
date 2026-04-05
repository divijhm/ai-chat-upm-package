using UnityEditor;
using UnityEngine;
using System;
using System.IO;
using System.Reflection;
using System.Threading;

public partial class AIChatWindow
{
    private void HandleClipboardPaste()
    {
        Event evt = Event.current;
        if (evt == null) return;

        bool pasteValidate = evt.type == EventType.ValidateCommand && evt.commandName == "Paste";
        bool pasteCommand = evt.type == EventType.ExecuteCommand && evt.commandName == "Paste";
        bool pastePressed = evt.type == EventType.KeyDown
                        && evt.keyCode == KeyCode.V
                        && (evt.control || evt.command);
        bool shiftInsert = evt.type == EventType.KeyDown
                        && evt.keyCode == KeyCode.Insert
                        && evt.shift;

        bool wantsPaste = pasteValidate || pasteCommand || pastePressed || shiftInsert;
        if (!wantsPaste) return;

        if (DebugPasteLogs)
        {
            Debug.Log($"[AIChatWindow] Paste event detected. type={evt.type}, command={evt.commandName}, key={evt.keyCode}, ctrl={evt.control}, cmd={evt.command}, shift={evt.shift}");
        }

        if (TryAddImageFromClipboard())
        {
            if (DebugPasteLogs)
                Debug.Log($"[AIChatWindow] Image attachment added. pendingAttachments={pendingAttachments.Count}");
            evt.Use();
            Repaint();
        }
        else if (DebugPasteLogs)
        {
            Debug.LogWarning("[AIChatWindow] Paste triggered but no clipboard image could be imported.");
        }
    }

    private bool TryAddImageFromClipboard()
    {
        if (DebugPasteLogs)
            Debug.Log("[AIChatWindow] Trying clipboard path/image import...");

        if (TryAddImageFromClipboardPath())
        {
            if (DebugPasteLogs)
                Debug.Log("[AIChatWindow] Imported image from clipboard path/text.");
            return true;
        }

#if UNITY_EDITOR_WIN
        if (DebugPasteLogs)
            Debug.Log("[AIChatWindow] Clipboard path import failed. Trying Windows clipboard image APIs...");

        bool ok = TryAddImageFromWindowsClipboard();
        if (DebugPasteLogs)
            Debug.Log($"[AIChatWindow] Windows clipboard import result={ok}");
        return ok;
#else
        if (DebugPasteLogs)
            Debug.LogWarning("[AIChatWindow] Platform is not Windows in this build path; no native clipboard image fallback available.");
        return false;
#endif
    }

    private bool TryAddImageFromClipboardPath()
    {
        string clip = EditorGUIUtility.systemCopyBuffer;
        if (DebugPasteLogs)
            Debug.Log($"[AIChatWindow] systemCopyBuffer length={(clip == null ? 0 : clip.Length)}");

        if (string.IsNullOrWhiteSpace(clip))
        {
            if (DebugPasteLogs)
                Debug.Log("[AIChatWindow] systemCopyBuffer is empty/whitespace.");
            return false;
        }

        foreach (string raw in clip.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = raw.Trim().Trim('"');
            if (DebugPasteLogs)
                Debug.Log($"[AIChatWindow] Clipboard line candidate: {candidate}");

            if (TryAddImageFromPath(candidate))
                return true;
        }

        if (DebugPasteLogs)
            Debug.Log("[AIChatWindow] No valid image file paths found in systemCopyBuffer.");

        return false;
    }

#if UNITY_EDITOR_WIN
    private bool TryAddImageFromWindowsClipboard()
    {
        try
        {
            byte[] bytes = TryReadWindowsClipboardImageBytes();
            if (bytes == null || bytes.Length == 0)
            {
                if (DebugPasteLogs)
                    Debug.Log("[AIChatWindow] Windows clipboard returned no image bytes.");
                return false;
            }

            if (DebugPasteLogs)
                Debug.Log($"[AIChatWindow] Windows clipboard image bytes length={bytes.Length}");

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(tex, bytes, false))
            {
                if (DebugPasteLogs)
                    Debug.LogWarning("[AIChatWindow] ImageConversion.LoadImage failed for Windows clipboard image bytes.");
                DestroyImmediate(tex);
                return false;
            }

            pendingAttachments.Add(new ImageAttachment(tex, "clipboard-image.png", isRuntime: true));
            if (DebugPasteLogs)
                Debug.Log("[AIChatWindow] Added image from Windows clipboard image bytes.");
            return true;
        }
        catch (Exception ex)
        {
            if (DebugPasteLogs)
                Debug.LogWarning($"[AIChatWindow] TryAddImageFromWindowsClipboard exception: {ex.Message}\n{ex.StackTrace}");
            return false;
        }
    }

    private static byte[] TryReadWindowsClipboardImageBytes()
    {
        byte[] pngBytes = null;

        var staThread = new Thread(() =>
        {
            try
            {
                Type clipboardType = ResolveType("System.Windows.Forms.Clipboard", "System.Windows.Forms");
                if (clipboardType == null)
                {
                    if (DebugPasteLogs)
                        Debug.LogWarning("[AIChatWindow] Could not resolve System.Windows.Forms.Clipboard type.");
                    TryReadWpfClipboardImageBytes(ref pngBytes);
                    return;
                }

                MethodInfo getDataObject = clipboardType.GetMethod("GetDataObject", BindingFlags.Public | BindingFlags.Static);
                if (getDataObject != null && DebugPasteLogs)
                {
                    object dataObject = getDataObject.Invoke(null, null);
                    if (dataObject != null)
                    {
                        MethodInfo getFormats = dataObject.GetType().GetMethod("GetFormats", Type.EmptyTypes);
                        if (getFormats != null)
                        {
                            string[] formats = getFormats.Invoke(dataObject, null) as string[];
                            if (formats != null && formats.Length > 0)
                                Debug.Log("[AIChatWindow] WinForms clipboard formats: " + string.Join(", ", formats));
                            else
                                Debug.Log("[AIChatWindow] WinForms clipboard formats: <none>");
                        }
                    }
                }

                // Some apps place image data as raw PNG data but not as
                // Clipboard.ContainsImage() bitmap. Try this path first.
                MethodInfo containsData = clipboardType.GetMethod("ContainsData", new[] { typeof(string) });
                MethodInfo getData = clipboardType.GetMethod("GetData", new[] { typeof(string) });
                if (containsData != null && getData != null)
                {
                    bool hasPng = (bool)containsData.Invoke(null, new object[] { "PNG" });
                    if (DebugPasteLogs)
                        Debug.Log($"[AIChatWindow] Clipboard.ContainsData('PNG')={hasPng}");
                    if (hasPng)
                    {
                        object pngData = getData.Invoke(null, new object[] { "PNG" });
                        if (pngData is MemoryStream pngStream)
                        {
                            pngBytes = pngStream.ToArray();
                            if (DebugPasteLogs)
                                Debug.Log($"[AIChatWindow] Read PNG clipboard data from MemoryStream. bytes={pngBytes.Length}");
                            return;
                        }

                        if (pngData is byte[] rawPng)
                        {
                            pngBytes = rawPng;
                            if (DebugPasteLogs)
                                Debug.Log($"[AIChatWindow] Read PNG clipboard data from byte[]. bytes={pngBytes.Length}");
                            return;
                        }

                        if (DebugPasteLogs)
                            Debug.Log($"[AIChatWindow] Clipboard PNG payload type={pngData?.GetType().FullName ?? "null"}");
                    }
                }
                else if (DebugPasteLogs)
                {
                    Debug.Log("[AIChatWindow] Clipboard.ContainsData/GetData methods unavailable.");
                }

                MethodInfo containsImage = clipboardType.GetMethod("ContainsImage", BindingFlags.Public | BindingFlags.Static);
                if (containsImage == null)
                {
                    if (DebugPasteLogs)
                        Debug.LogWarning("[AIChatWindow] Clipboard.ContainsImage method unavailable.");
                    return;
                }

                bool hasImage = (bool)containsImage.Invoke(null, null);
                if (DebugPasteLogs)
                    Debug.Log($"[AIChatWindow] Clipboard.ContainsImage()={hasImage}");
                if (!hasImage)
                {
                    TryReadWpfClipboardImageBytes(ref pngBytes);
                    return;
                }

                MethodInfo getImage = clipboardType.GetMethod("GetImage", BindingFlags.Public | BindingFlags.Static);
                if (getImage == null)
                {
                    if (DebugPasteLogs)
                        Debug.LogWarning("[AIChatWindow] Clipboard.GetImage method unavailable.");
                    return;
                }

                object imageObj = getImage.Invoke(null, null);
                if (imageObj == null)
                {
                    if (DebugPasteLogs)
                        Debug.LogWarning("[AIChatWindow] Clipboard.GetImage returned null.");
                    return;
                }

                Type imageFormatType = ResolveType("System.Drawing.Imaging.ImageFormat", "System.Drawing")
                                     ?? ResolveType("System.Drawing.Imaging.ImageFormat", "System.Drawing.Common");
                if (imageFormatType == null)
                {
                    if (DebugPasteLogs)
                        Debug.LogWarning("[AIChatWindow] Could not resolve System.Drawing.Imaging.ImageFormat type.");
                    return;
                }

                object pngFormat = imageFormatType.GetProperty("Png", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (pngFormat == null)
                {
                    if (DebugPasteLogs)
                        Debug.LogWarning("[AIChatWindow] ImageFormat.Png resolved to null.");
                    return;
                }

                using (var stream = new MemoryStream())
                {
                    MethodInfo save = imageObj.GetType().GetMethod("Save", new[] { typeof(Stream), imageFormatType });
                    if (save == null)
                    {
                        if (DebugPasteLogs)
                            Debug.LogWarning("[AIChatWindow] Could not find Save(Stream, ImageFormat) on clipboard image object.");
                        return;
                    }

                    save.Invoke(imageObj, new[] { (object)stream, pngFormat });
                    pngBytes = stream.ToArray();
                    if (DebugPasteLogs)
                        Debug.Log($"[AIChatWindow] Converted clipboard bitmap to PNG bytes. bytes={pngBytes.Length}");
                }

                if (imageObj is IDisposable disposable)
                    disposable.Dispose();
            }
            catch (Exception ex)
            {
                // Swallow clipboard access errors and report as no-image.
                if (DebugPasteLogs)
                    Debug.LogWarning($"[AIChatWindow] STA clipboard read failed: {ex.Message}\n{ex.StackTrace}");
                TryReadWpfClipboardImageBytes(ref pngBytes);
            }
        });

        staThread.SetApartmentState(ApartmentState.STA);
        staThread.Start();
        staThread.Join();

        return pngBytes;
    }

    private static void TryReadWpfClipboardImageBytes(ref byte[] pngBytes)
    {
        if (pngBytes != null && pngBytes.Length > 0) return;

        try
        {
            Type wpfClipboardType = ResolveType("System.Windows.Clipboard", "PresentationCore")
                                 ?? ResolveType("System.Windows.Clipboard", "PresentationFramework");
            if (wpfClipboardType == null)
            {
                if (DebugPasteLogs)
                    Debug.Log("[AIChatWindow] WPF Clipboard type not available.");
                return;
            }

            if (DebugPasteLogs)
            {
                MethodInfo containsData = wpfClipboardType.GetMethod("ContainsData", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
                if (containsData != null)
                {
                    bool hasDib = (bool)containsData.Invoke(null, new object[] { "DeviceIndependentBitmap" });
                    bool hasBitmap = (bool)containsData.Invoke(null, new object[] { "Bitmap" });
                    bool hasPng = (bool)containsData.Invoke(null, new object[] { "PNG" });
                    Debug.Log($"[AIChatWindow] WPF Clipboard.ContainsData: DIB={hasDib}, Bitmap={hasBitmap}, PNG={hasPng}");
                }
            }

            MethodInfo wpfContainsImage = wpfClipboardType.GetMethod("ContainsImage", BindingFlags.Public | BindingFlags.Static);
            MethodInfo wpfGetImage = wpfClipboardType.GetMethod("GetImage", BindingFlags.Public | BindingFlags.Static);
            if (wpfContainsImage == null || wpfGetImage == null)
            {
                if (DebugPasteLogs)
                    Debug.Log("[AIChatWindow] WPF Clipboard image methods unavailable.");
                return;
            }

            bool hasImage = (bool)wpfContainsImage.Invoke(null, null);
            if (DebugPasteLogs)
                Debug.Log($"[AIChatWindow] WPF Clipboard.ContainsImage()={hasImage}");
            if (!hasImage) return;

            object bitmapSource = wpfGetImage.Invoke(null, null);
            if (bitmapSource == null)
            {
                if (DebugPasteLogs)
                    Debug.Log("[AIChatWindow] WPF Clipboard.GetImage() returned null.");
                return;
            }

            Type bitmapSourceType = ResolveType("System.Windows.Media.Imaging.BitmapSource", "PresentationCore");
            Type bitmapFrameType = ResolveType("System.Windows.Media.Imaging.BitmapFrame", "PresentationCore");
            Type pngEncoderType = ResolveType("System.Windows.Media.Imaging.PngBitmapEncoder", "PresentationCore");

            if (bitmapSourceType == null || bitmapFrameType == null || pngEncoderType == null)
            {
                if (DebugPasteLogs)
                    Debug.Log("[AIChatWindow] WPF imaging types unavailable for PNG encoding.");
                return;
            }

            MethodInfo createFrame = bitmapFrameType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static, null, new[] { bitmapSourceType }, null);
            if (createFrame == null)
            {
                if (DebugPasteLogs)
                    Debug.Log("[AIChatWindow] BitmapFrame.Create(BitmapSource) not found.");
                return;
            }

            object frame = createFrame.Invoke(null, new[] { bitmapSource });
            object encoder = Activator.CreateInstance(pngEncoderType);
            if (frame == null || encoder == null)
            {
                if (DebugPasteLogs)
                    Debug.Log("[AIChatWindow] Failed to create WPF frame/encoder.");
                return;
            }

            PropertyInfo framesProp = pngEncoderType.GetProperty("Frames", BindingFlags.Public | BindingFlags.Instance);
            object framesCollection = framesProp?.GetValue(encoder);
            MethodInfo addFrame = framesCollection?.GetType().GetMethod("Add");
            if (framesCollection == null || addFrame == null)
            {
                if (DebugPasteLogs)
                    Debug.Log("[AIChatWindow] Unable to access PngBitmapEncoder.Frames collection.");
                return;
            }

            addFrame.Invoke(framesCollection, new[] { frame });

            using (var ms = new MemoryStream())
            {
                MethodInfo save = pngEncoderType.GetMethod("Save", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(Stream) }, null);
                if (save == null)
                {
                    if (DebugPasteLogs)
                        Debug.Log("[AIChatWindow] PngBitmapEncoder.Save(Stream) not found.");
                    return;
                }

                save.Invoke(encoder, new object[] { ms });
                pngBytes = ms.ToArray();
                if (DebugPasteLogs)
                    Debug.Log($"[AIChatWindow] WPF clipboard image converted to PNG bytes. bytes={pngBytes.Length}");
            }
        }
        catch (Exception ex)
        {
            if (DebugPasteLogs)
                Debug.LogWarning($"[AIChatWindow] WPF clipboard fallback failed: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private static Type ResolveType(string fullTypeName, string assemblyName)
    {
        Type t = Type.GetType($"{fullTypeName}, {assemblyName}");
        if (t != null) return t;

        try
        {
            Assembly asm = Assembly.Load(assemblyName);
            if (asm != null)
                t = asm.GetType(fullTypeName);
        }
        catch
        {
            // Return null below; caller handles fallback/logging.
        }

        return t;
    }
#endif

    private static bool IsImagePath(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" ||
               ext == ".tga" || ext == ".gif" || ext == ".tif" || ext == ".tiff" ||
               ext == ".webp";
    }

    private void HandleDragAndDropImages(Rect dropRect)
    {
        Event evt = Event.current;
        if (evt == null) return;
        if (!dropRect.Contains(evt.mousePosition)) return;

        if (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform)
            return;

        bool hasImageCandidate = false;

        if (DragAndDrop.paths != null)
        {
            foreach (string path in DragAndDrop.paths)
            {
                if (File.Exists(path) && IsImagePath(path))
                {
                    hasImageCandidate = true;
                    break;
                }
            }
        }

        if (!hasImageCandidate)
        {
            DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
            return;
        }

        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

        if (evt.type == EventType.DragPerform)
        {
            DragAndDrop.AcceptDrag();
            int added = 0;

            foreach (string path in DragAndDrop.paths)
            {
                if (TryAddImageFromPath(path))
                    added++;
            }

            if (DebugPasteLogs)
                Debug.Log($"[AIChatWindow] Drag-and-drop processed. added={added}, totalPending={pendingAttachments.Count}");

            Repaint();
        }

        evt.Use();
    }

    private bool TryAddImageFromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (!File.Exists(path))
        {
            if (DebugPasteLogs)
                Debug.Log("[AIChatWindow] Candidate is not an existing file path.");
            return false;
        }

        if (!IsImagePath(path))
        {
            if (DebugPasteLogs)
                Debug.Log("[AIChatWindow] Candidate file exists but is not a supported image extension.");
            return false;
        }

        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(tex, bytes, false))
            {
                if (DebugPasteLogs)
                    Debug.LogWarning($"[AIChatWindow] Failed to decode image bytes from path: {path}");
                DestroyImmediate(tex);
                return false;
            }

            pendingAttachments.Add(new ImageAttachment(tex, Path.GetFileName(path), isRuntime: true));
            if (DebugPasteLogs)
                Debug.Log($"[AIChatWindow] Added image from path: {path}");
            return true;
        }
        catch (Exception ex)
        {
            if (DebugPasteLogs)
                Debug.LogWarning($"[AIChatWindow] Failed reading image from path '{path}': {ex.Message}");
            return false;
        }
    }

    private static void DestroyRuntimeAttachments(System.Collections.Generic.List<ImageAttachment> attachments)
    {
        if (attachments == null) return;

        foreach (var attachment in attachments)
        {
            if (attachment == null || !attachment.IsRuntime || attachment.Texture == null) continue;
            DestroyImmediate(attachment.Texture);
        }
    }
}

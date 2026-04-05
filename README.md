# AI Chat Unity Package

This package contains the full AI Chat editor window scripts from your current setup.

It adds:

- `AI Chat/Open Chat Window`
- `AI Chat/Clear API Key`

## Install from Git URL

In Unity:

1. Open **Window > Package Manager**
2. Click the **+** button
3. Choose **Add package from git URL...**
4. Paste your repo URL, for example:

   `https://github.com/<user>/<repo>.git`

Then open the menu: **AI Chat > Open Chat Window**.

If your project already has local `AIChatWindow*.cs` files under `Assets/Editor`, remove them first to avoid duplicate menu registrations/classes.

## Repository Layout

The repository root must contain `package.json`.

```
package.json
README.md
Editor/
  AIChatWindow.cs
  AIChatWindow.GUI.cs
  AIChatWindow.ChatFlow.cs
  AIChatWindow.Attachments.cs
  SERC.AIChatDemo.Editor.asmdef
```

## Optional: pin a version tag

You can install a specific tag:

`https://github.com/<user>/<repo>.git#v1.0.0`

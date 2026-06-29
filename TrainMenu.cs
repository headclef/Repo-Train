using System.Collections;
using MenuLib;
using MenuLib.MonoBehaviors;
using TMPro;
using UnityEngine;

#pragma warning disable CS8618

namespace Train;

/// <summary>
/// A "Train" entry placed beside Improve's button in the main, escape, and lobby menus. It
/// opens a scrollable page showing every trainable stat's current level, the Improve cap, and
/// the progress to the next level. Added through MenuLib directly (rather than patching
/// Improve's menu) so the dependency stays one-directional.
/// </summary>
public static class TrainMenu
{
    private static REPOPopupPage _page;

    internal static void Initialize()
    {
        MenuAPI.AddElementToMainMenu(parent => AddTrainButton(parent));
        MenuAPI.AddElementToEscapeMenu(parent => AddTrainButton(parent));
        MenuAPI.AddElementToLobbyMenu(parent => AddTrainButton(parent));
    }

    private static void AddTrainButton(Transform parent)
    {
        var btn = MenuAPI.CreateREPOButton("Train", OpenTrainMenu, parent, new Vector2(0, 300));
        Train.Instance.StartCoroutine(FixRepoButton(btn, parent));
    }

    // Mirror Improve's button fix-up, but stack the Train button just above Improve's
    // bottom-right button so the two never overlap.
    private static IEnumerator FixRepoButton(REPOButton btn, Transform parent)
    {
        yield return null;
        Canvas.ForceUpdateCanvases();

        var tmp = btn.GetComponentInChildren<TextMeshProUGUI>(true);
        if (tmp != null)
        {
            var btnRt = btn.rectTransform;
            var textRt = (RectTransform)tmp.transform;

            textRt.SetParent(btnRt, false);
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.pivot = new Vector2(0.5f, 0.5f);
            textRt.offsetMin = new Vector2(14, 6);
            textRt.offsetMax = new Vector2(-14, -6);

            tmp.alignment = TextAlignmentOptions.Right | TextAlignmentOptions.Midline;
            tmp.enableWordWrapping = false;
        }

        if (parent is RectTransform parentRt)
        {
            float marginRight = 40f;
            float marginBottom = 20f + btn.rectTransform.rect.height + 10f;
            float x = parentRt.rect.width - btn.rectTransform.rect.width - marginRight;
            btn.rectTransform.localPosition = new Vector3(x, marginBottom, 0);
        }
    }

    private static void OpenTrainMenu()
    {
        Train.Instance.Config.Reload();

        _page = MenuAPI.CreateREPOPopupPage("Train", REPOPopupPage.PresetSide.Right, false, false);

        const float fontScale = 0.75f;

        AddScrollLabel($"Improve Level (cap): {SaveData.ImproveLevel()}", fontScale);

        foreach (var s in SaveData.Stats)
        {
            int eff = SaveData.EffectiveLevel(s);
            int trained = SaveData.TrainedLevel(s);
            int toNext = SaveData.ProgressToNext(s);

            string line = SaveData.IsCapped(s)
                ? $"{s.Display}:  Lv {eff}  (earned {trained}, capped)  -  {toNext} to next"
                : $"{s.Display}:  Lv {eff}  -  {toNext} to next";

            AddScrollLabel(line, fontScale);
        }

        var resetBtn = MenuAPI.CreateREPOButton("Reset Train", () =>
            MenuAPI.OpenPopup("Reset Train", Color.red,
                "Wipe ALL trained progress? Your earned stat levels will be lost. This cannot be undone.",
                () =>
                {
                    SaveData.ResetAll();
                    TrainApplier.Apply();
                    _page.ClosePage(true);
                }),
            _page.transform, default);
        if (resetBtn.labelTMP != null) resetBtn.labelTMP.fontSize *= fontScale;
        _page.AddElementToScrollView(resetBtn.rectTransform);

        var closeBtn = MenuAPI.CreateREPOButton("Close", () => _page.ClosePage(true), _page.transform, default);
        if (closeBtn.labelTMP != null) closeBtn.labelTMP.fontSize *= fontScale;
        _page.AddElementToScrollView(closeBtn.rectTransform);

        _page.scrollView.spacing = 5;
        _page.OpenPage(false);
    }

    private static void AddScrollLabel(string text, float fontScale)
    {
        var lbl = MenuAPI.CreateREPOLabel(text, _page.transform, default);
        if (lbl.labelTMP != null) lbl.labelTMP.fontSize *= fontScale;
        _page.AddElementToScrollView(lbl.rectTransform);
    }
}

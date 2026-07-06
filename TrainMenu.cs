using System.Collections;
using MenuLib;
using MenuLib.MonoBehaviors;
using MenuLib.Structs;
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

        _page = MenuAPI.CreateREPOPopupPage("Train", shouldCachePage: false, pageDimmerVisibility: true, spacing: 5f);

        const float fontScale = 0.75f;

        // MenuLib gathers the template's visible parts (Panel background, header, scroll box)
        // under rectTransform ("Page Content"); the Panel keeps its prefab-local position when
        // reparented, so its localPosition + rect is the page's visible area in content space.
        var contentRt = _page.rectTransform;
        var panelRt = contentRt.Find("Panel") as RectTransform;
        var holderRt = _page.transform.parent as RectTransform;

        // The template panel is a narrow portrait box; widen it to the screen's aspect ratio
        // so the page truly fills the screen once scaled (a uniform scale alone maxes out on
        // height and leaves the width untouched).
        float extraW = 0f;
        if (panelRt != null && holderRt != null && holderRt.rect.height > 0f && panelRt.rect.height > 0f)
        {
            Rect p0 = panelRt.rect;
            float targetW = p0.height * (holderRt.rect.width / holderRt.rect.height);
            extraW = Mathf.Max(0f, targetW - p0.width);

            // Widen the panel background around its own centre, whatever its pivot is.
            panelRt.sizeDelta += new Vector2(extraW, 0f);
            panelRt.localPosition -= new Vector3((0.5f - panelRt.pivot.x) * extraW, 0f, 0f);

            // Widen the scroll mask with it (negative padding expands past the template
            // default), keeping the bottom strip clear for the pinned buttons.
            _page.maskPadding = new Padding(-extraW / 2f, 0f, -extraW / 2f, 60f);

            // The mask setter only re-tracks the scrollbar vertically — push it to the new
            // right edge ourselves.
            _page.scrollBarRectTransform.localPosition += new Vector3(extraW / 2f, 0f, 0f);
        }
        else
        {
            _page.maskPadding = new Padding(0f, 0f, 0f, 60f);
        }

        // The mask grew extraW/2 to the left; start the rows at its new left edge.
        float labelX = -extraW / 2f;

        AddScrollLabel($"Improve Level (cap): {SaveData.ImproveLevel()}", fontScale, labelX);

        foreach (var s in SaveData.Stats)
        {
            int eff = SaveData.EffectiveLevel(s);
            int trained = SaveData.TrainedLevel(s);
            int toNext = SaveData.ProgressToNext(s);

            string line = SaveData.IsCapped(s)
                ? $"{s.Display}:  Lv {eff}  (earned {trained}, capped)  -  {toNext} to next"
                : $"{s.Display}:  Lv {eff}  -  {toNext} to next";

            AddScrollLabel(line, fontScale, labelX);
        }

        // Pin Reset/Close to the bottom of the panel, outside the scroll view, so they are
        // always visible without scrolling — one on each side of the panel's centre line.
        // Parented to Page Content so they scale with it.
        Rect panel = panelRt != null ? panelRt.rect : new Rect(0, 0, 340, 260);
        Vector2 panelMin = panelRt != null ? (Vector2)panelRt.localPosition + panel.min : panel.min;
        float rowY = panelMin.y + 20f;
        float centerX = panelMin.x + panel.width / 2f;

        var resetBtn = MenuAPI.CreateREPOButton("Reset Train", () =>
            MenuAPI.OpenPopup("Reset Train", Color.red,
                "Wipe ALL trained progress? Your earned stat levels will be lost. This cannot be undone.",
                () =>
                {
                    SaveData.ResetAll();
                    TrainApplier.Apply();
                    _page.ClosePage(true);
                }),
            contentRt, new Vector2(centerX - 190f, rowY));
        if (resetBtn.labelTMP != null) resetBtn.labelTMP.fontSize *= fontScale;
        resetBtn.rectTransform.sizeDelta = new Vector2(160, resetBtn.rectTransform.sizeDelta.y);

        var closeBtn = MenuAPI.CreateREPOButton("Close", () => _page.ClosePage(true),
            contentRt, new Vector2(centerX + 30f, rowY));
        if (closeBtn.labelTMP != null) closeBtn.labelTMP.fontSize *= fontScale;
        closeBtn.rectTransform.sizeDelta = new Vector2(160, closeBtn.rectTransform.sizeDelta.y);

        // Full-page: scale the whole page up to (nearly) fill the menu screen and centre it.
        // Uniform scale keeps the template's look and grows the fonts with it.
        float scale = 1.3f;
        if (holderRt != null && holderRt.rect.width > 0f && panel.width > 0f && panel.height > 0f)
            scale = Mathf.Min(holderRt.rect.width / panel.width, holderRt.rect.height / panel.height) * 0.92f;
        contentRt.localScale = new Vector3(scale, scale, 1f);

        if (panelRt != null && holderRt != null)
        {
            Vector3 target = holderRt.TransformPoint(holderRt.rect.center);
            Vector3 current = panelRt.TransformPoint(panelRt.rect.center);
            contentRt.position += target - current;
        }

        _page.OpenPage(false);
    }

    private static void AddScrollLabel(string text, float fontScale, float x)
    {
        var lbl = MenuAPI.CreateREPOLabel(text, _page.transform, default);
        if (lbl.labelTMP != null) lbl.labelTMP.fontSize *= fontScale;
        _page.AddElementToScrollView(lbl.rectTransform, new Vector2(x, 0f));
    }
}

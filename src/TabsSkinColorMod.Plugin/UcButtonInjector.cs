using System;
using System.Collections.Generic;
using System.Text;
using Landfall.TABS;
using UnityEngine;
using UnityEngine.UI;

namespace TabsSkinColorMod.Plugin
{
    /// <summary>
    /// Injects a real uGUI button into the Unit Creator's own canvas, positioned next
    /// to the red/blue team color swatches. The visible swatches live on
    /// 'RedBlue' (UnitEditorTeamToggle) with children 'Red'/'Blue' (UnitEditorTeamButton),
    /// directly under the 'Unit Editor UI' canvas root. We clone the Red swatch, strip
    /// its team-click behaviour, tint it green and park it beside the pair. A standalone
    /// overlay button is the last-resort fallback.
    /// </summary>
    internal sealed class UcButtonInjector : MonoBehaviour
    {
        public const string ButtonName = "SkinColorModButton";
        private static readonly Color32 SwatchColor = new Color32(72, 199, 142, 255);
        private static readonly Color32 SwatchHover = new Color32(110, 226, 173, 255);
        private static readonly Color32 SwatchPressed = new Color32(52, 158, 110, 255);

        private GameObject _injected;
        private Canvas _overlayCanvas;
        private float _nextScan;
        private bool _loggedCandidates;

        private void Update()
        {
            var plugin = SkinColorModPlugin.Instance;
            if (plugin == null || !plugin.UcButtonEnabled.Value) return;

            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + 0.5f;

            var anchor = FindSwatchParent();
            bool ucOpen = anchor != null && anchor.gameObject != null && anchor.gameObject.activeInHierarchy;

            if (!ucOpen)
            {
                if (_injected != null) { Destroy(_injected); _injected = null; }
                return;
            }

            if (_injected != null) return;

            try
            {
                _injected = InjectClone(anchor, plugin);
                if (_injected != null)
                    SkinColorModPlugin.Log.LogInfo("[SkinColor] injected SKIN COLOR button next to the team swatches (RedBlue)");
            }
            catch (Exception e)
            {
                SkinColorModPlugin.Log.LogWarning("[SkinColor] swatch clone failed (" + e.Message + "); using standalone fallback");
                _injected = CreateStandaloneButton(plugin);
            }

            if (_injected == null)
            {
                if (!_loggedCandidates)
                {
                    _loggedCandidates = true;
                    LogToggleCandidates();
                }
                _injected = CreateStandaloneButton(plugin);
                if (_injected != null)
                    SkinColorModPlugin.Log.LogInfo("[SkinColor] injected standalone top-right button");
            }
        }

        // ---------------------------------------------------------------- anchor

        /// <summary>The 'RedBlue' object that hosts the visible red/blue team swatches.</summary>
        private static UnitEditorTeamToggle FindSwatchParent()
        {
            try { return FindObjectOfType<UnitEditorTeamToggle>(); }
            catch { return null; }
        }

        // ------------------------------------------------------------- injection

        private static GameObject InjectClone(UnitEditorTeamToggle swatchPair, SkinColorModPlugin plugin)
        {
            // Pick the Red child (fallback: Blue, then any UnitEditorTeamButton child).
            Transform swatch = swatchPair.transform.Find("Red");
            if (swatch == null) swatch = swatchPair.transform.Find("Blue");
            if (swatch == null)
            {
                foreach (Transform child in swatchPair.transform)
                    if (child.GetComponent("UnitEditorTeamButton") != null) { swatch = child; break; }
            }
            if (swatch == null)
            {
                SkinColorModPlugin.Log.LogWarning("[SkinColor] RedBlue found but no Red/Blue child swatches");
                return null;
            }

            var pairRect = swatchPair.transform as RectTransform;
            var blue = swatchPair.transform.Find("Blue");

            // Parent the clone INSIDE RedBlue (same parent as Red) so it inherits the
            // exact same anchor/scale coordinate space — no math that can go wrong.
            var go = UnityEngine.Object.Instantiate(swatch.gameObject, swatchPair.transform, false);
            go.name = ButtonName;

            // Strip the team-button behaviour so it doesn't switch teams when clicked.
            foreach (var comp in go.GetComponents<Component>())
            {
                if (comp == null) continue;
                var type = comp.GetType();
                if (type.Name == "UnitEditorTeamButton" || type.Name == "UISounds")
                    Destroy(comp);
            }

            var image = go.GetComponent<Image>();
            var button = go.GetComponent<Button>() ?? go.AddComponent<Button>();
            if (image != null)
            {
                button.targetGraphic = image;
                var colors = button.colors;
                colors.normalColor = SwatchColor;
                colors.highlightedColor = SwatchHover;
                colors.pressedColor = SwatchPressed;
                colors.selectedColor = SwatchColor;
                colors.disabledColor = SwatchColor;
                button.colors = colors;
                image.color = SwatchColor;
            }
            button.onClick.AddListener(() =>
            {
                if (plugin != null && plugin.Editor != null)
                    plugin.Editor.ToggleWindow();
            });

            // Position: park it to the right of Red using the Blue↔Red spacing as
            // the swatch pitch, so it lines up perfectly with the vanilla pair.
            var myRect = go.transform as RectTransform;
            var srcRect = swatch as RectTransform;
            float pitch = 0f;
            if (myRect != null && srcRect != null)
            {
                pitch = pairRect != null && blue != null && blue as RectTransform != null
                    ? Mathf.Abs(((RectTransform)blue).anchoredPosition.x - srcRect.anchoredPosition.x)
                    : srcRect.rect.width * 1.2f;
                if (pitch <= 0f) pitch = srcRect.rect.width * 1.2f;
                myRect.anchoredPosition = srcRect.anchoredPosition + new Vector2(pitch, 0f);
            }

            SkinColorModPlugin.Log.LogInfo(string.Format(
                "[SkinColor] clone placed: src anchored={0} size={1} pitch={2} pair anchored={3} pairSize={4}",
                srcRect.anchoredPosition, srcRect.rect.size, pitch,
                pairRect != null ? pairRect.anchoredPosition.ToString() : "?",
                pairRect != null ? pairRect.rect.size.ToString() : "?"));

            go.SetActive(true);
            return go;
        }

        /// <summary>
        /// Guaranteed-visible fallback: own overlay canvas, button pinned top-right,
        /// next to the vanilla team swatches.
        /// </summary>
        private GameObject CreateStandaloneButton(SkinColorModPlugin plugin)
        {
            try
            {
                if (_overlayCanvas == null)
                {
                    var cgo = new GameObject("TabsSkinColorModOverlay", typeof(Canvas), typeof(CanvasScaler));
                    UnityEngine.Object.DontDestroyOnLoad(cgo);
                    var canvas = cgo.GetComponent<Canvas>();
                    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    canvas.sortingOrder = 30000;
                    cgo.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                    _overlayCanvas = canvas;
                }

                var go = new GameObject(ButtonName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
                var rect = go.GetComponent<RectTransform>();
                rect.SetParent(_overlayCanvas.transform, false);
                rect.anchorMin = new Vector2(1f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(1f, 1f);
                rect.sizeDelta = new Vector2(44f, 44f);
                rect.anchoredPosition = new Vector2(-160f, -12f);

                var image = go.GetComponent<Image>();
                var button = go.GetComponent<Button>();
                button.targetGraphic = image;
                var colors = button.colors;
                colors.normalColor = SwatchColor;
                colors.highlightedColor = SwatchHover;
                colors.pressedColor = SwatchPressed;
                colors.selectedColor = SwatchColor;
                colors.disabledColor = SwatchColor;
                button.colors = colors;
                image.color = SwatchColor;
                button.onClick.AddListener(() =>
                {
                    if (plugin != null && plugin.Editor != null)
                        plugin.Editor.ToggleWindow();
                });
                return go;
            }
            catch (Exception e)
            {
                SkinColorModPlugin.Log.LogWarning("[SkinColor] standalone button failed too: " + e);
                return null;
            }
        }

        // ----------------------------------------------------------- diagnostics

        private static void LogToggleCandidates()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("[SkinColor] no team swatch anchor found while UC is open; scene Toggles:");
                int n = 0;
                foreach (var tog in Resources.FindObjectsOfTypeAll<Toggle>())
                {
                    if (tog == null || tog.gameObject == null) continue;
                    if (tog.gameObject.hideFlags != HideFlags.None || !tog.gameObject.scene.isLoaded) continue;
                    sb.AppendLine("  toggle: '" + tog.name + "'  active: " + tog.gameObject.activeInHierarchy);
                    if (++n >= 40) { sb.AppendLine("  ..."); break; }
                }
                if (n == 0) sb.AppendLine("  (none)");
                SkinColorModPlugin.Log.LogInfo(sb.ToString());
            }
            catch (Exception e)
            {
                SkinColorModPlugin.Log.LogWarning("[SkinColor] candidate logging failed: " + e);
            }
        }
    }
}

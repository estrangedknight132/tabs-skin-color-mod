using System.Collections.Generic;
using Landfall.TABS;
using TabsSkinColorMod.Core;
using UnityEngine;

namespace TabsSkinColorMod.Plugin
{
    internal sealed class TrackedUnit
    {
        public Unit Unit;
        public Color32 Color;
        public readonly List<Renderer> Renderers = new List<Renderer>();
        public float NextCheck;
    }

    /// <summary>
    /// Central MonoBehaviour: consumes queued Unit.Awake events, applies saved colors
    /// after the game's own color init has run, and (optionally) re-applies colors that
    /// the game overwrites later. Also keeps a "last unit" reference for the editor UI.
    /// </summary>
    internal sealed class UnitTracker : MonoBehaviour
    {
        private struct QueuedUnit
        {
            public Unit Unit;
            public float DueTime;
        }

        private readonly Queue<QueuedUnit> _pending = new Queue<QueuedUnit>();
        private readonly HashSet<Unit> _pendingSet = new HashSet<Unit>();
        private readonly List<TrackedUnit> _tracked = new List<TrackedUnit>();
        private const int MaxTracked = 300;

        public Unit LastUnit { get; private set; }

        public void QueueUnit(Unit unit)
        {
            if (unit == null) return;
            lock (_pending)
            {
                if (_pending.Count > 512) return; // safety valve
                if (!_pendingSet.Add(unit)) return; // already queued (SetTeamColor fires per LOD)
                _pending.Enqueue(new QueuedUnit { Unit = unit, DueTime = Time.time + 0.1f });
            }
        }

        private int _lastImmediateFrame = -1;
        private Unit _lastImmediateUnit;

        /// <summary>Applies the saved color synchronously (same frame as the game's team
        /// assignment) so spawned units never display the original color first.</summary>
        public void ApplySavedNow(Unit unit)
        {
            if (unit == null) return;
            if (_lastImmediateFrame == Time.frameCount && ReferenceEquals(_lastImmediateUnit, unit)) return;
            _lastImmediateFrame = Time.frameCount;
            _lastImmediateUnit = unit;

            var plugin = SkinColorModPlugin.Instance;
            if (plugin == null) return;
            ApplySaved(unit, plugin, new PatternMatcher(plugin.MaterialNameFilter.Value));
        }

        private void Update()
        {
            var plugin = SkinColorModPlugin.Instance;
            if (plugin == null) return;
            var filter = new PatternMatcher(plugin.MaterialNameFilter.Value);

            lock (_pending)
            {
                while (_pending.Count > 0 && Time.time >= _pending.Peek().DueTime)
                {
                    var q = _pending.Dequeue();
                    _pendingSet.Remove(q.Unit);
                    ProcessUnit(q.Unit, plugin, filter);
                }
            }

            if (plugin.StickyReapply.Value)
                StickyCheck(plugin, filter);

            Cleanup();
        }

        private readonly HashSet<Unit> _loggedApplied = new HashSet<Unit>();
        private readonly HashSet<Unit> _loggedNoTargets = new HashSet<Unit>();

        private void ProcessUnit(Unit unit, SkinColorModPlugin plugin, PatternMatcher filter)
        {
            ApplySaved(unit, plugin, filter);
        }

        private void ApplySaved(Unit unit, SkinColorModPlugin plugin, PatternMatcher filter)
        {
            if (unit == null) return;
            // The UC's visible display body can carry a Unit with a STALE blueprint key
            // (the template it was cloned from). Tinting it from the store re-applies the
            // previous unit's color forever — the editor's own wanted-color path handles
            // preview bodies, so the store path must skip them.
            if (SkinEditorUI.IsUcPreviewUnit(unit) || SkinEditorUI.IsUnderUcManager(unit.transform))
            {
                SkinEditorUI.MarkPreviewUnit(unit);
                return;
            }
            LastUnit = unit;

            bool isUnitCreator = IsUnitCreatorUnit(unit);
            if (isUnitCreator && !plugin.EnableInUnitCreator.Value) return;
            if (!isUnitCreator && !plugin.EnableInBattle.Value) return;

            var key = Refs.UnitKey(unit);
            // UC TEST UNIT mode: enemies spawned for the test keep vanilla (only the
            // tested unit itself may use its saved color).
            if (!SkinApplier.StoreColorAllowedForKey(key)) return;
            if (!plugin.Store.TryGetColor(key, out var hex)) return;
            if (!RgbaColor.TryParse(hex, out var rgba)) return;

            var color = rgba.ToColor32();
            if (SkinApplier.ApplyColor(unit, color, filter, out var applied))
            {
                TrackUnit(unit, color, applied);
                if (_loggedApplied.Add(unit))
                    SkinColorModPlugin.Log.LogInfo("[SkinColor] applied " + hex + " to spawned unit '" + key + "' (" + applied.Count + " targets)");
            }
            else if (_loggedNoTargets.Add(unit))
            {
                SkinColorModPlugin.Log.LogInfo("[SkinColor] NO skin targets on spawned unit '" + key + "'");
            }
        }

        private bool IsUnitCreatorUnit(Unit unit)
        {
            try
            {
                var handler = UnitEditorHandler.Instance;
                if (handler == null) return false;
                var root = handler.UnitRoot;
                if (root == null) return false;
                return unit.gameObject == root || unit.transform.root.gameObject == root;
            }
            catch
            {
                return false;
            }
        }

        private void TrackUnit(Unit unit, Color32 color, List<SkinTarget> applied)
        {
            var tracked = FindTracked(unit);
            if (tracked == null)
            {
                tracked = new TrackedUnit { Unit = unit };
                _tracked.Add(tracked);
            }
            tracked.Color = color;
            tracked.Renderers.Clear();
            foreach (var t in applied)
                if (t.Renderer != null && !tracked.Renderers.Contains(t.Renderer))
                    tracked.Renderers.Add(t.Renderer);
            tracked.NextCheck = Time.time + 1f;
        }

        private TrackedUnit FindTracked(Unit unit)
        {
            foreach (var t in _tracked)
                if (ReferenceEquals(t.Unit, unit))
                    return t;
            return null;
        }

        private void StickyCheck(SkinColorModPlugin plugin, PatternMatcher filter)
        {
            foreach (var tracked in _tracked)
            {
                if (tracked.Unit == null) continue; // Unity fake-null
                if (tracked.Unit.dead) continue;    // don't fight the death-gray fade
                if (SkinEditorUI.IsUcPreviewUnit(tracked.Unit)) continue; // editor owns the color
                if (Time.time < tracked.NextCheck) continue;
                tracked.NextCheck = Time.time + 1f;

                var expected = (Color)(tracked.Color);
                bool drifted = false;
                foreach (var rend in tracked.Renderers)
                {
                    if (rend == null) continue;
                    var mats = rend.materials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var m = mats[i];
                        if (m == null) continue;
                        if (!m.HasProperty("_Color")) continue;
                        if (!ColorsClose(m.color, expected))
                        {
                            if (SkinApplier.TryGetOriginalColor(m, out _)) { /* keep tracking */ }
                            m.color = expected;
                            drifted = true;
                        }
                    }
                }
                if (drifted)
                    SkinColorModPlugin.Log.LogDebug("[SkinColor] re-applied color on '" + Refs.UnitKey(tracked.Unit) + "' after game overwrite");
            }
        }

        private void Cleanup()
        {
            for (int i = _tracked.Count - 1; i >= 0; i--)
            {
                var t = _tracked[i];
                if (t.Unit == null || t.Renderers.Count == 0 || t.Renderers.TrueForAll(r => r == null))
                    _tracked.RemoveAt(i);
            }
            while (_tracked.Count > MaxTracked)
                _tracked.RemoveAt(0);

            _loggedApplied.RemoveWhere(u => u == null);
            _loggedNoTargets.RemoveWhere(u => u == null);
        }

        private static bool ColorsClose(Color a, Color b)
        {
            const float eps = 1f / 255f / 2f;
            return Mathf.Abs(a.r - b.r) < eps && Mathf.Abs(a.g - b.g) < eps && Mathf.Abs(a.b - b.b) < eps && Mathf.Abs(a.a - b.a) < eps;
        }
    }
}

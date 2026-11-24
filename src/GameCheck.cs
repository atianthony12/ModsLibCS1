using System;
using System.Collections.Generic;
using System.Reflection;
using ColossalFramework.UI;
using UnityEngine;

namespace ModsLibCS1
{
    /// <summary>
    /// The game-update safeguard (GAME-UPDATE-SAFEGUARD-PLAN.md in the mods' folder, 2026-11-24):
    /// at start-up a mod checks that everything it needs of the game is still there -- its Harmony
    /// targets, the members it reads by name -- and if anything is missing it undoes all its
    /// patches, runs nothing (or, for a mod whose save data must outlive it, only what keeps that
    /// data) and shows one pop-up. All or nothing: a half-patched mod is what breaks a game.
    /// </summary>
    /// <remarks>
    /// The same file in every mod: compiled in through ModsLibCS1.props, or copied unchanged by a
    /// mod without the library. It names no Harmony type (some mods have none): patching and
    /// undoing are the mod's own delegates. Not fool-proof: a member that keeps its name and
    /// signature but works differently is not caught.
    /// <code>
    /// var check = new GameCheck("My Mod", MyLog.Error);
    /// check.RequireField(typeof(RenderManager), "m_renderables", BindingFlags.Static | BindingFlags.NonPublic, typeof(FastList&lt;IRenderableManager&gt;));
    /// check.RequireMethod(typeof(PropTool), "RenderOverlay", BindingFlags.Static | BindingFlags.Public, new[] { ... }, "cameraInfo", "info", ...);
    /// check.ForcedFailure();                                   // GAMECHECK_FAIL=My Mod (testing)
    /// // later, when Harmony is ready, each patch class on its own:
    /// check.Patch("MyPatch", () => harmony.CreateClassProcessor(typeof(MyPatch)).Patch());
    /// check.UndoIfFailed(() => harmony.UnpatchAll(MyHarmonyId)); // never UnpatchAll() without the id
    /// if (check.Failed) { check.Summary(MyMod.Version); check.ShowOnce(title, text); }
    /// </code>
    /// </remarks>
    public sealed class GameCheck
    {
        /// <summary>The environment variable that makes a check fail on purpose: its value is the mod's name (testing only).</summary>
        public const string FailVariable = "GAMECHECK_FAIL";

        private readonly string _mod;
        private readonly Action<string> _error;
        private readonly List<string> _problems = new List<string>();
        private bool _undone;

        /// <param name="modName">The mod's name as a player reads it (also what <see cref="FailVariable"/> names).</param>
        /// <param name="error">Where each problem's ERROR line goes (the mod's log).</param>
        public GameCheck(string modName, Action<string> error)
        {
            _mod = modName;
            _error = error;
        }

        /// <summary>Anything missing: the mod is to switch itself off.</summary>
        public bool Failed
        {
            get { return _problems.Count > 0; }
        }

        /// <summary>Each problem, as logged.</summary>
        public IList<string> Problems
        {
            get { return _problems.AsReadOnly(); }
        }

        /// <summary>The game's version ("1.21.2-f5"), or "unknown".</summary>
        public static string GameVersion
        {
            get
            {
                try
                {
                    return BuildConfig.applicationVersion;
                }
                catch (Exception)
                {
                    return "unknown";
                }
            }
        }

        /// <summary>Records a problem and logs it.</summary>
        public void Problem(string what)
        {
            _problems.Add(what);
            Say("game check: " + what);
        }

        /// <summary>
        /// A field the mod reads by name: there, with these binding flags, of exactly this type
        /// (<paramref name="fieldType"/> null: any type). Null and a problem if not.
        /// </summary>
        public FieldInfo RequireField(Type type, string name, BindingFlags flags, Type fieldType)
        {
            try
            {
                FieldInfo field = type.GetField(name, flags);
                if (field == null)
                {
                    Problem(type.FullName + "." + name + " (a field) is missing");
                    return null;
                }

                if (fieldType != null && field.FieldType != fieldType)
                {
                    Problem(type.FullName + "." + name + " is a " + field.FieldType.FullName + " now, not a " + fieldType.FullName);
                    return null;
                }

                return field;
            }
            catch (Exception e)
            {
                Problem(type.FullName + "." + name + " could not be looked up: " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// A method the mod patches or calls by name: there, with these binding flags and exactly
        /// these parameter types (by-ref ones as typeof(float).MakeByRefType()), and -- for a
        /// Harmony patch, which binds its arguments by name -- these parameter names, in order
        /// (none given: not checked). Null and a problem if not.
        /// </summary>
        public MethodInfo RequireMethod(Type type, string name, BindingFlags flags, Type[] parameters, params string[] parameterNames)
        {
            string what = type.FullName + "." + name + "(" + Describe(parameters) + ")";
            try
            {
                MethodInfo method = type.GetMethod(name, flags, null, parameters, null);
                if (method == null)
                {
                    Problem(what + " is missing or its parameters changed");
                    return null;
                }

                if (parameterNames != null && parameterNames.Length > 0)
                {
                    ParameterInfo[] p = method.GetParameters();
                    for (int i = 0; i < parameterNames.Length; i++)
                    {
                        if (i >= p.Length || p[i].Name != parameterNames[i])
                        {
                            Problem(what + ": its parameter " + (i + 1) + " is '" + (i < p.Length ? p[i].Name : "none") + "' now, not '" + parameterNames[i] + "'");
                            return null;
                        }
                    }
                }

                return method;
            }
            catch (Exception e)
            {
                Problem(what + " could not be looked up: " + e.Message);
                return null;
            }
        }

        /// <summary>A type the mod needs by name, in the game's assembly holding <paramref name="sibling"/>. Null and a problem if missing.</summary>
        public Type RequireType(Type sibling, string fullName)
        {
            try
            {
                Type t = sibling.Assembly.GetType(fullName, false);
                if (t == null)
                    Problem(fullName + " (a type) is missing");
                return t;
            }
            catch (Exception e)
            {
                Problem(fullName + " could not be looked up: " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// Testing: with the environment variable <see cref="FailVariable"/> set to this mod's
        /// name, one check of a member that does not exist -- the whole switch-off path runs.
        /// </summary>
        public void ForcedFailure()
        {
            string wanted;
            try
            {
                wanted = Environment.GetEnvironmentVariable(FailVariable);
            }
            catch (Exception)
            {
                return;
            }

            if (!string.IsNullOrEmpty(wanted) && string.Equals(wanted.Trim(), _mod, StringComparison.OrdinalIgnoreCase))
                RequireField(typeof(GameCheck), "GameCheckForcedFailure", BindingFlags.Static | BindingFlags.NonPublic, null);
        }

        /// <summary>
        /// One patch class applied (the mod's delegate: harmony.CreateClassProcessor(t).Patch()),
        /// caught: a throw is a problem. Nothing is applied once the check has failed. True if it applied.
        /// </summary>
        public bool Patch(string name, Action apply)
        {
            if (Failed)
                return false;

            try
            {
                apply();
                return true;
            }
            catch (Exception e)
            {
                Exception inner = e;
                while (inner.InnerException != null)
                    inner = inner.InnerException;
                Problem("the patch " + name + " could not be applied: " + inner.GetType().Name + ": " + inner.Message);
                return false;
            }
        }

        /// <summary>
        /// When the check has failed, undoes every patch of the mod's (its delegate:
        /// harmony.UnpatchAll(itsId) -- never UnpatchAll() without the id, which takes every other
        /// mod's too), once. True if it had failed.
        /// </summary>
        public bool UndoIfFailed(Action undo)
        {
            if (!Failed)
                return false;

            if (!_undone && undo != null)
            {
                _undone = true;
                try
                {
                    undo();
                }
                catch (Exception e)
                {
                    Say("game check: undoing the patches threw: " + e.Message);
                }
            }

            return true;
        }

        /// <summary>The line a player's report needs: what switched itself off, on which game.</summary>
        public void Summary(string modVersion)
        {
            Say(_mod + " " + modVersion + " has switched itself off: " + _problems.Count + " thing(s) it relies on changed in the game " +
                GameVersion + " (listed above)");
        }

        /// <summary>
        /// The pop-up, ONCE per launch: the game's own ExceptionPanel, as soon as there is a UI to
        /// show it in and nothing is loading (a mod is enabled before the main menu exists).
        /// </summary>
        public void ShowOnce(string title, string message)
        {
            if (_shown)
                return;
            _shown = true;

            try
            {
                var go = new GameObject("GameCheck " + _mod);
                UnityEngine.Object.DontDestroyOnLoad(go);
                Popup popup = go.AddComponent<Popup>();
                popup.Title = title;
                popup.Message = message;
                popup.Error = _error;
            }
            catch (Exception e)
            {
                Say("game check: could not show the pop-up: " + e.Message);
            }
        }

        private bool _shown;

        private void Say(string line)
        {
            try
            {
                if (_error != null)
                {
                    _error(line);
                    return;
                }
            }
            catch (Exception)
            {
                // fall through to Unity's log
            }

            try
            {
                Debug.LogError("[" + _mod + "] " + line);
            }
            catch (Exception)
            {
                // nothing left to say it with
            }
        }

        private static string Describe(Type[] types)
        {
            if (types == null)
                return "";
            var names = new string[types.Length];
            for (int i = 0; i < types.Length; i++)
                names[i] = types[i] != null ? types[i].Name : "?";
            return string.Join(", ", names);
        }

        /// <summary>Waits for the UI, shows the panel, goes.</summary>
        private sealed class Popup : MonoBehaviour
        {
            public string Title, Message;
            public Action<string> Error;
            private int _tries;

            private void Update()
            {
                try
                {
                    if (UIView.GetAView() == null || UIView.library == null)
                        return;
                    if (ColossalFramework.Singleton<LoadingManager>.exists && ColossalFramework.Singleton<LoadingManager>.instance.m_currentlyLoading)
                        return;

                    ExceptionPanel panel = UIView.library.ShowModal<ExceptionPanel>("ExceptionPanel");
                    if (panel == null)
                    {
                        if (++_tries < 600)
                            return;
                    }
                    else
                    {
                        panel.SetMessage(Title, Message, true);
                    }
                }
                catch (Exception e)
                {
                    if (Error != null)
                        Error("game check: the pop-up could not be shown: " + e.Message);
                }

                Destroy(gameObject);
            }
        }
    }
}

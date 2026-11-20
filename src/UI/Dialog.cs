using System;
using ColossalFramework.UI;
using UnityEngine;

namespace ModsLibCS1.UI
{
    /// <summary>
    /// A modal question in a mod's own panel look: a title bar, a paragraph or a few and a row of buttons,
    /// built by a <see cref="Builder"/>, in front of everything with the game's own dimming behind it.
    /// </summary>
    /// <remarks>
    /// Instead of the game's ConfirmPanel, which always looks like the game's and has only yes and no.
    /// Modal through UIView.PushModal, as the game's own panels are: nothing else takes input until it
    /// is answered. The answer is the index of the button clicked, or <see cref="Closed"/> for the close
    /// button and Escape. The owner closes it with <see cref="Close"/> when it goes away first (a city
    /// unloading): a modal never popped leaves the game's UI taking no input.
    /// </remarks>
    public sealed class Dialog
    {
        /// <summary>The answer when the close button or Escape closed it.</summary>
        public const int Closed = -1;

        public const float Width = 520f;
        private const float ButtonHeight = 32f;
        private const float ButtonGap = 8f;
        private const float ButtonMinWidth = 110f;

        private UIPanel _panel;
        private UIComponent _effect;
        private bool _effectShown;
        private Action<int> _onAnswer;

        /// <summary>True until it is answered or closed.</summary>
        public bool IsOpen
        {
            get { return _panel != null; }
        }

        /// <summary>A yellow for a paragraph that warns, readable on every skin's background.</summary>
        public static readonly Color32 WarningYellow = new Color32(255, 214, 92, 255);

        /// <summary>One paragraph of a dialog: its text, in the skin's text colour or <see cref="Colour"/>.</summary>
        public struct Paragraph
        {
            public string Text;
            public Color32? Colour;

            public Paragraph(string text, Color32? colour = null)
            {
                Text = text;
                Colour = colour;
            }
        }

        /// <summary>Space between two paragraphs.</summary>
        private const float ParagraphGap = 10f;

        /// <summary>
        /// Shows a dialog: <paramref name="buttons"/> left to right at its bottom right, the one at
        /// <paramref name="accent"/> in the skin's accent look (-1 for none). <paramref name="onAnswer"/> runs
        /// once, after it has closed, with the index clicked or <see cref="Closed"/>. Null when the game's UI
        /// is not there.
        /// </summary>
        public static Dialog Show(Builder ui, string title, string message, string[] buttons, int accent,
            Action<int> onAnswer)
        {
            return Show(ui, title, new[] { new Paragraph(message) }, buttons, accent, onAnswer);
        }

        /// <summary>
        /// As <see cref="Show(Builder, string, string, string[], int, Action{int})"/>, with the message in
        /// paragraphs, each in its own colour (a warning in <see cref="WarningYellow"/>, say). Empty ones are left out.
        /// </summary>
        public static Dialog Show(Builder ui, string title, Paragraph[] paragraphs, string[] buttons, int accent,
            Action<int> onAnswer)
        {
            UIView view = UIView.GetAView();
            if (view == null || ui == null)
                return null;

            Dialog dialog = new Dialog();
            dialog._onAnswer = onAnswer;
            dialog.Build(view, ui, title, paragraphs ?? new Paragraph[0], buttons ?? new string[0], accent);
            return dialog;
        }

        private void Build(UIView view, Builder ui, string title, Paragraph[] paragraphs, string[] buttons, int accent)
        {
            Theme theme = ui.Theme;
            float margin = theme.Margin;
            float textWidth = Width - 2f * margin;
            float textScale = theme.TextScale;

            int written = 0;
            float[] heights = new float[paragraphs.Length];
            float textHeight = 0f;
            for (int i = 0; i < paragraphs.Length; i++)
            {
                if (string.IsNullOrEmpty(paragraphs[i].Text))
                    continue;
                heights[i] = Mathf.Ceil(ui.MeasureWrapped(paragraphs[i].Text, textScale, textWidth)) + 4f;
                textHeight += heights[i] + (written > 0 ? ParagraphGap : 0f);
                written++;
            }

            float height = theme.TitleHeight + margin + textHeight + margin + ButtonHeight + margin;

            UIPanel panel = (UIPanel)view.AddUIComponent(typeof(UIPanel));
            panel.name = "ModsLibCS1Dialog";
            panel.size = new Vector2(Width, height);
            panel.relativePosition = new Vector3(Mathf.Round((view.fixedWidth - Width) * 0.5f),
                                                 Mathf.Round((view.fixedHeight - height) * 0.5f));
            _panel = panel;

            ui.AddFrame(panel, title, () => Answer(Closed));

            float top = theme.TitleHeight + margin;
            for (int i = 0; i < paragraphs.Length; i++)
            {
                if (string.IsNullOrEmpty(paragraphs[i].Text))
                    continue;

                UILabel text = ui.AddLabel(panel, paragraphs[i].Text, new Vector3(margin, top), textScale);
                text.autoSize = false;
                text.wordWrap = true;
                text.size = new Vector2(textWidth, heights[i]);
                if (paragraphs[i].Colour.HasValue)
                    ui.Paint(text, paragraphs[i].Colour.Value);
                top += heights[i] + ParagraphGap;
            }

            // The buttons, right-aligned, each as wide as its text needs.
            float[] widths = new float[buttons.Length];
            float total = 0f;
            for (int i = 0; i < buttons.Length; i++)
            {
                string shown = ui.Caps(buttons[i]) ?? string.Empty;
                float spacing = theme.Caps ? shown.Length : 0f;
                widths[i] = Mathf.Max(ButtonMinWidth, Mathf.Ceil(ui.Measure(shown, textScale) + spacing) + 32f);
                total += widths[i] + (i > 0 ? ButtonGap : 0f);
            }

            float x = Width - margin - total;
            float y = height - margin - ButtonHeight;
            for (int i = 0; i < buttons.Length; i++)
            {
                UIButton button = ui.AddButton(panel, buttons[i], new Vector2(widths[i], ButtonHeight), new Vector3(x, y));
                if (i == accent)
                    ui.SetAccent(button, true);
                int index = i;
                button.eventClick += (component, e) => Answer(index);
                x += widths[i] + ButtonGap;
            }

            panel.canFocus = true;
            panel.eventKeyDown += (component, e) =>
            {
                if (e.keycode == KeyCode.Escape)
                {
                    e.Use();
                    Answer(Closed);
                }
            };

            // The game's own dimming behind it, as its modal panels have (UIDynamicPanels.ShowModal, read from its
            // IL: FitTo(null), Show, opacity faded 0 -> 1 -- a hidden one is left at opacity 0, so it is set here).
            _effect = view.panelsLibraryModalEffect;
            if (_effect != null)
            {
                _effect.FitTo(null);
                if (!_effect.isVisible)
                {
                    _effect.Show();
                    _effectShown = true;
                }
                _effect.opacity = 1f;
                _effect.BringToFront();
            }

            panel.BringToFront();
            UIView.PushModal(panel);
            panel.Focus();
            Builder.KeepOnScreen(panel);
        }

        private void Answer(int index)
        {
            if (_panel == null)
                return;

            Action<int> onAnswer = _onAnswer;
            Close();

            if (onAnswer != null)
                onAnswer(index);
        }

        /// <summary>Closes it without an answer (its owner is going away). Safe to call again.</summary>
        public void Close()
        {
            UIPanel panel = _panel;
            if (panel == null)
                return;

            _panel = null;
            _onAnswer = null;

            try
            {
                if (UIView.GetModalComponent() == panel)
                    UIView.PopModal();
            }
            catch (Exception e)
            {
                LibLog.Msg("dialog: could not end its modal input: " + e.Message);
            }

            if (_effectShown && _effect != null)
                _effect.Hide();
            _effectShown = false;

            UnityEngine.Object.Destroy(panel.gameObject);
        }
    }
}

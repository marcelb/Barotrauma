﻿using EventInput;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barotrauma
{

    delegate void TextBoxEvent(GUITextBox sender, Keys key);

    class GUITextBox : GUIComponent, IKeyboardSubscriber
    {        
        public event TextBoxEvent OnSelected;
        public event TextBoxEvent OnDeselected;
        
        bool caretVisible;
        float caretTimer;

        private GUIFrame frame;
        private GUITextBlock textBlock;

        public Func<string, string> textFilterFunction;

        public delegate bool OnEnterHandler(GUITextBox textBox, string text);
        public OnEnterHandler OnEnterPressed;
        
        public event TextBoxEvent OnKeyHit;

        public delegate bool OnTextChangedHandler(GUITextBox textBox, string text);
        /// <summary>
        /// Don't set the Text property on delegates that register to this event, because modifying the Text will launch this event -> stack overflow. 
        /// If the event launches, the text should already be up to date!
        /// </summary>
        public event OnTextChangedHandler OnTextChanged;

        public bool CaretEnabled { get; set; }
        public Color? CaretColor { get; set; }

        private int? maxTextLength;

        private int _caretIndex;
        private int CaretIndex
        {
            get { return _caretIndex; }
            set
            {
                previousCaretIndex = _caretIndex;
                _caretIndex = value;
                caretPosDirty = true;
            }
        }
        private bool caretPosDirty;
        protected Vector2 caretPos;
        public Vector2 CaretScreenPos => Rect.Location.ToVector2() + caretPos;

        private bool isSelecting;
        private string selectedText = string.Empty;
        private int selectedCharacters;
        private int selectionStartIndex;
        private int selectionEndIndex;
        private bool IsLeftToRight => selectionStartIndex <= selectionEndIndex;
        private int previousCaretIndex;
        private Vector2 selectionStartPos;
        private Vector2 selectionEndPos;
        private Vector2 selectionRectSize;

        private readonly Memento<string> memento = new Memento<string>();

        public GUITextBlock.TextGetterHandler TextGetter
        {
            get { return textBlock.TextGetter; }
            set { textBlock.TextGetter = value; }
        }

        private bool selected;
        public bool Selected
        {
            get
            {
                return selected;
            }
            set
            {
                if (!selected && value)
                {
                    Select();
                }
                else if (selected && !value)
                {
                    Deselect();
                }
            }
        }

        public bool Wrap
        {
            get { return textBlock.Wrap; }
            set
            {
                textBlock.Wrap = value;
            }
        }

        //should the text be limited to the size of the box
        //ignored when MaxTextLength is set or text wrapping is enabled
        public bool ClampText
        {
            get;
            set;
        }

        public int? MaxTextLength
        {
            get { return maxTextLength; }
            set
            {
                textBlock.OverflowClip = value != null;                
                maxTextLength = value;
            }
        }

        public bool OverflowClip
        {
            get { return textBlock.OverflowClip; }
            set { textBlock.OverflowClip = value; }
        }

        public override bool Enabled
        {
            get { return enabled; }
            set
            {
                enabled = value;
                if (!enabled && Selected)
                {
                    Deselect();
                }
            }
        }

        public bool Censor
        {
            get { return textBlock.Censor; }
            set { textBlock.Censor = value; }
        }

        public override string ToolTip
        {
            get
            {
                return base.ToolTip;
            }
            set
            {
                base.ToolTip = value;
                textBlock.ToolTip = value;
            }
        }

        public override ScalableFont Font
        {
            set
            {
                base.Font = value;
                if (textBlock == null) return;
                textBlock.Font = value;
            }
        }

        public override Color Color
        {
            get { return color; }
            set
            {
                color = value;
                textBlock.Color = color;
            }
        }

        public Color TextColor
        {
            get { return textBlock.TextColor; }
            set { textBlock.TextColor = value; }
        }

        public override Color HoverColor
        {
            get
            {
                return base.HoverColor;
            }
            set
            {
                base.HoverColor = value;
                textBlock.HoverColor = value;
            }
        }

        // TODO: should this be defined in the stylesheet?
        public Color SelectionColor { get; set; } = Color.White * 0.25f;

        public string Text
        {
            get
            {
                return textBlock.Text;
            }
            set
            {
                SetText(value, store: false);
                CaretIndex = Text.Length;
                OnTextChanged?.Invoke(this, Text);
            }
        }

        public string WrappedText
        {
            get { return textBlock.WrappedText; }
        }
        
        public GUITextBox(RectTransform rectT, string text = "", Color? textColor = null, ScalableFont font = null,
            Alignment textAlignment = Alignment.Left, bool wrap = false, string style = "", Color? color = null)
            : base(style, rectT)
        {
            Enabled = true;
            this.color = color ?? Color.White;
            frame = new GUIFrame(new RectTransform(Vector2.One, rectT, Anchor.Center), style, color);
            GUI.Style.Apply(frame, style == "" ? "GUITextBox" : style);
            textBlock = new GUITextBlock(new RectTransform(Vector2.One, frame.RectTransform, Anchor.Center), text, textColor, font, textAlignment, wrap);
            GUI.Style.Apply(textBlock, "", this);
            CaretEnabled = true;
            caretPosDirty = true;

            Font = textBlock.Font;
            
            rectT.SizeChanged += () => { caretPosDirty = true; };
            rectT.ScaleChanged += () => { caretPosDirty = true; };
        }

        private bool SetText(string text, bool store = true)
        {
            if (textFilterFunction != null)
            {
                text = textFilterFunction(text);
            }
            if (textBlock.Text == text) { return false; }
            textBlock.Text = text;
            if (textBlock.Text == null) textBlock.Text = "";
            if (textBlock.Text != "" && !Wrap)
            {
                if (maxTextLength != null)
                {
                    if (textBlock.Text.Length > maxTextLength)
                    {
                        textBlock.Text = textBlock.Text.Substring(0, (int)maxTextLength);
                    }
                }
                else
                {
                    while (ClampText && textBlock.Text.Length>0 && Font.MeasureString(textBlock.Text).X > (int)(textBlock.Rect.Width - textBlock.Padding.X - textBlock.Padding.Z))
                    {
                        textBlock.Text = textBlock.Text.Substring(0, textBlock.Text.Length - 1);
                    }
                }
            }
            if (store)
            {
                memento.Store(textBlock.Text);
            }
            return true;
        }

        private void CalculateCaretPos()
        {
            string textDrawn = Censor ? textBlock.CensoredText : textBlock.WrappedText;
            if (textDrawn.Contains("\n"))
            {
                string[] lines = textDrawn.Split('\n');
                int totalIndex = 0;
                for (int i = 0; i < lines.Length; i++)
                {
                    int currentLineLength = lines[i].Length;
                    totalIndex += currentLineLength;
                    // The caret is on this line
                    if (CaretIndex < totalIndex || totalIndex == textBlock.Text.Length)
                    {
                        int diff = totalIndex - CaretIndex;
                        int index = currentLineLength - diff;
                        Vector2 lineTextSize = Font.MeasureString(lines[i].Substring(0, index));
                        Vector2 lastLineSize = Font.MeasureString(lines[i]);
                        float totalTextHeight = Font.MeasureString(textDrawn.Substring(0, totalIndex)).Y;
                        caretPos = new Vector2(lineTextSize.X, totalTextHeight - lastLineSize.Y) + textBlock.TextPos - textBlock.Origin;
                        break;
                    }
                }
            }
            else
            {
                textDrawn = Censor ? textBlock.CensoredText : textBlock.Text;
                Vector2 textSize = Font.MeasureString(textDrawn.Substring(0, CaretIndex));
                caretPos = new Vector2(textSize.X, 0) + textBlock.TextPos - textBlock.Origin;
            }
            caretPosDirty = false;
        }

        protected List<Tuple<Vector2, int>> GetAllPositions()
        {
            string textDrawn = Censor ? textBlock.CensoredText : textBlock.WrappedText;
            var positions = new List<Tuple<Vector2, int>>();
            if (textDrawn.Contains("\n"))
            {
                string[] lines = textDrawn.Split('\n');
                int index = 0;
                int totalIndex = 0;
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    totalIndex += line.Length;
                    float totalTextHeight = Font.MeasureString(textDrawn.Substring(0, totalIndex)).Y;
                    for (int j = 0; j <= line.Length; j++)
                    {
                        Vector2 lineTextSize = Font.MeasureString(line.Substring(0, j));
                        Vector2 indexPos = new Vector2(lineTextSize.X + textBlock.Padding.X, totalTextHeight + textBlock.Padding.Y);
                        //DebugConsole.NewMessage($"index: {index}, pos: {indexPos}", Color.AliceBlue);
                        positions.Add(new Tuple<Vector2, int>(textBlock.Rect.Location.ToVector2() + indexPos, index + j));
                    }
                    index = totalIndex;
                }
            }
            else
            {
                textDrawn = Censor ? textBlock.CensoredText : textBlock.Text;
                for (int i = 0; i <= textBlock.Text.Length; i++)
                {
                    Vector2 textSize = Font.MeasureString(textDrawn.Substring(0, i));
                    Vector2 indexPos = new Vector2(textSize.X + textBlock.Padding.X, textSize.Y + textBlock.Padding.Y) + textBlock.TextPos - textBlock.Origin;
                    //DebugConsole.NewMessage($"index: {i}, pos: {indexPos}", Color.WhiteSmoke);
                    positions.Add(new Tuple<Vector2, int>(textBlock.Rect.Location.ToVector2() + indexPos, i));
                }
            }
            return positions;
        }

        public int GetCaretIndexFromScreenPos(Vector2 pos)
        {
            var positions = GetAllPositions().OrderBy(p => Vector2.DistanceSquared(p.Item1, pos));
            var posIndex = positions.FirstOrDefault();
            //GUI.AddMessage($"index: {posIndex.Item2}, pos: {posIndex.Item1}", Color.WhiteSmoke);
            return posIndex != null ? posIndex.Item2 : textBlock.Text.Length;
        }

        public void Select()
        {
            if (memento.Current == null)
            {
                memento.Store(Text);
            }
            CaretIndex = GetCaretIndexFromScreenPos(PlayerInput.MousePosition);
            ClearSelection();
            selected = true;
            GUI.KeyboardDispatcher.Subscriber = this;
            OnSelected?.Invoke(this, Keys.None);
        }

        public void Deselect()
        {
            memento.Clear();
            selected = false;

            if (GUI.KeyboardDispatcher.Subscriber == this)
            {
                GUI.KeyboardDispatcher.Subscriber = null;
            }
            OnDeselected?.Invoke(this, Keys.None);
        }

        public override void Flash(Color? color = null, float flashDuration = 1.5f, bool useRectangleFlash = false, Vector2? flashRectOffset = null)
        {
            textBlock.Flash(color, flashDuration, useRectangleFlash, flashRectOffset);
        }
        
        protected override void Update(float deltaTime)
        {
            if (!Visible) return;

            if (flashTimer > 0.0f) flashTimer -= deltaTime;
            if (!Enabled) return;
            if (MouseRect.Contains(PlayerInput.MousePosition) && (GUI.MouseOn == null || GUI.IsMouseOn(this)))
            {
                state = ComponentState.Hover;
                if (PlayerInput.LeftButtonDown())
                {
                    Select();
                }
                else
                {
                    isSelecting = PlayerInput.LeftButtonHeld();
                }
                if (PlayerInput.DoubleClicked())
                {
                    SelectAll();
                }
                if (isSelecting)
                {
                    if (!MathUtils.NearlyEqual(PlayerInput.MouseSpeed.X, 0))
                    {
                        CaretIndex = GetCaretIndexFromScreenPos(PlayerInput.MousePosition);
                        CalculateCaretPos();
                        CalculateSelection();
                    }
                }
            }
            else
            {
                if (PlayerInput.LeftButtonClicked() && selected) Deselect();
                isSelecting = false;
                state = ComponentState.None;
            }
            if (!isSelecting)
            {
                isSelecting = PlayerInput.KeyDown(Keys.LeftShift) || PlayerInput.KeyDown(Keys.RightShift);
            }

            if (CaretEnabled)
            {
                if (textBlock.OverflowClipActive)
                {
                    if (CaretScreenPos.X < Rect.X + textBlock.Padding.X)
                    {
                        textBlock.TextPos = new Vector2(textBlock.TextPos.X + ((Rect.X + textBlock.Padding.X) - CaretScreenPos.X), textBlock.TextPos.Y);
                        CalculateCaretPos();
                    }
                    else if (CaretScreenPos.X > Rect.Right - textBlock.Padding.Z)
                    {
                        textBlock.TextPos = new Vector2(textBlock.TextPos.X - (CaretScreenPos.X - (Rect.Right - textBlock.Padding.Z)), textBlock.TextPos.Y);
                        CalculateCaretPos();
                    }
                }
                caretTimer += deltaTime;
                caretVisible = ((caretTimer * 1000.0f) % 1000) < 500;
                if (caretVisible && caretPosDirty)
                {
                    CalculateCaretPos();
                }
            }

            if (GUI.KeyboardDispatcher.Subscriber == this)
            {
                state = ComponentState.Selected;
                Character.DisableControls = true;
                if (OnEnterPressed != null &&  PlayerInput.KeyHit(Keys.Enter))
                {
                    OnEnterPressed(this, Text);
                }
            }
            else if (Selected)
            {
                Deselect();
            }

            textBlock.State = state;
        }

        protected override void Draw(SpriteBatch spriteBatch)
        {
            if (!Visible) return;
            base.Draw(spriteBatch);
            // Frame is not used in the old system.
            frame?.DrawManually(spriteBatch);
            textBlock.DrawManually(spriteBatch);
            if (Selected)
            {
                if (caretVisible )
                {
                    GUI.DrawLine(spriteBatch,
                        new Vector2(Rect.X + (int)caretPos.X + 2, Rect.Y + caretPos.Y + 3),
                        new Vector2(Rect.X + (int)caretPos.X + 2, Rect.Y + caretPos.Y + Font.MeasureString("I").Y - 3),
                        CaretColor ?? textBlock.TextColor * (textBlock.TextColor.A / 255.0f));
                }
                if (selectedCharacters > 0)
                {
                    DrawSelectionRect(spriteBatch);
                }
                //GUI.DrawString(spriteBatch, new Vector2(100, 0), selectedCharacters.ToString(), Color.LightBlue, Color.Black);
                //GUI.DrawString(spriteBatch, new Vector2(100, 20), selectionStartIndex.ToString(), Color.White, Color.Black);
                //GUI.DrawString(spriteBatch, new Vector2(140, 20), selectionEndIndex.ToString(), Color.White, Color.Black);
                //GUI.DrawString(spriteBatch, new Vector2(100, 40), selectedText.ToString(), Color.Yellow, Color.Black);
                //GUI.DrawString(spriteBatch, new Vector2(100, 60), $"caret index: {CaretIndex.ToString()}", Color.Red, Color.Black);
                //GUI.DrawString(spriteBatch, new Vector2(100, 80), $"caret pos: {caretPos.ToString()}", Color.Red, Color.Black);
                //GUI.DrawString(spriteBatch, new Vector2(100, 100), $"caret screen pos: {CaretScreenPos.ToString()}", Color.Red, Color.Black);
                //GUI.DrawString(spriteBatch, new Vector2(100, 120), $"text start pos: {(textBlock.TextPos - textBlock.Origin).ToString()}", Color.White, Color.Black);
                //GUI.DrawString(spriteBatch, new Vector2(100, 140), $"cursor pos: {PlayerInput.MousePosition.ToString()}", Color.White, Color.Black);
            }
        }

        private void DrawSelectionRect(SpriteBatch spriteBatch)
        {
            if (textBlock.WrappedText.Contains("\n"))
            {
                // Multiline selection
                string[] lines = textBlock.WrappedText.Split('\n');
                int totalIndex = 0;
                int previousCharacters = 0;
                Vector2 offset = textBlock.TextPos - textBlock.Origin;
                for (int i = 0; i < lines.Length; i++)
                {
                    string currentLine = lines[i];
                    int currentLineLength = currentLine.Length;
                    totalIndex += currentLineLength;
                    bool containsSelection = IsLeftToRight
                        ? selectionStartIndex < totalIndex && selectionEndIndex > previousCharacters
                        : selectionEndIndex < totalIndex && selectionStartIndex > previousCharacters;
                    if (containsSelection)
                    {
                        Vector2 currentLineSize = Font.MeasureString(currentLine);
                        if ((IsLeftToRight && selectionStartIndex < previousCharacters && selectionEndIndex > totalIndex)
                            || !IsLeftToRight && selectionEndIndex < previousCharacters && selectionStartIndex > totalIndex)
                        {
                            // select the whole line
                            Vector2 topLeft = offset + new Vector2(0, currentLineSize.Y * i);
                            GUI.DrawRectangle(spriteBatch, Rect.Location.ToVector2() + topLeft, currentLineSize, SelectionColor, isFilled: true);
                        }
                        else
                        {
                            if (IsLeftToRight)
                            {
                                bool selectFromTheBeginning = selectionStartIndex <= previousCharacters;
                                int startIndex = selectFromTheBeginning ? 0 : Math.Abs(selectionStartIndex - previousCharacters);
                                int endIndex = Math.Abs(selectionEndIndex - previousCharacters);
                                int characters = Math.Min(endIndex - startIndex, currentLineLength - startIndex);
                                Vector2 selectedTextSize = Font.MeasureString(currentLine.Substring(startIndex, characters));
                                Vector2 topLeft = selectFromTheBeginning
                                    ? new Vector2(offset.X, offset.Y + currentLineSize.Y * i)
                                    : new Vector2(selectionStartPos.X, offset.Y + currentLineSize.Y * i);
                                GUI.DrawRectangle(spriteBatch, Rect.Location.ToVector2() + topLeft, selectedTextSize, SelectionColor, isFilled: true);
                            }
                            else
                            {
                                bool selectFromTheBeginning = selectionStartIndex >= totalIndex;
                                bool selectFromTheStart = selectionEndIndex <= previousCharacters;
                                int startIndex = selectFromTheBeginning ? currentLineLength : Math.Abs(selectionStartIndex - previousCharacters);
                                int endIndex = selectFromTheStart ? 0 : Math.Abs(selectionEndIndex - previousCharacters);
                                int characters = Math.Min(Math.Abs(endIndex - startIndex), currentLineLength);
                                Vector2 selectedTextSize = Font.MeasureString(currentLine.Substring(endIndex, characters));
                                Vector2 topLeft = selectFromTheBeginning
                                    ? new Vector2(offset.X + currentLineSize.X - selectedTextSize.X, offset.Y + currentLineSize.Y * i)
                                    : new Vector2(selectionStartPos.X - selectedTextSize.X, offset.Y + currentLineSize.Y * i);
                                GUI.DrawRectangle(spriteBatch, Rect.Location.ToVector2() + topLeft, selectedTextSize, SelectionColor, isFilled: true);
                            }
                        }
                    }
                    previousCharacters = totalIndex;
                }
            }
            else
            {
                // Single line selection
                Vector2 topLeft = IsLeftToRight ? selectionStartPos : selectionEndPos;
                GUI.DrawRectangle(spriteBatch, Rect.Location.ToVector2() + topLeft, selectionRectSize, SelectionColor, isFilled: true);
            }
        }

        public void ReceiveTextInput(char inputChar)
        {
            ReceiveTextInput(inputChar.ToString());
        }

        public void ReceiveTextInput(string input)
        {
            if (selectedCharacters > 0)
            {
                RemoveSelectedText();
            }
            Vector2 textPos = textBlock.TextPos;
            bool wasOverflowClipActive = textBlock.OverflowClipActive;
            if (SetText(Text.Insert(CaretIndex, input)))
            {
                CaretIndex = Math.Min(Text.Length, CaretIndex + input.Length);
                OnTextChanged?.Invoke(this, Text);
                if (textBlock.OverflowClipActive && wasOverflowClipActive && !MathUtils.NearlyEqual(textBlock.TextPos, textPos))
                {
                    textBlock.TextPos = textPos + Vector2.UnitX * Font.MeasureString(input).X;
                }
            }
        }

        public void ReceiveCommandInput(char command)
        {
            if (Text == null) Text = "";

            switch (command)
            {
                case '\b': //backspace
                    if (selectedCharacters > 0)
                    {
                        RemoveSelectedText();
                    }
                    else if (Text.Length > 0 && CaretIndex > 0)
                    {
                        CaretIndex--;
                        SetText(Text.Remove(CaretIndex, 1));
                        CalculateCaretPos();
                        ClearSelection();
                    }
                    OnTextChanged?.Invoke(this, Text);
                    break;
                case (char)0x3: // ctrl-c
                    CopySelectedText();
                    break;
                case (char)0x16: // ctrl-v
                    string text = GetCopiedText();
                    RemoveSelectedText();
                    if (SetText(Text.Insert(CaretIndex, text)))
                    {
                        CaretIndex = Math.Min(Text.Length, CaretIndex + text.Length);
                        OnTextChanged?.Invoke(this, Text);
                    }
                    break;
                case (char)0x18: // ctrl-x
                    CopySelectedText();
                    RemoveSelectedText();
                    break;
                case (char)0x1: // ctrl-a
                    SelectAll();
                    break;
                case (char)0x1A: // ctrl-z
                    text = memento.Undo();
                    if (text != Text)
                    {
                        SetText(text, false);
                        CaretIndex = Text.Length;
                        OnTextChanged?.Invoke(this, Text);
                    }
                    break;
                case (char)0x12: // ctrl-r
                    text = memento.Redo();
                    if (text != Text)
                    {
                        SetText(text, false);
                        CaretIndex = Text.Length;
                        OnTextChanged?.Invoke(this, Text);
                    }
                    break;
            }
        }

        public void ReceiveSpecialInput(Keys key)
        {
            switch (key)
            {
                case Keys.Left:
                    if (isSelecting)
                    {
                        InitSelectionStart();
                    }
                    CaretIndex = Math.Max(CaretIndex - 1, 0);
                    caretTimer = 0;
                    HandleSelection();
                    break;
                case Keys.Right:
                    if (isSelecting)
                    {
                        InitSelectionStart();
                    }
                    CaretIndex = Math.Min(CaretIndex + 1, Text.Length);
                    caretTimer = 0;
                    HandleSelection();
                    break;
                case Keys.Up:
                    if (isSelecting)
                    {
                        InitSelectionStart();
                    }
                    float lineHeight = Font.MeasureString(Text).Y;
                    int newIndex = GetCaretIndexFromScreenPos(new Vector2(CaretScreenPos.X, CaretScreenPos.Y - lineHeight / 2));
                    CaretIndex = newIndex != CaretIndex ? newIndex : 0;
                    caretTimer = 0;
                    HandleSelection();
                    break;
                case Keys.Down:
                    if (isSelecting)
                    {
                        InitSelectionStart();
                    }
                    lineHeight = Font.MeasureString(Text).Y;
                    newIndex = GetCaretIndexFromScreenPos(new Vector2(CaretScreenPos.X, CaretScreenPos.Y + lineHeight * 2));
                    CaretIndex = newIndex != CaretIndex ? newIndex : Text.Length;
                    caretTimer = 0;
                    HandleSelection();
                    break;
                case Keys.Delete:
                    if (selectedCharacters > 0)
                    {
                        RemoveSelectedText();
                    }
                    else if (Text.Length > 0 && CaretIndex < Text.Length)
                    {
                        SetText(Text.Remove(CaretIndex, 1));
                        OnTextChanged?.Invoke(this, Text);
                        caretPosDirty = true;
                    }
                    break;
                case Keys.Tab:
                    // Select the next text box.
                    var editor = RectTransform.GetParents().Select(p => p.GUIComponent as SerializableEntityEditor).FirstOrDefault(e => e != null);
                    if (editor == null) { break; }
                    var allTextBoxes = GetAndSortTextBoxes(editor).ToList();
                    if (allTextBoxes.Any())
                    {
                        int currentIndex = allTextBoxes.IndexOf(this);
                        int nextIndex = Math.Min(allTextBoxes.Count - 1, currentIndex + 1);
                        var next = allTextBoxes[nextIndex];
                        if (next != this)
                        {
                            next.Select();
                            next.Flash(Color.White * 0.5f, 0.5f);
                        }
                        else
                        {
                            // Select the first text box in the next editor that has text boxes.
                            var listBox = RectTransform.GetParents().Select(p => p.GUIComponent as GUIListBox).FirstOrDefault(lb => lb != null);
                            if (listBox == null) { break; }
                            // TODO: The get's out of focus if the selection is out of view.
                            // Not sure how's that possible, but it seems to work when the auto scroll is disabled and you handle the scrolling manually.
                            listBox.SelectNext();
                            while (SelectNextTextBox(listBox) == null)
                            {
                                var previous = listBox.SelectedComponent;
                                listBox.SelectNext();
                                if (listBox.SelectedComponent == previous) { break; }
                            }
                        }
                    }
                    IEnumerable<GUITextBox> GetAndSortTextBoxes(GUIComponent parent) => parent.GetAllChildren().Select(c => c as GUITextBox).Where(t => t != null).OrderBy(t => t.Rect.Y).ThenBy(t => t.Rect.X);
                    GUITextBox SelectNextTextBox(GUIListBox listBox)
                    {
                        var textBoxes = GetAndSortTextBoxes(listBox.SelectedComponent);
                        if (textBoxes.Any())
                        {
                            var next = textBoxes.First();
                            next.Select();
                            next.Flash(Color.White * 0.5f, 0.5f);
                            return next;
                        }
                        return null;
                    }
                    break;
            }
            OnKeyHit?.Invoke(this, key);
            void HandleSelection()
            {
                if (isSelecting)
                {
                    InitSelectionStart();
                    CalculateSelection();
                }
                else
                {
                    ClearSelection();
                }
            }
        }

        public void SelectAll()
        {
            CaretIndex = 0;
            CalculateCaretPos();
            selectionStartPos = caretPos;
            selectionStartIndex = 0;
            CaretIndex = Text.Length;
            CalculateSelection();
        }

        private void CopySelectedText()
        {
            Clipboard.SetText(selectedText);
        }

        private void ClearSelection()
        {
            selectedCharacters = 0;
            selectionStartIndex = -1;
            selectionEndIndex = -1;
            selectedText = string.Empty;
        }

        private string GetCopiedText()
        {
            string t;
            t = Clipboard.GetText();

            return t;
        }

        private void RemoveSelectedText()
        {
            if (selectedText.Length == 0) { return; }
            if (IsLeftToRight)
            {
                SetText(Text.Remove(selectionStartIndex, selectedText.Length));
                CaretIndex = Math.Min(Text.Length, selectionStartIndex);
            }
            else
            {
                SetText(Text.Remove(selectionEndIndex, selectedText.Length));
                CaretIndex = Math.Min(Text.Length, selectionEndIndex);
            }
            ClearSelection();
            OnTextChanged?.Invoke(this, Text);
        }

        private void InitSelectionStart()
        {
            if (caretPosDirty)
            {
                CalculateCaretPos();
            }
            if (selectionStartIndex == -1)
            {
                selectionStartIndex = CaretIndex;
                selectionStartPos = caretPos;
            }
        }

        private void CalculateSelection()
        {
            string textDrawn = Censor ? textBlock.CensoredText : textBlock.WrappedText;
            InitSelectionStart();
            selectionEndIndex = CaretIndex;
            selectionEndPos = caretPos;
            selectedCharacters = Math.Abs(selectionStartIndex - selectionEndIndex);
            if (IsLeftToRight)
            {
                selectedText = Text.Substring(selectionStartIndex, selectedCharacters);
                selectionRectSize = Font.MeasureString(textDrawn.Substring(selectionStartIndex, selectedCharacters));
            }
            else
            {
                selectedText = Text.Substring(selectionEndIndex, selectedCharacters);
                selectionRectSize = Font.MeasureString(textDrawn.Substring(selectionEndIndex, selectedCharacters));
            }
        }
    }
}

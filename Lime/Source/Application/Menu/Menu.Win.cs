#if WIN
using System;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;

namespace Lime
{
	public class Menu : List<ICommand>, IMenu
	{
		readonly List<MenuItem> items = new List<MenuItem>();

		private MenuStrip nativeMainMenu;
		private ContextMenuStrip nativeContextMenu;
		internal MenuStrip NativeMainMenu
		{
			get
			{
				if (nativeMainMenu == null) {
					nativeMainMenu = new MenuStrip() {
						Renderer = new Renderer(new Colors()),
						ForeColor = Colors.Text,
						BackColor = Colors.Main,
					};
					UpdateNativeMenu(nativeMainMenu);
				}
				return nativeMainMenu;
			}
		}

		private bool showImageMargin = true;
		public bool ShowImageMargin
		{
			get
			{
				return showImageMargin;
			}

			set
			{
				if (value != showImageMargin) {
					if (nativeContextMenu != null) {
						nativeContextMenu.ShowImageMargin = value;
					}
					showImageMargin = value;
				}
			}
		}

		internal ContextMenuStrip NativeContextMenu
		{
			get
			{
				if (nativeContextMenu == null) {
					nativeContextMenu = new ContextMenuStrip {
						ShowImageMargin = showImageMargin,
						ForeColor = Colors.Text,
						BackColor = Colors.Main,
						Renderer = new Renderer(new Colors()),
					};
					UpdateNativeMenu(nativeContextMenu);
				}
				return nativeContextMenu;
			}
		}

		private IEnumerable<ICommand> AllCommands()
		{
			foreach (var i in this) {
				yield return i;
			}
			foreach (var i in this) {
				if (i.Menu != null) {
					foreach (var j in ((Menu)i.Menu).AllCommands()) {
						yield return j;
					}
				}
			}
		}

		public ICommand FindCommand(string text)
		{
			return AllCommands().First(i => i.Text == text);
		}

		private void Rebuild()
		{
			items.Clear();
			foreach (var i in this) {
				items.Add(new MenuItem(i));
			}
			if (nativeMainMenu != null) {
				UpdateNativeMenu(nativeMainMenu);
			}
			if (nativeContextMenu != null) {
				UpdateNativeMenu(nativeContextMenu);
			}
		}

		private void UpdateNativeMenu(ToolStrip menu)
		{
			menu.Items.Clear();
			foreach (var i in items) {
				menu.Items.Add(i.NativeItem);
			}
		}

		public void Refresh()
		{
			if (items.Count != Count) {
				Rebuild();
				return;
			}
			int j = 0;
			foreach (var i in items) {
				if (i.Command != this[j++]) {
					Rebuild();
					break;
				}
				i.Refresh();
			}
		}

		public void Popup()
		{
			Refresh();
			var w = Window.Current as Window;
			w.Input.ClearKeyState();
			var mp = w.WorldToWindow(w.Input.MousePosition);
			NativeContextMenu.Show(w.Form, new Point(mp.X.Round(), mp.Y.Round()));
		}

		public void Popup(IWindow window, Vector2 position, float minimumWidth, ICommand command)
		{
			Refresh();
			window.Input.ClearKeyState();
			NativeContextMenu.MinimumSize = new System.Drawing.Size(
				(int)minimumWidth, NativeContextMenu.MinimumSize.Height);
			foreach (var menuItem in items) {
				var ni = menuItem.NativeItem;
				ni.AutoSize = false;
				ni.Width = NativeContextMenu.Width;
				if (menuItem.Command == command) {
					ni.Select();
				}
			}
			var mp = ((Window)window).WorldToWindow(position);
			NativeContextMenu.Show(window.Form, new System.Drawing.Point(mp.X.Round(), mp.Y.Round()));
		}

		public class Colors : ProfessionalColorTable
		{
			public static Color Secondary => Theme.Colors.WhiteBackground.ToColor();
			public static Color Main => Theme.Colors.GrayBackground.ToColor();
			public static Color Text => Theme.Colors.BlackText.ToColor();
			public static Color Highlight => Theme.Colors.SelectedBackground.ToColor();

			public Colors() : base()
			{
				base.UseSystemColors = false;
			}

			public override Color MenuItemSelected => Highlight;
			public override Color MenuItemSelectedGradientBegin => Highlight;
			public override Color MenuItemSelectedGradientEnd => Highlight;
			public override Color MenuItemPressedGradientBegin => Secondary;
			public override Color MenuItemPressedGradientEnd => Secondary;
			public override Color MenuItemBorder => Main;
			public override Color MenuBorder => Highlight;
			public override Color MenuStripGradientBegin => Main;
			public override Color MenuStripGradientEnd => Secondary;
			public override Color ImageMarginGradientBegin => Main;
			public override Color ImageMarginGradientEnd => Main;
			public override Color ImageMarginGradientMiddle => Main;
			public override Color ImageMarginRevealedGradientBegin => Secondary;
			public override Color ImageMarginRevealedGradientEnd => Secondary;
			public override Color ImageMarginRevealedGradientMiddle => Secondary;
			public override Color ButtonCheckedGradientBegin => Main;
			public override Color ButtonCheckedGradientEnd => Main;
			public override Color ButtonCheckedGradientMiddle => Main;
			public override Color ButtonCheckedHighlight => Highlight;
			public override Color ButtonCheckedHighlightBorder => Highlight;
			public override Color CheckBackground => Secondary;
			public override Color CheckSelectedBackground => Secondary;
			public override Color CheckPressedBackground => Secondary;
			public override Color MenuItemPressedGradientMiddle => Secondary;
			public override Color ToolStripDropDownBackground => Secondary;
		}

		public class Renderer : ToolStripProfessionalRenderer
		{
			public Renderer(ProfessionalColorTable professionalColorTable) : base(professionalColorTable)
			{
			}

			private static readonly Pen Pen = new Pen(Colors.Text, 2);
			private static PointF[] CheckMark;

			protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
			{
				var tsMenuItem = e.Item as ToolStripMenuItem;
				if (tsMenuItem != null) {
					if (CheckMark == null) {
						int px = e.ImageRectangle.Left;
						int py = e.ImageRectangle.Top;
						int sizex = e.ImageRectangle.Size.Width;
						int sizey = e.ImageRectangle.Size.Height;
						CheckMark = new PointF[] {
							new PointF(px + 0.25f * sizex, py + 0.5f * sizey),
							new PointF(px + 0.5f * sizex, py + 0.75f * sizey),
							new PointF(px + 0.8f * sizex, py + 0.2f * sizey)
						};
					}
					e.Graphics.DrawLines(Pen, CheckMark);
				}
			}
		}
	}

	class MenuItem
	{
		private int commandVersion;
		public readonly ICommand Command;
		public readonly ToolStripItem NativeItem;

		public MenuItem(ICommand command)
		{
			Command = command;
			if (command == Lime.Command.MenuSeparator) {
				NativeItem = new ToolStripSeparator();
				var mi = (ToolStripSeparator)NativeItem;
			} else {
				NativeItem = new ToolStripMenuItem();
				NativeItem.Click += (s, e) => CommandQueue.Instance.Add((Command)Command);
			}
			Refresh();
		}

		public void Refresh()
		{
			Command.Menu?.Refresh();
			if (Command.Version == commandVersion) {
				return;
			}
			commandVersion = Command.Version;
			NativeItem.Visible = Command.Visible;
			NativeItem.Enabled = Command.Enabled;
			NativeItem.Text = Command.Text;
			var mi = NativeItem as ToolStripMenuItem;
			if (mi == null)
				return;
			mi.ShortcutKeys = ToNativeKeys(Command.Shortcut);
			mi.Checked = Command.Checked;
			mi.DropDown = ((Menu)Command.Menu)?.NativeContextMenu;

		}

		private static Keys ToNativeKeys(Shortcut shortcut)
		{
			return ToNativeKeys(shortcut.Modifiers) | ToNativeKeys(shortcut.Main);
		}

		private static readonly Func<Keys> InvalidKeyExceptionFunc = () => { throw new ArgumentException(); };
		private static Keys ToNativeKeys(Key key)
		{
			if (key == Key.Unknown) {
				return Keys.None;
			}
			if (key >= Key.A && key <= Key.Z) {
				return Keys.A + key - Key.A;
			}
			if (key >= Key.Number0 && key <= Key.Number9) {
				return Keys.D0 + key - Key.Number0;
			}
			if (key >= Key.F1 && key <= Key.F12) {
				return Keys.F1 + key - Key.F1;
			}
			return key == Key.Up ? Keys.Up :
				key == Key.Down ? Keys.Down :
				key == Key.Left ? Keys.Left :
				key == Key.Right ? Keys.Right :
				key == Key.Enter ? Keys.Enter :
				key == Key.Escape ? Keys.Escape :
				key == Key.Space ? Keys.Space :
				key == Key.Tab ? Keys.Tab :
				key == Key.Back ? Keys.Back :
				key == Key.BackSpace ? Keys.Back :
				key == Key.Insert ? Keys.Insert :
				key == Key.Delete ? Keys.Delete :
				key == Key.PageUp ? Keys.PageUp :
				key == Key.PageDown ? Keys.PageDown :
				key == Key.Home ? Keys.Home :
				key == Key.End ? Keys.End :
				key == Key.CapsLock ? Keys.CapsLock :
				key == Key.ScrollLock ? Keys.Scroll :
				key == Key.PrintScreen ? Keys.PrintScreen :
				key == Key.Pause ? Keys.Pause :
				key == Key.EqualsSign ? Keys.Oemplus :
				key == Key.Minus ? Keys.OemMinus :
				key == Key.LBracket ? Keys.OemOpenBrackets :
				key == Key.RBracket ? Keys.OemCloseBrackets :
				InvalidKeyExceptionFunc();
		}

		private static Keys ToNativeKeys(Modifiers modifiers)
		{
			var keys = Keys.None;
			if ((modifiers & Modifiers.Alt) != 0) {
				keys |= Keys.Alt;
			}
			if ((modifiers & Modifiers.Control) != 0) {
				keys |= Keys.Control;
			}
			if ((modifiers & Modifiers.Shift) != 0) {
				keys |= Keys.Shift;
			}
			return keys;
		}
	}

	public static class Extensions
	{
		public static Color ToColor(this Color4 color)
		{
			return Color.FromArgb(color.A, color.R, color.G, color.B);
		}
	}
}
#endif

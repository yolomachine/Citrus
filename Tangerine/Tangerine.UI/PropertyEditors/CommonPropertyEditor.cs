using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Lime;
using Tangerine.Core;

namespace Tangerine.UI
{
	public class CommonPropertyEditor<T> : IPropertyEditor
	{
		public IPropertyEditorParams EditorParams { get; private set; }
		public Widget ContainerWidget { get; private set; }
		public SimpleText PropertyLabel { get; private set; }

		public CommonPropertyEditor(IPropertyEditorParams editorParams)
		{
			EditorParams = editorParams;
			ContainerWidget = new Widget {
				Layout = new HBoxLayout { IgnoreHidden = false },
				LayoutCell = new LayoutCell { StretchY = 0 },
			};
			editorParams.InspectorPane.AddNode(ContainerWidget);
			if (editorParams.ShowLabel) {
				PropertyLabel = new ThemedSimpleText {
					Text = editorParams.DisplayName ?? editorParams.PropertyName,
					VAlignment = VAlignment.Center,
					LayoutCell = new LayoutCell(Alignment.LeftCenter, stretchX: 0),
					ForceUncutText = false,
					MinWidth = 120,
					OverflowMode = TextOverflowMode.Minify,
					HitTestTarget = true,
					TabTravesable = new TabTraversable()
				};
				PropertyLabel.Tasks.Add(ManageLabelTask());
				ContainerWidget.AddNode(PropertyLabel);
			}
		}

		IEnumerator<object> ManageLabelTask()
		{
			while (true) {
				var popupMenu = PropertyLabel.Input.WasMouseReleased(1);
				if (popupMenu || PropertyLabel.Input.WasMouseReleased(0)) {
					PropertyLabel.SetFocus();
				}
				PropertyLabel.Color = PropertyLabel.IsFocused() ? Theme.Colors.KeyboardFocusBorder : Theme.Colors.BlackText;
				if (popupMenu) {
					// Wait until the label actually change its color.
					yield return null;
					yield return null;
					ShowPropertyContextMenu();
				}
				if (PropertyLabel.IsFocused()) {
					if (Command.Copy.WasIssued()) {
						Command.Copy.Consume();
						Copy();
					}
					if (Command.Paste.WasIssued()) {
						Command.Paste.Consume();
						Paste();
					}
					if (resetToDefault.WasIssued()) {
						resetToDefault.Consume();
						ResetToDefault();
					}
				}
				yield return null;
			}
		}

		protected virtual void ResetToDefault()
		{
			var defaultValue = EditorParams.DefaultValueGetter();
			if (defaultValue != null)
				SetProperty(defaultValue);
		}

		static Yuzu.Json.JsonSerializer serializer = new Yuzu.Json.JsonSerializer {
			JsonOptions = new Yuzu.Json.JsonSerializeOptions { FieldSeparator = " ", Indent = "", EnumAsString = true }
		};

		static Yuzu.Json.JsonDeserializer deserializer = new Yuzu.Json.JsonDeserializer {
			JsonOptions = new Yuzu.Json.JsonSerializeOptions { EnumAsString = true }
		};

		protected virtual void Copy()
		{
			var v = CoalescedPropertyValue().GetValue();
			try {
				Clipboard.Text = Serialize(v);
			} catch (System.Exception) { }
		}

		protected virtual void Paste()
		{
			try {
				var v = Deserialize(Clipboard.Text);
				SetProperty(v);
			} catch (System.Exception) { }
		}

		protected virtual string Serialize(T value) => serializer.ToString(value);
		protected virtual T Deserialize(string source) => deserializer.FromString<T>(source + ' ');

		protected void DoTransaction(Action block)
		{
			if (EditorParams.History != null) {
				using (EditorParams.History.BeginTransaction()) {
					block();
					EditorParams.History.CommitTransaction();
				}
			} else {
				block();
			}
		}

		private ICommand resetToDefault = new Command("Reset To Default");

		void ShowPropertyContextMenu()
		{
			var menu = new Menu {
				Command.Copy,
				Command.Paste
			};
			if (EditorParams.DefaultValueGetter != null) {
				menu.Insert(0, resetToDefault);
			}
			if (EditorParams.Objects.Count == 1) {
				var owner = EditorParams.Objects.First();
				var value = CoalescedPropertyValue().GetValue();
				var pi = EditorParams.PropertyInfo;
				if (value != null) {
					string path = null;
					if (pi.PropertyType == typeof(ITexture)) {
						path = (value as ITexture).SerializationPath;
					} else if (pi.PropertyType == typeof(SerializableSample)) {
						path = (value as SerializableSample).SerializationPath;
					} else if (pi.PropertyType == typeof(SerializableFont)) {
						var name = (value as SerializableFont).Name;
						if (string.IsNullOrEmpty(name)) {
							name = FontPool.DefaultFontName;
						}
						path = FontPool.DefaultFontDirectory + name;
					} else if (owner is Movie && pi.Name == "Path") {
						path = (owner as Movie).Path;
					} else if (owner is Node && pi.Name == "ContentsPath") {
						path = (owner as Node).ContentsPath;
					}
					if (!string.IsNullOrEmpty(path)) {
						path = Path.Combine(Project.Current.AssetsDirectory, path);
						FilesystemCommands.NavigateTo.UserData = path;
						menu.Insert(0, FilesystemCommands.NavigateTo);
						FilesystemCommands.OpenInSystemFileManager.UserData = path;
						menu.Insert(0, FilesystemCommands.OpenInSystemFileManager);
					}
				}
			}
			menu.Popup();
		}

		public virtual void DropFiles(IEnumerable<string> files) { }

		protected IDataflowProvider<T> CoalescedPropertyValue(T defaultValue = default(T))
		{
			IDataflowProvider<T> provider = null;
			foreach (var o in EditorParams.Objects) {
				var p = new Property<T>(o, EditorParams.PropertyName);
				provider = (provider == null) ? p : provider.SameOrDefault(p, defaultValue);
			}
			return provider;
		}

		protected IDataflowProvider<ComponentType> CoalescedPropertyComponentValue<ComponentType>(Func<T, ComponentType> selector, ComponentType defaultValue = default(ComponentType))
		{
			IDataflowProvider<ComponentType> provider = null;
			foreach (var o in EditorParams.Objects) {
				var p = new Property<T>(o, EditorParams.PropertyName).Select(selector);
				provider = (provider == null) ? p : provider.SameOrDefault(p, defaultValue);
			}
			return provider;
		}

		protected void SetProperty(object value)
		{
			DoTransaction(() => {
				foreach (var o in EditorParams.Objects) {
					EditorParams.PropertySetter(o, EditorParams.PropertyName, value);
				}
			});
		}

		public virtual void Submit()
		{

		}
	}
}
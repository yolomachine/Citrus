using System.IO;
using System.Linq;
using Lime;
using Tangerine.Core;

namespace Tangerine.UI
{
	public class FontPropertyEditor : CommonPropertyEditor<SerializableFont>
	{
		private DropDownList selector;

		public FontPropertyEditor(IPropertyEditorParams editorParams) : base(editorParams)
		{
			selector = editorParams.DropDownListFactory();
			selector.LayoutCell = new LayoutCell(Alignment.Center);
			EditorContainer.AddNode(selector);
			var propType = editorParams.PropertyInfo.PropertyType;
			var items = AssetBundle.Current.EnumerateFiles("Fonts").
				Where(i => i.EndsWith(".fnt") || i.EndsWith(".tft")).
				Select(i => new DropDownList.Item(RemoveFontExtensions(i), i));
			foreach (var i in items) {
				selector.Items.Add(i);
			}

			var current = CoalescedPropertyValue().GetValue();
			selector.Text = current.IsDefined ? GetFontNameWithoutExtension(current.Value) : ManyValuesText;
			selector.Changed += a => {
				SetProperty(new SerializableFont((string)a.Value));
			};
			selector.AddChangeWatcher(CoalescedPropertyValue(), i => {
				selector.Text = i.IsDefined ? GetFontNameWithoutExtension(i.Value) : ManyValuesText;
			});
		}

		private static string GetFontName(SerializableFont i)
		{
			return string.IsNullOrEmpty(i?.Name) ? "Default" : i.Name;
		}

		protected override void EnabledChanged()
		{
			base.EnabledChanged();
			selector.Enabled = Enabled;
		}

		private static string GetFontNameWithoutExtension(SerializableFont i)
		{
			return RemoveFontExtensions(GetFontName(i));
		}

		private static string RemoveFontExtensions(string s)
		{
			return s.Replace(".fnt", string.Empty).Replace(".tft", string.Empty);
		}
	}
}

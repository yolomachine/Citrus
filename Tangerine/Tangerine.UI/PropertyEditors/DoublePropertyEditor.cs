using Lime;
using Tangerine.Core;
using Tangerine.Core.ExpressionParser;

namespace Tangerine.UI
{
	public class DoublePropertyEditor : CommonPropertyEditor<double>
	{
		private NumericEditBox editor;

		public DoublePropertyEditor(IPropertyEditorParams editorParams) : base(editorParams)
		{
			editor = editorParams.NumericEditBoxFactory();
			ContainerWidget.AddNode(editor);
			var current = CoalescedPropertyValue();
			editor.Submitted += text => {
				if (Parser.TryParse(text, out double newValue)) {
					SetProperty(newValue);
				}

				editor.Text = current.GetValue().ToString();
			};
			editor.AddChangeWatcher(current, v => editor.Text = v.ToString());
		}
	}
}
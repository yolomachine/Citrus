#if ORANGE_CLI
using System;

namespace Orange
{
	class MainClass
	{
		[STAThread]
		public static void Main (string[] args)
		{
			var culture = System.Globalization.CultureInfo.InvariantCulture;
			System.Threading.Thread.CurrentThread.CurrentCulture = culture;
			PluginLoader.RegisterAssembly(typeof(MainClass).Assembly);
			UserInterface.Instance = new ConsoleUI();
			UserInterface.Instance.Initialize();
		}
	}
}
#endif // ORANGE_CLI
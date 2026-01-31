using Microsoft.UI.Xaml;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Gallery.App.WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : MauiWinUIApplication
{
	/// <summary>
	/// Workspace path from --workspace command-line argument.
	/// Null if not specified (normal gallery mode).
	/// </summary>
	public static string? WorkspacePath { get; private set; }

	/// <summary>
	/// Initializes the singleton application object.  This is the first line of authored code
	/// executed, and as such is the logical equivalent of main() or WinMain().
	/// </summary>
	public App()
	{
		ParseCommandLineArgs();
		this.InitializeComponent();
	}

	private static void ParseCommandLineArgs()
	{
		var args = Environment.GetCommandLineArgs();

		// args[0] is the executable path, actual args start at index 1
		for (int i = 1; i < args.Length; i++)
		{
			if (args[i] == "--workspace" && i + 1 < args.Length)
			{
				WorkspacePath = args[i + 1];
				break;
			}
		}
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}


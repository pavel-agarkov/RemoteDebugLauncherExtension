using System;
using System.ComponentModel.Design;
using System.IO;
using System.Text.RegularExpressions;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace RemoteDebugLauncher
{
	/// <summary>
	/// Command handler
	/// </summary>
	internal sealed class LaunchCommand
	{
		/// <summary>
		/// Command ID.
		/// </summary>
		public const int CommandId = 0x0100;

		private const string launchFileName = "launch.json";

		/// <summary>
		/// Command menu group (command set GUID).
		/// </summary>
		public static readonly Guid CommandSet = new Guid("b31119a2-2da1-4ae9-b66c-6fcf5e36ae34");

		/// <summary>
		/// VS Package that provides this command, not null.
		/// </summary>
		private readonly AsyncPackage package;

		/// <summary>
		/// Initializes a new instance of the <see cref="LaunchCommand"/> class.
		/// Adds our command handlers for menu (commands must exist in the command table file)
		/// </summary>
		/// <param name="package">Owner package, not null.</param>
		/// <param name="commandService">Command service to add command to, not null.</param>
		private LaunchCommand(AsyncPackage package, OleMenuCommandService commandService)
		{
			this.package = package ?? throw new ArgumentNullException(nameof(package));
			commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

			var menuCommandID = new CommandID(CommandSet, CommandId);
			var menuItem = new MenuCommand(Execute, menuCommandID);
			commandService.AddCommand(menuItem);
		}

		/// <summary>
		/// Gets the instance of the command.
		/// </summary>
		public static LaunchCommand Instance
		{
			get;
			private set;
		}

		/// <summary>
		/// Gets the service provider from the owner package.
		/// </summary>
		private Microsoft.VisualStudio.Shell.IAsyncServiceProvider ServiceProvider => package;

		/// <summary>
		/// Initializes the singleton instance of the command.
		/// </summary>
		/// <param name="package">Owner package, not null.</param>
		public static async Task InitializeAsync(AsyncPackage package)
		{
			// Switch to the main thread - the call to AddCommand in LaunchCommand's constructor requires
			// the UI thread.
			await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

			var commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
			Instance = new LaunchCommand(package, commandService);
		}

		/// <summary>
		/// This function is the callback used to execute the command when the menu item is clicked.
		/// See the constructor to see how the menu item is associated with this function using
		/// OleMenuCommandService service and MenuCommand class.
		/// </summary>
		/// <param name="sender">Event sender.</param>
		/// <param name="e">Event args.</param>
		private void Execute(object sender, EventArgs e)
		{
			ThreadHelper.ThrowIfNotOnUIThread();

			var dte = (DTE2)Package.GetGlobalService(typeof(SDTE));
			if (dte.SelectedItems.Count > 0)
			{
				var project = dte.SelectedItems.Item(1).Project;
				var solRoot = Path.GetDirectoryName(dte.Solution.FileName);
				var solRootBash = "/" + solRoot.Replace('\\', '/').Replace(":", "");
				var projRoot = Path.GetDirectoryName(project.FileName);
				var projRootBash = "/" + projRoot.Replace('\\', '/').Replace(":", "");
				var solRootExp = new Regex(@"%(workspaceRoot|SolutionRoot|root|rootDir)%", RegexOptions.IgnoreCase);
				var solRootBashExp = new Regex(@"%(workspaceRoot|SolutionRoot|root|rootDir)ForBash%", RegexOptions.IgnoreCase);
				var projRootExp = new Regex(@"%(projectRoot|projectDirectory|projDir)%", RegexOptions.IgnoreCase);
				var projRootBashExp = new Regex(@"%(projectRoot|projectDirectory|projDir)ForBash%", RegexOptions.IgnoreCase);
				var projNameExp = new Regex(@"%(projectName|projName)%", RegexOptions.IgnoreCase);

				var launchFilePath = FindLaunchFile(solRoot, projRoot);
				if (launchFilePath != null)
				{
					var launchJson = File.ReadAllText(launchFilePath);
					launchJson = projRootBashExp
						.Replace(solRootBashExp
						.Replace(projNameExp
						.Replace(projRootExp
						.Replace(solRootExp
						.Replace(launchJson, solRoot), projRoot), project.Name), solRootBash), projRootBash);
					launchJson = Environment.ExpandEnvironmentVariables(launchJson);
					Directory.CreateDirectory(Path.Combine(projRoot, "bin"));
					var finalLaunchFilePath = Path.Combine(projRoot, "bin", launchFileName);
					File.WriteAllText(finalLaunchFilePath, launchJson);
					dte.ExecuteCommand("DebugAdapterHost.Launch", $"/LaunchJson:\"{finalLaunchFilePath}\"");
				}
				else
				{
					ShowError("Could not find launch.json");
				}
			}
			else
			{
				ShowError("There are no selected projects to debug");
			}
		}

		private string FindLaunchFile(string solRoot, string projRoot)
		{
			var paths = new[] {
				Path.Combine(projRoot, "Properties", launchFileName),
				Path.Combine(projRoot, launchFileName),
				Path.Combine(solRoot, launchFileName)
			};
			foreach (var path in paths)
			{
				Logger.Log($"Checking {path}");
				if (File.Exists(path))
				{
					Logger.Log($"Using {path}");
					return path;
				}
			}
			return null;
		}

		private void ShowError(string message)
		{
			var title = "Remote Debug";
			Logger.Log(message);
			VsShellUtilities.ShowMessageBox(
				package,
				message,
				title,
				OLEMSGICON.OLEMSGICON_CRITICAL,
				OLEMSGBUTTON.OLEMSGBUTTON_OK,
				OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
		}
	}
}

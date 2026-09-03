using Avalonia.Controls;
using Avalonia.Input.Platform;
using Froststrap.UI.Utility;

namespace Froststrap.UI.Elements.Dialogs
{
    public partial class ExceptionDialog : Base.AvaloniaWindow
    {
        public ExceptionDialog()
        {
            InitializeComponent();
            SpectreChrome.Apply(this, ShellGlass, AbyssBackground);
            SpectreChrome.AttachPointer(this, AbyssBackground);
        }

        public ExceptionDialog(Exception exception) : this()
        {
            App.FrostRPC?.SetDialog("Exception");

            AddException(exception);

            if (!App.Logger.Initialized)
                LocateLogFileButton.Content = Strings.Dialog_Exception_CopyLogContents;

            string repoUrl = $"https://github.com/{App.ProjectRepository}";
            string wikiUrl = $"{repoUrl}/wiki";

            // Do not auto-attach logs into a remote issue URL.
            string helpMessage = String.Format(Strings.Dialog_Exception_Info_2_Alt, wikiUrl);

            HelpMessageMarkdown.MarkdownText = helpMessage;
            VersionText.Text = String.Format(Strings.Menu_About_Version, App.Version);

            ReportExceptionButton.IsVisible = false;

            LocateLogFileButton.Click += async delegate
            {
                if (App.Logger.Initialized && !String.IsNullOrEmpty(App.Logger.FileLocation))
                {
                    Utilities.ShellExecute(App.Logger.FileLocation);
                }
                else
                {
                    var topLevel = TopLevel.GetTopLevel(this);
                    if (topLevel?.Clipboard != null)
                    {
                        await topLevel.Clipboard.SetTextAsync(App.Logger.AsDocument);
                    }
                }
            };

            Loaded += (_, _) =>
            {
                Activate();
                Topmost = true;
                Topmost = false;
            };
        }

        private void AddException(Exception exception, bool inner = false)
        {
            var sb = new StringBuilder();

            if (!inner)
                sb.AppendLine($"{exception.GetType()}: {exception.Message}");
            else
                sb.AppendLine($"[Inner Exception]\n{exception.GetType()}: {exception.Message}");

            if (exception.StackTrace != null)
                sb.AppendLine($"\nStack Trace:\n{exception.StackTrace}");

            if (exception.InnerException != null)
            {
                sb.AppendLine();
                AddExceptionToBuilder(exception.InnerException, sb, true);
            }

            ErrorTextBox.Text = sb.ToString();
        }

        private static void AddExceptionToBuilder(Exception exception, StringBuilder sb, bool inner = false)
        {
            if (inner)
                sb.AppendLine($"[Inner Exception]\n{exception.GetType()}: {exception.Message}");
            else
                sb.AppendLine($"{exception.GetType()}: {exception.Message}");

            if (exception.StackTrace != null)
                sb.AppendLine($"\nStack Trace:\n{exception.StackTrace}");

            if (exception.InnerException != null)
            {
                sb.AppendLine();
                AddExceptionToBuilder(exception.InnerException, sb, true);
            }
        }
    }
}

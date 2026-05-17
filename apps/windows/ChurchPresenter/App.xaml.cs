using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

using ChurchPresenter.Hosting;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.AppLifecycle;

namespace ChurchPresenter;

/// <summary>
/// WinUI application entry. Dependency injection follows the WinUI MVVM tutorial pattern
/// (<see href="https://learn.microsoft.com/windows/apps/tutorials/winui-mvvm-toolkit/dependency-injection"/>):
/// build an <see cref="IServiceProvider"/> at launch and resolve services from it rather than constructing dependencies in views.
/// </summary>
public partial class App : Application
{
    private Window? _window;
    private AppInstance? _mainInstance;
    private ILogger<App>? _logger;
    private int _unexpectedErrorDialogOpen;

    /// <summary>Strongly typed app singleton; use for <c>App.Current.Services</c>-style access from code-behind.</summary>
    public static new App Current => (App)Application.Current;

    /// <summary>Root dependency injection container for this process; assigned at the start of <see cref="OnLaunched"/>.</summary>
    public static IServiceProvider Services { get; private set; } = null!;

    /// <summary>Same provider as <see cref="Services"/>; use when you already hold the <see cref="App"/> instance.</summary>
    public IServiceProvider HostServices => Services;

    public static Window? MainWindow { get; set; }

    public App()
    {
        InitializeComponent();
        UnhandledException += App_UnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Services = AppServices.Build();
        _logger = Services.GetService<ILogger<App>>();

        _mainInstance = AppInstance.FindOrRegisterForKey("main");
        if (!_mainInstance.IsCurrent)
        {
            var activatedEventArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
            _ = _mainInstance.RedirectActivationToAsync(activatedEventArgs);
            global::System.Diagnostics.Process.GetCurrentProcess().Kill();
            return;
        }

        Services.GetRequiredService<IAppActivationService>()
            .SetPendingPresentationPath(TryGetActivatedPresentationPath(AppInstance.GetCurrent().GetActivatedEventArgs()));

        _mainInstance.Activated += (_, activation) =>
        {
            if (_window?.DispatcherQueue == null)
                return;

            _window.DispatcherQueue.TryEnqueue(() =>
            {
                string? path = TryGetActivatedPresentationPath(activation);
                if (!string.IsNullOrWhiteSpace(path))
                    Services.GetRequiredService<IAppActivationService>().SetPendingPresentationPath(path);

                if (_window is MainWindow mainWindow)
                    mainWindow.NavigateToShowPage();

                _window?.Activate();
                if (!string.IsNullOrWhiteSpace(path))
                    _ = Services.GetRequiredService<ShowViewModel>().OpenPresentationFromPathAsync(path);
            });
        };

        var mainWindow = Services.GetRequiredService<MainWindow>();
        _window = mainWindow;
        MainWindow = mainWindow;
        string? theme = Services.GetService<ISettingsService>()?.Settings?.Theme;
        AppThemeHelper.ApplyToWindow(_window, theme);
        mainWindow.NavigateToShowPage();
        _window.Activate();

        _ = StartBackgroundContentMaintenanceAsync(mainWindow);
    }

    private async Task StartBackgroundContentMaintenanceAsync(MainWindow mainWindow)
    {
        try
        {
            var maintenance = Services.GetRequiredService<IContentStartupMaintenanceService>();
            maintenance.Changed += (_, _) =>
            {
                if (mainWindow.DispatcherQueue == null)
                    return;

                mainWindow.DispatcherQueue.TryEnqueue(() =>
                {
                    string? updatedTheme = Services.GetService<ISettingsService>()?.Settings?.Theme;
                    AppThemeHelper.ApplyToWindow(mainWindow, updatedTheme);
                });
            };

            await maintenance.StartAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "Startup content maintenance could not complete; continuing with the current content state.");
        }
    }

    /// <summary>
    /// When env <c>CHURCH_PRESENTER_DEBUG_THROW=1</c> is set, the exception is left unhandled
    /// (<see cref="Microsoft.UI.Xaml.UnhandledExceptionEventArgs.Handled"/> false) after logging so Visual Studio
    /// can break according to its exception settings. The default Debug experience keeps recoverable UI-thread
    /// exceptions handled so the app does not stop at this method for every reported error.
    /// </summary>
    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        ReportUnexpectedException("UI thread", e.Exception, canRecover: true);

        e.Handled = !ShouldLeaveUnhandledUiExceptionUnhandledForDebugging();
    }

    private static bool ShouldLeaveUnhandledUiExceptionUnhandledForDebugging()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("CHURCH_PRESENTER_DEBUG_THROW"), "1", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private void CurrentDomain_UnhandledException(object? sender, System.UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception
            ?? new InvalidOperationException(e.ExceptionObject?.ToString() ?? "Unknown unhandled exception.");
        ReportUnexpectedException("AppDomain", exception, canRecover: false);
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        ReportUnexpectedException("TaskScheduler", e.Exception, canRecover: true);
        e.SetObserved();
    }

    private void ReportUnexpectedException(string source, Exception exception, bool canRecover)
    {
        try
        {
            _logger?.LogError(exception, "Unhandled exception from {Source}. Recoverable: {CanRecover}", source, canRecover);
            if (exception is COMException com)
                _logger?.LogError("COM HRESULT=0x{HResult:X8}", unchecked((uint)com.HResult));
        }
        catch
        {
            // Best effort only.
        }

        try
        {
            Debug.WriteLine($"Unhandled exception from {source}:{Environment.NewLine}{exception}");
        }
        catch
        {
            // Best effort only.
        }

        TryReportStatusMessage(exception, canRecover);
        _ = ShowUnexpectedErrorDialogAsync(source, exception, canRecover);
    }

    private void TryReportStatusMessage(Exception exception, bool canRecover)
    {
        try
        {
            if (Services.GetService<ShowViewModel>() is not ShowViewModel show)
                return;

            show.StatusMessage = canRecover
                ? $"Recovered from an unexpected error: {exception.Message}"
                : $"A fatal error occurred: {exception.Message}";
        }
        catch
        {
            // The UI may not be ready yet.
        }
    }

    private Task ShowUnexpectedErrorDialogAsync(string source, Exception exception, bool canRecover)
    {
        var dispatcher = _window?.DispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
        if (dispatcher == null)
            return Task.CompletedTask;

        var dialogText = BuildUnexpectedErrorMessage(source, exception, canRecover);
        var tcs = new TaskCompletionSource();
        if (!dispatcher.TryEnqueue(async () =>
            {
                if (Interlocked.Exchange(ref _unexpectedErrorDialogOpen, 1) == 1)
                {
                    tcs.TrySetResult();
                    return;
                }

                try
                {
                    if (_window?.Content is not FrameworkElement root || root.XamlRoot == null)
                    {
                        tcs.TrySetResult();
                        return;
                    }

                    var dialog = new ContentDialog
                    {
                        Title = canRecover ? "Unexpected error handled" : "Unexpected error",
                        Content = dialogText,
                        CloseButtonText = "Close",
                        DefaultButton = ContentDialogButton.Close,
                        XamlRoot = root.XamlRoot,
                    };

                    await dialog.ShowAsync();
                }
                catch (Exception dialogException)
                {
                    try
                    {
                        _logger?.LogError(dialogException, "Failed to show unexpected error dialog.");
                    }
                    catch
                    {
                        // Best effort only.
                    }
                }
                finally
                {
                    Interlocked.Exchange(ref _unexpectedErrorDialogOpen, 0);
                    tcs.TrySetResult();
                }
            }))
        {
            tcs.TrySetResult();
        }

        return tcs.Task;
    }

    private static string BuildUnexpectedErrorMessage(string source, Exception exception, bool canRecover)
    {
        var builder = new StringBuilder();
        builder.AppendLine(canRecover
            ? "The last action hit an unexpected error, but the app kept running."
            : "The app hit an unexpected error and may need to be restarted.");
        builder.AppendLine();
        builder.Append("Source: ").AppendLine(source);
        builder.Append("Type: ").AppendLine(exception.GetType().FullName);
        builder.Append("Message: ").AppendLine(exception.Message);

        if (exception.InnerException?.Message is { Length: > 0 } innerMessage)
            builder.Append("Inner: ").AppendLine(innerMessage);

        var stack = exception.StackTrace;
        if (!string.IsNullOrWhiteSpace(stack))
        {
            var lines = stack.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            var take = Math.Min(lines.Length, 12);
            builder.AppendLine();
            builder.AppendLine("Stack (first lines):");
            for (var i = 0; i < take; i++)
                builder.AppendLine(lines[i].TrimEnd());
        }

        return builder.ToString();
    }

    private static string? TryGetActivatedPresentationPath(AppActivationArguments? activation)
    {
        if (activation?.Kind != ExtendedActivationKind.File)
            return null;

        if (activation.Data is not Windows.ApplicationModel.Activation.IFileActivatedEventArgs fileActivation)
            return null;

        return fileActivation.Files
            .OfType<Windows.Storage.IStorageFile>()
            .Select(static file => file.Path)
            .FirstOrDefault(static path =>
                !string.IsNullOrWhiteSpace(path)
                && string.Equals(Path.GetExtension(path), ".cpres", StringComparison.OrdinalIgnoreCase));
    }
}
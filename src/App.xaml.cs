using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace Frosty;

public partial class App : System.Windows.Application
{
    private const string MutexName = @"Local\Frosty_SingleInstance_Mutex_v1";
    private Mutex? _instanceMutex;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _instanceMutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        _ownsMutex = createdNew;

        if (!createdNew)
        {
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        base.OnStartup(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        CleanupTrayBeforeCrash();
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        CleanupTrayBeforeCrash();
    }

    private void CleanupTrayBeforeCrash()
    {
        try
        {
            if (MainWindow is MainWindow mw)
            {
                mw.EmergencyCleanup();
            }
        }
        catch
        {
                                                                    
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnDomainUnhandledException;

        if (_instanceMutex != null)
        {
            if (_ownsMutex)
            {
                try
                {
                    _instanceMutex.ReleaseMutex();
                }
                catch
                {
                                                                         
                }
            }
            _instanceMutex.Dispose();
            _instanceMutex = null;
        }

        base.OnExit(e);
    }
}
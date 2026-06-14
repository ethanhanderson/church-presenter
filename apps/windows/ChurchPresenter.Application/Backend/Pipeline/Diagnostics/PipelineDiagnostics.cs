using ChurchPresenter.Backend.Pipeline.Commands;

namespace ChurchPresenter.Backend.Pipeline.Diagnostics;

/// <summary>
/// Pipeline timing diagnostics surface.
/// </summary>
public interface IPipelineDiagnostics
{
    /// <summary>Most recently recorded pipeline timing snapshot.</summary>
    PipelineTiming? LastTiming { get; }

    /// <summary>Records a new timing snapshot.</summary>
    void RecordTiming(PipelineTiming timing);

    /// <summary>Raised when timing diagnostics change.</summary>
    event EventHandler? Changed;
}

/// <summary>
/// In-memory pipeline diagnostics recorder.
/// </summary>
public sealed class PipelineDiagnostics : IPipelineDiagnostics
{
    /// <inheritdoc />
    public PipelineTiming? LastTiming { get; private set; }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public void RecordTiming(PipelineTiming timing)
    {
        ArgumentNullException.ThrowIfNull(timing);
        LastTiming = timing;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

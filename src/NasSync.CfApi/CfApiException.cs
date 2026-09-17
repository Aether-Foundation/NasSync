namespace NasSync.CfApi;

/// <summary>
/// Exception thrown when a Windows Cloud Filter API (CfAPI) native call fails.
/// Wraps the HRESULT error code returned by the underlying Win32 function.
/// </summary>
public class CfApiException : Exception
{
    /// <summary>
    /// Gets the HRESULT error code from the failed native API call.
    /// </summary>
    public int HResult { get; }

    /// <summary>
    /// Gets the name of the native function that failed (e.g., "CfRegisterSyncRoot").
    /// </summary>
    public string FunctionName { get; }

    /// <summary>
    /// Creates a new CfApiException with the specified function name and error code.
    /// </summary>
    /// <param name="functionName">The name of the native CfAPI function that failed.</param>
    /// <param name="hresult">The HRESULT error code returned by the function.</param>
    public CfApiException(string functionName, int hresult)
        : base($"CfAPI call '{functionName}' failed with HRESULT 0x{hresult:X8}")
    {
        FunctionName = functionName;
        HResult = hresult;
    }

    /// <summary>
    /// Creates a new CfApiException with the specified function name, error code, and message.
    /// </summary>
    /// <param name="functionName">The name of the native CfAPI function that failed.</param>
    /// <param name="hresult">The HRESULT error code returned by the function.</param>
    /// <param name="message">A human-readable description of the error.</param>
    public CfApiException(string functionName, int hresult, string message)
        : base($"CfAPI call '{functionName}' failed with HRESULT 0x{hresult:X8}: {message}")
    {
        FunctionName = functionName;
        HResult = hresult;
    }

    /// <summary>
    /// Creates a new CfApiException with the specified function name, error code, message, and inner exception.
    /// </summary>
    /// <param name="functionName">The name of the native CfAPI function that failed.</param>
    /// <param name="hresult">The HRESULT error code returned by the function.</param>
    /// <param name="message">A human-readable description of the error.</param>
    /// <param name="innerException">The underlying exception that caused the failure.</param>
    public CfApiException(string functionName, int hresult, string message, Exception innerException)
        : base($"CfAPI call '{functionName}' failed with HRESULT 0x{hresult:X8}: {message}", innerException)
    {
        FunctionName = functionName;
        HResult = hresult;
    }
}

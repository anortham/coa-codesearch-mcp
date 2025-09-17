using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using TreeSitter;
using COA.CodeSearch.McpServer.Services.TypeExtraction.Interop;

namespace COA.CodeSearch.McpServer.Services.TypeExtraction;

/// <summary>
/// Thread-safe registry for caching Tree-sitter language handles and parsers.
/// Eliminates the performance overhead of reloading grammar libraries on every parse.
/// </summary>
public interface ILanguageRegistry : IDisposable
{
    Task<LanguageHandle?> GetLanguageHandleAsync(string languageName);
    bool IsLanguageSupported(string languageName);
}

public class LanguageRegistry : ILanguageRegistry
{
    private readonly ILogger<LanguageRegistry> _logger;
    private readonly ConcurrentDictionary<string, LanguageHandle> _handles = new();
    private readonly SemaphoreSlim _initSemaphore = new(1, 1);
    private readonly object _disposeLock = new();
    private bool _disposed = false;

    // Languages that don't have Tree-sitter DLLs available or have compatibility issues
    private static readonly HashSet<string> UnsupportedTreeSitterLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        "kotlin", "r", "objective-c", "lua", "dart", "zig", "elm", "clojure", "elixir",
        "go", "swift" // Currently have missing DLLs or version compatibility issues
    };

    public LanguageRegistry(ILogger<LanguageRegistry> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsLanguageSupported(string languageName)
    {
        return !UnsupportedTreeSitterLanguages.Contains(languageName) && languageName != "razor";
    }

    public async Task<LanguageHandle?> GetLanguageHandleAsync(string languageName)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(LanguageRegistry));
        }

        if (!IsLanguageSupported(languageName))
        {
            return null;
        }

        // Fast path - check if already cached
        if (_handles.TryGetValue(languageName, out var cached))
        {
            return cached;
        }

        // Slow path - need to load (with double-check locking)
        await _initSemaphore.WaitAsync();
        try
        {
            // Double-check pattern - another thread might have loaded it
            if (_handles.TryGetValue(languageName, out cached))
            {
                return cached;
            }

            _logger.LogDebug("Loading Tree-sitter language handle for: {Language}", languageName);

            var handle = await LoadLanguageAsync(languageName);
            if (handle != null)
            {
                _handles[languageName] = handle;
                _logger.LogInformation("Successfully cached Tree-sitter language handle for: {Language}", languageName);
            }
            else
            {
                _logger.LogWarning("Failed to load Tree-sitter language handle for: {Language}", languageName);
            }

            return handle;
        }
        finally
        {
            _initSemaphore.Release();
        }
    }

    private async Task<LanguageHandle?> LoadLanguageAsync(string languageName)
    {
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                return await LoadLanguageMacOSAsync(languageName);
            }
            else
            {
                return await LoadLanguageStandardAsync(languageName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load language handle for {Language}", languageName);
            return null;
        }
    }

    private async Task<LanguageHandle?> LoadLanguageStandardAsync(string languageName)
    {
        return await Task.Run(() =>
        {
            try
            {
                Language language;
                if (languageName == "c-sharp")
                {
                    language = new Language("tree-sitter-c-sharp", "tree_sitter_c_sharp");
                }
                else
                {
                    language = new Language(languageName);
                }

                // Create a parser for this language
                var parser = new Parser(language);

                return new LanguageHandle
                {
                    Language = language,
                    Parser = parser,
                    LanguageName = languageName,
                    IsNative = false
                };
            }
            catch (Exception ex) when (ex.Message.Contains("Could not find entry point") || ex.Message.Contains("incompatible"))
            {
                _logger.LogDebug("Tree-sitter library for {Language} is not available or incompatible: {Error}", languageName, ex.Message);
                return null;
            }
        });
    }

    private async Task<LanguageHandle?> LoadLanguageMacOSAsync(string languageName)
    {
        return await Task.Run(() =>
        {
            string libName = languageName == "c-sharp" ? "tree-sitter-c-sharp" : $"tree-sitter-{languageName}";
            var exportName = languageName == "c-sharp" ? "tree_sitter_c_sharp" : $"tree_sitter_{languageName.Replace('-', '_')}";

            // Ensure core library is preloaded
            TryPreloadMacNativeLibrary("tree-sitter");
            TryPreloadMacNativeLibrary(libName);

            IntPtr langLibHandle = IntPtr.Zero;

            // Probe common names for the grammar library
            var dllBaseNames = new[]
            {
                libName,
                libName.Replace("tree-sitter-", string.Empty),
                languageName
            };

            foreach (var baseName in dllBaseNames)
            {
                if (NativeLibrary.TryLoad(baseName, out langLibHandle))
                {
                    break;
                }
            }

            if (langLibHandle == IntPtr.Zero)
            {
                // Last resort: try direct dylib path probing
                string dylibFile = $"lib{libName}.dylib";
                var prefixes = new[] { "/opt/homebrew", "/usr/local", "/opt/local" };
                foreach (var prefix in prefixes)
                {
                    var candidate = Path.Combine(prefix, "lib", dylibFile);
                    if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out langLibHandle))
                    {
                        break;
                    }
                    var optCandidate = Path.Combine(prefix, "opt", "tree-sitter", "lib", dylibFile);
                    if (File.Exists(optCandidate) && NativeLibrary.TryLoad(optCandidate, out langLibHandle))
                    {
                        break;
                    }
                }
            }

            if (langLibHandle == IntPtr.Zero)
            {
                _logger.LogError("Could not load Tree-sitter grammar library '{LibName}' on macOS", libName);
                return null;
            }

            try
            {
                var langFuncPtr = NativeLibrary.GetExport(langLibHandle, exportName);
                var factory = Marshal.GetDelegateForFunctionPointer<LanguageFactory>(langFuncPtr);
                var languagePtr = factory();

                if (languagePtr == IntPtr.Zero)
                {
                    _logger.LogError("Failed to obtain TSLanguage* from '{ExportName}'", exportName);
                    NativeLibrary.Free(langLibHandle);
                    return null;
                }

                // Create native parser
                var parser = TreeSitterNative.ts_parser_new();
                if (parser == IntPtr.Zero)
                {
                    _logger.LogError("ts_parser_new returned null for {Language}", languageName);
                    NativeLibrary.Free(langLibHandle);
                    return null;
                }

                TreeSitterNative.ts_parser_set_language(parser, languagePtr);

                return new LanguageHandle
                {
                    LanguageName = languageName,
                    IsNative = true,
                    NativeLibraryHandle = langLibHandle,
                    NativeLanguagePtr = languagePtr,
                    NativeParser = parser,
                    LanguageFactory = factory
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize native language handle for {Language}", languageName);
                if (langLibHandle != IntPtr.Zero)
                {
                    NativeLibrary.Free(langLibHandle);
                }
                return null;
            }
        });
    }

    private static void TryPreloadMacNativeLibrary(string libraryName)
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            var fileName = $"lib{libraryName}.dylib";
            var prefixes = new[] { "/opt/homebrew", "/usr/local", "/opt/local" };
            foreach (var prefix in prefixes)
            {
                var candidate = Path.Combine(prefix, "lib", fileName);
                if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out _))
                {
                    return;
                }
                var optCandidate = Path.Combine(prefix, "opt", "tree-sitter", "lib", fileName);
                if (File.Exists(optCandidate) && NativeLibrary.TryLoad(optCandidate, out _))
                {
                    return;
                }
            }
        }
        catch
        {
            // Ignore preload failures
        }
    }

    public void Dispose()
    {
        lock (_disposeLock)
        {
            if (_disposed) return;
            _disposed = true;
        }

        _logger.LogDebug("Disposing LanguageRegistry with {Count} cached handles", _handles.Count);

        // Dispose all cached handles
        foreach (var handle in _handles.Values)
        {
            try
            {
                handle.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing language handle for {Language}", handle.LanguageName);
            }
        }

        _handles.Clear();
        _initSemaphore.Dispose();
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr LanguageFactory();
}

/// <summary>
/// Represents a cached language handle with associated parser.
/// Handles both managed TreeSitter.DotNet objects and native macOS handles.
/// </summary>
public class LanguageHandle : IDisposable
{
    public string LanguageName { get; set; } = string.Empty;
    public bool IsNative { get; set; }

    // Managed path (Windows/Linux)
    public Language? Language { get; set; }
    public Parser? Parser { get; set; }

    // Native path (macOS)
    public IntPtr NativeLibraryHandle { get; set; } = IntPtr.Zero;
    public IntPtr NativeLanguagePtr { get; set; } = IntPtr.Zero;
    public IntPtr NativeParser { get; set; } = IntPtr.Zero;
    public object? LanguageFactory { get; set; }

    private bool _disposed = false;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (IsNative)
        {
            // Native cleanup
            if (NativeParser != IntPtr.Zero)
            {
                TreeSitterNative.ts_parser_delete(NativeParser);
                NativeParser = IntPtr.Zero;
            }

            if (NativeLibraryHandle != IntPtr.Zero)
            {
                NativeLibrary.Free(NativeLibraryHandle);
                NativeLibraryHandle = IntPtr.Zero;
            }
        }
        else
        {
            // Managed cleanup
            Parser?.Dispose();
            Language?.Dispose();
        }
    }
}
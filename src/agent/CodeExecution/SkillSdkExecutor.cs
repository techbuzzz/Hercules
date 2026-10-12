using System.Reflection;
using System.Runtime.Loader;
using Hercules.SkillSdk;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Hercules.CodeExecution;

/// <summary>
///     In-process SkillSdk executor for file-based C# skills.
///     Compiles user code together with Hercules.SkillSdk, loads it into a collectible
///     <see cref="AssemblyLoadContext"/>, and invokes <see cref="IHerculesSkillContext"/> entry point.
/// </summary>
public sealed class SkillSdkExecutor : ICodeExecutor
{
    private readonly SandboxOptions _options;
    private readonly ISkillContextFactory _contextFactory;

    public SkillSdkExecutor(SandboxOptions options, ISkillContextFactory contextFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(contextFactory);
        options.Validate();
        _options = options;
        _contextFactory = contextFactory;
    }

    public string Name => "skill-sdk-in-process";

    public IReadOnlySet<string> SupportedLanguages { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "csharp", "cs", "c#"
    };

    public async Task<ExecutionResult> ExecuteAsync(ExecutionRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!SupportedLanguages.Contains(request.Language))
        {
            return ExecutionResult.Failed($"Unsupported language: {request.Language}");
        }

        var codeBytes = System.Text.Encoding.UTF8.GetByteCount(request.Code);
        if (codeBytes > _options.MaxCodeSizeKb * 1024)
        {
            return ExecutionResult.Failed($"Code size {codeBytes} bytes exceeds limit {_options.MaxCodeSizeKb * 1024} bytes");
        }

        // Layer 1: DangerousCodeScanner (blacklist)
        var blacklist = DangerousCodeScanner.Scan(request.Code, _options);
        if (!blacklist.IsAllowed)
        {
            return ExecutionResult.Rejected(blacklist.BlockedReasons);
        }

        // Layer 2: SkillSdk whitelist
        var whitelist = SkillSdkWhitelistScanner.Scan(request.Code);
        if (!whitelist.IsAllowed)
        {
            return new ExecutionResult(
                0,
                "",
                string.Join("; ", whitelist.BlockedReasons),
                0,
                "rejected",
                whitelist.BlockedReasons);
        }

        if (!SkillSdkWhitelistScanner.ContainsSkillSdkReference(request.Code))
        {
            // Not a SkillSdk app; we still allow execution but no context is injected.
            // For plain C# without SkillSdk the legacy DotnetFileBasedExecutor is preferred.
            return ExecutionResult.Failed("SkillSdkExecutor requires a reference to Hercules.SkillSdk. Use DotnetFileBasedExecutor for plain C#.");
        }

        var sessionId = Guid.NewGuid().ToString("N")[..12];
        var skillName = request.WorkingDir ?? "skill";
        var context = _contextFactory.Create(sessionId, userId: null, skillName, ct);

        try
        {
            var result = await CompileAndRunAsync(request.Code, context, ct);
            return new ExecutionResult(
                result.ExitCode,
                result.Stdout,
                result.Stderr,
                result.DurationMs,
                result.ExitCode == 0 ? "ok" : "failed",
                Array.Empty<string>());
        }
        catch (OperationCanceledException)
        {
            return ExecutionResult.TimedOut(_options.CpuTimeoutSeconds * 1000);
        }
        catch (Exception ex)
        {
            return ExecutionResult.Failed($"SkillSdk executor exception: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr, long DurationMs)> CompileAndRunAsync(
        string code,
        IHerculesSkillContext context,
        CancellationToken ct)
    {
        var sdkPath = GetSkillSdkAssemblyPath();
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(sdkPath)
        };

        AddNetCoreAppReferences(references);

        var syntaxTree = CSharpSyntaxTree.ParseText(code);
        var compilation = CSharpCompilation.Create(
            assemblyName: $"SkillSdkRun_{Guid.NewGuid():N}",
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                allowUnsafe: false));

        using var ms = new MemoryStream();
        var emitResult = compilation.Emit(ms);
        if (!emitResult.Success)
        {
            var diagnostics = string.Join("\n", emitResult.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => $"{d.Id}: {d.GetMessage()}"));
            return (-1, "", $"Compilation failed:\n{diagnostics}", 0);
        }

        ms.Seek(0, SeekOrigin.Begin);

        var alc = new CollectibleSkillLoadContext();
        try
        {
            var assembly = alc.LoadFromStream(ms);
            var entry = FindEntryPoint(assembly);
            if (entry is null)
            {
                return (-1, "", "No valid SkillSdk entry point found. Expected: public static Task|void Main(IHerculesSkillContext)", 0);
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var stdoutCapture = new StringWriter();
            var stderrCapture = new StringWriter();

            var originalOut = Console.Out;
            var originalErr = Console.Error;
            try
            {
                Console.SetOut(stdoutCapture);
                Console.SetError(stderrCapture);

                var parameters = entry.GetParameters();
                if (parameters.Length == 1 && parameters[0].ParameterType == typeof(IHerculesSkillContext))
                {
                    var returnValue = entry.Invoke(null, new object[] { context });
                    if (returnValue is Task task)
                    {
                        await task.WaitAsync(ct);
                    }
                }
                else if (parameters.Length == 0)
                {
                    var returnValue = entry.Invoke(null, null);
                    if (returnValue is Task task)
                    {
                        await task.WaitAsync(ct);
                    }
                }
                else
                {
                    return (-1, "", "Entry point signature is not supported", 0);
                }

                sw.Stop();
                return (0, stdoutCapture.ToString(), stderrCapture.ToString(), sw.ElapsedMilliseconds);
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalErr);
            }
        }
        finally
        {
            alc.Unload();
        }
    }

    private static MethodInfo? FindEntryPoint(Assembly assembly)
    {
        foreach (var type in assembly.GetTypes())
        {
            if (type.Name != "Program" && type.Name != "<><Program>$")
            {
                continue;
            }

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            var method = type.GetMethod("Main", flags);
            if (method is null)
            {
                // Top-level statements generate a type named "<Program>$" or similar with a Main method.
                method = type.GetMethods(flags).FirstOrDefault(m => m.Name.Contains("Main", StringComparison.Ordinal));
            }

            if (method is null)
            {
                continue;
            }

            var parameters = method.GetParameters();
            var returnsVoidOrTask = method.ReturnType == typeof(void) || method.ReturnType == typeof(Task);
            var hasContextParam = parameters.Length == 1 && parameters[0].ParameterType == typeof(IHerculesSkillContext);

            if (returnsVoidOrTask && (parameters.Length == 0 || hasContextParam))
            {
                return method;
            }
        }

        return null;
    }

    private static string GetSkillSdkAssemblyPath()
    {
        var assembly = typeof(IHerculesSkillContext).Assembly;
        return assembly.Location;
    }

    private static void AddNetCoreAppReferences(List<MetadataReference> references)
    {
        var refDir = FindNetCoreAppRefDirectory();
        if (refDir is null)
        {
            // Fallback to runtime assemblies (less reliable, but works in self-contained scenarios).
            refDir = Path.GetDirectoryName(typeof(object).Assembly.Location);
            if (refDir is null)
            {
                throw new InvalidOperationException("Unable to locate .NET reference assemblies for SkillSdk compilation");
            }
        }

        foreach (var dll in Directory.GetFiles(refDir, "*.dll"))
        {
            try
            {
                references.Add(MetadataReference.CreateFromFile(dll));
            }
            catch
            {
                // Best effort: skip reference assemblies that cannot be loaded.
            }
        }
    }

    internal static string? FindNetCoreAppRefDirectory()
    {
        var coreLibPath = typeof(object).Assembly.Location;
        var sharedDir = Path.GetDirectoryName(coreLibPath);
        if (string.IsNullOrEmpty(sharedDir))
        {
            return null;
        }

        // sharedDir = C:\Program Files\dotnet\shared\Microsoft.NETCore.App\10.0.11
        var version = Path.GetFileName(sharedDir);
        var dotnetRoot = Directory.GetParent(sharedDir)?.Parent?.Parent?.FullName;
        if (string.IsNullOrEmpty(dotnetRoot))
        {
            return null;
        }

        // TFM folder is "net10.0" for version "10.0.11".
        var tfm = version.IndexOf('.') > 0 ? $"net{version[..version.IndexOf('.')]}.0" : $"net{version}.0";
        var refDir = Path.Combine(dotnetRoot, "packs", "Microsoft.NETCore.App.Ref", version, "ref", tfm);
        return Directory.Exists(refDir) ? refDir : null;
    }

    private sealed class CollectibleSkillLoadContext : AssemblyLoadContext
    {
        public CollectibleSkillLoadContext()
            : base(isCollectible: true)
        {
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            // Do not resolve into the default context for user code references,
            // except for the shared framework assemblies that we already referenced.
            return null;
        }
    }
}

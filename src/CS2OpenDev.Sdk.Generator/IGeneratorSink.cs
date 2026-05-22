namespace CS2SchemaGen;

// Output channel for ModuleEmitter and friends. Decouples the emitters from any
// particular host — the Exporter implements a DiskSink that writes straight into
// `src/CS2OpenDev.Sdk/`, while tests implement a CapturingSink that retains the
// output in memory for assertions.
//
// `relativePath` is the file path under the SDK root, without a `.cs` extension.
// Examples: "Client/CCSPlayer", "Common/CFoo", "Attributes", "SchemaNames".
internal interface IGeneratorSink
{
    void AddSource(string relativePath, string source);

    void ReportDiagnostic(GeneratorDiagnostic diagnostic, params object[] messageArgs);
}

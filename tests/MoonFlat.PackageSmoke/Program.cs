using MoonSharp.Interpreter;

var script = new Script(CoreModules.Preset_SoftSandbox);
var result = script.DoString("return 6 * 7");
if (result.Type != DataType.Number || result.Number != 42)
    throw new InvalidOperationException($"Installed MoonFlat package returned {result}, expected 42.");

Console.WriteLine("MOONFLAT_PACKAGE_SMOKE passed=1");

using MoonSharp.Interpreter;

var script = new Script(CoreModules.Preset_SoftSandbox);
var result = script.DoString("local function greet(name) return 'Hello, ' .. name end; return greet('MoonFlat')");
Console.WriteLine(result.String);
